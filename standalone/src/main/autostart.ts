import { copyFileSync, existsSync, mkdirSync, rmSync, writeFileSync } from "node:fs"
import { homedir } from "node:os"
import path from "node:path"
import { app } from "electron"

/**
 * Per-user start-at-login registration, which replaced the machine-wide Windows service the core used to generate.
 *
 * Windows and macOS go through Electron's login-item API (the Run key and the Login Items list respectively). Linux
 * has no such API, so an XDG autostart entry is written under `~/.config/autostart`. The shell launches with
 * `--hidden` from all three so login does not pop the status window.
 */
const hiddenArgument = "--hidden"

/** Where the autostart entry's icon is installed: the bundled mark is inside the app archive, out of a desktop's reach. */
function linuxIconPath(): string {
	const dataHome = process.env.XDG_DATA_HOME || path.join(homedir(), ".local", "share")
	return path.join(dataHome, "icons", "hicolor", "512x512", "apps", "plaintorch.png")
}

function linuxAutostartPath(): string {
	const configHome = process.env.XDG_CONFIG_HOME || path.join(homedir(), ".config")
	return path.join(configHome, "autostart", "plaintorch.desktop")
}

/** Whether the shell is currently registered to start at login. */
export function isAutostartEnabled(): boolean {
	if (process.platform === "linux") {
		return existsSync(linuxAutostartPath())
	}

	return app.getLoginItemSettings({ args: [hiddenArgument] }).openAtLogin
}

/** Registers or unregisters the shell to start at login. A development run is never registered. */
export function setAutostartEnabled(enabled: boolean): void {
	if (!app.isPackaged) {
		return
	}

	if (process.platform === "linux") {
		const entryPath = linuxAutostartPath()
		if (!enabled) {
			rmSync(entryPath, { force: true })
			return
		}

		mkdirSync(path.dirname(entryPath), { recursive: true })
		const iconPath = linuxIconPath()
		mkdirSync(path.dirname(iconPath), { recursive: true })
		copyFileSync(path.join(__dirname, "assets", "plaintorch-full.png"), iconPath)
		// Inside an AppImage the executable lives in a temporary mount; the AppImage file itself is what persists.
		const executable = process.env.APPIMAGE || process.execPath
		const entry = [
			"[Desktop Entry]",
			"Type=Application",
			"Name=PLAINTORCH",
			"Comment=PLAINTORCH background core",
			`Exec="${executable}" ${hiddenArgument}`,
			`Icon=${iconPath}`,
			"Terminal=false",
			"X-GNOME-Autostart-enabled=true",
			""
		].join("\n")
		writeFileSync(entryPath, entry, "utf8")
		return
	}

	app.setLoginItemSettings({
		openAtLogin: enabled,
		args: [hiddenArgument]
	})
}
