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
				width: auto;
				padding: 0;
				border: none;
				background: none;
			}

			.kind {
				text-transform: uppercase;
				letter-spacing: .04em;
				font-size: .7em;
			}

			.grid {
				display: flex;
				flex-direction: column;
				align-items: center;
				gap: .2em;
				text-align: center;

				p7t-icon {
					width: 3.6em;
					height: 3.6em;
				}
			}

			.title {
				position: absolute;
				font-weight: 400;
				white-space: nowrap;
				bottom: -1.6em;
				background-color: 
					color-mix(in srgb, var(--background-modifier-message) 90%, transparent);
				padding: .2em .5em;
				border-radius: 4em;
				font-size: 1.1em;
				line-height: 1.1em;
			}
		`
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon=${this.milestone ? 'milestone' : 'checkpoint' as IconName}></p7t-icon>`
	}

	protected override get preTitle() {
		return html``
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
