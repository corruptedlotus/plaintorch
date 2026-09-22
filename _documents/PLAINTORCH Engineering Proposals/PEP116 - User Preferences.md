---
status: implemented
assignee: Claude 🤖
phase: 2d
---
# User Preferences

PLAINTORCH has no user-preferences surface. There are two settings stores today — the per-user daemon config (`~/.pleiades/plaintorch/config.json`, `PlaintorchUserConfiguration`, which vault is active) and the per-vault settings file (`.plaintorch`, `VaultSettings`, schema version + location keys) — but nothing a user can tune *about a vault*: default calendar, default time zone, agenda and CalDAV rendering, watcher timings. This PEP adds a **vault-bound user-preferences store**.

The design constraint that shapes everything: preferences must **seed with defaults on a fresh vault** and **gain new defaults cleanly as new preferences are introduced**, without a migration per preference.

## Model: sparse key/value, code-owned defaults, surfaced as Options

- **Storage is a key/value table** in the vault database — `UserPreferenceRecord { Key (PK), Value, UpdatedUtc }`. The value is compact JSON. This is the *only* schema migration preferences ever need; new preferences never touch the schema.
- **Defaults live in code**, as the property initializers of an Options POCO (e.g. `WatcherPreferences.NoteQueueTimeout = 2000`). The POCO *is* the default registry.
- **The table is sparse**: it holds a row only for a preference the user has explicitly changed. A read resolves `storedValue ?? codeDefault`, and a malformed/stale stored value degrades to the default rather than throwing (total deserialization).

This answers the two constraints directly:

- **Seeding a fresh vault** — nothing to do. An empty table *is* fully-defaulted, because absence resolves to the code default.
- **Adding a new preference** — add a property (with a default) to an Options POCO, a `PreferenceKeys` constant, and a one-line binding. No migration, no seed, no reconcile, and it applies retroactively to every existing vault. Changing a default in a later release likewise propagates to every vault that has not overridden it — impossible with materialized default rows, which freeze the old value.

Deliberately rejected: EF `HasData` seeding (the codebase never uses it, and it cannot express "add new defaults over time"), and a materialized row-per-preference reconcile (freezes defaults, needs an activation pass).

## The .NET Options surface

Preferences are consumed through the standard Options pattern, so any service can inject them:

- `UserPreferenceStore` (singleton) is a copy-on-write, in-memory snapshot of the active vault's overrides — the synchronous hot-read path behind the Options binding, so resolving a preference during request or watcher work never hits the database. It is loaded from the vault on activation (`PlaintorchEngine.InitializeVaultAsync`, alongside the other reconciles) and kept current by write-through.
- `UserPreferenceService` (scoped) owns the database read/write (`GetAsync`/`SetAsync`/`ResetAsync`/`LoadAsync`); a `Set`/`Reset` writes the row *and* the store together.
- Each preference group binds off the store: `services.AddOptions<WatcherPreferences>().Configure<UserPreferenceStore>(...)`. Consumers inject **`IOptionsSnapshot<T>`** for live, per-scope values (an `IOptions<T>` is computed once and would not reflect a runtime change).

Because the daemon serves one active vault at a time, a vault switch re-runs `LoadAsync` and the next scope's snapshot reflects the new vault. This mirrors the PEP108 dismissal pattern (durable rows + a per-vault in-memory projection loaded on activation).

## Tier placement

A deliberate third settings surface:

| Tier | Store | Scope |
|---|---|---|
| Daemon / global | `~/.pleiades/plaintorch/config.json` (`PlaintorchUserConfiguration`) | per-user, which vault is active |
| Vault file | `.plaintorch` (`VaultSettings`) | schema version + location keys |
| **Vault preferences (this PEP)** | vault SQLite table `UserPreferences` | per-vault user preferences |

The DB was chosen over extending the `.plaintorch` JSON so preferences are transactional and are backed up and migrated with the vault's other authoritative DB-only state (temporal metadata, snapshots, dismissals, checkpoints), rather than as loose JSON.

## Preferences

Implemented:
- **`WatcherPreferences.NoteQueueTimeout`** (`watcher.note-queue-timeout`, int ms, default **2000**) — how long a note may wait in the watcher's write queue before it is drained.
- **`AgendaPreferences.AutoMaterialiseOptOut`** (`agenda.auto-materialise-optout`, bool, default **false**) — whether an opted-out eventive is materialized automatically as time passes; off means an OptOut occurrence is hardened only by a user opting it back in, never by time-passage alone. (Consumer: the rolling materialization pass, PEP111 Preparation.)
- **`CalDavPreferences.FloatingRender`** (`caldav.floating-render`, enum `AllDay`|`PinToStart`, default **`AllDay`**) — how a floating occurrence (granularity window wider than its duration) is projected on export, which CalDAV cannot represent natively; `AllDay` never invents a start time. (Consumer: the CalDAV integration, PEP111.)

