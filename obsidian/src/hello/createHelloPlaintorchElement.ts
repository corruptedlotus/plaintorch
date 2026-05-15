export function createHelloPlaintorchElement(document: Document, extraText?: string): HTMLElement {
  const host = document.createElement("div");
  host.className = "plaintorch-note-banner";
  host.dataset.plaintorchHello = "true";
  host.textContent = "Hello PLAINTORCH" + (extraText ? `: ${extraText}` : "");
  return host;
}