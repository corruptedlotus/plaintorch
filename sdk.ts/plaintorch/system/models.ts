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
	/** The watcher's rolled-up health, as {@link WatcherIssueReport.status} reports it (see {@link WatcherHealth}). */
	watcherStatus: WatcherHealth | (string & {})
	watcherIssueCount: number
	/** How many live (undismissed) issues are critical or fatal — those whose {@link WatcherIssueRecord.isCritical} is set. */
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

/**
 * A watcher issue's graded severity (PEP108), mildest first — the core sorts a report's issues worst-first:
 * - `info`: rare, with no consequence; it does not affect health.
 * - `warning`: no breaking consequence, but best resolved (a locked file, content the core already enforced).
 * - `error`: invalid or illegal content the user must resolve; left unresolved, an entity may not sync or may corrupt.
 * - `critical`: a technical failure keeps the watcher from part of its job (permission denied, a failed sync, a root not
 *   watched).
 * - `fatal`: the watcher cannot run or do its job at all, and health reads `standby`. An unreachable vault, a failed
 *   sweep or unresolvable roots put it to sleep and it retries on its own; a crashed session waits for the next vault
 *   activation (each issue's message says which). A fatal issue cannot be dismissed.
 */
export type WatcherSeverity = "info" | "warning" | "error" | "critical" | "fatal"

/**
 * The watcher's rolled-up health (PEP108), from the worst live (undismissed) severity:
 * - `ok`: no live issues, or only `info` ones.
 * - `issues`: the worst is an `error` or a `warning`.
 * - `critical`: the worst is `critical`; the watcher runs, but part of its work is blocked.
 * - `standby`: the worst is `fatal`, or the watcher sleeps, or no vault is active; it is not watching.
 *
 * A client that cannot fetch a report at all synthesises its own `offline`; the core never sends it.
 */
export type WatcherHealth = "ok" | "issues" | "critical" | "standby"

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
	/** Whether the severity is `critical` or `fatal`: the watcher cannot do part, or all, of its job. */
	isCritical: boolean
	/**
	 * The graded severity (PEP108; see {@link WatcherSeverity}): `info` < `warning` < `error` < `critical` < `fatal`.
	 * Typed open so a severity a newer core adds still reads.
	 */
	severity: WatcherSeverity | (string & {})
	/** The vault-relative files involved in this status. */
	files: string[]
	criterion: string
	resolutionCriterion: string
	occurrenceCount: number
	originPath: string | undefined
	originVaultRelativePath: string | undefined
	firstObservedUtc: string | undefined
	lastObservedUtc: string | undefined
	/**
	 * Whether the user has dismissed this issue (PEP108): excluded from health and counts, kept for a "Dismissed" view.
	 * Never set on a `fatal` issue — the core refuses to dismiss one.
	 */
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
	/** The watcher's rolled-up health (see {@link WatcherHealth}). Typed open so a health a newer core adds still reads. */
	status: WatcherHealth | (string & {})
	issueCount: number
	/** How many live (undismissed) issues are critical or fatal — those whose {@link WatcherIssueRecord.isCritical} is set. */
	criticalIssueCount: number
	criteriaCount: number
	failedCriteriaCount: number
	scopedPath: string | undefined
	scopedPathIsDirectory: boolean
	issues: WatcherIssueRecord[]
	criteria: WatcherCriterionRecord[]
}
