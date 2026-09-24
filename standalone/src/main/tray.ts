import path from "node:path"
import { Menu, Tray, nativeImage, nativeTheme, type MenuItemConstructorOptions } from "electron"
import type { ShellStatus } from "../shared/contracts"
import { hostsCore } from "./flavor"

export interface TrayActions {
	openBriefing(): void
	openStatus(): void
	activateVault(): void
	deactivateVault(): void
	setAutostart(enabled: boolean): void
	openLogs(): void
	restartCore(): void
	checkForUpdates(): void
	installUpdate(): void
	quit(): void
}

/**
 * The tray icon and its menu, rebuilt from every status so the phase line, the vault entries, and the update entry
 * always reflect the shell's state. Clicking the icon opens the briefing, the shell's main window.
 */
export class ShellTray {
	private tray?: Tray
	private destroyed = false
	private latest?: ShellStatus

	public constructor(private readonly actions: TrayActions) { }

	public getPath() {
		const iconName = nativeTheme.shouldUseDarkColors
			? 'plaintorch-mono-dark.png'
			: 'plaintorch-mono-light.png'
		const icon = nativeImage.createFromPath(path.join(__dirname, "assets", iconName))
		return icon
	}

	/** Creates the tray icon. */
	public create(): void {
		const icon = this.getPath()
		this.tray = new Tray(icon)
		this.tray.setToolTip("PLAINTORCH")
		this.tray.on("click", () => this.actions.openBriefing())
		this.tray.on("double-click", () => this.actions.openBriefing())
		if (this.latest) {
			this.update(this.latest)
		}
	}

	/** Follows a light/dark theme change; brings the tray back if the platform dropped it, but never after {@link destroy}. */
	public updateIcon() {
		if (this.destroyed) {
			return
		}

		if (this.tray && !this.tray.isDestroyed()) {
			const icon = this.getPath()
			this.tray.setImage(icon)
		} else {
			this.create()
		}
	}

	/** Rebuilds the menu and tooltip for a status. */
	public update(status: ShellStatus): void {
		this.latest = status
		if (!this.tray || this.tray.isDestroyed()) {
			return
		}

		this.tray.setToolTip(`PLAINTORCH: ${describe(status)}`)
		this.tray.setContextMenu(Menu.buildFromTemplate(this.template(status)))
	}

	/** Removes the tray icon. */
	public destroy(): void {
		this.destroyed = true
		this.tray?.destroy()
		this.tray = undefined
	}

	private template(status: ShellStatus): MenuItemConstructorOptions[] {
		const items: MenuItemConstructorOptions[] = [
			{ label: describe(status), enabled: false },
			{ label: status.vault ? `Vault: ${status.vault}` : "No vault active", enabled: false },
			{ type: "separator" },
			{ label: "Open briefing", click: () => this.actions.openBriefing() },
			{ label: "Open status", click: () => this.actions.openStatus() },
			{ label: "Activate vault...", click: () => this.actions.activateVault() },
			{ label: "Deactivate vault", enabled: !!status.activeVaultSetting, click: () => this.actions.deactivateVault() },
			{ type: "separator" },
			{ label: "Start at login", type: "checkbox", checked: status.autostart, click: item => this.actions.setAutostart(item.checked) },
			{ label: "Open logs folder", click: () => this.actions.openLogs() }
		]
		if (hostsCore) {
			items.push({ label: "Restart core", click: () => this.actions.restartCore() })
		}

		items.push({ type: "separator" }, this.updateItem(status), { type: "separator" }, { label: "Quit PLAINTORCH", click: () => this.actions.quit() })
		return items
	}

	private updateItem(status: ShellStatus): MenuItemConstructorOptions {
		const update = status.updateState
		switch (update.kind) {
			case "available":
				return { label: `Download update ${update.version}...`, click: () => this.actions.installUpdate() }
			case "ready":
				return { label: `Install update ${update.version} and restart`, click: () => this.actions.installUpdate() }
			case "downloading":
				return { label: `Downloading update (${update.percent}%)`, enabled: false }
			case "checking":
				return { label: "Checking for updates...", enabled: false }
			case "unavailable":
				return { label: "Check for updates...", enabled: false }
			default:
				return { label: "Check for updates...", click: () => this.actions.checkForUpdates() }
		}
	}
}

/** One line for the tooltip and the first menu entry. */
export function describe(status: ShellStatus): string {
	if (status.sweeping) {
		return status.attachment === "attached" ? "attached, startup sweep" : "startup sweep"
	}

	switch (status.attachment) {
		case "absent":
			return "no core running"
		case "exited":
			return "core exited"
		case "starting":
			return "core starting"
		case "attached":
			return `attached, ${status.phase.toLowerCase()}`
		default:
			return status.phase.toLowerCase()
	}
}
