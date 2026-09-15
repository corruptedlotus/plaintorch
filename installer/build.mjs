// Builds a PLAINTORCH desktop installer.
//
//   node installer/build.mjs --flavor standalone [--rid win-x64] [--no-publish-core]
//   node installer/build.mjs --flavor client
//
// The standalone flavour publishes the core (self-contained, single directory) for the target runtime into
// standalone/core-dist, which electron-builder packs as resources/core. The client flavour ships no core. Both then
// bundle the shell for that flavour and hand off to electron-builder with installer/electron-builder.yml.
import { execSync } from "node:child_process"
import { existsSync, rmSync } from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"

const here = path.dirname(fileURLToPath(import.meta.url))
const repo = path.resolve(here, "..")
const standalone = path.join(repo, "standalone")
const coreProject = path.join(repo, "core", "plaintorch.csproj")
const coreOutput = path.join(standalone, "core-dist")

const args = process.argv.slice(2)
const option = (name, fallback) => {
	const index = args.indexOf(name)
	return index >= 0 ? args[index + 1] : fallback
}
const flavor = option("--flavor", "standalone")
if (flavor !== "standalone" && flavor !== "client") {
	console.error("Use --flavor standalone or --flavor client.")
	process.exit(1)
}

const defaultRid = process.platform === "win32" ? "win-x64" : process.platform === "darwin" ? (process.arch === "arm64" ? "osx-arm64" : "osx-x64") : "linux-x64"
const rid = option("--rid", defaultRid)
const publishCore = !args.includes("--no-publish-core")
const run = (command, cwd) => {
	console.log(`\n> ${command}`)
	execSync(command, { cwd, stdio: "inherit", env: { ...process.env, PLAINTORCH_FLAVOR: flavor } })
}

rmSync(coreOutput, { recursive: true, force: true })
if (flavor === "standalone" && publishCore) {
	run(`dotnet publish "${coreProject}" -c Release -r ${rid} --self-contained true -o "${coreOutput}" -p:PublishSingleFile=false -p:DebugType=none`, repo)
	// Neither the design-time factory's needs nor the dev sub-profile belong in a shipped core.
	for (const stray of ["appsettings.Development.json"]) {
		rmSync(path.join(coreOutput, stray), { force: true })
	}
}
else if (flavor === "standalone" && !existsSync(coreOutput)) {
	console.error("--no-publish-core was given but standalone/core-dist does not exist.")
	process.exit(1)
}

run(`node esbuild.config.mjs --flavor ${flavor} --production`, standalone)
const builder = path.join(standalone, "node_modules", ".bin", process.platform === "win32" ? "electron-builder.cmd" : "electron-builder")
if (!existsSync(builder)) {
	console.error("electron-builder is not installed. Run `npm install` in standalone/ first (it is a devDependency there).")
	process.exit(1)
}

run(`"${builder}" --config "${path.join(here, "electron-builder.yml")}" --publish never`, standalone)
