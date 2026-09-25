import { css } from "@a11d/lit"

/**
 * The shared visual language for list items — the notch + toplane + title grid.
 *
 * Extracted so PUCK-tracked entity rows (`p7t-entity-item`) and non-tracked occurrence rows
 * (reflectives, attentives, eventives) read identically without either drifting from the other.
 * It deliberately excludes the `.extra-action` fly-out, which only the entity item carries.
 *
 * It also carries the item **flare** (PEP100 patch 3), the side glass glow an item wears while its host sets the
 * `flare` attribute — an executive or reflective whose affinity timeframe is active right now. The glow is drawn in the flare
 * accent and scaled throughout by `--flare-intensity`, which the host stylesheet registers as an animatable
 * percentage, so it ignites and fades instead of snapping. Its body is the host's own background layers, so it
 * sits beneath the row's content without making a stacking context; the two pseudo-elements add the light spilling
 * past the left edge and the glass rim, with a glint that runs along the rim once as the flare ignites.
 */
export const itemLayoutStyles = css`
	@keyframes fade-in {
		from {
			opacity: 0;
			transform: translateY(-1em);
		}
	}

	@keyframes flare-breathe {
		to {
			opacity: calc(var(--flare-intensity) * .35);
			scale: 1 .8;
		}
	}

	@keyframes flare-glint {
		from {
			background-position: -100% 0, 0 0;
		}
		to {
			background-position: 200% 0, 0 0;
		}
	}

	:host {
		--flare-intensity: 0%;
		--p7t-item-flare: var(--p7t-flare-accent, var(--interactive-accent));
		display: flex;
		align-items: center;
		justify-content: stretch;
		padding-inline: 5px 10px;
		padding-block: 8px;
		font-family: var(--font-interface);
		transition: .6s ease, --flare-intensity 1.2s cubic-bezier(.2, .7, .2, 1);
		border-radius: 12px;
		box-sizing: border-box;
		position: relative;
		anchor-name: --entity-item;
		gap: .5em;
		animation: fade-in .3s ease;
		/* The flare's body, transparent while --flare-intensity rests at 0%. */
		background-image:
			/* the specular sheen across the top of the glass, brightest in the lit corner */
			radial-gradient(90% 110% at 0% 0%, color-mix(in oklab, white calc(var(--flare-intensity) * .08), transparent), transparent),
			/* the edge light down the left side, fading toward the rounded corners */
			linear-gradient(to bottom, transparent 10%, color-mix(in oklab, color-mix(in oklab, var(--p7t-item-flare) 55%, white) var(--flare-intensity), transparent) 50%, transparent 90%),
			/* the bleed hugging that edge */
			linear-gradient(to right, color-mix(in oklab, var(--p7t-item-flare) calc(var(--flare-intensity) * .4), transparent), transparent 1.8em),
			/* the bloom spreading into the row */
			radial-gradient(farthest-side at 0% 50%, color-mix(in oklab, var(--p7t-item-flare) calc(var(--flare-intensity) * .34), transparent), transparent);
		background-size: 100% 100%, 2px 100%, 100% 100%, 65% 100%;
		background-position: 0 0, left center, 0 0, left center;
		background-repeat: no-repeat;
	}

		:host(:not([interactive])) {
			padding: 0;
			pointer-events: none;
		}

		:host([interactive]:hover) {
			background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
		}

		:host([flare]) {
			--flare-intensity: 100%;
		}

		/* The light spilling past the left edge; it breathes while the flare burns. */
		:host::before {
			content: '';
			position: absolute;
			inset-block: 10%;
			left: -8px;
			width: 16px;
			background-image: radial-gradient(closest-side, color-mix(in oklab, var(--p7t-item-flare) 70%, white), color-mix(in oklab, var(--p7t-item-flare) 30%, transparent) 55%, transparent);
			opacity: calc(var(--flare-intensity) * .8);
			pointer-events: none;
		}

		:host([flare])::before {
			animation: flare-breathe 3.6s ease-in-out infinite alternate;
		}

		/* The glass rim: a hairline lit from the left, and the glint that runs along it as the flare ignites. */
		:host::after {
			content: '';
			position: absolute;
			inset: 0;
			padding: 1px;
			border-radius: inherit;
			background-image:
				linear-gradient(to right, transparent, rgb(255 255 255 / .7), transparent),
				radial-gradient(70% 170% at 0% 40%, color-mix(in oklab, var(--p7t-item-flare) 80%, white), color-mix(in oklab, var(--p7t-item-flare) 40%, transparent) 45%, transparent);
			background-size: 40% 100%, 100% 100%;
			background-position: -100% 0, 0 0;
			background-repeat: no-repeat;
			-webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
			-webkit-mask-composite: xor;
			mask: linear-gradient(#000 0 0) content-box exclude, linear-gradient(#000 0 0);
			opacity: var(--flare-intensity);
			pointer-events: none;
		}

		:host([flare])::after {
			animation: flare-glint 1.6s ease-out .2s;
		}

		@media (prefers-reduced-motion: reduce) {
			:host([flare])::before,
			:host([flare])::after {
				animation: none;
			}
		}

	.grid {
		flex: 1;
		anchor-scope: --entity-item;
		display: grid;
		gap: 0 .6ch;
		grid-template-columns: 3.6em 1fr;
		grid-template-rows: auto auto;
		grid-template-areas:
			"notch toplane"
			"notch title";
		align-items: center;
	}

	.notch {
		display: flex;
		align-items: stretch;
		justify-content: center;
		grid-area: notch;
		padding: 4px;
		box-sizing: border-box;
		position: relative;
		align-self: stretch;

		& ::slotted(p7t-icon),
		& p7t-icon {
			width: 1.9em;
			height: 1.9em;
		}
	}

	.toplane {
		display: flex;
		align-items: center;
		gap: 5px;
		grid-area: toplane;
		font-family: var(--font-text);
		margin-top: -2px;

		& .filler {
			flex: 1;
		}
	}

	.title {
		font-weight: 300;
		font-size: 1.3em;
		line-height: 1;
		margin-block: -.1em .1em;
		grid-area: title;
		display: flex;
		align-items: center;
		justify-content: flex-start;
		cursor: pointer;
		gap: .4ch;
	}

	.part {
		border-radius: 8px;
		border: 1px solid transparent;
		transition: .3s ease;

		&:hover {
			border-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, transparent);
		}
	}
	
	p7t-directive-item {
		font-size: .9em;
		opacity: .6;
	}

	.grid.disabled,
	.grid.disabled ~ *:not(.extra-action) {
		opacity: .4;
	}
`
