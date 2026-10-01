import { readFileSync, statSync, watch, writeFileSync, type FSWatcher } from "node:fs"
import path from "node:path"
import { app, BrowserWindow, dialog, ipcMain, type WebContents } from "electron"
import { ipc, planningFileExtension, type PlanningFileSnapshot, type PlanningSaveOutcome } from "../shared/contracts"

/** How long a run of edits rests before it is written: a drag's end and the pin change after it land as one write. */
const writeDelayMs = 400

/** How long the file's folder rests after a change before the file is read again: an editor's save is often several. */
const settleDelayMs = 150

/** The name the save dialog offers a new planning file, numbered when the vault already has one by that name. */
const defaultPlanningName = "Global planning"

/**
 * The planning graph files a launch names: its arguments that end in the extension and name an existing file, resolved
 * against the launching process's working directory, each once. Switches are skipped, and so is what Chromium adds to a
 * second instance's command line.
 */
export function planningFilesIn(argv: readonly string[], workingDirectory: string): string[] {
	const suffix = `.${planningFileExtension}`
	const files: string[] = []
	for (const argument of argv) {
		if (argument.startsWith("-") || !argument.toLowerCase().endsWith(suffix)) {
			continue
		}

		const file = path.resolve(workingDirectory, argument)
		if (isFile(file) && !files.some(known => planningFileKey(known) === planningFileKey(file))) {
			files.push(file)
		}
	}

	return files
}

/** What tells two paths to one planning file apart: the absolute path, without case where the file system has none. */
export function planningFileKey(file: string): string {
	return withoutCase(path.resolve(file))
}

/** A path or a name as the file system compares it: Windows and (by default) macOS ignore case. */
function withoutCase(value: string): string {
	return process.platform === "win32" || process.platform === "darwin" ? value.toLowerCase() : value
}

/**
 * The file behind one planning window. The sandboxed page has no file access, so the main process owns the file: the
 * page reads and writes it through here and is told here when it changes on disk from elsewhere — Obsidian showing the
 * same file, another editor, a sync.
 *
 * Writes are coalesced, and one that would leave the file as it is is skipped. The folder is watched rather than the
 * file, which an editor that saves by renaming a new file over it would leave the watch behind on; once the folder
 * rests the file is read again, and only a text that differs from the one last read or written reaches the page, so
 * the shell's own writes never echo back. A change from elsewhere wins over a write still waiting, as it does in the
 * Obsidian view. A file moved or deleted elsewhere is not written back into being: writes stop until a file is at the
 * path again.
 */
export class PlanningFile {
	/** The text last read from or written to the file; undefined while there is no file at the path. */
	private known?: string
	private pending?: string
	private writeTimer?: NodeJS.Timeout
	private settleTimer?: NodeJS.Timeout
	private watcher?: FSWatcher
	private closed = false

	/** Reads the file and starts watching it; `announce` hears each change from elsewhere. */
	public constructor(public readonly path: string, private readonly announce: (snapshot: PlanningFileSnapshot) => void) {
		this.known = readText(path)
		this.watch()
	}

	/** The file's name without its extension. */
	public get name(): string {
		return path.basename(this.path, path.extname(this.path))
	}

	/** The file as last read or written. */
	public get snapshot(): PlanningFileSnapshot {
		return { path: this.path, name: this.name, text: this.known }
	}

	/** Replaces the file's text once edits have rested for a moment. */
	public write(text: string): void {
		if (this.closed) {
			return
		}

		this.pending = text
		clearTimeout(this.writeTimer)
		this.writeTimer = setTimeout(() => this.flush(), writeDelayMs)
	}

	/** Lands the write still waiting, if there is one. Synchronous, so a quit cannot outrun it. */
	public flush(): void {
		clearTimeout(this.writeTimer)
		const text = this.pending
		this.pending = undefined
		if (text === undefined || this.known === undefined || text === this.known) {
			return
		}

		try {
			writeFileSync(this.path, text, "utf8")
			this.known = text
		}
		catch (error) {
			console.warn(`[planning] Could not write ${this.path}: ${error instanceof Error ? error.message : String(error)}`)
		}
	}

	/**
	 * Replaces the file's text at once on behalf of something other than its window — a new file saved over it — and
	 * tells the window, which drops a write it still had waiting. Throws when the file cannot be written.
	 */
	public replace(text: string): void {
		clearTimeout(this.writeTimer)
		this.pending = undefined
		writeFileSync(this.path, text, "utf8")
		this.known = text
		this.announce(this.snapshot)
	}

	/** Lands the last write and stops watching, for a window that closed. */
	public close(): void {
		if (this.closed) {
			return
		}

		this.flush()
		this.closed = true
		clearTimeout(this.settleTimer)
		this.watcher?.close()
		this.watcher = undefined
	}

	private watch(): void {
		const fileName = withoutCase(path.basename(this.path))
		try {
			this.watcher = watch(path.dirname(this.path), (_, changed) => {
				// Where the platform leaves the name out, any change in the folder is read as one to the file.
				if (changed && withoutCase(changed.toString()) !== fileName) {
					return
				}

				clearTimeout(this.settleTimer)
				this.settleTimer = setTimeout(() => this.readAgain(), settleDelayMs)
			})
			this.watcher.on("error", () => {
				this.watcher?.close()
				this.watcher = undefined
			})
		}
		catch {
			// An unwatchable folder (gone, or on a file system without notifications) only costs following outside edits.
			this.watcher = undefined
		}
	}

