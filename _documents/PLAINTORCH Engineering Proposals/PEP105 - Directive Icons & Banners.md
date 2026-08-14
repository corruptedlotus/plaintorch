---
status: implemented
patches:
  - Patch105.1 - Vault Media Subsystem
  - Patch105.2 - Directive Icons & Banners
assignee: Soraya 🧙‍♀️
---
# Patches
Directives gain a custom **icon** and a **banner** image. The title says "directive", but the machinery is not directive-shaped: what a directive references is a general **media subsystem** that any entity can later attach an image to. So this lands in two layers — the subsystem first, then directives as its first consumer.

## Patch105.1 - Vault Media Subsystem
Binary assets now live in the vault beside the entity that owns them, tracked by nothing but their key.

### Asset folders and two schemes
A media key resolves two ways. **`media:`** is self/level media — a file in the owning entity's own **`_assets`** folder (inside its directory when folder-backed, or under `_assets/{fileBaseName}` beneath the location root when monofile). **`vault:`** is vault-level shared media — a file in the vault root's `_assets` folder, referenceable from anywhere. A key with neither scheme is a plain glyph name. The leading underscore is load-bearing — `VaultWatcherPathPolicy` already skips any path segment beginning with `_`, so images are never mistaken for markdown. No new database entity, no discovery, no sync: an asset is just a file, and the reference to it is a string.

### Model-agnostic resolution
A **`[Media]`** attribute marks a string key property; its resolved view lands in a sibling **`{Name}Media`** companion (a `[NotMapped] MediaReference`) — for an `Icon` key, an `IconMedia`. `VaultMediaService.EnrichMedia` reflects over an entity's `[Media]` keys and fills each companion, so any entity attaches media without new code. A `MediaReference` carries the raw **key**, its **type** (`icon` · `media` · `vault`), and — for custom media — the resolved vault-relative **path**; a client renders straight from it without knowing the asset-folder convention. Only the entity's own self-folder is model-specific, and it is resolved (and the enrichment run) only when a `media:` key is actually present.

### The service
`VaultMediaService` (`core/Vault/Media/VaultMediaService.cs`) is entity-agnostic — it takes a resolved markdown path plus a `VaultStorageShape`, never an entity type. It resolves an asset folder (`GetAssetFolder`), writes an upload into it (`StoreAsync` — sanitising the file name, enforcing an image-extension allow-list and an 8 MB cap, suppressing the watcher around the write), resolves a stored file name to a forward-slashed vault-relative path (`ResolveVaultRelative`), removes one through the file graveyard rather than a hard delete (`DeleteAsync` → `VaultTemporalDataService.ArchivePathAsync`, recoverable and audited), and turns any key into a `MediaReference` (`ResolveReference` / `ParseKey`, with `ToSelfReference` / `ToVaultReference` on the write side).

## Patch105.2 - Directive Icons & Banners
A directive is the media subsystem's first consumer: a custom icon and a header image, both keyed and resolved through the `[Media]` machinery.

### The fields
`Directive` gains two `[MarkdownField] [Media]` keys on the shared base, so both stellar and lunar kinds inherit them: `icon` (a glyph/lucide name, a `media:` self image, or a `vault:` shared image) and `banner` (a `media:` or `vault:` image). Both round-trip through frontmatter like `codename` and `tags` — no watcher DB-only-clobber hazard applies — and each carries its transient `[NotMapped] MediaReference` companion, `IconMedia` / `BannerMedia`, filled on the way out. One migration, `AddDirectiveIconAndBanner`, adds the two nullable columns.

### The API
`PUT /api/directives/{id}/icon` and `.../banner` (both kinds) take a raw `reference` key, an `upload` (with a `vault` flag choosing the shared root over the directive's own folder), or a `clear`. The core owns every write: it decodes the Base64 upload, stores it through `VaultMediaService`, sets the keyed reference, and archives the previous image when it is orphaned — **but only self (`media:`) media**, since a `vault:` file is shared and a glyph has no file. Reads (get/list/find and the mutation responses) run the reflective enrichment; a directive with no `media:` key never touches the database or filesystem for its self-folder. Entity→note association is a known gap for directives (they are excluded from path-sync scanning), so the self asset folder is composed from the directive's **canonical** location via `MarkdownFileLocator` rather than a resolved note path.

### The client
The SDK model grows `icon`/`banner` keys and their `iconMedia`/`bannerMedia` companions (`MediaReference` = `{ key, type, path }`), the request DTOs, and ergonomic `uploadIcon` / `setIconReference` / `setIconGlyph` / `clearIcon` (+ banner peers, all Base64-encoding without `btoa`/`Buffer`, with a `vault` option on upload). On the plugin side: `p7t-icon` renders a full-colour `<img>` when its value is a resolved resource URL instead of a monochrome glyph mask, so a media icon works anywhere the element is used; `resolveMediaIcon` / `mediaUrl` turn a `MediaReference` into a glyph name or a resource URL; `pickImageFile` reads a chosen image's bytes for upload; and the shim gains `getResourcePath`/`getFileByPath`.

### The banner
A shared `DirectiveBanner` base drives both directive banners: it renders the directive's icon (its custom image — self or vault — its glyph, or the per-kind default in that order) in the identity grid, an optional full-width header image above it (a new `bannerImageTemplate` slot on `EntityBanner`, off by default so any entity can opt in later), and the affordances to set, replace, and clear each through the SDK.

### Open edges
- **Icon reach.** Custom icons render in the directive banner. Carrying them into the entity grid/tree and the dependency-canvas nodes is left as deliberate follow-up design — the helpers (`p7t-icon` media branch, `resolveMediaIcon`, the `MediaReference` companion) make that a wiring step, not new plumbing.
- **Monofile assets.** `VaultMediaService` composes a self asset folder for both storage shapes, but only the folder-backed directive path is exercised today; the monofile layout awaits its first consumer.
- **Freeform placement.** A directive may live anywhere (`Freeform`), yet its self asset folder is composed from its canonical location — the same root cause as the unresolved directive→note association. A directive relocated away from canonical would look for its self assets in the wrong place; closing this rides on the registry/shape-strategy refactor.
- **Transport.** Uploads travel as Base64 over the loopback transport, which carries JSON only; the 8 MB cap keeps that honest until a media surface needs something larger.
