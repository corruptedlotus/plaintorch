import { css, type CSSResult } from '@a11d/lit'

/**
 * The document-level rules the components rely on but cannot declare from inside a shadow root: the registration of
 * `--flare-intensity` (the item flare) and the Polaris / Onrush accent tokens scoped to `.plaintorch-root`. Every host
 * installs them once with {@link adoptComponentStyles}, so they live beside the components rather than in one host's
 * stylesheet.
 */
export const componentStyles: CSSResult = css`
	@property --flare-intensity {
		syntax: '<percentage>';
		initial-value: 0%;
	}

	.plaintorch-root {
		--p7t-accent-polaris: #038899;
		--p7t-accent-onrush: #d4227b;
	}
`

/** Adopts stylesheets into a document once each; adopting one that is already there does nothing. */
export function adoptStyles(document: Document, ...styles: readonly CSSResult[]): void {
	const sheets = styles.map(style => style.styleSheet).filter((sheet): sheet is CSSStyleSheet => !!sheet)
	const missing = sheets.filter(sheet => !document.adoptedStyleSheets.includes(sheet))
	if (missing.length) {
		document.adoptedStyleSheets = [...document.adoptedStyleSheets, ...missing]
	}
}

/** Installs {@link componentStyles} into a document. */
export function adoptComponentStyles(document: Document): void {
	adoptStyles(document, componentStyles)
}
