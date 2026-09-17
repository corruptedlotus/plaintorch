---
status: implemented
assignee: Copilot 🤖
phase: 2a
---
# Vault Migration System
PLAINTORCH keeps state in two coupled places: the EF Core database inside `.plaintorch-data`, and the human-authored markdown files of the vault itself. Database schema changes are already handled through EF Core migrations. Vault changes are not.

This PEP introduces a **vault migration system** that runs alongside database migration and preserves the contents of the vault while upgrading its on-disk conventions to match the current engine.

## Motivation
PEP091 is the first change that requires it. `Objective` moved from filename-embedded PUCK (`{id} - Title.md`) to frontmatter PUCK (`Title.md` with a `puck` field). Existing objective files on disk are no longer recognised by the current engine until they are converted. Every future change to a storage convention — filename form, frontmatter keys, folder shape, partition folders, location roots, PUCK grammar — has the same problem.

There is currently no version marker for the vault anywhere. The vault-local `.plaintorch` settings document only stores location keys, and the only applied-change ledger in the system is EF's own `__EFMigrationsHistory`.

## Model: Loader, then Re-canonicalisation
Vault migration is **not** a per-file diff. It is a two-phase pass:

1. **Load.** The vault is read with the *loader for the version it is currently stored at*. That loader reconstructs the full canonical state of every vault entity (identity, fields, and markdown body) using the conventions of that stored version.
2. **Re-canonicalise.** The current engine re-emits that loaded state using the *current* conventions — renaming, relocating, and rewriting frontmatter as needed — while preserving each entity's markdown body verbatim.

The database is the canonical intermediate between the two phases. Markdown bodies, which live only in files, are carried across the round-trip untouched.

## Convention Records
Each vault version has a **convention record**: a declarative description of where its files live and how they are read (location keys, storage mode, shape, PUCK storage form, filename grammar, the PUCK frontmatter key, per-field frontmatter keys, partitioning, and parent nesting). These records are the "loaders": a single generic reader is parameterised by whichever convention record matches the vault's stored version.

A convention record that cannot be expressed declaratively may be backed by a hand-written loader for that specific version, but this is expected to be rare.

Extracting these conventions out of the code that currently hardcodes them (they are spread across the storage attribute, the file locator, the frontmatter serializer, and several duplicated string literals) is the substantive work of this PEP, and it doubles as the first real abstraction of the vault↔database boundary.

## Versioning and History
- The vault records its current schema version in the vault-local `.plaintorch` document. A missing version is treated as the pre-migration baseline.
- Migrations are ordered and gated by that version. On startup, if the stored version is behind the current version, the pending migrations run in order and the version is advanced.
- Each applied migration is recorded in a vault migration history table (version, timestamp, actor, and counts of what was loaded, rewritten, and archived), complementing the per-file audit-log and graveyard records that already exist.

## Ordering
Vault migration runs during vault initialisation, **after** database migration (its state-driven phase depends on an up-to-date schema) and **before** the existing consistency reconciliation and lore reindex. This mirrors and sits alongside the database migration step rather than replacing it.

## Safety
Vault migration must preserve vault contents. It reuses the machinery already built for this:
- markdown **body preservation** and identity-based file location, rename, and relocation through the canonical storage service;
- the **file graveyard** as a rollback substrate — every file a migration replaces is archived first;
- **write-barrier suppression** so the live watcher does not fight the migration;
- **idempotent, resumable** execution — transforms are identity-keyed and no-op on unchanged content, so a re-run or a crash-recovery run is safe. The version advance is the commit point.

## First Migration
The first migration is the PEP091 objective canonicalisation: load objectives under the pre-PEP091 conventions (filename-embedded PUCK, no `puck` frontmatter), re-emit them as title-only files with `puck` frontmatter, and record their implicit synchronisation boundary since their files now exist.

---

# Plan of Action

