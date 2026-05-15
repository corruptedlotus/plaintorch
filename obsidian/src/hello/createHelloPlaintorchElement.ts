import { EditorView, WidgetType } from "@codemirror/view";

export class HelloPlaintorchWidget extends WidgetType {
  public override toDOM(view: EditorView): HTMLElement {
    const host = view.dom.ownerDocument.createElement("div");
    host.className = "plaintorch-note-banner";
    host.dataset.plaintorchHello = "true";
    host.textContent = "Hello PLAINTORCH";
    return host;
  }

  public override eq(): boolean {
    return true;
  }
}