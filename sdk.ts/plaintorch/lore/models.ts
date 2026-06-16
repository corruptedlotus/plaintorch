import { model } from "@a11d/api-dotnet"

@model("LorePage")
export class LorePage {
	get id() {
		return this.puck
	}
	puck!: string
	title!: string
	overrideIdentifier?: string
	parentPuck?: string
	beginning?: string
	level?: string
	relativePath?: string
	era?: number
	chapter?: number
	act?: number
	phase?: number
	indexedUtc!: string
	parent?: LorePage
}
