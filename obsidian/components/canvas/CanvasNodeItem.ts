import { component, css, event, html, property } from '@a11d/lit'
import { DependencyEndpointKind, ObjectiveStatus } from '@pleiades/sdk'
import { EntityItem, IconName, statusDescriptors } from '..'
import type { CanvasEntity } from './graphModel'

/** What each endpoint kind is drawn with, matching the icons the grid and banners already use. */
const kindIcons: Record<DependencyEndpointKind, IconName> = {
	[DependencyEndpointKind.Directive]: 'directive',
	[DependencyEndpointKind.Objective]: 'objective',
	[DependencyEndpointKind.Fate]: 'eventive',
	[DependencyEndpointKind.Eventive]: 'eventive',
	[DependencyEndpointKind.Checkpoint]: 'lucide:milestone'
}

const kindLabels: Record<DependencyEndpointKind, string> = {
	[DependencyEndpointKind.Directive]: 'Directive',
	[DependencyEndpointKind.Objective]: 'Objective',
	[DependencyEndpointKind.Fate]: 'Fate',
	[DependencyEndpointKind.Eventive]: 'Occurrence',
	[DependencyEndpointKind.Checkpoint]: 'Checkpoint'
}

/** What a node reports when a gesture starts on it, in viewport coordinates the canvas can transform. */
export interface CanvasNodePointer {
	readonly nodeKey: string
	readonly pointerId: number
	readonly clientX: number
	readonly clientY: number
}

/**
 * One node of the dependency canvas.
 *
 * An entity item with the affordances a graph needs rather than a component of its own: the card, the notch,
 * the title and — through the base's own {@link EntityItem} watch — the reactivity that makes a node follow
 * an edit made in a banner or the grid, all come from the derivation. What is added here is what a canvas
 * requires and a list does not: the two connection handles an edge is drawn from, and the states a node can
 * be in while the graph is being edited around it.
 */
@component('p7t-canvas-node')
export class CanvasNodeItem extends EntityItem<CanvasEntity> {
	/** Identifies this node to the canvas that owns it. */
	@property() nodeKey = ''

	@property({ type: Number }) kind: DependencyEndpointKind = DependencyEndpointKind.Objective

	@property({ type: Boolean, reflect: true }) selected = false

	/** Whether an unsatisfied dependency is holding this entity back. */
	@property({ type: Boolean, reflect: true }) blocked = false

	/** Whether an edge is being drawn somewhere on the canvas, which is when a node becomes a drop target. */
	@property({ type: Boolean, reflect: true }) linking = false

	/** Asks the canvas to begin drawing an edge out of this node. */
	@event({ bubbles: true, composed: true }) requestLinkStart!: EventDispatcher<CanvasNodePointer>

	/** Asks the canvas for this node's menu. */
	@event({ bubbles: true, composed: true }) requestNodeMenu!: EventDispatcher<CanvasNodePointer>

	static override get styles() {
		return css`
			${super.styles}

			:host {
				width: var(--p7t-canvas-node-width, 15em);
				box-sizing: border-box;
				background-color: var(--background-primary, var(--background-secondary));
				border: 1px solid color-mix(in srgb, var(--text-normal) 18%, transparent);
				border-radius: 12px;
				cursor: grab;
				user-select: none;
				/* The canvas drives pan and node drag from pointer events, which touch scrolling would steal. */
				touch-action: none;
			}

			:host([selected]) {
				border-color: var(--p7t-flare-accent, var(--interactive-accent));
				box-shadow: 0 0 0 2px color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 35%, transparent);
			}

			:host([blocked]) {
				border-color: color-mix(in srgb, var(--text-error, crimson) 45%, transparent);
			}

			/*
			 * While an edge is being drawn every other node is a candidate for its far end, so the whole card
			 * becomes the drop target rather than the incoming handle alone — aiming at a dot is a worse
			 * gesture than aiming at the thing it belongs to.
			 */
			:host([linking]:hover) {
				border-color: var(--p7t-flare-accent, var(--interactive-accent));
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 12%, var(--background-primary));
			}

			.handle {
				position: absolute;
				top: 50%;
				width: .8em;
				height: .8em;
				box-sizing: border-box;
				border-radius: 50%;
				transform: translateY(-50%);
				background-color: color-mix(in srgb, var(--text-normal) 25%, var(--background-primary));
				border: 1px solid color-mix(in srgb, var(--text-normal) 30%, transparent);
				opacity: 0;
				transition: opacity .2s ease, background-color .2s ease;
				touch-action: none;
				z-index: 2;
			}

			.handle-in {
				inset-inline-start: -.4em;
			}

			.handle-out {
				inset-inline-end: -.4em;
				cursor: crosshair;
			}

			:host(:hover) .handle,
			:host([selected]) .handle,
			:host([linking]) .handle-in {
				opacity: 1;
			}

			.handle-out:hover {
				background-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.kind {
				display: flex;
				align-items: center;
				gap: 4px;
				opacity: .5;
				font-weight: 400;
				font-size: .9em;
				line-height: .9;
			}

			.blocked-badge {
				display: flex;
				align-items: center;
				gap: 3px;
				font-size: .85em;
				line-height: .9;
				color: color-mix(in srgb, var(--text-error, crimson) 80%, var(--text-normal));

				& p7t-icon {
					width: 1.1em;
					height: 1.1em;
				}
			}
		`
	}

	protected override get template() {
		return html`
			<div class='handle handle-in' part='handle-in'></div>
			${super.template}
			<div
				class='handle handle-out'
				part='handle-out'
				@pointerdown=${(e: PointerEvent) => this.onLinkStart(e)}>
			</div>
		`
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon=${this.notchIcon}></p7t-icon>`
	}

	protected override get preTitle() {
		return html`
			<div class='kind'>
				<span>${kindLabels[this.kind]}</span>
			</div>
		`
	}

	protected override get info() {
		return !this.blocked ? html`` : html`
			<div class='blocked-badge'>
				<p7t-icon icon='state-blocked'></p7t-icon>
				<span>Blocked</span>
			</div>
		`
	}

	/**
	 * The status icon for an objective, the kind icon for anything else.
	 *
	 * Only an objective carries a workflow the canvas can read off the entity behind an endpoint; a
	 * checkpoint has no lifecycle at all, and the rest are told apart by what they are.
	 */
	protected get notchIcon(): IconName {
		if (this.kind === DependencyEndpointKind.Objective) {
			const status = (this.entity as { status?: ObjectiveStatus }).status
			const name = status === undefined ? undefined : ObjectiveStatus[status] as keyof typeof statusDescriptors
			const icon = name === undefined ? undefined : statusDescriptors[name]?.icon
			if (icon) {
				return icon
			}
		}

		return kindIcons[this.kind]
	}

	private onLinkStart(e: PointerEvent) {
		// The handle owns this gesture outright: letting it reach the card would start a node drag underneath.
		e.preventDefault()
		e.stopPropagation()
		this.requestLinkStart.dispatch({
			nodeKey: this.nodeKey,
			pointerId: e.pointerId,
			clientX: e.clientX,
			clientY: e.clientY
		})
	}

	protected override async notchAction() {
		this.requestNodeMenu.dispatch({
			nodeKey: this.nodeKey,
			pointerId: -1,
			clientX: 0,
			clientY: 0
		})
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-canvas-node': CanvasNodeItem
	}
}
