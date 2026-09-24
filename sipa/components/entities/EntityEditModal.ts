import { App, Modal } from 'obsidian'

/** The runtime type names an entity can be edited through a full banner here — the same set the delete path allows. */
const editableTypes = new Set(['Objective', 'StellarDirective', 'LunarDirective', 'Fate', 'Decree', 'LorePage'])

/** The full-banner properties this modal sets; it resolves and observes everything else from the PUCK. */
interface FullBannerElement extends HTMLElement {
	puck?: string
	xtype?: string
}

/**
 * Views and edits any entity through the full banner — the general form of the graph's {@link EntityDetailModal}, keyed
 * by the runtime type name rather than a graph endpoint kind so it serves an entity item or a grid row just as well as a
 * canvas node.
 *
 * `<p7t-full-banner>` does the work: handed the type and the entity's PUCK it asks that repository for the canonical
 * instance, resolves and observes it, and composes the banner, the entity actions, and any special editors the kind
 * carries. An edit made here reaches every other surface at once, and edits made elsewhere reach it.
 */
export class EntityEditModal extends Modal {
	public constructor(
		app: App,
		private readonly typeName: string,
		private readonly entity: { id: string, title: string }
	) {
		super(app)
	}

	/** Whether an entity of this type can be edited through a banner here. */
	static supports(typeName: string | undefined): boolean {
		return !!typeName && editableTypes.has(typeName)
	}

	/** A modal for an entity, or `undefined` when its type has no banner editor. */
	static forEntity(app: App, typeName: string | undefined, entity: { id: string, title: string }): EntityEditModal | undefined {
		return EntityEditModal.supports(typeName) ? new EntityEditModal(app, typeName!, entity) : undefined
	}

	public override onOpen(): void {
		this.titleEl.setText(this.entity.title)
		this.contentEl.addClass('plaintorch-root')

		const banner = document.createElement('p7t-full-banner') as FullBannerElement
		banner.addClass('plaintorch-modal-content')
		banner.xtype = this.typeName
		banner.puck = this.entity.id
		this.contentEl.appendChild(banner)
	}

	public override onClose(): void {
		this.contentEl.empty()
	}
}
