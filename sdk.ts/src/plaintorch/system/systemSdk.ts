import type { PlaintorchCoreClient } from "../coreClient";
import type { PlaintorchBriefing, PlaintorchCoreNoteResolution } from "./contracts";

interface CacheEntry {
  expiresAt: number;
  value: PlaintorchCoreNoteResolution | null;
}

export class PlaintorchSystemSdk {
  private readonly noteResolutionCache = new Map<string, CacheEntry>();

  public constructor(
    private readonly client: PlaintorchCoreClient,
    private readonly cacheTtlMs: number
  ) {}

  public async resolveNote(vaultRelativePath: string): Promise<PlaintorchCoreNoteResolution | null> {
    const normalizedPath = normalizeVaultRelativePath(vaultRelativePath);
    const cached = this.noteResolutionCache.get(normalizedPath);
    if (cached && cached.expiresAt > Date.now()) {
      return cached.value;
    }

    const result = await this.client.getJson<PlaintorchCoreNoteResolution>(
      `/api/system/resolve-note?path=${encodeURIComponent(normalizedPath)}`
    );

    this.noteResolutionCache.set(normalizedPath, {
      expiresAt: Date.now() + this.cacheTtlMs,
      value: result
    });

    return result;
  }

  public async getBriefing(): Promise<PlaintorchBriefing | null> {
    return await this.client.getJson<PlaintorchBriefing>("/api/system/briefing");
  }
}

function normalizeVaultRelativePath(value: string): string {
  return value.replaceAll("\\", "/").replace(/^\/+/, "");
}