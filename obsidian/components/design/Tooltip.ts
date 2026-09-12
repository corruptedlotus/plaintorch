import { component, css, html, HTMLTemplateResult, ifDefined, PropertyValues } from "@a11d/lit"
import { popover, PopoverCssAnchorPositionController } from "@3mo/popover"
import { Tooltip as MoTooltip, TooltipPlacement } from "@3mo/tooltip"

/*
 * Force @3mo/popover onto its Floating UI position controller instead of CSS anchor positioning.
 *
 * `mo-popover` has two position controllers and picks one from a runtime probe: CSS anchor positioning when the
 * engine reports support, else Floating UI. Only the Floating UI path carries `mo-tooltip`'s viewport-edge
 * `shift({ crossAxis: true })` clamp; the CSS-anchor path leaves edge-fitting to the browser's `position-area` /
 * `position-try`, which *flips* but does not reliably *shift* a center-aligned tip back inside the viewport — so
 * near an edge the tip overflows (a regression from both the old hand-rolled tooltip, which JS-clamped, and
 * @3mo's own pre-anchor-positioning behaviour). Pre-seeding the controller's cached probe to `false` (a private
 * static, hence the cast) selects Floating UI, whose JS `left`/`top` clamps correctly.
 *
 * This alone is not enough: the probe is also read when `mo-popover`'s styles are first *finalised*, which can
 * happen before this line runs, baking the CSS-anchor `position-area` rules into the element. Those rules then
 * override the Floating UI controller's JS `left`/`top` (a grid `position-area` wins over `inset`), so the tip
 * still lands where anchor positioning put it — off-screen on Chromium builds whose `position-area` doesn't
 * clamp a centered box (e.g. Electron's Chrome 142; it happens to clamp on 152/Edge, which masked this). The
 * companion neutraliser is in {@link Tooltip}'s styles: `position-area: none` on `mo-popover`, so JS wins.
 */
;(PopoverCssAnchorPositionController as unknown as { implicitAnchorSupported?: boolean }).implicitAnchorSupported = false

/** Plain text, rich markup, or a function deferring either (a function is treated as rich — see {@link tooltip}). */
export type TooltipContent = string | HTMLTemplateResult | (() => string | HTMLTemplateResult)

/**
 * A hover/focus tooltip, applied as a directive rather than a wrapper element:
 *
 * ```ts
 * html`<button ${tooltip('Delete')}>…</button>`
 * ```
 *
 * The directive tethers a top-layer overlay to the element it sits on — no extra element in the markup, no
 * slots, and no per-call-site `p7t-tooltip { display: … }` fix-ups. It reuses @3mo's tooltip machinery
 * wholesale: @3mo/popover's `popover` directive drives interest tracking (hover, keyboard focus, a held touch)
 * and @floating-ui positioning against a native Popover-API overlay, so the tip is never clipped by an
 * ancestor's overflow nor trapped under a stacking context.
 *
 * `content` is plain text — coerced onto the anchor as its `aria-label` right away, i.e. before the tip ever
 * materializes — or a `TemplateResult` for rich markup. A function defers either and, being non-textual, names
 * no `aria-label`; hand a function (e.g. `() => 'Delete'`) when the anchor already names itself and should keep
 * its own label.
 *
 * `placement` defaults to above the trigger (the app's long-standing tooltip position), flipping below when there
 * is no room. The look is entirely ours — see {@link Tooltip}.
 */
export const tooltip = (content: TooltipContent, placement: TooltipPlacement = TooltipPlacement.BlockStart) => popover(() => html`
	<p7t-tooltip placement=${ifDefined(placement)}>
		${typeof content === 'function' ? content() : content}
	</p7t-tooltip>
`, {
	trigger: 'interest',
	// Textual content names the anchor immediately, i.e. without materializing the tooltip first. Rich or
	// deferred (function) content is expected to name itself, so it assigns no aria-label.
	label: typeof content === 'function' ? undefined : String(content),
})

/**
 * The overlay the {@link tooltip} directive materializes: @3mo's `mo-tooltip` re-skinned as `p7t-tooltip`.
 *
 * Only the styling is ours. We replace @3mo's `mo-tooltip` styles wholesale — LitElement reads `static styles`
 * off the most-derived class, so this override, not the base's, is what the element finalizes — dressing the
 * inner `mo-popover` in the app's surface, border, shadow and type (the same face the old wrapper drew). The
 * anchor tracking, placement, rich/plain detection and top-layer promotion are all inherited unchanged.
 *
 * Overriding works because encapsulation context is sorted above specificity in the cascade: these rules live
 * in `p7t-tooltip`'s shadow, one context outside `mo-popover`'s own, so as normal declarations they beat its
 * inner `:host` rules for the same properties (padding, overflow), and the custom properties feed straight into
 * the `var()`s its `:host` reads (border, shadow).
 */
@component('p7t-tooltip')
export class Tooltip extends MoTooltip {
	static override get styles() {
		return css`
			mo-popover {
				/*
				 * Neutralise any CSS anchor-positioning @3mo baked into mo-popover (see the controller force at the
				 * top of this module): a live position-area overrides the Floating UI controller's JS left/top, re-
				 * centering the tip on its anchor and overflowing the viewport on some Chromium builds. Off, the JS
				 * insets — which clamp correctly — take effect.
				 */
				position-area: none;
				box-sizing: border-box;
				max-width: 24rem;
				padding: .55em .7em;
				border-radius: 10px;
				font-family: var(--font-interface);
				/*
				 * Absolute (rem) size, not em: the trigger may be a large heading, and an em-relative tooltip would
				 * balloon to match it. rem pins the overlay to the app's root size so every tooltip reads the same;
				 * the padding/max-width above are em, now relative to this fixed rem base — they scale with it, not
				 * with the trigger. The @3mo knob keeps working, defaulting to ours.
				 */
				font-size: var(--mo-tooltip-font-size, .85rem);
				font-weight: 400;
				line-height: 1.35;
				background: var(--background-secondary, #1e1e1e);
				color: var(--text-normal);
				overflow: clip;
				/* Inert, hover-only — the pointer never lands on the tip, matching every tooltip in the app. */
				pointer-events: none;
				/* Feed @3mo/popover's own :host rules the app's border colour and shadow. */
				--mo-shadow: 0 8px 26px color-mix(in srgb, black 45%, transparent);
				--mo-color-transparent-gray-3: color-mix(in srgb, var(--text-normal) 18%, transparent);
			}
		`
	}
	/*
	 * Make the tooltip transparent to clicks on its trigger. mo-popover subscribes a click handler to the anchor
	 * that, while the tip is open, light-dismisses it AND stopPropagation()s the click — swallowing the trigger’s
	 * own behaviour (a button’s action, a delegated/parent handler). A tooltip is inert and driven solely by the
	 * interest controller (hover + focus), so it must never react to clicks: no-op its mo-popover’s handleClick.
	 */
	override firstUpdated(props: PropertyValues) {
		super.firstUpdated(props)
		const moPopover = this.renderRoot.querySelector('mo-popover') as (Element & { handleClick?: (event: Event) => void }) | null
		if (moPopover) {
			moPopover.handleClick = () => {}
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-tooltip': Tooltip
	}
}
