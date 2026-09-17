import { component, Component, css, html, HTMLTemplateResult, nothing, property } from '@a11d/lit'
import type { MediaReference } from '@pleiades/sdk'
import { ContextMenuController, EntityWatch, getApp, resolveMediaIcon, type ContextMenuSpec } from '..'
import { entityContextMenu, openEntityEditor } from './entityMenu'
import { itemLayoutStyles } from './itemStyles'

@component('p7t-entity-item')
export class EntityItem<T extends { id: string, title: string }> extends Component {
	@property({ type: Object }) entity?: T
	@property({ type: Boolean, reflect: true }) interactive = false

	/**
	 * A context menu supplied by a host that owns it — the dependency canvas sets each node's menu, so its single
	 * inherited controller raises the canvas's own per-mode menu rather than the generic entity one. Left unset
	 * everywhere else, where the entity default applies. Plain (non-reactive) so a host re-supplying it every render
	 * never provokes a re-render; the controller only reads it on a right-click.
	 */
	menu?: ContextMenuSpec

	get disabled() { return false }

	/**
	 * The entity arrives as a property from whichever aggregate rendered this item, and that instance is
	 * canonical. Observing it is what makes a list row follow an edit made in a banner elsewhere.
	 */
	protected readonly watch = new EntityWatch(this, () => this.entity)

	/**
	 * Raises the entity's context menu on right-click — deletion, editing, opening its note, and more per kind. As a
	 * controller it needs no handler in the template; it withholds the menu (passing the event through) whenever
	 * {@link contextMenuSpec} returns nothing.
	 */
	protected readonly contextMenu = new ContextMenuController(this, () => this.contextMenuSpec())

	/**
	 * The context menu this item raises: a host-supplied {@link menu} when set, otherwise the entity's own menu.
	 * The single inherited controller reads this, so a host parametrizes the menu by setting {@link menu} rather
	 * than shadowing the controller with a second handler.
	 */
	protected contextMenuSpec(): ContextMenuSpec | undefined {
		return this.menu ?? (this.interactive && this.entity ? entityContextMenu(this.entity) : undefined)
	}

	/** The title click opens the entity's editing modal (its banner). Overridable — the canvas selects instead. */
	protected async titleAction() {
		if (!this.interactive || !this.entity) return
		openEntityEditor(this.entity)
	}

	static override get styles() {
		return css`
			${itemLayoutStyles}

			/* The compact one-liner: icon, title, chips — sized to sit inside a select or a dense list. */
			:host([compact]) {
				padding: .15em .3em;
				animation: none;
			}

			.compact {
				display: flex;
				align-items: center;
				gap: .6ch;
				min-width: 0;
				font-family: var(--font-interface);
			}

			.compact.disabled {
				opacity: .4;
			}

			.compact-icon {
				flex: 0 0 auto;
				width: 1.3em;
				height: 1.3em;
			}

			.compact-title {
				flex: 0 1 auto;
				min-width: 0;
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
				font-weight: 400;
			}

			.chips {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
				flex: 0 0 auto;
				font-size: .78em;
			}

			.chip,
			.chips > * {
				padding: .05em .55ch;
				border-radius: 4px;
				background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
				color: color-mix(in srgb, var(--text-normal) 75%, transparent);
				white-space: nowrap;
			}

			.extra-action {
				position: fixed;
				display: flex;
				align-items: center;
				justify-content: center;
				padding: .2em;
				position-anchor: --entity-item;
				/*position-area: inline-end span-all;*/
				top: anchor(top);
				bottom: anchor(bottom);
				left: anchor(right);
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, black);
				z-index: 30;
				interpolate-size: allow-keywords;
				transform: translateX(0);
				margin-block: .3em;
				margin-inline: -.4em;
				width: 2em;
				cursor: pointer;

				@starting-style {
					opacity: 0;
					transform: translateX(-3em);
				}

				:host(:not(:hover)) & {
					opacity: 0;
					display: none;
					transform: translateX(-3em);
				}

				& p7t-icon {
					width: 1.9em;
					height: 1.9em;
				}
			}
		`
	}

	/**
	 * The one-line form, for a dense list or a select's face: the parent directive's icon (its media, or the
	 * asteroid glyph for a world quest), the title, and a few small chips — the kind, and whatever
	 * {@link compactChips} adds (college, Celestron). No notch, no fly-out, no interaction of its own.
	 */
	@property({ type: Boolean, reflect: true }) compact = false

	/** The directive this entity belongs to, for the compact icon. Subclasses with one supply it. */
	protected get compactDirective(): { iconMedia?: MediaReference | undefined } | undefined {
		return undefined
	}

	/** The compact form's icon: the directive's media, or the asteroid for an entity outside any directive. */
	protected get compactIcon(): string {
		const directive = this.compactDirective
		return directive ? resolveMediaIcon(directive.iconMedia, getApp(), 'directive') : 'lucide:astroid'
	}

	/** What kind of entity this is, for the compact form's first chip. */
	protected get compactKind(): string {
		return 'Entity'
	}

	/** The chips after the kind chip in the compact form — a college, a Celestron. */
	protected get compactChips(): HTMLTemplateResult {
		return html``
	}

	protected get compactTemplate() {
		return html`
			<div class='compact ${this.disabled ? 'disabled' : ''}'>
				<p7t-icon class='compact-icon' .icon=${this.compactIcon}></p7t-icon>
				<span class='compact-title' @click=${() => this.titleAction()}>${this.entity?.title}</span>
				<span class='chips'>
					<span class='chip'>${this.compactKind}</span>
					${this.compactChips}
				</span>
			</div>
		`
	}

	protected override get template() {
		if (this.compact) {
			return this.compactTemplate
		}

		return html`
			<div class='grid ${this.disabled ? 'disabled' : ''}'>
				<div @click=${async () => await this.notchAction()} class='notch part'>${this.notchTemplate}</div>
				<div class='toplane'>
					${this.preTitle}
					<div class='filler'></div>
					${this.info}
				</div>
				<div class='title'>
					<span @click=${() => this.titleAction()}>${this.entity?.title}</span>
					${this.titleSuffix}
				</div>
			</div>
			${this.highlightInfo}
			${!this.extraActionTemplate ? nothing : html`
				<div @click=${async () => await this.extraAction()} class='extra-action part'>${this.extraActionTemplate}</div>
			`}
		`
	}

	protected get highlightInfo() : HTMLTemplateResult | undefined {
		return html``
	}

	protected get preTitle() {
		return html``
	}

	protected get titleSuffix() {
		return html``
	}

	protected get info() {
		return html``
	}

	protected get notchTemplate() {
		return html`
			<slot>
				<p7t-icon class='notch-icon' icon='state-active'></p7t-icon>
			</slot>
		`
	}

	protected get extraActionTemplate() : HTMLTemplateResult | undefined {
		return undefined
	}

	protected async extraAction() { }

	protected async notchAction() { }
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-item': EntityItem<unknown & { id: string, title: string }>
	}
}