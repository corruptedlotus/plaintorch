import type { LorePage } from "../lore/models"
import type { OnrushSprint } from "../onrush/models"
import type { PolarisCycle } from "../polaris/models"

export interface VaultNoteAuthorityResolution {
	vaultRelativePath: string
	isPlaintorchEntity: boolean
	entityKind: string | undefined
	entityName: string | undefined
	tagName: string | undefined
	puck: string | undefined
	title: string | undefined
}

export interface EntityExistence {
	puck: string
	exists: boolean
	entityType: string | undefined
	entity: unknown
	associatedNote: string | undefined
}

export interface SystemBriefing {
	status: string
	timestamp: string
	activeVaultPath: string
	pleiadeanToday: string
	celestronBanked: number
	onrushSelectionMode: string | undefined
	currentOnrush: OnrushSprint | undefined
	currentPolaris: PolarisCycle | undefined
	activeLorePages: LorePage[]
}

export interface HealthStatus {
	status: string
}
