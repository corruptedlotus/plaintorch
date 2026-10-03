import { component, css, html, nothing, property } from '@a11d/lit'
import { EditablePart } from '../editing/EditableDataLink'
import { EntityWatch, moveEntity, parentIdOf, type GridEntity } from '..'
import './DirectiveBreadcrumb'

/**
 * An entity's place in the directive hierarchy, edited in place: the breadcrumb of the directive it sits under —
 * a directive's parent chain, an incentive's owning directive and its ancestors — with a move affordance beside it.
 *
 * Its face is a {@link DirectiveBreadcrumb} of {@link entity}'s parent, so it follows the entity as it is moved from
 * anywhere (the backlog grid's drag and drop, the "Move to…" row of its menu, this control). The crumbs stay what
 * they are elsewhere — a press on one opens that directive's note — which is why the {@link editButton} is on by
 * default: the way into editing is the small affordance tethered beside the chain, never a press on the chain.
 *
 * Editing is the same move the grid's drop performs ({@link moveEntity}): the directive picker opens over the
 * directives the entity may go under, led by the top level (or the world) when it is not there already, and the
 * choice is written through the entity's repository, so every surface drawing the hierarchy reshapes on its own. The
 * value is the id of the directive the entity now sits under (`null` at the top), published through `edit`/`change`
 * once the move has been written; a dismissed picker or a refused move changes nothing and publishes nothing.
 */
@component('p7t-editable-hierarchy')
export class EditableHierarchy extends EditablePart<string | null> {
	/** The entity whose place is shown and moved: a directive, or an incentive. */
	@property({ type: Object }) entity?: GridEntity

	/** What the chain reads as when the entity sits under no directive (a world quest, a constellation directive). */
	@property() placeholder?: string

	/** On by default: the crumbs are links, so the content must stay pressable without starting an edit. */
	@property({ type: Boolean, reflect: true }) override editButton = true

	/**
	 * The entity arrives from whichever banner or item holds it, and that instance is canonical; observing it is what
	 * redraws the chain when the entity is moved from another surface.
	 */
	protected readonly watch = new EntityWatch(this, () => this.entity)

	/** The directive the chain starts from: the entity's current parent, or the last committed one without an entity. */
	private get rootId(): string | undefined {
		return this.entity ? parentIdOf(this.entity) : this.value ?? undefined
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				justify-content: flex-start;
			}
		`
	}

	/**
	 * Asks where to move the entity and moves it there. Resolves to the new parent's id (`null` for the top level) once
	 * the move is written; rejects — which the base reads as a cancel — when the picker is dismissed, the move is
	 * refused, or there is no entity to move.
	 */
	override doEdit = async (): Promise<string | null> => {
		const entity = this.entity
		const parent = entity ? await moveEntity(entity) : undefined
		if (parent === undefined) {
			throw new Error('The move was cancelled.')
		}

		return parent?.id ?? null
	}

	/** The move affordance, in place of the base's pencil. */
	protected override get editButtonTemplate() {
		return !this.editButton || this.disabled || this.active
			? nothing
			: this.affordanceTemplate('edit', 'lucide:folder-symlink', 'Move to…', () => this.startEditing())
	}

	protected override get contentTemplate() {
		return html`<p7t-directive-breadcrumb .rootId=${this.rootId} .placeholder=${this.placeholder}></p7t-directive-breadcrumb>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-hierarchy': EditableHierarchy
	}
}
