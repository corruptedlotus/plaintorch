import { Component, component, css, html, nothing, property, query } from '@a11d/lit'
import { IconName } from '../PleiadesIcon'

/** A clickable menu row. */
export interface ContextMenuAction {
	label: string
	icon?: IconName
	/** The action to perform. Its return is ignored; when it is async the menu still awaits it before it is gone. */
	run: () => void
	/** Shown in the error colour, for a destructive action. */
	danger?: boolean
	/** Rendered with a selected accent, for a toggle among mutually exclusive options. */
	pressed?: boolean
	disabled?: boolean
}

/** A thin divider between groups. */
export interface ContextMenuSeparator { separator: true }

/** An inert explanatory line, for when a row is withheld and the reason is worth saying. */
export interface ContextMenuNote { note: string }

export type ContextMenuEntry = ContextMenuAction | ContextMenuSeparator | ContextMenuNote

/** A whole menu: an optional title over a list of entries. */
export interface ContextMenuSpec {
	title?: string
	entries: ContextMenuEntry[]
}

const isSeparator = (entry: ContextMenuEntry): entry is ContextMenuSeparator => 'separator' in entry
const isNote = (entry: ContextMenuEntry): entry is ContextMenuNote => 'note' in entry

/**
 * A reusable right-click / context menu — originally the dependency canvas's, now shared so any surface can raise
 * one (an entity item, a grid row, the canvas).
 *
 * It renders as a native `popover`, so it sits in the top layer — never clipped by whatever it was opened inside —
 * and light-dismisses on an outside click or Escape for free. Open it imperatively through {@link ContextMenu.open}:
 * it mounts a transient menu to the document, positions it at the pointer within the viewport, closes it after an
 * action runs, and removes itself on dismissal.
 */
@component('p7t-context-menu')
export class ContextMenu extends Component {
	@property({ type: Object }) spec?: ContextMenuSpec

	@query('.menu') private readonly panel!: HTMLElement

	/**
	 * Opens a menu at a viewport point. Mounts a transient instance to the document body — so it escapes any
	 * clipping or stacking context of the opener — and disposes of it on dismissal.
	 */
	static open(clientX: number, clientY: number, spec: ContextMenuSpec): ContextMenu {
		const menu = document.createElement('p7t-context-menu') as ContextMenu
		menu.spec = spec
		document.body.appendChild(menu)
		void menu.openAt(clientX, clientY)
		return menu
	}

	static override get styles() {
		return css`
			:host {
				display: contents;
			}

			/* A popover, so the menu renders in the top layer and is not clipped by whatever raised it. */
			.menu {
				position: fixed;
				margin: 0;
				padding: .3em;
				border-radius: 10px;
				border: 1px solid var(--background-modifier-border, color-mix(in srgb, var(--text-normal) 20%, transparent));
				background-color: var(--background-secondary, #2b2b2b);
				color: var(--text-normal);
				box-shadow: 0 6px 24px rgb(0 0 0 / .28);
				font-family: var(--font-interface);
				min-width: 13em;
			}

			.menu-title {
				padding: .35em .6em;
				opacity: .55;
				font-size: .8em;
			}

			.menu-item {
				display: flex;
				align-items: center;
				gap: .5em;
				width: 100%;
				padding: .4em .6em;
				border: none;
				border-radius: 7px;
				background: transparent;
				color: inherit;
				font-family: inherit;
				font-size: .95em;
				text-align: start;
				cursor: pointer;
			}

			.menu-item:hover {
				background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
			}

			.menu-item[aria-pressed='true'] {
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.menu-item.danger:hover {
				background-color: color-mix(in srgb, var(--text-error, crimson) 14%, transparent);
			}

			.menu-item.danger {
				color: var(--text-error, crimson);
			}

			.menu-item:disabled {
				opacity: .4;
				cursor: default;
			}

			.menu-item p7t-icon {
				width: 1.1em;
				height: 1.1em;
				opacity: .85;
			}

			.menu-separator {
				height: 1px;
				margin: .25em .3em;
				background-color: color-mix(in srgb, var(--text-normal) 12%, transparent);
			}

			.menu-note {
				padding: .4em .6em;
				opacity: .6;
				font-size: .85em;
				line-height: 1.25;
			}
		`
	}

	protected override get template() {
		const spec = this.spec
		if (!spec) {
			return html``
		}

		return html`
			<div class='menu' popover='auto' @beforetoggle=${(e: Event) => this.onToggle(e)}>
				${!spec.title ? nothing : html`<div class='menu-title'>${spec.title}</div>`}
				${spec.entries.map(entry => this.entryTemplate(entry))}
			</div>
		`
	}

	private entryTemplate(entry: ContextMenuEntry) {
		if (isSeparator(entry)) {
			return html`<div class='menu-separator'></div>`
		}

		if (isNote(entry)) {
			return html`<div class='menu-note'>${entry.note}</div>`
		}

		return html`
			<button
				class='menu-item ${entry.danger ? 'danger' : ''}'
				aria-pressed=${entry.pressed ?? nothing}
				?disabled=${entry.disabled}
				@click=${() => void this.choose(entry)}>
				${!entry.icon ? nothing : html`<p7t-icon icon=${entry.icon}></p7t-icon>`}
				<span>${entry.label}</span>
			</button>
		`
	}

	/** Closes first, then runs — so a slow action never leaves the menu hanging over the surface it acted on. */
	private async choose(entry: ContextMenuAction) {
		if (entry.disabled) {
			return
		}

		this.panel.hidePopover()
		await entry.run()
	}

	/** Shows the popover and places it at the pointer, flipping above and clamping to the viewport as needed. */
	async openAt(clientX: number, clientY: number) {
		// The size to place against is the size of the content just assigned, which is not laid out until the
		// update it triggered has run.
		await this.updateComplete
		const panel = this.panel
		panel.showPopover()
		const margin = 8
		const width = panel.offsetWidth
		const height = panel.offsetHeight
		panel.style.left = `${Math.max(margin, Math.min(clientX, window.innerWidth - width - margin))}px`
		panel.style.top = `${clientY + height + margin > window.innerHeight ? Math.max(margin, clientY - height - margin) : clientY + margin}px`
	}

	private onToggle(e: Event) {
		if ((e as Event & { newState?: string }).newState === 'closed') {
			// Light-dismissed or closed after an action: the transient instance has done its job.
			this.remove()
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-context-menu': ContextMenu
	}
}
