---
status: implemented
patches:
  - Patch105.1 - Vault Media Subsystem
  - Patch105.2 - Directive Icons & Banners
  - Patch105.3 - Editable Media & the Media Domain
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

## Patch105.3 - Editable Media & the Media Domain
Patch105.2 gave a directive an icon and a banner, but folded two concerns into one call: the endpoint that *set the field* also *stored the file*. That is a domain violation — an icon or a banner can be anything media supports, so wiring the field straight to an upload endpoint conflates selecting a key with storing bytes. This patch splits them. A **media domain** owns storing and listing assets and hands back a key; every field only ever references a key; and a reusable **editable media control** walks a viewer through both steps as one gesture. It also brings custom media to its first inline editors — a timeframe's icon and the directive banner itself.

### Upload and selection are two calls now
- **Selection stays with the field.** The directive icon/banner API is reference-or-clear only: `DirectiveIconRequest` / `DirectiveBannerRequest` lose `upload` and `vault`, and `ApplyMediaChangeAsync` no longer stores anything — it sets a key or clears, still archiving an orphaned self (`media:`) image. The SDK's `uploadIcon` / `uploadBanner` are gone; `setIconReference` / `setIconGlyph` / `clearIcon` (and banner peers) remain. A field holds any key media supports — a glyph, a lucide name, a `media:` or `vault:` file — and knows nothing about how that file came to exist.
- **Storing is the media domain.** `IMediaApi` / `MediaApiService` / `MediaModule` serve `/api/media`: `POST /api/media/vault` and `POST /api/media/entity/{entityType}/{entityId}` decode the upload, store it through the same `VaultMediaService` (write-barrier, graveyard, size and extension guards), and return a `MediaStoreResult` key; the `GET` peers list an asset folder. An entity's self folder resolves through a new shared `MediaAssetFolderResolver` — the composition lifted out of `DirectiveApiService` so the media domain's upload and the directive's own orphan-archival draw on one resolver rather than two copies. On the client a `core.media` SDK (`uploadVault` / `listVault` / `uploadEntity` / `listEntity`) carries the Base64 encoding that moved off the directives SDK.

### The media component
Three plugin components under `obsidian/components/media`, reusable by any media field:
- **`p7t-media`** — a display that resolves a `MediaReference` (or a raw key) to a custom image, a glyph, or the caller's default-when-empty. An `icon` flag constrains it to a contained square; without it a custom picture *covers* its frame, which is what a banner wants. A `vault:` key previews instantly off its deterministic `_assets` path, before the core round-trips a resolved companion back.
- **`SelectMediaModal`** — one suggest box, four modes read off its text. The empty root offers **entity media** (`media:`), **vault media** (`vault:`), an **icon** search, and **remove**; typing a `media:` / `vault:` scheme browses that folder with **upload** always first; anything else searches the Pleiades and lucide icon catalogs, lucide hits keeping their `lucide:` scheme in both results and query. Uploads and listings go through `core.media`. Navigation rows re-drive the box in a new mode *without closing* (an overridden `selectSuggestion`); a terminal row resolves to a key, to `null` to clear, or the modal rejects on dismissal. The catalog is real — Pleiades glyphs from the bundled asset index, and every Obsidian-bundled lucide icon via `getIconIds()`, rewritten from Obsidian's `lucide-` id form to the `lucide:` scheme `p7t-icon` expects.
- **`p7t-editable-media`** — the media member of the editable family (beside the text, orbit, and time editables): it shows the field through `p7t-media`, opens the modal on click, and emits the chosen key (the empty string for cleared) through the editable `change`, leaving the field's owner to persist it. An optional `entity` ref enables the entity-media option; a field whose entity keeps no self folder simply omits it.

### First editors
- **Timeframe icon** — the lunar directive editor (PEP100 Patch100.1) edits it through `p7t-editable-media` in place of a raw-key text field. A timeframe keeps no self asset folder, so the picker offers vault media, icons, and removal — most often a bundled glyph.
- **Directive banner** — `DirectiveBanner`'s icon and header are now `p7t-editable-media`: clicking the icon (a contained square) or the banner (a covering strip, showing a dashed "add banner" affordance while empty) opens the picker, and the chosen key is referenced onto the field — the two-step flow, made inline. A new overridable `iconTemplate` on `EntityBanner` lets a banner swap its display-only icon for an editable one; the old set/change/clear buttons are gone.

### Superseding & still open
- This supersedes Patch105.2's directive **upload** path: those endpoints and SDK helpers are removed, and its "the core owns every write, including the upload" now reads as a split — the media domain owns the write, the field owns the reference. The migrated `VaultMediaTests` exercise both steps end to end (media upload → directive reference).
- Entity-level self folders resolve for directives only; another entity gaining `[Media]` self media adds a branch to `MediaAssetFolderResolver`. Timeframes deliberately have none.
- The picker previews a vault asset inline but a self asset by name, and has no thumbnail grid — a richer browse is a later pass.
