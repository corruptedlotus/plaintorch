import { existsSync, readFileSync, statSync } from "node:fs"
import path from "node:path"

/** How the core executable was found. */
export type CoreOrigin = "PLAINTORCH_CORE_PATH override" | "packaged core" | "Debug build" | "Release build" | "installer publish"

/** What {@link locateCore} needs to know about the running app, so it stays free of Electron and testable. */
export interface CoreLocationContext {
	/** `process.env.PLAINTORCH_CORE_PATH`: an explicit executable that wins over everything. */
	override?: string
	/** `app.isPackaged`. */
	packaged: boolean
	/** `process.resourcesPath`: the packaged core lives under its `core` folder. */
	resourcesPath: string
	/** `app.getAppPath()`: the `standalone` folder in a development run. */
	appPath: string
	/** Picks the executable's name; defaults to `process.platform`. */
	platform?: NodeJS.Platform
}

/** The core executable the shell will spawn, and what it should say about it. */
export interface CoreLocation {
	executable: string
	origin: CoreOrigin
	/** The core's product version, `<version>+<commit>`, read from its assembly; undefined when it cannot be read. */
	version?: string
	/** Warnings the shell must surface: it fell back to the installer's publish, or it passed over a newer build. */
	warnings: string[]
}

interface CoreCandidate {
	executable: string
	origin: CoreOrigin
}

/**
 * Finds the core executable to spawn:
 *
 * 1. `PLAINTORCH_CORE_PATH`, whenever it is set.
 * 2. A packaged app: the core it ships, `resources/core`.
 * 3. A development (unpackaged) run: the core project's own build, `core/bin/Debug/net10.0` and then
 *    `core/bin/Release/net10.0`, so a fresh `dotnet build` is what runs.
 * 4. Only when there is no build at all: `standalone/core-dist`, the installer's publish. It is git-ignored and pinned
 *    to whatever commit the last installer build saw, so it silently outlives every later change (a newer core may
 *    have migrated the vault past it) and choosing it always warns.
 *
 * A development run also warns when a candidate it passed over has a newer `plaintorch.dll` than the one it chose.
 * Returns undefined when no candidate exists.
 */
export function locateCore(context: CoreLocationContext): CoreLocation | undefined {
	if (context.override) {
		return describe({ executable: context.override, origin: "PLAINTORCH_CORE_PATH override" })
	}

	const fileName = (context.platform ?? process.platform) === "win32" ? "plaintorch.exe" : "plaintorch"
	if (context.packaged) {
		const packaged = path.join(context.resourcesPath, "core", fileName)
		return existsSync(packaged) ? describe({ executable: packaged, origin: "packaged core" }) : undefined
	}

	const coreBin = path.join(context.appPath, "..", "core", "bin")
	const candidates: CoreCandidate[] = [
		{ executable: path.join(coreBin, "Debug", "net10.0", fileName), origin: "Debug build" },
		{ executable: path.join(coreBin, "Release", "net10.0", fileName), origin: "Release build" },
		{ executable: path.join(context.appPath, "core-dist", fileName), origin: "installer publish" },
	]
	const present = candidates.filter(candidate => existsSync(candidate.executable))
	const chosen = present[0]
	if (!chosen) {
		return undefined
	}

	const location = describe(chosen)
	if (chosen.origin === "installer publish") {
		location.warnings.push(`No core build was found under ${path.resolve(coreBin)}, so the shell fell back to the installer's publish, ${label(location)}. It may be older than the vault's schema: build the core (\`dotnet build core\`) or delete standalone/core-dist.`)
	}

	const newest = present.reduce((newer, candidate) => builtAt(candidate) > builtAt(newer) ? candidate : newer)
	if (newest !== chosen) {
		location.warnings.push(`The ${newest.origin}, ${label(describe(newest))}, is newer than the ${chosen.origin} the shell chose. Rebuild the ${chosen.origin} or point PLAINTORCH_CORE_PATH at the newer one.`)
	}

	return location
}

/** One line naming the core, where it came from, and its version, for the shell's log. */
export function describeCoreLocation(location: CoreLocation): string {
	return `Using the ${location.origin}: ${label(location)}`
}

const productVersionKey = Buffer.from("ProductVersion\0", "utf16le")

/**
 * Reads the `ProductVersion` string of a Win32 version resource, which is what `FileVersionInfo.ProductVersion`
 * reports; for a .NET assembly it carries the informational version, `<version>+<commit>`. The compiler embeds that
 * resource on every platform, so this works on any OS. Returns undefined when the file or the entry cannot be read.
 */
export function readProductVersion(file: string): string | undefined {
	let image: Buffer
	try {
		image = readFileSync(file)
	}
	catch {
		return undefined
	}

	// A version-resource String is { wLength, wValueLength (in WCHARs), wType (1 = text), szKey, padding to 32 bits, Value }.
	const key = image.lastIndexOf(productVersionKey)
	const start = key - 6
	if (key < 0 || start < 0 || image.readUInt16LE(start + 4) !== 1) {
		return undefined
	}

	const valueStart = start + Math.ceil((6 + productVersionKey.length) / 4) * 4
	const valueEnd = Math.min(image.length, valueStart + image.readUInt16LE(start + 2) * 2)
	const value = image.toString("utf16le", valueStart, valueEnd).split("\0")[0]
	return value || undefined
}

function describe(candidate: CoreCandidate): CoreLocation {
	return { ...candidate, version: readProductVersion(assemblyOf(candidate.executable)), warnings: [] }
}

function label(location: CoreLocation): string {
	return `${path.resolve(location.executable)} (${location.version ?? "version unknown"})`
}

/** The managed assembly beside an apphost: `plaintorch.exe` and `plaintorch` both run `plaintorch.dll`. */
function assemblyOf(executable: string): string {
	return path.join(path.dirname(executable), `${path.parse(executable).name}.dll`)
}

/** When a candidate was built: its assembly's modification time, which every build rewrites. */
function builtAt(candidate: CoreCandidate): number {
	for (const file of [assemblyOf(candidate.executable), candidate.executable]) {
		try {
			return statSync(file).mtimeMs
		}
		catch {
			// Try the next one.
		}
	}

	return 0
}
