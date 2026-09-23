import { Controller, type ReactiveElement } from '@a11d/lit'
import { ContextMenu, type ContextMenuSpec } from './ContextMenu'

/**
 * Supplies the menu to raise for a right-click, or `undefined` to raise none. The triggering event is passed so a
 * host with more than one menuable target can resolve which one was clicked.
 */
export type ContextMenuSource = (event: MouseEvent) => ContextMenuSpec | undefined

/**
 * Binds a component to a context menu (component-controller model, akin to {@link EntityWatch} / {@link DerivedRef}).
 *
 * A host declares one as a field and is done: the controller catches the `contextmenu` event on the host for the
 * host's whole lifetime, asks the source for the menu that fits the current state, and — only when there is one —
 * suppresses the browser's default menu, stops the event so an ancestor's controller does not also fire, and opens
 * it at the pointer. An empty or absent menu is left to pass through, so a host withholds its menu simply by
 * returning nothing. This is the automatic form of the event-catch-and-open a surface used to hand-roll.
 */
export class ContextMenuController extends Controller {
	public constructor(host: ReactiveElement, private readonly source: ContextMenuSource) {
		super(host)
	}

	private get element(): HTMLElement {
		return this.host as unknown as HTMLElement
	}

	public override hostConnected(): void {
		this.element.addEventListener('contextmenu', this.onContextMenu)
	}

	public override hostDisconnected(): void {
		this.element.removeEventListener('contextmenu', this.onContextMenu)
	}

	private readonly onContextMenu = (event: MouseEvent): void => {
		const spec = this.source(event)
		if (!spec || spec.entries.length === 0) {
			return
		}

		event.preventDefault()
		event.stopPropagation()
		ContextMenu.open(event.clientX, event.clientY, spec)
	}
}
