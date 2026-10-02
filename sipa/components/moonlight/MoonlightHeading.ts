import { Component, component, css, html, nothing, property, type PropertyValues } from '@a11d/lit'
import type { Directive } from '@pleiades/sdk'
import {
	ContextMenuController, EntityWatch, LunarDirectiveModal, ReactiveBinder, SelectStatusModal,
	entityContextMenu, lunarDirectiveStatusDescriptors
} from '..'
import { directiveActions, entityStatusName, isInactiveEntity, saveEntityField } from '../grid/entityActions'

/** How prominently a {@link MoonlightHeading} reads: as a card's own heading, a section's, or a box's. */
export type MoonlightHeadingLevel = 'card' | 'section' | 'box'

/**
 * The heading of one lunar directive on the Moonlight page: its crest, its editable title and state, a button that
 * opens its full banner, and — where the layout around it offers none of its own — its add actions.
 *
 * The card, its sections and the boxes inside them all head their directive with this, differing only in how
 * prominently it reads ({@link level}). An edit writes straight into the canonical directive, so every other surface
 * showing it follows before the write is even sent; a right-click raises the directive's context menu.
 */
@component('p7t-moonlight-heading')
export class MoonlightHeading extends Component {
	@property({ type: Object }) directive?: Directive
	@property({ reflect: true }) level: MoonlightHeadingLevel = 'section'
	/** Whether the heading offers to add beneath its directive. A card leaves it off: its add section offers that. */
	@property({ type: Boolean }) addable = false

	/** The directive arrives from the page's listing and is canonical; observing it follows edits made elsewhere. */
	protected readonly watch = new EntityWatch(this, () => this.directive)

	protected readonly binder = new ReactiveBinder<Directive>(this, 'directive', {
		sourceUpdated: async (_, keyPath) => {
			const directive = this.directive
			if (!directive || (keyPath !== 'title' && keyPath !== 'status')) {
				return
			}

			this.watch.publish()
			await saveEntityField(directive, keyPath)
		}
	})

	protected readonly contextMenu = new ContextMenuController(this, () =>
		this.directive ? entityContextMenu(this.directive) : undefined)

	static override get styles() {
		return css`
			:host {
				--p7t-moonlight-crest-size: 1.7em;
				display: flex;
				align-items: center;
				gap: .55em;
				min-width: 0;
				font-family: var(--font-interface);
			}

			:host([level='card']) {
				--p7t-moonlight-crest-size: 2.8em;
				gap: .7em;
			}

			:host([level='box']) {
				--p7t-moonlight-crest-size: 1.9em;
				align-items: flex-start;
			}

			.crest {
				flex: 0 0 auto;
				width: var(--p7t-moonlight-crest-size);
				height: var(--p7t-moonlight-crest-size);
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 55%, var(--text-normal));
			}

			.text {
				flex: 1;
				display: flex;
				flex-direction: column;
				align-items: flex-start;
				gap: .15em;
				min-width: 0;
			}

			.title {
				max-width: 100%;
				overflow: hidden;
				text-overflow: ellipsis;
				font-size: 1.12em;
				font-weight: 350;
				line-height: 1.15;
			}

			:host([level='card']) .title {
				font-size: 1.6em;
				font-weight: 250;
			}

			/* A box is narrow, so its title may take a second line before it gives up. */
			:host([level='box']) .title {
				display: -webkit-box;
				-webkit-box-orient: vertical;
				-webkit-line-clamp: 2;
				white-space: normal;
				font-size: 1.02em;
			}

			p7t-status-item {
				color: var(--text-muted);
				font-size: .85em;
			}

			/* The state icons are sized for a banner; a heading wants them at text scale. */
			p7t-status-item::part(icon) {
				width: 1.3em;
				height: 1.3em;
			}

			.actions {
				flex: 0 0 auto;
				display: flex;
				align-items: center;
				color: var(--text-muted);
				opacity: .6;
				transition: opacity .2s ease;
			}

			:host(:hover) .actions,
			.actions:focus-within {
				opacity: 1;
			}

			:host([level='box']) .actions {
				flex-direction: column;
				margin-block: -.2em;
			}

			/* A stale directive is dimmed like any item done with; its actions keep their strength. */
			:host([inactive]) :is(.crest, .text) {
				opacity: .4;
			}
		`
	}

	protected override updated(changed: PropertyValues<this>) {
		super.updated(changed)
		// Read off the watched directive each render rather than set as a property, so a state edited anywhere shows here.
		this.toggleAttribute('inactive', isInactiveEntity(this.directive))
	}

	protected override get template() {
		const directive = this.directive
		if (!directive) {
			return html``
		}

		return html`
			<p7t-media class='crest' icon .media=${directive.iconMedia} default='directive-lunar'></p7t-media>
			<div class='text'>
				<p7t-editable-plaintext required class='title' label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
				<p7t-editable .doEdit=${() => SelectStatusModal.prompt(lunarDirectiveStatusDescriptors)} ${this.binder.bind('status')}>
					<p7t-status-item small .status=${entityStatusName(directive)}></p7t-status-item>
				</p7t-editable>
			</div>
			<div class='actions'>
				<p7t-button ghost icon='lucide:pencil' label='Edit' @click=${() => this.edit()}></p7t-button>
				${!this.addable ? nothing : html`
					<p7t-expanding-actions .actions=${directiveActions(directive)} actionLabel='Add'></p7t-expanding-actions>
				`}
			</div>
		`
	}

	/** Opens the directive's full banner, where everything else about it — its timeframes among them — is edited. */
	private edit() {
		if (this.directive) {
			new LunarDirectiveModal(this.directive).open()
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-moonlight-heading': MoonlightHeading
	}
}
