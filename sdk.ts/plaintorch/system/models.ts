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

/** The core's lifecycle phase, as `PlaintorchHostPhase` serializes it. */
export type CorePhase = "Starting" | "Idle" | "Activating" | "Active" | "Failed" | "Stopping"

export interface HealthStatus {
	status: string
	/** `idle` while no vault is served, `active` once one is. */
	mode: "idle" | "active"
	/** The vault being served, when any. */
	activeVault?: string
	/** The finer lifecycle phase behind `mode`, observable even while a vault is still activating or has failed to. */
	phase: CorePhase
	/** A human-readable line describing the phase. */
	message?: string
	/** The vault the phase concerns, when any (a failed activation names the vault that failed). */
	vault?: string
	/** Whether the watcher's startup sweep is still running (the core is Active and serving while it does). */
	sweeping?: boolean
	/** The transport endpoint the core answered on. */
	endpoint: string
	/** When the current phase began. */
	since: string
}

export interface WatcherIssueRecord {
	key: string
	type: string
	category: string
	/** The reason's generic descriptor message — what this kind of issue means. */
	message: string
	/**
	 * The specific detail of this occurrence (the policy's reason, an exception message, the offending files), or
	 * undefined when the reason alone says it all. Separate from `message` so a surface can show, fold, or hide it.
	 */
	detail: string | undefined
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
	/** Whether the user has dismissed this issue (PEP108): excluded from health and counts, kept for a "Dismissed" view. */
	dismissed: boolean
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
