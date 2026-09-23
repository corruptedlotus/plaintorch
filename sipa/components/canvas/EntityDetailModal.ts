import { DependencyEndpointKind } from '@pleiades/sdk'
import { App, Modal } from 'obsidian'
import type { CanvasEntity } from './graphModel'

/** The banner element each endpoint kind is viewed through; the generic banner covers the rest. */
const bannerTagByKind: Partial<Record<DependencyEndpointKind, string>> = {
	[DependencyEndpointKind.Objective]: 'p7t-objective-banner',
	[DependencyEndpointKind.Directive]: 'p7t-sdirective-banner',
	[DependencyEndpointKind.Fate]: 'p7t-fate-banner',
	[DependencyEndpointKind.Checkpoint]: 'p7t-checkpoint-banner'
}

/** The banner properties this modal sets; every banner carries these, checkpoints add `milestone`. */
interface BannerElement extends HTMLElement {
	app?: App
	puck?: string
	entity?: CanvasEntity
	milestone?: boolean
}

export interface EntityDetailOptions {
	/** Whether this checkpoint is the sprint's milestone, so its banner reads as one. */
	readonly milestone?: boolean
}

/**
 * Views and edits one graph entity through the same banner it has everywhere else.
 *
 * The banner does the work: handed the entity's id it resolves and observes the canonical instance, so an
 * edit made here reaches every other surface at once, and edits made elsewhere reach it. The kind only picks
 * which banner — an objective's, a checkpoint's, a directive's — and the entity supplies a title to show
 * while the resolution settles.
 */
export class EntityDetailModal extends Modal {
	public constructor(
		app: App,
		private readonly kind: DependencyEndpointKind,
		private readonly entity: CanvasEntity,
		private readonly options: EntityDetailOptions = {}
	) {
		super(app)
	}

	public override onOpen(): void {
		this.titleEl.setText(`Editing ${this.entity.id}`)
		this.contentEl.addClass('plaintorch-root')

		const tag = bannerTagByKind[this.kind] ?? 'p7t-entity-banner'
		const banner = document.createElement(tag) as BannerElement
		banner.addClass('plaintorch-modal-content')
		banner.app = this.app
		// The provided entity shows immediately; the id resolves the canonical instance the banner then edits.
		banner.entity = this.entity
		banner.puck = this.entity.id
		if (this.kind === DependencyEndpointKind.Checkpoint) {
			banner.milestone = this.options.milestone === true
		}

		this.contentEl.appendChild(banner)
	}

	public override onClose(): void {
		this.contentEl.empty()
	}
}
