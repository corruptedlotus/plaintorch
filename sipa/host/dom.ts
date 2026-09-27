/**
 * Appends a new element to a parent and returns it — optionally with a text content and a class. The plain-DOM
 * stand-in for the element helpers Obsidian patches onto every element (`createEl`), which exist nowhere else.
 */
export function createChild<K extends keyof HTMLElementTagNameMap>(
	parent: HTMLElement,
	tag: K,
	options?: { readonly text?: string, readonly cls?: string }
): HTMLElementTagNameMap[K] {
	const element = document.createElement(tag)
	if (options?.text !== undefined) {
		element.textContent = options.text
	}

	if (options?.cls) {
		element.className = options.cls
	}

	parent.append(element)
	return element
}