	private readAgain(): void {
		if (this.closed) {
			return
		}

		const text = readText(this.path)
		// A file that is there but cannot be read is still being written by whoever changed it, not gone: the change
		// that finishes the write reads it.
		if (text === this.known || (text === undefined && isFile(this.path))) {
			return
		}

		clearTimeout(this.writeTimer)
		this.pending = undefined
		this.known = text
		this.announce(this.snapshot)
	}
}

/** What {@link handlePlanningFiles} needs of the shell. */
export interface PlanningFileHost {
	/** The file of the planning window a page runs in; undefined for any other page. */
	fileOf(contents: WebContents): PlanningFile | undefined
	/** The file of the planning window open on a path, if one is. */
	fileAt(filePath: string): PlanningFile | undefined
	/** The vault the core serves, where a new planning file is offered a place. */
	servedVault(): string | undefined
	/** Opens a planning file in its window. */
	open(filePath: string): void
}

/**
 * Answers the pages: a planning window's page reads and writes the file of the window it runs in, and no other file;
 * any page may save a new planning file, through a save dialog, and have it opened.
 */
export function handlePlanningFiles(host: PlanningFileHost): void {
	ipcMain.handle(ipc.planningRead, event => host.fileOf(event.sender)?.snapshot)
	ipcMain.handle(ipc.planningWrite, (event, text: unknown) => {
		if (typeof text === "string") {
			host.fileOf(event.sender)?.write(text)
		}
	})
	ipcMain.handle(ipc.planningSaveAs, async (event, text: unknown): Promise<PlanningSaveOutcome | undefined> => {
		if (typeof text !== "string") {
			return undefined
		}

		const vault = host.servedVault()
		const file = await askForNewPlanningFile(BrowserWindow.fromWebContents(event.sender) ?? undefined, vault)
		if (file === undefined) {
			return undefined
		}

		if ("error" in file) {
			return file
		}

		try {
			// A file already open in a planning window is replaced through it, so the window shows the new text at
			// once and a write it had waiting cannot land over it.
			const open = host.fileAt(file.path)
			if (open) {
				open.replace(text)
			}
			else {
				writeFileSync(file.path, text, "utf8")
			}
		}
		catch (error) {
			return { error: error instanceof Error ? error.message : String(error) }
		}

		host.open(file.path)
		return { path: displayPath(file.path, vault) }
	})
}

/**
 * Asks where a new planning file goes: a save dialog, modal to `owner`, starting in the served vault (the documents
 * folder without one) under a name no file there has yet, and showing planning graph files. The dialog confirms
 * replacing a file. A name typed without the extension gets it, and a file that already has the name that makes is
 * refused rather than replaced, since the dialog confirmed only the name as typed. Undefined when cancelled.
 */
async function askForNewPlanningFile(owner: BrowserWindow | undefined, vault: string | undefined): Promise<{ path: string } | { error: string } | undefined> {
	const folder = vault ?? app.getPath("documents")
	const suffix = `.${planningFileExtension}`
	let name = `${defaultPlanningName}${suffix}`
	for (let number = 2; isFile(path.join(folder, name)) && number < 100; number++) {
		name = `${defaultPlanningName} ${number}${suffix}`
	}

	const options: Electron.SaveDialogOptions = {
		title: "Save global planning context",
		buttonLabel: "Save",
		defaultPath: path.join(folder, name),
		filters: [{ name: "PLAINTORCH Planning Graph", extensions: [planningFileExtension] }],
		properties: ["showOverwriteConfirmation", "createDirectory"]
	}
	const result = owner ? await dialog.showSaveDialog(owner, options) : await dialog.showSaveDialog(options)
	if (result.canceled || !result.filePath) {
		return undefined
	}

	if (result.filePath.toLowerCase().endsWith(suffix)) {
		return { path: result.filePath }
	}

	const extended = `${result.filePath}${suffix}`
	return isFile(extended)
		? { error: `A file named "${path.basename(extended)}" already exists.` }
		: { path: extended }
}

/** A path as the user knows it: vault-relative, forward-slashed, inside the served vault; absolute anywhere else. */
function displayPath(file: string, vault: string | undefined): string {
	if (vault) {
		const inside = path.relative(vault, file)
		if (inside && inside !== ".." && !inside.startsWith(`..${path.sep}`) && !path.isAbsolute(inside)) {
			return inside.split(path.sep).join("/")
		}
	}

	return file
}

/** The file's text without a byte-order mark, which would fail it as JSON; undefined when it cannot be read. */
function readText(file: string): string | undefined {
	try {
		return readFileSync(file, "utf8").replace(/^﻿/, "")
	}
	catch {
		return undefined
	}
}

function isFile(file: string): boolean {
	try {
		return statSync(file).isFile()
	}
	catch {
		return false
	}
}
