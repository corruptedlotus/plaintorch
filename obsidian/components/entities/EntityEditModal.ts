import { App, Modal } from 'obsidian'

/** The banner element each entity is edited through, keyed by the runtime type name the core stamps. */
const bannerTagByType: Record<string, string> = {
	'Objective': 'p7t-objective-banner',
	'StellarDirective': 'p7t-sdirective-banner',
	'LunarDirective': 'p7t-ldirective-banner',
	'Fate': 'p7t-fate-banner',
	'Decree': 'p7t-decree-banner',
	'LorePage': 'p7t-lore-banner'
}

/** The banner properties this modal sets; every banner resolves and observes from `puck`. */
interface BannerElement extends HTMLElement {
	app?: App
	puck?: string
	entity?: { id: string, title: string }
}

/**
 * Views and edits any entity through the same banner it uses everywhere else — the general form of the graph's
 * {@link EntityDetailModal}, keyed by the runtime type name rather than a graph endpoint kind so it serves an entity
 * item or a grid row just as well as a canvas node.
 *
 * The banner does the work: handed the entity's id it resolves and observes the canonical instance, so an edit made
 * here reaches every other surface at once, and edits made elsewhere reach it. The type only picks which banner; the
 * entity supplies a title to show while the resolution settles.
 */
export class EntityEditModal extends Modal {
	public constructor(
		app: App,
		private readonly bannerTag: string,
		private readonly entity: { id: string, title: string }
	) {
		super(app)
	}

	/** Whether an entity of this type can be edited through a banner here. */
	static supports(typeName: string | undefined): boolean {
		return !!typeName && typeName in bannerTagByType
	}

	/** A modal for an entity, or `undefined` when its type has no banner editor. */
	static forEntity(app: App, typeName: string | undefined, entity: { id: string, title: string }): EntityEditModal | undefined {
		const tag = typeName ? bannerTagByType[typeName] : undefined
		return tag ? new EntityEditModal(app, tag, entity) : undefined
	}

	public override onOpen(): void {
		this.titleEl.setText(this.entity.title)
		this.contentEl.addClass('plaintorch-root')

		const banner = document.createElement(this.bannerTag) as BannerElement
		banner.addClass('plaintorch-modal-content')
		banner.app = this.app
		// The provided entity shows immediately; the id resolves the canonical instance the banner then edits.
		banner.entity = this.entity
		banner.puck = this.entity.id
		this.contentEl.appendChild(banner)
	}

	public override onClose(): void {
		this.contentEl.empty()
	}
}
