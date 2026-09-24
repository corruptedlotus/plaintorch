import { component, css, html, nothing, state } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { Decree, DecreeStatus, DecreeUpdate, PolarisCycle } from '@pleiades/sdk'
import { Notice } from "obsidian"
import { core, IconName, followRenamedNote, ReactiveBinder, SelectDecreeStatusModal } from ".."

/**
 * Banner for a Decree declarative (PEP100). Decrees are enduring routines: the banner shows
 * their Orbit definition above the actions, exposes the two decree-appropriate actions
 * (add to Polaris, and the per-run Celestron reward), and — only inside a Moonlight (lunar)
 * hierarchy — a toggleable Lunar Reflection row.
 */
@component('p7t-decree-banner')
export class DecreeBanner extends EntityBanner<Decree> {
	// TODO(icons): no dedicated 'decree' icon exists yet; 'everglow' stands in for the enduring/law-like nature.
	override icon: IconName = 'decree'

	@state() activePolaris?: PolarisCycle

	protected binder = new ReactiveBinder<Decree>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity!
			const update: DecreeUpdate = {}
			switch (keyPath) {
				case 'status':
					update.status = entity.status
					break
				case 'orbit':
					// Empty string clears the schedule server-side; undefined would be a no-op.
					update.orbit = entity.orbit ?? ''
					break
				case 'activeCelestron':
					update.activeCelestron = entity.activeCelestron
					break
				case 'reflect':
					update.reflect = entity.reflect
					break
				case 'title':
					update.title = entity.title
					break
				default:
					return
			}

			const saved = await this.commitEntityEdit(async () => await core.declaratives.updateDecree(entity.id, update))
			if (!saved) {
				return
			}

			if (keyPath === 'title') {
				await followRenamedNote(entity.id)
			}
		}
	})

	protected override readonly entityTypeName = 'Decree' as const

	protected override async loadRelated() {
		this.activePolaris = await core.polaris.getCurrent()
	}

	/** A decree participates in Moonlight reflection only when its directive is lunar (PEP100). */
	protected get isLunarHierarchy() {
		return this.entity!.directive?.isLunar ?? false
	}

	protected get isInActivePolaris() {
		if (!this.activePolaris || !this.entity) return false
		// A decree in a cycle is a decree-backed executive now (PEP111), so membership is read off the executives.
		return this.activePolaris.executives.some(executive => executive.incentiveId === this.entity!.id)
	}

	addToPolaris = async () => {
		if (this.isInActivePolaris) return
		const decreeId = this.entity!.id
		const executive = await core.repos.decrees.mutate(decreeId, async () =>
			await core.polaris.addDecreeExecutive({ decreeId }))
		if (executive) {
			new Notice('Added to active Polaris cycle.')
			this.activePolaris = await core.polaris.getCurrent()
		}
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding-inline: 1.2em;
			}

			:host::part(sub-heading) {
				font-weight: 300;
				font-size: .9em;
				margin-top: -.2em;
				opacity: 1;
			}

			p7t-status-item::part(icon) {
				height: 1.4em;
			}

			.switcher {
				font-size: .7em;
				opacity: .6;
				line-height: .9;
			}

			.marker-icon {
				width: 1.4em;
				height: 1.4em;
			}

			.reflection p7t-icon-item {
				font-weight: 300;
			}

			.schedule {
				display: flex;
				align-items: center;
				gap: .5em;
				font-weight: 300;
				opacity: .85;
			}

			.per-run {
				display: flex;
				flex-direction: column;
				align-items: center;
				line-height: 1;
			}
		`
	}

	protected override get preHeadingTemplate() {
		return this.isLunarHierarchy ? html`
			<span>Moonlight Decree</span>
		` : html`
			<span>Pleiades Decree</span>
		`
	}

	protected override get secondary() {
		return html`
			<p7t-directive-item .directive=${this.entity!.directive}></p7t-directive-item>
			${!this.isLunarHierarchy ? nothing : this.reflectionRow}
		`
	}

	/** Editable Lunar Reflection toggle; shown only inside a lunar hierarchy. */
	protected get reflectionRow() {
		const reflected = this.entity!.reflect
		return html`
			<p7t-editable
				class='reflection'
				.doEdit=${(current?: boolean) => Promise.resolve(!current)}
				${this.binder.bind('reflect')}>
				<p7t-icon-item
					.icon=${reflected ? 'reflective' : ('attentive' as IconName)}>
					${reflected ? 'Lunar Reflection Enabled' : 'Not Reflected'}
				</p7t-icon-item>
			</p7t-editable>
		`
	}

	protected override get info() {
		return html`
			<div class='schedule'>
				<p7t-editable-orbit ${this.binder.bind('orbit')}></p7t-editable-orbit>
			</div>
		`
	}

	protected override get actions() {
		const inPolaris = this.isInActivePolaris
		return html`
			<div class='per-run'>
				<p7t-editable-starfire ${this.binder.bind('activeCelestron')}></p7t-editable-starfire>
				<span class='switcher'>per run</span>
			</div>
			<p7t-button ?disabled=${inPolaris} large icon='polaris' @click=${() => this.addToPolaris()}>
				${!inPolaris ? html`<span>Add to Polaris</span>` : html`
					<p7t-icon class='marker-icon' icon='lucide:check'></p7t-icon>
				`}
			</p7t-button>
		`
	}

	protected override get headingTemplate() {
		return html`
			<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}

	protected override get subHeadingTemplate() {
		return html`
			<p7t-editable .doEdit=${SelectDecreeStatusModal.prompt} ${this.binder.bind('status')}>
				<p7t-status-item
					.status=${DecreeStatus[this.entity!.status] as keyof typeof DecreeStatus}>
				</p7t-status-item>
			</p7t-editable>
		`
	}
}

declare global {
	interface HTMLTagNameMap {
		'p7t-decree-banner': DecreeBanner
	}
}
