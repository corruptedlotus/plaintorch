import { MarkdownPostProcessorContext, MarkdownSectionInformation } from 'obsidian';

export const createHelloPlaintorchReadingModeRenderer =
	(el: HTMLElement, _ctx: MarkdownPostProcessorContext) => {
		const si = _ctx.getSectionInfo(el) as MarkdownSectionInformation;
		if (si.lineStart !== 0) {
			return;
		}

		// Create the hello banner element
		const banner = document.createElement('div');
		banner.className = 'plaintorch-note-banner';
		banner.textContent = 'Hello PLAINTORCH';

		// Insert at the very top of the rendered content
		el.prepend(banner);
	};