## Context
The system stores state in the EF Core database and in vault markdown files, which must stay in lockstep. Database migrations exist; vault migrations do not, and no vault version marker exists anywhere. PEP091 created the first concrete need (objective files must be converted from `{id} - Title.md` to `Title.md` + `puck` frontmatter). This plan builds the general framework, then lands PEP091 as its first migration to validate it end-to-end.

## Core model
```
stored SchemaVersion (.plaintorch)  <  current SchemaVersion (code)
        │
        ▼
   LOAD  ── VaultLoader(conventions[stored]) reads every vault file →
            reconstructs entity state (identity + fields + body)
        │
        ▼
   RE-CANONICALISE ── current engine re-emits each entity under conventions[current]
                      (rename / relocate / rewrite frontmatter, preserve body), archiving old files first
        │
        ▼
   bump stored SchemaVersion, record history
```
The migration is "read with the old convention set, write with the new." Most convention changes need zero bespoke code — they are captured entirely by the difference between the stored and current convention records.

## Central abstraction: `VaultConventionSet`
A serializable, version-frozen descriptor consumed by both the loader (read) and the writer (emit), capturing per entity type what today lives in `[VaultStorage]` and its helpers:
- `LocationKey`, `Mode`, `Shape`, `PuckStorage`, `PartitionUnder`, parent nesting
- filename grammar (`PuckNamedIdentity.Separator = " - "`; title-only vs `{id} - Title`)
- the PUCK frontmatter key (the literal `"puck"` is currently duplicated in four places: `MarkdownFrontMatterSerializer.QuietPuckFieldName`, `MarkdownFileLocator.TryReadFrontMatterPuck`, `VaultMarkdownDiscoveryService.ResolveFreeformDirectiveParentIdAsync`, `PlaintorchMarkdownStorageService.IsIdentityMatchAsync`)
- per-field frontmatter key map (`[MarkdownField(...)]`)
- scalar formatting rules (`MarkdownFrontMatterSerializer.FormatScalar`)

Extraction also fixes latent coupling: `GetOnrushSprintFilePath` / `GetPolarisCycleFilePath` / `GetLorePageFilePath` hardcode the Index filename form instead of honouring `PuckStorage`; partition names are cached in `VaultPathSyncModelCatalog`.

