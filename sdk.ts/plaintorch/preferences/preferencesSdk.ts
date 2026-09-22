import type { PlaintorchCoreClient } from "../coreClient"
import type { PreferenceView } from "./contracts"

/**
 * Reads and writes vault-bound user preferences (PEP116). The listing is registry-driven by the core, so a new
 * preference appears here automatically without any client change.
 */
export class PlaintorchPreferencesSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	/** Every known preference, with its current resolved value and its default. */
	public async list(): Promise<PreferenceView[]> {
		return (await this.client.getJson<PreferenceView[]>("/api/preferences")) ?? []
	}

	/**
	 * Sets a preference. Resolves to the updated view, or `undefined` when the key is unknown or the value was
	 * rejected as the wrong kind.
	 */
	public async set(key: string, value: number | boolean | string): Promise<PreferenceView | undefined> {
		return await this.client.putForJson<PreferenceView>(`/api/preferences/${encodeURIComponent(key)}`, { value })
	}

	/** Resets a preference to its default, removing any stored override. Resolves to whether it succeeded. */
	public async reset(key: string): Promise<boolean> {
		return await this.client.delete(`/api/preferences/${encodeURIComponent(key)}`)
	}
}
