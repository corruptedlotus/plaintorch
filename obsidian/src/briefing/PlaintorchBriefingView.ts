import { ItemView, type WorkspaceLeaf } from "obsidian"
import type { SystemBriefing, VaultNoteAuthorityResolution } from "@pleiades/sdk/plaintorch"

export const PLAINTORCH_BRIEFING_VIEW_TYPE = "plaintorch-briefing"

type PlaintorchEntityKind = "directive" | "objective" | "onrush-sprint" | "polaris-cycle"

interface PlaintorchEntityDescriptor {
	kind: PlaintorchEntityKind
	label: string
	title: string
	puck?: string
}

const entityMetadata: Record<PlaintorchEntityKind, { label: string }> = {
	directive: { label: "Directive" },
	objective: { label: "Objective" },
	"onrush-sprint": { label: "Onrush Sprint" },
	"polaris-cycle": { label: "Polaris Cycle" }
}

const kindAliases = new Map<string, PlaintorchEntityKind>([
	["directive", "directive"],
	["directives", "directive"],
	["objective", "objective"],
	["objectives", "objective"],
	["onrush", "onrush-sprint"],
	["onrushsprint", "onrush-sprint"],
	["onrush-sprint", "onrush-sprint"],
	["sprint", "onrush-sprint"],
	["polaris", "polaris-cycle"],
	["polariscycle", "polaris-cycle"],
	["polaris-cycle", "polaris-cycle"],
	["cycle", "polaris-cycle"],
	["journal", "polaris-cycle"]
])

type PlaintorchNodeCoreClient = typeof import("@pleiades/sdk/plaintorch/node").plaintorchNodeCoreClient

let cachedCoreClient: PlaintorchNodeCoreClient | undefined

export class PlaintorchBriefingView extends ItemView {
	public constructor(leaf: WorkspaceLeaf) {
		super(leaf)
	}

	public override getViewType(): string {
		return PLAINTORCH_BRIEFING_VIEW_TYPE
	}

	public override getDisplayText(): string {
		return "PLAINTORCH Briefing"
	}

	public override getIcon(): string {
		return "sparkles"
	}

	public override async onOpen(): Promise<void> {
		await this.render()
	}

	public override async onClose(): Promise<void> {
		this.contentEl.empty()
	}

	private async render(): Promise<void> {
		/*const coreClient = await getPlaintorchNodeCoreClient()
		const activeFile = this.app.workspace.getActiveFile()
		const briefing = await coreClient.system.getBriefing()
		const activeEntity = activeFile ? await detectPlaintorchEntity(coreClient, activeFile.path, activeFile.basename) : null

		this.contentEl.empty()
		this.contentEl.addClass("plaintorch-briefing-view")

		const hero = this.contentEl.createDiv({ cls: "plaintorch-briefing-hero" })
		hero.createEl("h1", { text: "PLAINTORCH Briefing" })
		hero.createEl("p", {
			text: briefing
				? `Status ${briefing.status} • ${briefing.pleiadeanToday}`
				: "PLAINTORCH core is unavailable."
		})

		const grid = this.contentEl.createDiv({ cls: "plaintorch-briefing-grid" })

		this.createCard(grid, "Core status", [
			briefing ? `Status: ${briefing.status}` : "Status: unavailable",
			briefing ? `Pleiadean day: ${briefing.pleiadeanToday}` : "Pleiadean day: unavailable",
			briefing ? `Celestron banked: ${briefing.celestronBanked}` : "Celestron banked: unavailable",
			"Transport: per-user PLAINTORCH socket"
		])

		this.createOnrushCard(grid, briefing)

		this.createPolarisCard(grid, briefing)

		this.createCard(grid, "Active note", [
			activeFile ? `Path: ${activeFile.path}` : "No active markdown file",
			activeEntity ? `Detected entity: ${activeEntity.label}` : "Detected entity: unavailable",
			activeEntity?.puck ? `PUCK: ${activeEntity.puck}` : "PUCK: unavailable"
		])

		if (briefing?.currentOnrush) {
			this.createObjectiveCollectionCard(this.contentEl, `${this.capitalize(briefing.currentOnrush.selectionMode)} onrush objectives`, briefing.currentOnrush.objectives.map((objective) => ({
				puck: objective.id,
				headline: `${objective.id} — ${objective.title}`,
				detail: `Status: ${objective.status} • College: ${objective.college} • Celestron: ${objective.celestronValue}${objective.isEnduring ? " • Enduring" : ""}`
			})))
		}

		if (briefing?.currentPolaris) {
			this.createExecutiveCollectionCard(this.contentEl, briefing.currentPolaris.executives.map((executive) => ({
				headline: `${executive.id} — ${executive.title ?? executive.objectiveTitle ?? "Untitled executive"}`,
				objectivePuck: executive.objectiveId,
				objectiveTitle: executive.objectiveTitle,
				executed: executive.executed
			})))
		}*/

		this.contentEl.empty()
		this.contentEl.addClass("plaintorch-briefing-view")
		this.contentEl.addClass("plaintorch-root")
		const elem = this.contentEl.createEl("p7t-briefing")
	}

