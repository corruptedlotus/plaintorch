import type { PlaintorchCoreClient } from "../coreClient"
import type { EntityExistence, HealthStatus, SystemBriefing, WatcherIssueReport } from "./contracts"

export class PlaintorchSystemSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	/**
	 * Resolves which entity a vault note is the authority for.
	 *
	 * Uncached on purpose: `repos.noteResolution` owns freshness for this. A second cache behind it would
	 * make a forced refresh silently no-op for as long as the inner entry lived.
	 */
	public async resolveNote(vaultRelativePath: string): Promise<EntityExistence | undefined> {
		const normalizedPath = normalizeVaultRelativePath(vaultRelativePath)
		return await this.client.getJson<EntityExistence>(
			`/api/system/resolve-note?path=${encodeURIComponent(normalizedPath)}`
		)
	}

	public async getBriefing(): Promise<SystemBriefing | undefined> {
		return await this.client.getJson<SystemBriefing>("/api/system/briefing")
	}

	public async resolveEntity(puck: string): Promise<EntityExistence | undefined> {
		const normalizedPuck = puck.trim()
		if (!normalizedPuck) {
			return undefined
		}

		return await this.client.getJson<EntityExistence>(`/api/system/resolve/${encodeURIComponent(normalizedPuck)}`)
	}

	public async getHealth(): Promise<HealthStatus | undefined> {
		return await this.client.getJson<HealthStatus>("/healthz")
	}

	public async getWatcherIssues(): Promise<WatcherIssueReport | undefined> {
		return await this.client.getJson<WatcherIssueReport>("/api/system/watcher/issues")
	}

	public async getWatcherIssuesForPath(vaultRelativePath: string): Promise<WatcherIssueReport | undefined> {
		const normalizedPath = normalizeVaultRelativePath(vaultRelativePath)
		if (!normalizedPath) {
			return undefined
		}

		return await this.client.getJson<WatcherIssueReport>(
			`/api/system/watcher/issues-for?path=${encodeURIComponent(normalizedPath)}`
		)
	}

	/**
	 * Dismisses a watcher issue (PEP108 dismiss feature) by its opaque {@link WatcherIssueRecord.key}, so it stops
	 * counting toward health and nagging. The default `instance` scope snoozes just this issue until a different
	 * problem arises on the same file; `file` / `reason` are the reserved broader scopes. Resolves to whether a
	 * dismissal was recorded — false for a `fatal` issue, which the core refuses to dismiss.
	 */
	public async dismissWatcherIssue(issueKey: string, scope?: WatcherIssueDismissalScope): Promise<boolean> {
		const result = await this.client.postForJson<boolean>("/api/system/watcher/issues/dismiss", { key: issueKey, scope })
		return result ?? false
	}

	/** Restores (un-dismisses) a watcher issue so it counts and surfaces again. Resolves to whether one was removed. */
	public async restoreWatcherIssue(issueKey: string, scope?: WatcherIssueDismissalScope): Promise<boolean> {
		const result = await this.client.postForJson<boolean>("/api/system/watcher/issues/restore", { key: issueKey, scope })
		return result ?? false
	}
}

/** The breadth a watcher-issue dismissal applies to. Only `instance` is surfaced today; `file`/`reason` are reserved. */
export type WatcherIssueDismissalScope = "instance" | "file" | "reason"

function normalizeVaultRelativePath(value: string): string {
	return value.replaceAll("\\", "/").replace(/^\/+/, "")
}