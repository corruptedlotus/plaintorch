import { Controller, type ReactiveElement } from "@a11d/lit"
import type { WatcherHealth as CoreWatcherHealth } from "@pleiades/sdk"
import { core } from "../data"
import { subscribeTick } from "./globalTick"

export type WatcherIssueReport = NonNullable<Awaited<ReturnType<typeof core.system.getWatcherIssues>>>
export type WatcherIssueRecord = WatcherIssueReport["issues"][number]

/**
 * The report's rolled-up health (PEP108) — the core's `ok`, `issues`, `critical` or `standby` (a fatal problem, the
 * watcher asleep, or no vault active) — or `offline` when no report can be fetched.
 */
export type WatcherHealth = CoreWatcherHealth | "offline" | (string & {})

/** How often the report is fetched while anything shows it, in ticks of the app-wide clock (seconds). */
const pollTicks = 5

const healthLabels: Readonly<Record<string, string>> = {
	ok: "Healthy",
	issues: "Issues",
	critical: "Critical",
	standby: "Standby",
	offline: "Offline",
} satisfies Record<CoreWatcherHealth | "offline", string>

/** The display name of a health. */
export function watcherHealthLabel(health: WatcherHealth): string {
	return healthLabels[health] ?? health
}

type ReportListener = (report: WatcherIssueReport | undefined) => void

const listeners = new Set<ReportListener>()
let latest: WatcherIssueReport | undefined
let fetched = false
let stopTicking: (() => void) | undefined
let ticks = 0

/**
 * Fetches the watcher report now and hands it to every subscriber — after a dismissal, say, so every surface showing
 * the report agrees at once rather than at the next poll. A failed fetch reads as no report (offline).
 */
export async function refreshWatcherReport(): Promise<void> {
	let report: WatcherIssueReport | undefined
	try {
		report = await core.system.getWatcherIssues() ?? undefined
	}
	catch {
		report = undefined
	}

	latest = report
	fetched = true
	for (const listener of [...listeners]) {
		listener(report)
	}
}

/**
 * Subscribes to the core's watcher report (PEP108) and returns an unsubscribe. One poll serves the whole document —
 * the status-bar indicator and a status card alike — riding the app-wide clock rather than an interval of its own: it
 * starts with the first subscriber, which gets a fresh report at once, and stops with the last.
 */
export function subscribeWatcherReport(listener: ReportListener): () => void {
	listeners.add(listener)
	if (fetched) {
		listener(latest)
	}

	if (!stopTicking) {
		ticks = 0
		void refreshWatcherReport()
		stopTicking = subscribeTick(() => {
			if (++ticks % pollTicks === 0) {
				void refreshWatcherReport()
			}
		})
	}

	return () => {
		listeners.delete(listener)
		if (listeners.size === 0) {
			stopTicking?.()
			stopTicking = undefined
		}
	}
}

/**
 * The watcher report for a component, kept current while it is connected: `report`, its `health`, and its issues
 * split into the `live` ones (what health and counts reflect) and the `dismissed` ones.
 */
export class WatcherReportController extends Controller {
	report?: WatcherIssueReport
	private unsubscribe?: () => void

	public constructor(host: ReactiveElement) {
		super(host)
	}

	public override hostConnected(): void {
		this.unsubscribe ??= subscribeWatcherReport(report => {
			this.report = report
			this.host.requestUpdate()
		})
	}

	public override hostDisconnected(): void {
		this.unsubscribe?.()
		this.unsubscribe = undefined
	}

	get health(): WatcherHealth {
		return this.report?.status ?? "offline"
	}

	get live(): WatcherIssueRecord[] {
		return this.report?.issues.filter(issue => !issue.dismissed) ?? []
	}

	get dismissed(): WatcherIssueRecord[] {
		return this.report?.issues.filter(issue => issue.dismissed) ?? []
	}
}