### Representing old loaders
- **Primary:** declarative convention snapshots frozen in code — one immutable `VaultConventionSet` per historical version (mirrors EF's model-snapshot-in-code pattern); a single generic `VaultLoader` reads according to whichever set it is handed.
- **Escape hatch:** allow registering a bespoke `IVaultLoader` for a version whose reading logic cannot be expressed declaratively.

The current pipeline (`VaultMarkdownDiscoveryService`, `MarkdownFileLocator`, `MarkdownFrontMatterSerializer`) becomes the current-version loader/writer once it reads conventions from a passed-in `VaultConventionSet` (defaulting to the live one built from `[VaultStorage]`) instead of reflecting attributes inline.

## What is recorded
1. **Version pointer** — `int SchemaVersion` added to `VaultSettings` (`core/Vault/VaultSettings.cs`), persisted in `.plaintorch` via its existing `Load`/`Save`; missing ⇒ pre-migration baseline.
2. **Convention snapshots** — the frozen `VaultConventionSet` per version, in code.
3. **Applied-migration history** — a `VaultMigrationHistory` table (`Version`, `AppliedUtc`, `AppliedBy`, `DurationMs`, `EntitiesLoaded`, `FilesRewritten`, `FilesArchived`, `Conflicts`, `Outcome`), complementing the existing `AuditLogEntry` (category `migration`) and `FileGraveyardEntry` provenance/rollback records.

## Integration seam & reuse
- **Hook:** `VaultMigrationRunner.RunAsync` in `PlaintorchEngine.InitializeVaultAsync` (`core/Plaintorch/PlaintorchEngine.cs`), after `bootstrapper.InitializeAsync` and before `autoGeneratedPuckConsistencyService.ReconcileAsync` + lore reindex; mirror in the sync `InitializeVault`.
- **Re-canonicalise primitive:** `PlaintorchMarkdownStorageService.SaveCanonicalMarkdownAsync` — already locates a legacy file by identity (filename PUCK or `puck` frontmatter), renames/relocates, rewrites frontmatter, preserves body + unknown frontmatter keys, suppresses the watcher, and is content-idempotent. Call with `sourcePath` = the loaded legacy file.
- **Rollback:** `VaultTemporalDataService.ArchivePathAsync` (files) / `ArchiveEntityAsync` (rows).
- **Self-write suppression:** `VaultWatcherWriteBarrier.Suppress`.
- **Generalises the existing sweep:** `VaultAutoGeneratedPuckConsistencyService` is the proven catalog-driven, audited, graveyard-backed template; refactor into / subsume by the re-canonicalisation pass rather than duplicate it.

## Build sequence
1. **Version store** — `SchemaVersion` on `VaultSettings`; a `VaultVersionService` to read/bump via `VaultLayout.SettingsPath`; a `const CurrentSchemaVersion`.
2. **`VaultConventionSet` extraction** — introduce the descriptor; build the current set; route `MarkdownFileLocator` and `MarkdownFrontMatterSerializer` through a passed-in set (defaulting to current). *The bulk of the work and the main regression risk.*
3. **Generic `VaultLoader`** — a reader parameterised by a `VaultConventionSet` that reconstructs entity state (identity/fields/body); a convention-parameterised generalisation of `VaultMarkdownDiscoveryService` inspection minus reconcile decisioning.
4. **`VaultMigrationRunner` + `IVaultMigration`** — discover ordered migrations; if `stored < current`, load-with-stored → re-canonicalise-with-next → archive → bump → record history + audit. Include a `--plan`/dry-run report.
5. **History table + EF migration** — `VaultMigrationHistory` entity and `dotnet ef` migration.
6. **DI + hook** — register runner/loader/version service in `PlaintorchModule.cs`; call from `PlaintorchEngine`.
7. **First migration — PEP091 objective canonicalisation** — load objectives under pre-PEP091 conventions, re-canonicalise to Quiet, `EnsureBoundaryBegunAsync` per objective, disambiguate title collisions via the existing `ResolveFreeformFallbackPath`-style logic.

## Verification
- **Unit-ish:** a pre-PEP091 `VaultConventionSet` reads a legacy `j… - Ship it.md` (no `puck` frontmatter) back to the right entity id + body.
- **End-to-end (temp vault):** place a legacy `Objectives/j0000001 - Ship it.md` with a body and matching DB row, set `.plaintorch` `SchemaVersion` to the old value, run `init`/`serve`, and confirm the file becomes `Objectives/Ship it.md` with `puck: j0000001`, body preserved, a `BoundaryBegin` audit row exists, the old file is in the graveyard, `SchemaVersion` is bumped, and a second run is a no-op. Then delete the file and confirm boundary-authoritative DB deletion.
- **Regression:** on an already-current vault the runner short-circuits (`stored == current`); watcher startup and `serve` are unchanged.

## Risks
- **Biggest lift = step 2** (parameterising loader/writer by a convention set); it touches the hot watcher pipeline. Stage it behind the current set as default so behaviour is identical until a migration supplies an older set. Cover with the end-to-end test before wiring PEP091.
- **Body is file-only** — the round-trip must never lose it; the writer path must go through `SaveCanonicalMarkdownAsync`, not raw writes.
- **DB-vs-file source of truth** — for `FileFirst` / identity-driven types the file is authoritative, so the loader (not just the DB) must reconstruct state; this is why the versioned-loader model is more robust than a purely DB-driven backfill.
- Do not confuse the runtime runner with `PlainfraContextDesignTimeFactory` (design-time EF only).
