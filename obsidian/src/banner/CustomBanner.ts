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
		const host = document.createElement("div")
		host.className = "plaintorch-note-banner"
		const banner = document.createElement("p7t-note-banner") as NoteBanner
		banner.file = this.file
		banner.app = this.app
		host.appendChild(banner)
		return host
	}
}