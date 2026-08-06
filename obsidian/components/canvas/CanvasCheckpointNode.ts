import { component, css, html, property } from '@a11d/lit'
import { IconName } from '..'
import { CanvasNodeItem } from './CanvasNodeItem'
import { Checkpoint } from '@pleiades/sdk'

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

			/*
			 * A checkpoint has no card to tint, so its state colours the glyph and its label instead. Blocked
			 * borrows the same severe colour a blocked entity wears — unmet incoming dependencies. Raced borrows
			 * the softer one — dependencies met, but a toll or an external condition still owed.
			 */
			:host([lock='blocked']) .grid {
				color: color-mix(in srgb, var(--text-error, crimson) 80%, var(--text-normal));
			}

			:host([lock='raced']) .grid {
				color: color-mix(in srgb, var(--text-warning, goldenrod) 80%, var(--text-normal));
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

				.notch p7t-icon {
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

			.toplane {
				position: absolute;
				top: -1.6em;
				font-size: .8em;
				
				&:has(.celestron) {
					border-radius: 4em;
					padding: .16em .4em;
					background-color: color-mix(in srgb, currentColor 80%, transparent);
				}

				&:has(.celestron.paid) {
					background-color: color-mix(in srgb, var(--background-modifier-message) 20%, transparent);
				}
			}

			.celestron {
				display: flex;
				align-items: center;
				gap: .1em;
				color: var(--background-modifier-message);

				&.paid {
					color: var(--text-normal);
					opacity: .3;
				}
			}
		`
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon=${this.milestone ? 'milestone' : 'checkpoint' as IconName}></p7t-icon>`
	}

	protected override get preTitle() {
		const checkpoint = this.entity as Checkpoint
		return !checkpoint.celestronToll ? html`` : html`
			<span class='celestron ${!checkpoint.tollPaid ? '' : 'paid'}'>
				<p7t-icon icon='${!checkpoint.tollPaid ? 'starfire' : 'state-done'}'></p7t-icon>
				${checkpoint.celestronToll}
			</span>
		`
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
