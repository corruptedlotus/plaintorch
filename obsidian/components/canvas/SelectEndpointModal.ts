import { createDeferredExecutor, DeferredPromiseExecutor } from '@open-draft/deferred-promise'
import { DependencyEndpointKind, type EndpointHit } from '@pleiades/sdk'
import { SuggestModal } from 'obsidian'
import { core, getApp, type IconName } from '..'

/** The type icon each endpoint kind is drawn with, matching the canvas nodes. */
const kindIcons: Record<DependencyEndpointKind, IconName> = {
	[DependencyEndpointKind.Directive]: 'directive',
	[DependencyEndpointKind.Objective]: 'objective',
	[DependencyEndpointKind.Fate]: 'eventive',
	[DependencyEndpointKind.Eventive]: 'eventive',
	[DependencyEndpointKind.Checkpoint]: 'checkpoint'
}

const kindLabels: Record<DependencyEndpointKind, string> = {
	[DependencyEndpointKind.Directive]: 'Directive',
	[DependencyEndpointKind.Objective]: 'Objective',
	[DependencyEndpointKind.Fate]: 'Fate',
	[DependencyEndpointKind.Eventive]: 'Eventive',
	[DependencyEndpointKind.Checkpoint]: 'Checkpoint'
}

/**
 * Asks for an endpoint to bring onto a global context — a directive, objective, or fate, whatever kind.
 *
 * Backed by the cross-kind endpoint search rather than a single listing, since a global context draws from
 * anywhere in the backlog; the type icon on each row is what tells the kinds apart, the same distinction the
 * global nodes themselves draw. An empty query opens on a bounded slice so the modal is useful before typing.
 */
export class SelectEndpointModal extends SuggestModal<EndpointHit> {
	private dpe?: DeferredPromiseExecutor<EndpointHit | undefined>
	private excluded: ReadonlySet<string> = new Set()

	/** Prompts for an endpoint, resolving to nothing when dismissed. `excluded` holds the `kind:id` keys already pinned. */
	public static prompt(excluded: ReadonlySet<string> = new Set()): Promise<EndpointHit | undefined> {
		const modal = new SelectEndpointModal(getApp())
		modal.excluded = excluded
		modal.dpe = createDeferredExecutor()
		modal.setPlaceholder('Search directives, objectives, fates, checkpoints…')
		modal.open()
		return new Promise(modal.dpe)
	}

	override async getSuggestions(query: string): Promise<EndpointHit[]> {
		const found = await core.dependencies.searchEndpoints(query.trim() || undefined)
		return found.filter(hit => !this.excluded.has(`${DependencyEndpointKind[hit.kind]}:${hit.id}`))
	}

	override renderSuggestion(hit: EndpointHit, el: HTMLElement) {
		const item = el.createEl('p7t-icon-item')
		item.data = hit
		item.icon = kindIcons[hit.kind]
		// The kind icon tells them apart at a glance; the label follows for the kinds that share an icon.
		item.text = `${hit.title} · ${kindLabels[hit.kind]}`
	}

	override async onClose() {
		// The suggestion handler runs after the close, so resolving has to lose the race deliberately.
		await sleep(500)
		this.dpe?.resolve(undefined)
	}

	override onChooseSuggestion(hit: EndpointHit, _: MouseEvent | KeyboardEvent) {
		this.dpe?.resolve(hit)
	}
}
