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
| Interactive `serve` without a `PLAINTORCHDEV` account rejects with a setup hint | fault | ✅ | manual (sandbox) |
| `serve` under the service runner uses the normal per-user environment | happy | ⏳ | — |
| Interactive `serve` relaunches successfully as `PLAINTORCHDEV` | happy | 🔲 | needs the account; not CI-exercisable |
| Ephemeral profile is cleaned up after use | edge | ⏳ | — |

**Edge cases / faults:** relaunch failure (missing account, wrong password, non-interactive session) must reject, not
fall through to the real environment; ephemeral roots must never collide across parallel runs; the static dev password is
overridable via `PLAINTORCHDEV_PASSWORD`.

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
