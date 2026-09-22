import { App, PluginSettingTab, Setting } from "obsidian"
import type { PreferenceView } from "@pleiades/sdk"
import type PlaintorchObsidianPlugin from "../main"

type CoreClient = Awaited<ReturnType<PlaintorchObsidianPlugin["getCoreClient"]>>

/**
 * The PLAINTORCH settings tab (PEP116). It renders whatever the core's preference catalog reports — a control per
 * preference, grouped by section — so a new preference appears here automatically with no plugin change. Each
 * control writes straight through to the core; the reset button clears the override back to its default.
 */
export class PreferenceSettingTab extends PluginSettingTab {
	public constructor(app: App, private readonly plugin: PlaintorchObsidianPlugin) {
		super(app, plugin)
	}

	public override display(): void {
		this.containerEl.empty()
		this.containerEl.createEl("p", { text: "Loading preferences…" })
		void this.render()
	}

	private async render(): Promise<void> {
		let core: CoreClient
		let preferences: PreferenceView[]
		try {
			core = await this.plugin.getCoreClient()
			preferences = await core.preferences.list()
		}
		catch {
			this.containerEl.empty()
			this.containerEl.createEl("p", { text: "Preferences are unavailable — is the PLAINTORCH core running?" })
			return
		}

		this.containerEl.empty()
		if (preferences.length === 0) {
			this.containerEl.createEl("p", { text: "No preferences are available yet." })
			return
		}

		let lastGroup: string | undefined
		for (const preference of preferences) {
			if (preference.group !== lastGroup) {
				new Setting(this.containerEl).setName(preference.group).setHeading()
				lastGroup = preference.group
			}

			this.renderPreference(core, preference)
		}
	}

	private renderPreference(core: CoreClient, preference: PreferenceView): void {
		const setting = new Setting(this.containerEl).setName(preference.label)
		if (preference.description) {
			setting.setDesc(preference.description)
		}

		switch (preference.kind) {
			case "Boolean":
				setting.addToggle(toggle => toggle
					.setValue(Boolean(preference.value))
					.onChange(value => { void core.preferences.set(preference.key, value) }))
				break
			case "Enum":
				setting.addDropdown(dropdown => {
					for (const option of preference.options ?? []) {
						dropdown.addOption(option, option)
					}

					dropdown
						.setValue(String(preference.value))
						.onChange(value => { void core.preferences.set(preference.key, value) })
				})
				break
			case "Integer":
				setting.addText(text => {
					text.inputEl.type = "number"
					text
						.setValue(String(preference.value))
						.onChange(raw => {
							const parsed = Number.parseInt(raw, 10)
							if (Number.isInteger(parsed)) {
								void core.preferences.set(preference.key, parsed)
							}
						})
				})
				break
			default:
				setting.addText(text => text
					.setValue(String(preference.value))
					.onChange(value => { void core.preferences.set(preference.key, value) }))
				break
		}

		setting.addExtraButton(button => button
			.setIcon("rotate-ccw")
			.setTooltip("Reset to default")
			.onClick(async () => {
				await core.preferences.reset(preference.key)
				this.display()
			}))
	}
}
