import { EditorView, WidgetType } from "@codemirror/view"

export interface PageBannerComponent {
	render(document: HTMLDocument): HTMLElement,
}

export class PageBannerWidget<T extends PageBannerComponent> extends WidgetType {
	constructor(public component: T) { super(); }

	public override toDOM(view: EditorView): HTMLElement {
		return this.component.render(view.dom.ownerDocument)
	}

	public override eq(): boolean {
		return true
	}
}