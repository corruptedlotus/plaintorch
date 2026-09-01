import type { IndexedLorePage } from "../lore/models"
import type { OnrushSprint } from "../onrush/models"
import type { PolarisCycle } from "../polaris/models"

export interface EntityExistence {
	puck: string
	exists: boolean
	entityType: string | undefined
	entityKind: string | undefined
	entity: unknown
	associatedNote: string | undefined
}

export interface SystemBriefing {
	status: string
	timestamp: string
	activeVaultPath: string
	pleiadeanToday: string
	celestronBanked: number
	watcherStatus: string
	watcherIssueCount: number
	watcherCriticalIssueCount: number
	watcherCriteriaCount: number
	watcherFailedCriteriaCount: number
	onrushSelectionMode: string | undefined
	currentOnrush: OnrushSprint | undefined
	currentPolaris: PolarisCycle | undefined
	activeLorePages: IndexedLorePage[]
}

export interface HealthStatus {
	status: string
}

export interface WatcherIssueRecord {
	key: string
	type: string
	category: string
	message: string
	isCritical: boolean
	/** Graded severity: `info` | `warning` | `suspended` | `error` | `critical` (PEP108). */
	severity: string
	/** The vault-relative files involved in this status. */
	files: string[]
	criterion: string
	resolutionCriterion: string
	occurrenceCount: number
	originPath: string | undefined
	originVaultRelativePath: string | undefined
	firstObservedUtc: string | undefined
	lastObservedUtc: string | undefined
}

export interface WatcherCriterionRecord {
	criterion: string
	satisfied: boolean
	evaluatedUtc: string
	scopeKey: string | undefined
	originPath: string | undefined
	originVaultRelativePath: string | undefined
	detail: string | undefined
}

export interface WatcherIssueReport {
	status: string
	issueCount: number
	criticalIssueCount: number
	criteriaCount: number
	failedCriteriaCount: number
	scopedPath: string | undefined
	scopedPathIsDirectory: boolean
	issues: WatcherIssueRecord[]
	criteria: WatcherCriterionRecord[]
}
