import { css, type CSSResult } from '@a11d/lit'

/**
 * The standalone shell's document stylesheet: the PLAINTORCH palette as `--p7t-*` tokens, and the Obsidian theme
 * variables the components read — `--text-normal`, `--interactive-accent`, `--background-primary`, … — defined on top of
 * it, so the components render in the shell's colours without knowing where they run. It also styles the plain
 * light-DOM controls a dialog may draw (`input`, `button`, the `mod-cta` confirming button) the way Obsidian's own
 * stylesheet does in the plugin.
 */
export const sipaStyles: CSSResult = css`
	:root {
		color-scheme: dark;

		--p7t-bg: #1f1a22;
		--p7t-bg-alt: #251f29;
		--p7t-panel: #2a232e;
		--p7t-line: #3d3342;
		--p7t-text: #f2ebe4;
		--p7t-muted: #a89ca4;
		--p7t-faint: #756a72;
		--p7t-accent: #71549c;
		--p7t-accent-soft: #b994d8;
		--p7t-ok: #7cc48f;
		--p7t-warn: #e6b45a;
		--p7t-bad: #e26d6d;

		--background-primary: var(--p7t-bg);
		--background-primary-alt: var(--p7t-bg-alt);
		--background-secondary: var(--p7t-panel);
		--background-modifier-border: var(--p7t-line);
		--background-modifier-hover: rgba(242, 235, 228, 0.07);
		--background-modifier-form-field: #1a151d;
		--background-modifier-message: rgba(20, 16, 23, 0.92);

		--text-normal: var(--p7t-text);
		--text-muted: var(--p7t-muted);
		--text-faint: var(--p7t-faint);
		--text-accent: var(--p7t-accent-soft);
		--text-on-accent: #ffffff;
		--text-error: var(--p7t-bad);
		--text-warning: var(--p7t-warn);
		--text-success: var(--p7t-ok);

		--interactive-normal: #352c3a;
		--interactive-hover: #40354a;
		--interactive-accent: var(--p7t-accent);
		--interactive-accent-hover: #7f60ad;

		--color-green: var(--p7t-ok);
		--color-yellow: var(--p7t-warn);
		--color-red: var(--p7t-bad);

		--font-interface: "Outfit", "Space Grotesk", "Segoe UI", system-ui, sans-serif;
		--font-text: var(--font-interface);
		--font-monospace: "Cascadia Code", Consolas, ui-monospace, monospace;

		--modal-radius: 12px;
		--dialog-width: 560px;
	}

	html, body {
		margin: 0;
		background: var(--background-primary);
		color: var(--text-normal);
		font: 14px/1.45 var(--font-interface);
	}

	input[type=text], input:not([type]), textarea {
		box-sizing: border-box;
		padding: 6px 10px;
		border: 1px solid var(--background-modifier-border);
		border-radius: 6px;
		background: var(--background-modifier-form-field);
		color: var(--text-normal);
		font: inherit;
	}

	input[type=text]:focus-visible, input:not([type]):focus-visible, textarea:focus-visible {
		outline: 2px solid var(--interactive-accent);
		outline-offset: -1px;
	}

	button {
		padding: 6px 14px;
		border: 1px solid var(--background-modifier-border);
		border-radius: 6px;
		background: var(--interactive-normal);
		color: var(--text-normal);
		font: inherit;
		cursor: pointer;
	}

	button:hover {
		background: var(--interactive-hover);
	}

	button.mod-cta {
		border-color: var(--interactive-accent);
		background: var(--interactive-accent);
		color: var(--text-on-accent);
	}

	button.mod-cta:hover {
		background: var(--interactive-accent-hover);
	}
`
