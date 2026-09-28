# Criteria — Validators, Checks & Unit Tests

A living catalogue of what the core **must** guarantee, what is currently **tested**, and what remains open. It is both a
specification of invariants and the backlog for ongoing coverage. Introduced by PEP093.

**Status legend:** ✅ tested · ⏳ planned/pending · 🔲 not yet planned
**Kind:** _happy_ (nominal), _edge_ (boundary/unusual input), _fault_ (must fail safely / reject)

Tests live in `core.tests/` (xUnit v3). Each behaviour test runs against a fresh, isolated vault (own temp directory +
SQLite database) built through the real DI graph via the `TestVault` harness; run with `dotnet test core.tests`.

---

## Dev environment (PEP093)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `serve --ephemeral` uses a random user-independent temp profile (own socket/config) | happy | ✅ | manual (sandbox) |
| `serve` is socket-only by default; loopback is opt-in via `--loopback` | happy | ✅ | manual (sandbox) |
| Ordinary manual `serve` uses the persistent `~/.pleiades/plaintorch-dev` sub-profile, not the real per-user profile | happy | ⏳ | — |
| `serve --daemon` uses the real per-user profile (`~/.pleiades/plaintorch`) | happy | ⏳ | — |
| `serve` under the service runner uses the real per-user profile | happy | ⏳ | — |
| Ephemeral profile is cleaned up after use; the dev sub-profile persists across runs | edge | ⏳ | — |

**Edge cases / faults:** the dev sub-profile and the real per-user profile must never share a socket/config/port, so a
manual sandbox `serve` can never disturb an installed daemon; ephemeral roots must never collide across parallel runs and
must be removed after use, while the dev sub-profile must persist between runs.

## Version store (PEP092)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| `SchemaVersion` round-trips through `.plaintorch` | happy | ✅ | `VersionStoreTests` |
| A settings file without `SchemaVersion` loads as baseline (v1) | edge | ✅ | `VersionStoreTests` |
| A freshly initialized vault is stamped at the current version | happy | ✅ | `HarnessSmokeTests` |
| Corrupt `.plaintorch` loads as defaults rather than throwing | fault | ⏳ | — |

## Vault migration (PEP092)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Legacy v1 objective file re-canonicalised to quiet form, **body preserved** | happy | ✅ | `MigrationRoundTripTests` |
| Stored version advances to current; a history row is recorded | happy | ✅ | `MigrationRoundTripTests` |
| Runner short-circuits when the vault is already current | happy | ✅ | `MigrationRoundTripTests` |
| Legacy file archived to the graveyard before rewrite (rollback) | happy | ⏳ | — |
| Re-run after a completed migration is a no-op | edge | ⏳ | — |
| Title-only pre-PUCK files are left untouched | edge | ⏳ | — |
| Title collision on re-canonicalise is disambiguated | edge/fault | 🔲 | — |
| Filename-separator change requires a bespoke loader | fault | 🔲 | known limitation |

## Implicit boundary (PEP091)
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Creating an implicit objective writes **no** file but persists the row | happy | ✅ | `ImplicitBoundaryTests` |
| `begin` materializes a title-only file with `puck` frontmatter | happy | ✅ | `ImplicitBoundaryTests` |
| Deleting a boundary-begun file is authoritative (row removed) | happy | ✅ | `ImplicitBoundaryTests` |
| An objective without a begun boundary survives a scan | edge | ✅ | `ImplicitBoundaryTests` |
| Title-only deletion recovers identity via the `BoundaryBegin` location | edge | ✅ | `ImplicitBoundaryTests` (delete path) |
| Manual creation of a file for an existing implicit entity begins its boundary | edge | ⏳ | — |
| Unknown-PUCK assertion under an implicit root is purged | fault | ⏳ | — |

## Canonical storage / body preservation
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| API update rewrites frontmatter but preserves the body verbatim | happy | ✅ | `BodyPreservationTests` |
| Unknown frontmatter keys survive a canonical rewrite | edge | ⏳ | — |
| Rename/relocate on title/parent change moves the file (and children) | edge | ⏳ | — |
| Content-idempotent write: identical content is a no-op | edge | ⏳ | — |

## Stellar directive / objective placement matrix
`StellarDirectiveObjectiveMatrixTests` walks one repro for the full product of its variations (3,000 cases, one nested
class per directive-creation variation). Step 1 creates a stellar directive — core-first (kept, or its folder moved
outside the entity root) or initialised from a self-named note inside or outside the entity root — as the world, a
child, or a child-of-child. Step 2 places 1–3 objectives in the directive root, the default partition, an arbitrary
folder, a nested subfolder, or a folder inside the partition — core-first then moved, initialised in the partition then
moved, initialised at the target, or initialised in the world (optionally reparented through the API) then moved. Step 3
modifies all or one of them, by title or by a non-title field. Each case takes about half a second; the whole matrix is
several minutes, so filter it in or out with `FullyQualifiedName~StellarDirectiveObjectiveMatrixTests`.

| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| U1 — after placement and after modification, the objective exists | edge | ✅ | `StellarDirectiveObjectiveMatrixTests` |
| U2 — its one note is (and stays) in the target folder | edge | ✅ ⚠ | `StellarDirectiveObjectiveMatrixTests` |
| U3 — it is parented by the directive | edge | ✅ ⚠ | `StellarDirectiveObjectiveMatrixTests` |
| U4 — resolving its note yields the objective | edge | ✅ | `StellarDirectiveObjectiveMatrixTests` |
| U5 — a title change renames the note in place; an untouched objective keeps its file name | edge | ✅ | `StellarDirectiveObjectiveMatrixTests` |
| I1 — nothing U1–U4 asserted after placement changes when the objective is modified | edge | ✅ ⚠ | `StellarDirectiveObjectiveMatrixTests` |
| I2 — siblings placed the same way behave the same in U1–U4 | edge | ✅ ⚠ | `StellarDirectiveObjectiveMatrixTests` |

⚠ Failing where the target is an arbitrary folder (in the directive, nested, or inside the partition): a folder that is
neither the directive's own nor the partition resolves its "containing directive" from any note in it, the objective's own
included. The failing cases stand as acceptance criteria; see `.DISCUSSION.md`, *Stellar directive / objective placement
matrix*.

## Markdown serialize / parse
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Quiet objective round-trips PUCK, fields, and body | happy | ✅ | `SerializerRoundTripTests` |
| Unknown frontmatter keys are preserved on serialize | edge | ✅ | `SerializerRoundTripTests` |
| Missing required fields produce validation issues | fault | ✅ | `SerializerRoundTripTests` |
| Enum / relation / nested validation | fault | ⏳ | — |
| Scalar formatting (dates ISO, enums by name, list JSON) | edge | ⏳ | — |

## File locator
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Quiet storage → title-only filename | happy | ✅ | `FileLocatorTests` |
| Index storage → `{id} - Title` filename | happy | ✅ | `FileLocatorTests` |
| Directive-owned objective lands in the partition folder | happy | ✅ | `FileLocatorTests` |
| Self-named-directory shape (`{Name}/{Name}.md`) | edge | ⏳ | — |

## Discovery / classification
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Objective file under objectives root classifies as Objective | happy | ✅ | `PathClassificationTests` |
| Directive self-named file does **not** classify as Objective | fault | ✅ | `PathClassificationTests` |
| Objective in a directive partition folder classifies correctly | edge | ⏳ | — |
| Ownership-boundary directories excluded from directive inference | edge | ⏳ | — |
| Attachment / hidden / underscore folders ignored | edge | ⏳ | — |

## Sync actions
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| CreateFromFile / UpdateFromFile from a discovered candidate | happy | ⏳ | — |
| RewriteFromDatabase on a candidate with validation issues | fault | ⏳ | — |
| PurgeFile on a disallowed assertion (archived to graveyard) | fault | ⏳ | — |
| Self-write suppression prevents watcher feedback loops | edge | ⏳ | — |

## PUCK
| Behaviour / Invariant | Kind | Status | Test ref |
|---|---|---|---|
| Notation parse for each numerator (spiritgem/incremental/date/manual) | happy | ⏳ | — |
| `PuckNamedIdentity` format/parse (Index / title-only / loose) | happy | ⏳ | — |
| Reverse resolution (PUCK → entity type) | happy | ⏳ | — |
| Declaration parse-space uniqueness enforcement | fault | ⏳ | — |

---

## Known / possible faults to keep covered
- **Body loss** — any canonical rewrite must preserve the markdown body (regression-guarded by `BodyPreservationTests`; keep expanding to rename/relocate paths).
- **Cross-type misplacement** — a file whose PUCK resolves to a different entity type must not be adopted by the wrong model.
- **Title collisions** — two entities sharing a title in one folder must be disambiguated, not overwritten.
- **Title-only / pre-PUCK files** — must be left untouched, never destructively "normalized" without cause.
- **Partial-migration conflicts** — a migration that finishes with per-entity conflicts still advances the version; conflicts are audited and need manual attention (documented limitation).
- **Relaunch / permission failures** — dev-user relaunch must reject cleanly rather than silently using the real environment.
- **Self-write races** — service-originated writes must be suppressed so the watcher does not reconcile its own output.
- **Parent read from a sibling note** — the containing directive of a note must come from a directive's note, never
  from an objective note sharing its folder (guarded by `StellarDirectiveObjectiveMatrixTests`; currently failing).
