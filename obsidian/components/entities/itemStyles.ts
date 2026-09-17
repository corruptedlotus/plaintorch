import { css } from "@a11d/lit"

/**
 * The shared visual language for list items — the notch + toplane + title grid.
 *
 * Extracted so PUCK-tracked entity rows (`p7t-entity-item`) and non-tracked occurrence rows
 * (reflectives, attentives, eventives) read identically without either drifting from the other.
 * It deliberately excludes the `.extra-action` fly-out, which only the entity item carries.
 */
export const itemLayoutStyles = css`
	@keyframes fade-in {
		from {
			opacity: 0;
			transform: translateY(-1em);
		}
	}

	:host {
		--flare-intensity: 0%;
		display: flex;
		align-items: center;
		justify-content: stretch;
		padding-inline: 5px 10px;
		padding-block: 8px;
		font-family: var(--font-interface);
		transition: .6s ease;
		border-radius: 12px;
		box-sizing: border-box;
		position: relative;
		anchor-name: --entity-item;
		gap: .5em;
		animation: fade-in .3s ease;
	}

		:host(:not([interactive])) {
			padding: 0;
			pointer-events: none;
		}

		:host([interactive]:hover) {
			background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
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