	private createCard(parent: HTMLElement, title: string, lines: string[]): void {
		const card = parent.createDiv({ cls: "plaintorch-briefing-card" })
		card.createEl("h2", { text: title })

		const list = card.createEl("ul")
		lines.forEach((line) => {
			list.createEl("li", { text: line })
		})
	}

	private createOnrushCard(parent: HTMLElement, briefing: SystemBriefing | undefined): void {
		const card = parent.createDiv({ cls: "plaintorch-briefing-card" })
		card.createEl("h2", { text: "Onrush" })

		const list = card.createEl("ul")
		;(briefing?.currentOnrush
			? [
				`Selection: ${briefing.onrushSelectionMode ?? "unknown"}`,
				`Sprint: ${briefing.currentOnrush.id} — ${briefing.currentOnrush.title}`,
				`Dates: ${this.formatDateRange(briefing.currentOnrush.startDate, briefing.currentOnrush.endDate)}`,
				`Objectives: ${briefing.currentOnrush.objectives.length}`
			]
			: ["No active or planned onrush sprint"])
		.forEach((line) => list.createEl("li", { text: line }))

		const actions = card.createDiv({ cls: "plaintorch-inline-actions" })
		const button = actions.createEl("button", {
			text: !briefing?.currentOnrush
				? "Start new"
				: briefing.onrushSelectionMode === "planning"
					? "Start new"
					: "Conclude"
		})

		button.addEventListener("click", () => {
			void this.handleOnrushAction(briefing?.onrushSelectionMode, briefing?.currentOnrush?.id)
		})
	}

	private createPolarisCard(parent: HTMLElement, briefing: SystemBriefing | undefined): void {
		const card = parent.createDiv({ cls: "plaintorch-briefing-card" })
		card.createEl("h2", { text: "Polaris" })

		const list = card.createEl("ul")
		;(briefing?.currentPolaris
			? [
				`Cycle: ${briefing.currentPolaris.id} — ${briefing.currentPolaris.title}`,
				`Forecast: ${briefing.currentPolaris.isForecast ? "yes" : "no"}`,
				`Started: ${briefing.currentPolaris.startTime ?? "not started"}`,
				`Executives: ${briefing.currentPolaris.executives.length}`
			]
			: ["No active Polaris cycle"])
		.forEach((line) => list.createEl("li", { text: line }))

		const actions = card.createDiv({ cls: "plaintorch-inline-actions" })
		const button = actions.createEl("button", {
			text: !briefing?.currentPolaris
				? "Start new"
				: briefing.currentPolaris.isForecast
					? "Start new"
					: "Conclude"
		})

		button.addEventListener("click", () => {
			void this.handlePolarisAction(briefing?.currentPolaris?.isForecast ?? false, !!briefing?.currentPolaris)
		})
	}

	private createObjectiveCollectionCard(parent: HTMLElement, title: string, items: Array<{ puck: string; headline: string; detail: string }>): void {
		const card = parent.createDiv({ cls: "plaintorch-briefing-next-actions" })
		card.createEl("h2", { text: title })

		if (items.length === 0) {
			card.createEl("p", { text: "None" })
			return
		}

		const list = card.createEl("ul")
		items.forEach((itemData) => {
			const item = list.createEl("li")
			const link = item.createEl("button", { cls: "plaintorch-text-link", text: itemData.headline })
			link.addEventListener("click", () => {
				void this.openPlaintorchFileByPuck(itemData.puck)
			})
			item.createEl("div", { text: itemData.detail })
		})
	}

