import { RangeSetBuilder, StateField, type Extension, type Text } from "@codemirror/state"
import { Decoration, type DecorationSet, EditorView } from "@codemirror/view"
import { App, editorInfoField, MarkdownFileInfo, MarkdownPostProcessorContext, MarkdownSectionInformation } from "obsidian"
import { PageBannerComponent, PageBannerWidget } from "./PageBannerComponent"
import { CustomBanner } from "./CustomBanner"

export class PageBannerRenderer {
	app: App

	constructor(app: App) {
		this.app = app
	}

	createEditorExtension = () : Extension => {
		const field = StateField.define<DecorationSet>({
			create: (state) => {
				return this.createDecorations(state.doc, state.field(editorInfoField)!)
			},
			update: (decorations, transaction) => {
				if (transaction.docChanged) {
					return this.createDecorations(transaction.newDoc, transaction.state.field(editorInfoField)!)
				}

				return decorations.map(transaction.changes)
			},
			provide: (stateField) => {
				return EditorView.decorations.from(stateField)
			}
		})
		return [field]
	}

	createDecorations = (doc: Text, editorInfo: MarkdownFileInfo): DecorationSet => {
		const builder = new RangeSetBuilder<Decoration>()
		const insertPosition = this.getInsertPosition(doc.toString())
		builder.add(
			0,
			0,
			Decoration.widget({
				widget: new PageBannerWidget(new CustomBanner(editorInfo.file?.path!, this.app)),
				block: true,
				side: -1
			})
		)
		return builder.finish()
	}

	getInsertPosition(content: string): number {
		if (!content.startsWith("---")) {
			return 0
		}

		const match = content.match(/^---\r?\n?[\s\S]*?\r?\n---(?:\r?\n)?/)
		return match ? match[0].length : 0
	}

	readingModeRenderer = (el: HTMLElement, _ctx: MarkdownPostProcessorContext) => {
		const si = _ctx.getSectionInfo(el) as MarkdownSectionInformation
		if (si?.lineStart !== 0) {
			return
		}

		const banner = new CustomBanner(_ctx.sourcePath, this.app)
		const element = banner.render(document)

		el.prepend(element)
	}
	
	/*async createBannerForNote(path: string) : Promise<PageBannerComponent> {
		const client = (await import('@pleiades/sdk/plaintorch/node')).plaintorchNodeCoreClient
		const noteInfo = await client.system.resolveNote(path)
		switch (noteInfo?.entityKind) {
			case 'directive':
				const directive = await client.directives.get(noteInfo.puck!)
				return new DirectiveBanner(directive!)
			default:
				throw
		}
	}*/
}