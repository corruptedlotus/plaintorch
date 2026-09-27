import { Component, component, css, html, nothing, property } from "@a11d/lit"
import type { PlaintorchBridge, WindowFrameState } from "../shared/contracts"
import mark from "../../assets/plaintorch-mono-dark.png"

declare global {
	interface Window {
		plaintorch: PlaintorchBridge
	}
}

/** What each window button does. */
type WindowAction = "minimize" | "toggleMaximize" | "close"

/**
 * The click a SIPA modal offers before taking a click on its backdrop as a dismissal (`backdrop-click`, see
 * `isBackdropDismissal` in `@pleiades/sipa/hosts/sipa`): a modal leaves the page — this bar included — inert.
 */
type BackdropClick = CustomEvent<{ readonly clientX: number, readonly clientY: number }>

/**
 * A window's own frame, for the shell's frameless windows: a title bar — the PLAINTORCH mark, the window's title, room
 * for a page's own controls, and the minimize, maximize (restore) and close buttons — over the page, which fills and
 * scrolls in the rest of the window.
 *
 * The bar drags the window, and double-clicking it maximizes, as a system title bar does; the buttons and whatever a
 * page slots into the bar do not drag. It follows the window: dimmed while another window has the focus, the restore
 * glyph while maximized, gone in full screen. On macOS the system keeps its own traffic lights over the bar, so it
 * draws no buttons there and leaves them room. While a SIPA modal is open the bar is inert beneath its backdrop; a
 * click the modal offers (`backdrop-click`) that lands on a window button still works that button.
 *
 * Reflects `platform`, `maximized`, `inactive` and `fullscreen` for styling.
 *
 * @slot - The page.
 * @slot title-bar - A page's own controls in the title bar, after the title.
 *
 * @csspart title-bar - The bar.
 * @csspart icon - The PLAINTORCH mark.
 * @csspart title - The window's title.
 * @csspart controls - The window buttons.
 * @csspart control - Each window button; also `minimize`, `maximize` and `close`.
 * @csspart content - The page's area below the bar.
 *
 * @cssprop --p7t-title-bar-height - The bar's height (32px).
 * @cssprop --p7t-title-bar-background - The bar's background (the window's background).
 * @cssprop --p7t-title-bar-color - The title's and the glyphs' colour.
 * @cssprop --p7t-title-bar-border - The line under the bar.
 * @cssprop --p7t-title-bar-control-hover - A button's background under the pointer.
 * @cssprop --p7t-title-bar-close-hover - The close button's background under the pointer.
 */
@component("p7t-window-frame")
export class WindowFrame extends Component {
	/** The title shown in the bar; the document's title when unset. */
	@property() heading?: string
	@property({ reflect: true }) platform = ""
	@property({ type: Boolean, reflect: true }) maximized = false
	@property({ type: Boolean, reflect: true }) inactive = false
	@property({ type: Boolean, reflect: true }) fullscreen = false

	private unsubscribe?: () => void

