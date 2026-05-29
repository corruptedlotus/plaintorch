import type { PlaintorchCoreClient } from "../coreClient"
import type { EntityExistence, HealthStatus, SystemBriefing, VaultNoteAuthorityResolution, WatcherIssueReport } from "./contracts"
interface CacheEntry {
	expiresAt: number
	value: VaultNoteAuthorityResolution | undefined
}

export class PlaintorchSystemSdk {
	private readonly noteResolutionCache = new Map<string, CacheEntry>()
	public constructor(
		private readonly client: PlaintorchCoreClient,
		private readonly cacheTtlMs: number
	) { }

	public async resolveNote(vaultRelativePath: string): Promise<VaultNoteAuthorityResolution | undefined> {
		const normalizedPath = normalizeVaultRelativePath(vaultRelativePath)
		const cached = this.noteResolutionCache.get(normalizedPath)
		if (cached && cached.expiresAt > Date.now()) {
			return cached.value
		}

		const result = await this.client.getJson<VaultNoteAuthorityResolution>(
			`/api/system/resolve-note?path=${encodeURIComponent(normalizedPath)}`
		)
		this.noteResolutionCache.set(normalizedPath, {
			expiresAt: Date.now() + this.cacheTtlMs,
			value: result
		})
		return result
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
}

function normalizeVaultRelativePath(value: string): string {
	return value.replaceAll("\\", "/").replace(/^\/+/, "")
}