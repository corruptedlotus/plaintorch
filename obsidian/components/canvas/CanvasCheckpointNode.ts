import { component, css, html, property } from '@a11d/lit'
import { IconName } from '..'
import { CanvasNodeItem } from './CanvasNodeItem'

/**
 * A checkpoint on the dependency canvas (PEP102).
 *
 * Its own component rather than a variant of the entity node, because a checkpoint is a different thing: it
 * has no lifecycle, no note, and no begin or finish — so no status, and an edge to or from it leaves the
 * corresponding trigger or constraint empty. The milestone, the one checkpoint that stands for the onrush's
 * completion, is flagged for a look that is meant to be taken much further later.
 */
@component('p7t-canvas-checkpoint')
export class CanvasCheckpointNode extends CanvasNodeItem {
	/** Whether this checkpoint is the milestone of the onrush being planned. */
	@property({ type: Boolean, reflect: true }) milestone = false

	static override get styles() {
		return css`
			${super.styles}

			:host {
				border-radius: 999px;
			}

			.kind {
				text-transform: uppercase;
				letter-spacing: .04em;
				font-size: .8em;
			}

			/* Deliberately understated — a hook to build the milestone's real treatment on, not the treatment. */
			:host([milestone]) {
				border-width: 2px;
				border-color: color-mix(in srgb, var(--p7t-accent-onrush, var(--interactive-accent)) 70%, transparent);
			}
		`
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon=${this.milestone ? 'lucide:flag' : 'lucide:milestone' as IconName}></p7t-icon>`
	}

	protected override get preTitle() {
		return html`<div class='kind'><span>${this.milestone ? 'Milestone' : 'Checkpoint'}</span></div>`
	}

	/** A checkpoint carries no dependency lock badge of its own — it only aggregates, it is not gated. */
	protected override get info() {
		return html``
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-canvas-checkpoint': CanvasCheckpointNode
	}
}
