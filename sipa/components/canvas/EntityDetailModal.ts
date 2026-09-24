import { DependencyEndpointKind } from '@pleiades/sdk'
import { ModalBase } from '../../host'
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
export class EntityDetailModal extends ModalBase {
	public constructor(
		private readonly kind: DependencyEndpointKind,
		private readonly entity: CanvasEntity,
		private readonly options: EntityDetailOptions = {}
	) {
		super()
	}

	public override onOpen(): void {
		this.setTitle(`Editing ${this.entity.id}`)

		const tag = bannerTagByKind[this.kind] ?? 'p7t-entity-banner'
		const banner = document.createElement(tag) as BannerElement
		banner.classList.add('plaintorch-modal-content')
		// The provided entity shows immediately; the id resolves the canonical instance the banner then edits.
		banner.entity = this.entity
		banner.puck = this.entity.id
		if (this.kind === DependencyEndpointKind.Checkpoint) {
			banner.milestone = this.options.milestone === true
		}

		this.contentEl.appendChild(banner)
	}
}
