import { Component, component, css, html, state } from "@a11d/lit"
import { core } from ".."
import type { PreferenceView } from "@pleiades/sdk"

/**
 * The settings panel (PEP116). It renders whatever the core's preference catalog reports — a control per
 * preference, grouped by section — so a new preference appears here automatically with no client change. Each
 * control writes straight through to the core; the reset button clears the override back to its default. Hosted
 * inside {@link PreferenceModal}, opened from the briefing. A committed write also refreshes the shared preference
 * record every other surface reads (the preferred calendar an orbit is read on) at once, rather than waiting for the
 * change feed's echo.
 */
@component('p7t-preferences')
export class PreferencesPanel extends Component {
	@state() private preferences?: PreferenceView[]
	@state() private failed = false

	override connectedCallback() {
		super.connectedCallback()
		void this.load()
	}

	private async load() {
		try {
			this.preferences = await core.preferences.list()
		}
		catch {
			this.failed = true
		}
	}

	private async set(preference: PreferenceView, value: number | boolean | string) {
		const updated = await core.preferences.set(preference.key, value)
		if (updated) {
			this.preferences = this.preferences?.map(item => item.key === preference.key ? updated : item)
			void core.repos.preferences.revalidateIfObserved()
		}
	}

	private async reset(preference: PreferenceView) {
		if (await core.preferences.reset(preference.key)) {
			this.preferences = this.preferences?.map(item =>
				item.key === preference.key ? { ...item, value: item.default } : item)
			void core.repos.preferences.revalidateIfObserved()
		}
	}

	static override get styles() {
		return css`
			:host {
				display: block;
				min-width: min(28rem, 80vw);
			}

			.message {
				opacity: .7;
				padding: 1em 0;
			}

			.group {
				margin-top: .4em;
				margin-bottom: .2em;
				font-size: .8em;
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: .05em;
				color: var(--text-accent);
			}

			.row {
				display: flex;
				align-items: center;
				gap: 1em;
				padding: .7em 0;
				border-top: 1px solid var(--background-modifier-border);
			}

			.text {
				flex: 1;
				min-width: 0;
			}

			.label {
				font-weight: 500;
			}

			.desc {
				font-size: .85em;
				opacity: .7;
				margin-top: .15em;
			}

			.control {
				flex: 0 0 auto;
				display: flex;
				align-items: center;
				gap: .5em;
			}

			input[type='number'],
			select {
				font-family: var(--font-interface);
				color: var(--text-normal);
				background: var(--background-modifier-form-field);
				border: 1px solid var(--background-modifier-border);
				border-radius: 5px;
				padding: .3em .5em;
			}

			input[type='number'] {
				width: 6em;
			}

			input[type='checkbox'] {
				width: 1.1em;
				height: 1.1em;
				accent-color: var(--interactive-accent);
			}

			.reset {
				width: 1.1em;
				height: 1.1em;
				opacity: .4;
				cursor: pointer;
				transition: opacity .2s ease;
			}

			.reset:hover {
				opacity: 1;
			}
		`
	}

	override get template() {
		if (this.failed) {
			return html`<p class='message'>Preferences are unavailable — is the PLAINTORCH core running?</p>`
		}

		if (!this.preferences) {
			return html`<p class='message'>Loading…</p>`
		}

		if (this.preferences.length === 0) {
			return html`<p class='message'>No preferences are available yet.</p>`
		}

		let lastGroup: string | undefined
		return html`${this.preferences.map(preference => {
			const heading = preference.group !== lastGroup
				? html`<div class='group'>${preference.group}</div>`
				: html``
			lastGroup = preference.group
			return html`${heading}${this.renderRow(preference)}`
		})}`
	}

	private renderRow(preference: PreferenceView) {
		return html`
			<div class='row'>
				<div class='text'>
					<div class='label'>${preference.label}</div>
					${preference.description ? html`<div class='desc'>${preference.description}</div>` : html``}
				</div>
				<div class='control'>
					${this.renderControl(preference)}
					<p7t-icon
						class='reset'
						icon='lucide:rotate-ccw'
						title='Reset to default'
						@click=${() => void this.reset(preference)}>
					</p7t-icon>
				</div>
			</div>
		`
	}

	private renderControl(preference: PreferenceView) {
		switch (preference.kind) {
			case 'Boolean':
				return html`<input
					type='checkbox'
					.checked=${Boolean(preference.value)}
					@change=${(e: Event) => void this.set(preference, (e.target as HTMLInputElement).checked)}>`
			case 'Enum':
				return html`<select
					@change=${(e: Event) => void this.set(preference, (e.target as HTMLSelectElement).value)}>
					${(preference.options ?? []).map(option => html`
						<option value=${option} ?selected=${option === preference.value}>${option}</option>
					`)}
				</select>`
			case 'Integer':
				return html`<input
					type='number'
					.value=${String(preference.value)}
					@change=${(e: Event) => {
						const parsed = Number.parseInt((e.target as HTMLInputElement).value, 10)
						if (Number.isInteger(parsed)) {
							void this.set(preference, parsed)
						}
					}}>`
			default:
				return html`<input
					type='text'
					.value=${String(preference.value)}
					@change=${(e: Event) => void this.set(preference, (e.target as HTMLInputElement).value)}>`
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-preferences': PreferencesPanel
	}
}