Planned (each added by its consumer as it lands, one POCO property apiece — no migration):
- **Default calendar** — the calendar an Orbit resolves against. Today this is fixed per declarative type (Gregorian for fates, Pleiadean for decrees). It becomes a preference, defaulting to **Pleiadean for both** until set. **Migration note:** when the fate default flips to Pleiadean, existing fates must be pinned `Calendar = Gregorian` so their already-authored orbits keep their meaning; only fates created afterward take the Pleiadean default. (Consumer: the temporal / Declarative-&-Occurrence unification work, PEP111 Preparation.)
- **Default time zone** — for `Due` and occurrences; none = wall time.

Deliberately *not* a preference: how far an *inactive* declarative goes — pausing generation vs hiding from display vs excluding from the agenda — is expressed by the declarative's own status states, not a setting.

## Implementation

- Entity `core/Vault/Database/UserPreferenceRecord.cs`; `DbSet` on `PlainfraContext`; EF migration `AddUserPreferences` (a plain `CreateTable`, no seed).
- `core/Plaintorch/Preferences/`: `UserPreferenceStore`, `UserPreferenceService` (with `SetRawAsync` for the generic write path), `UserPreferenceSerializer` (total; enums stored by name), the Options POCOs (`WatcherPreferences`, `AgendaPreferences`, `CalDavPreferences`), `PreferenceKeys`, and `PreferenceCatalog` (the enumeration source; each default read from its POCO).
- Options bound off the store in `PlaintorchModule`; loaded on activation in `PlaintorchEngine.InitializeVault(Async)`.
- **API:** `PreferenceModule` exposes a registry-driven `/api/preferences` — `GET` lists every catalog entry resolved to its value and default, `PUT /{key}` validates against the descriptor's kind (400 on mismatch, 404 on unknown key) and write-throughs, `DELETE /{key}` resets. Adding a preference to the catalog exposes it with no endpoint change.
- **Client:** `sdk.ts/plaintorch/preferences/` (`core.preferences.list/set/reset`), and a settings **modal** opened from an icon button at the end of the briefing's tab row — `obsidian/components/settings/` (the lit `p7t-preferences` panel + the `PreferenceModal` that hosts it, mirroring the other entity modals). A control per preference grouped by section (toggle / dropdown / number), each writing straight through, with a reset-to-default button; the panel renders whatever the catalog reports, so a new preference appears automatically. (Deliberately *not* an Obsidian plugin settings tab — settings belong inside the app's own briefing surface.)

## Verification

`core.tests/Core/UserPreferencesTests.cs`: an unset preference resolves to the default (Options + direct, no row); a `Set` overrides, persists as a durable row, and is visible through `IOptionsSnapshot` in a fresh scope; a second `Set` is an idempotent upsert (one row); a `Reset` reverts and deletes the row; a malformed stored value falls back to the default. Full core suite green.

---

# Deferred — `.plaintorch` consolidation (Phase 2)

A follow-up, split from the substrate because it is a vault-*format* change with a wide blast radius: move `VaultSettings` (location keys + the PEP092 schema-version marker) **out of the `.plaintorch` JSON file and into the vault database**, and rename the metadata directory `.plaintorch-data → .plaintorch` (the file's name is freed once its contents move into the DB). This is a natural amendment to **PEP092** (which owns vault-format versioning).

Why it is not in this branch:

- **Neither existing migrator covers it.** EF migrations handle the new config-table *schema* but cannot touch the filesystem or the pre-open DB path; the PEP092 `VaultMigrationRunner` runs *after* the DB is open (too late to rename the directory the DB lives in) and is gated on the very `SchemaVersion` being relocated. It needs a **new, one-time, crash-safe pre-boot layout-migration step** in `VaultBootstrapper`, before `PlainfraContextInitializer` opens the DB: stage the JSON into the metadata dir → rename the dir → open + EF-migrate → if the config table is empty, write the staged values → delete the staged copy. Each step idempotent; "old layout" detected by the presence of the `.plaintorch` file.
- **`SchemaVersion` is easy; `LocationKeys` is invasive.** `VaultLayout` resolves folder roots synchronously from an eagerly-loaded `VaultSettings`, *before* the DB opens, so moving location keys into the DB forces a bootstrap reorder (open DB → read keys → create folders) and makes folder resolution DB-backed. Location keys are also a preference, but **their change must be core-mediated** — changing a folder mapping has to signal the watcher to relocate data — so they wait on the in-progress watcher-authority rework (PEP110 Refactor BETA) rather than becoming a naive preference write.
- **Behavioral note:** the version marker then shares the DB's lifetime. Since the DB already holds authoritative DB-only state, deleting it is already lossy and unsupported as a "reindex," so this adds no new casual-recovery hazard — it just makes that explicit.