	private createExecutiveCollectionCard(
		parent: HTMLElement,
		items: Array<{ headline: string; objectivePuck?: string; objectiveTitle?: string; executed: boolean }>
	): void {
		const card = parent.createDiv({ cls: "plaintorch-briefing-next-actions" })
		card.createEl("h2", { text: "Polaris executives" })

		if (items.length === 0) {
			card.createEl("p", { text: "None" })
			return
		}

		const list = card.createEl("ul")
		items.forEach((itemData) => {
			const item = list.createEl("li")
			item.createEl("strong", { text: itemData.headline })
			const detail = item.createEl("div")
			detail.textContent = `Executed: ${itemData.executed ? "yes" : "no"}`

			if (itemData.objectivePuck) {
				const separator = detail.createEl("span", { text: " • Objective: " })
				separator.addClass("plaintorch-inline-muted")
				const objectiveLink = detail.createEl("button", {
					cls: "plaintorch-text-link",
					text: itemData.objectiveTitle
						? `${itemData.objectivePuck} — ${itemData.objectiveTitle}`
						: itemData.objectivePuck
				})
				objectiveLink.addEventListener("click", () => {
					void this.openPlaintorchFileByPuck(itemData.objectivePuck!)
				})
			}
		})
	}

	private async handleOnrushAction(selectionMode?: string, onrushId?: string): Promise<void> {
		const coreClient = await getPlaintorchNodeCoreClient()
		const succeeded = !onrushId
			? await coreClient.onrush.startNew()
			: selectionMode === "planning"
				? await coreClient.onrush.begin(onrushId)
				: await coreClient.onrush.end(onrushId)

		if (succeeded) {
			await this.render()
		}
	}

	private async handlePolarisAction(isForecast: boolean, hasCurrent: boolean): Promise<void> {
		const coreClient = await getPlaintorchNodeCoreClient()
		const succeeded = !hasCurrent
			? await coreClient.polaris.startNew()
			: isForecast
				? await coreClient.polaris.begin()
				: await coreClient.polaris.end()

		if (succeeded) {
			await this.render()
		}
	}

	private formatDateRange(startDate?: string, endDate?: string): string {
		if (!startDate && !endDate) {
			return "unscheduled"
		}

		return `${startDate ?? "?"} → ${endDate ?? "?"}`
	}

	private capitalize(value: string): string {
		return value.length === 0 ? value : value[0]?.toUpperCase() + value.slice(1)
	}

	private async openPlaintorchFileByPuck(puck: string): Promise<boolean> {
		const normalizedPuck = puck.trim().toLowerCase()
		const file = this.app.vault.getMarkdownFiles().find((candidate) => {
			const basename = candidate.basename.toLowerCase()
			return basename === normalizedPuck || basename.startsWith(`${normalizedPuck} - `)
		})

		if (!file) {
			return false
		}

		await this.app.workspace.openLinkText(file.path, "", true)
		return true
	}
}

async function detectPlaintorchEntity(
	coreClient: PlaintorchNodeCoreClient,
	filePath: string,
	fallbackTitle: string
): Promise<PlaintorchEntityDescriptor | undefined> {
	const coreResult = await coreClient.system.resolveNote(filePath)
	if (!coreResult?.isPlaintorchEntity || !coreResult.entityKind) {
		return undefined
	}

	const kind = normalizeKind(coreResult)
	if (!kind) {
		return undefined
	}

	return {
		kind,
		label: entityMetadata[kind].label,
		title: coreResult.title ?? fallbackTitle,
		puck: coreResult.puck
	}
}

async function getPlaintorchNodeCoreClient(): Promise<PlaintorchNodeCoreClient> {
	if (cachedCoreClient) {
		return cachedCoreClient
	}

	const module = await import("@pleiades/sdk/plaintorch/node")
	cachedCoreClient = module.plaintorchNodeCoreClient
	return cachedCoreClient
}

function normalizeKind(result: VaultNoteAuthorityResolution): PlaintorchEntityKind | undefined {
	if (!result.entityKind) {
		return undefined
	}

	const normalized = result.entityKind.toLowerCase().replace(/[\s_]+/g, "-")
	const collapsed = normalized.replace(/-/g, "")
	return kindAliases.get(normalized) ?? kindAliases.get(collapsed)
}
