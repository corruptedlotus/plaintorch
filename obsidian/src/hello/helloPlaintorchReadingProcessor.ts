import type { MarkdownPostProcessorContext } from "obsidian";

import { createHelloPlaintorchElement } from "./createHelloPlaintorchElement";

export async function renderHelloPlaintorchInReadingView(
	element: HTMLElement,
	_context: MarkdownPostProcessorContext
): Promise<void> {
	try {
		/*if (element.previousElementSibling) {
			return;
		}*/

		/*const existing = element.querySelector<HTMLElement>(":scope > [data-plaintorch-hello='true']");
		if (existing) {
			return;
		}*/

		element.prepend(createHelloPlaintorchElement(element.ownerDocument));
	} catch {
		return;
	}
}