	static override get styles() {
		return css`
			:host {
				--p7t-title-bar-height: 36px;
				--p7t-title-bar-background: var(--p7t-bg, #1f1a22);
				--p7t-title-bar-color: var(--p7t-text, #f2ebe4);
				--p7t-title-bar-border: var(--p7t-line, #3d3342);
				--p7t-title-bar-control-hover: rgba(242, 235, 228, 0.08);
				--p7t-title-bar-close-hover: #c42b1c;
				display: flex;
				flex-direction: column;
				height: 100vh;
			}

			header {
				flex: none;
				display: grid;
				grid-template-columns: [icon] 1fr [title] auto [controls] 1fr;
				align-items: center;
				gap: 8px;
				height: var(--p7t-title-bar-height);
				padding-inline-start: 10px;
				background: var(--p7t-title-bar-background);
				color: color-mix(in srgb, var(--p7t-title-bar-color) 60%, transparent);
				font-size: 12px;
				user-select: none;
				-webkit-app-region: drag;
			}

			:host([platform=darwin]) header {
				/* The traffic lights' room. */
				padding-inline-start: 80px;
			}

			:host([fullscreen]) header {
				display: none;
			}

			.icon {
				width: 16px;
				height: 16px;
				grid-column: icon;
			}

			.title {
				min-width: 0;
				overflow: hidden;
				display: flex;
				justify-content: center;
				white-space: nowrap;
				text-overflow: ellipsis;
				letter-spacing: 0.04em;
				grid-column: title;
			}

			:host([inactive]) .icon, :host([inactive]) .title, :host([inactive]) .control {
				opacity: 0.55;
			}

			/* Only what sits in the bar: a no-drag page would cut the bar's drag region wherever it scrolls beneath it. */
			slot[name=title-bar]::slotted(*) {
				-webkit-app-region: no-drag;
			}

			.controls {
				display: flex;
				align-self: stretch;
				justify-content: flex-end;
				justify-self: flex-end;
				gap: 4px;
				grid-column: controls;
			}

			.control {
				all: unset;
				display: inline-grid;
				place-items: center;
				width: 48px;
				color: inherit;
				-webkit-app-region: no-drag;
			}

			.control:hover {
				background: var(--p7t-title-bar-control-hover);
			}

			.control.close:hover {
				background: var(--p7t-title-bar-close-hover);
				color: #ffffff;
				opacity: 1;
			}

			.control svg {
				width: 10px;
				height: 10px;
				fill: none;
				stroke: currentColor;
				stroke-width: 1;
			}

			main {
				flex: 1;
				min-height: 0;
				overflow: auto;
			}
		`
	}

	override connectedCallback() {
		super.connectedCallback()
		const frame = window.plaintorch.frame
		this.platform = frame.platform
		this.unsubscribe = frame.onState(state => this.follow(state))
		void frame.getState().then(state => state && this.follow(state))
		window.addEventListener("backdrop-click", this.takeBackdropClick)
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		this.unsubscribe?.()
		window.removeEventListener("backdrop-click", this.takeBackdropClick)
	}

	private readonly takeBackdropClick = (event: Event) => {
		const { clientX, clientY } = (event as BackdropClick).detail
		const button = Array.from(this.renderRoot.querySelectorAll<HTMLElement>("[data-action]")).find(candidate => {
			const box = candidate.getBoundingClientRect()
			return clientX >= box.left && clientX < box.right && clientY >= box.top && clientY < box.bottom
		})
		if (button) {
			event.preventDefault()
			this.act(button.dataset["action"] as WindowAction)
		}
	}

	private act(action: WindowAction) {
		void window.plaintorch.frame[action]()
	}

	private follow(state: WindowFrameState) {
		this.maximized = state.maximized
		this.inactive = !state.focused
		this.fullscreen = state.fullScreen
	}

	protected override get template() {
		return html`
			<header part="title-bar">
				<img class="icon" part="icon" src=${mark} alt="">
				<span class="title" part="title">${this.heading ?? document.title}</span>
				<slot name="title-bar"></slot>
				${this.platform === "darwin" ? nothing : this.controlsTemplate}
			</header>
			<main part="content">
				<slot></slot>
			</main>
		`
	}

	/** The window buttons: out of the tab order, as a system title bar's are, the keyboard having the system's shortcuts. */
	private get controlsTemplate() {
		return html`
			<div class="controls" part="controls">
				<button class="control" part="control minimize" data-action="minimize" tabindex="-1" title="Minimize"
					aria-label="Minimize" @click=${() => this.act("minimize")}>
					<svg viewBox="0 0 10 10"><path d="M0 5.5h10"/></svg>
				</button>
				<button class="control" part="control maximize" data-action="toggleMaximize" tabindex="-1"
					title=${this.maximized ? "Restore" : "Maximize"} aria-label=${this.maximized ? "Restore" : "Maximize"}
					@click=${() => this.act("toggleMaximize")}>
					${this.maximized
						? html`<svg viewBox="0 0 10 10"><path d="M.5 2.5h7v7h-7z M2.5 2.5v-2h7v7h-2"/></svg>`
						: html`<svg viewBox="0 0 10 10"><path d="M.5 .5h9v9h-9z"/></svg>`}
				</button>
				<button class="control close" part="control close" data-action="close" tabindex="-1" title="Close"
					aria-label="Close" @click=${() => this.act("close")}>
					<svg viewBox="0 0 10 10"><path d="M0 0l10 10M10 0L0 10"/></svg>
				</button>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		"p7t-window-frame": WindowFrame
	}
}
