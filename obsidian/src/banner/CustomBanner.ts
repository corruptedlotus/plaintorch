import { App } from "obsidian"
import { PageBannerComponent } from "./PageBannerComponent"
import { NoteBanner } from "components"

export class CustomBanner implements PageBannerComponent {
	file: string
	app: App
	
	constructor(file: string, app: App) {
		this.file = file
		this.app = app
	}

	render(document: HTMLDocument): HTMLElement {
		const host = document.createElement("p7t-note-banner") as NoteBanner
		host.className = "plaintorch-note-banner"
		host.file = this.file
		host.app = this.app
		return host
	}
}