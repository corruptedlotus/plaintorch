# REFACTOR Alpha — Agnosticising the Vault / Markdown / API Core

The plan for retiring per-type "glue" from the core: the layers between the declarative attribute seam
(`PuckEntity` / `PuckFormat` / `MarkdownField` / `VaultStorage`) and the database re-encode model facts as
`typeof` chains, `entity switch` dispatch, and one-hand-written-method-per-entity families. This document
tracks what has landed, what remains, the test gates each phase must clear, and the decisions taken along the
way. Phases build on each other but each lands independently green.

**Status legend:** ✅ done · 🚧 in progress · ⏳ planned · 🅿️ parked (has preconditions)

---

## Guiding principles

1. **Storage is policy, never model essence.** A `VaultStorage` declaration (location key, mode, shape,
   partitioning) is a policy *assigned* to an entity. Identity-driven modes make this concrete: Freeform
   (directives) and Implicit (objectives, PEP091) entities are not bound to their declared root — the location
   is only the canonical write default. No abstraction may bake "entity X lives under directory Y" into
   anything but the policy declaration itself.
2. **Declarations drive; hooks stay available.** Generic behaviour is derived from the attribute seam. Models
   *may* have genuinely special behaviour — those get explicit, named hook/override points, introduced when a
   real behaviour demands one, never speculatively. (Phase 1 deliberately introduced zero hooks: the one
   candidate — stellar-only base-declaration filtering — turned out to be a defensive redundancy.)
3. **Test-gated refactoring.** Every phase that touches delicate pipelines (path composition, watcher
   reconciliation) lands its golden-path tests *before* the refactor, in the `TestVault` harness
   (`core.tests`, real DI graph, isolated vault per test).
4. **Fail fast at activation.** Structural incoherence in declarations (missing kinds, ambiguous formats,
   anchor-less abstract types, unregistered entities) must reject vault activation with a precise message —
   never surface as a deep NRE mid-watcher. `PuckRuntimeCompilationCatalog` set the precedent;
   `VaultEntityModelCatalog.Validate` extends it.
5. **Genuinely domain-specific logic stays hand-written.** Relationship rules (incentive parenting,
   timeframes-are-lunar-only), workflow legality, materialization flows, and per-entity update/shift payload
   shapes are the domain — not glue. Do not genericise them.

---

## Phase ledger

| Phase | Scope | Status |
|---|---|---|
| 0 | `VaultEntityModelCatalog` — declarative entity registry + activation validation | ✅ |
| 1 | `VaultEntityGateway` — retire `typeof`/DbSet dispatch, generic snapshots, known-id loaders | ✅ |
| 2 | Storage shape strategies — collapse `MarkdownFileLocator` / path composition per-type code | ⏳ |
| 3 | Polymorphic family descriptor — first-class TPH families; registry-generated path-sync models | ⏳ |
| 4 | Storage-mode policy objects — consolidate Freeform/Implicit/Synced/… conditionals | ⏳ |
| 5 | API CRUD kit (optional) — generic list/get/find/create/delete plumbing | ⏳ (may be dropped) |
| P1 | Retire the base `A{S:6}` directive declaration | 🅿️ |
| P2 | Entity→note association for directives | 🅿️ (folds into phase 2) |
| P3 | Lunar directive file auto-discovery | 🅿️ (folds into phase 3) |

---

## Phase 0 — Entity model catalog ✅

`core/Vault/VaultEntityModelCatalog.cs` (singleton, pure metadata, no I/O). One reflection pass over every
type declaring `PuckEntity` / `PuckFormat` / `VaultStorage` yields, per entity: its own kind and PUCK
declaration, plus the *effective* storage policy and the hierarchy type that declares it (stellar anchors on
abstract `Directive`; lunar declares its own `Moonlight` policy).

Activation validation (called by `VaultBootstrapper` before schema init): PUCK formats require kinds; kinds
are unique; storage-bearing entities are PUCK-named; abstract anchors have ≥1 concrete member; concrete
entries are registered in the EF model.

Covered by `EntityModelCatalogTests` (enumeration completeness, storage anchoring, live-model validation).

## Phase 1 — Generic entity gateway ✅

`core/Vault/Database/VaultEntityGateway.cs` (scoped). Declaration-driven data access that deleted the glue:

- **Resolution**: `PuckEntityResolutionService`'s 11-branch `typeof` chain → one gateway lookup. Untracked
  lookups are `AsNoTracking().IgnoreAutoIncludes()`; tracked lookups keep default query behaviour.
- **Watcher**: `LoadExistingAsync` 8-branch chain → one tracked lookup; the ~110-line `CloneEntity` switch →
  one generic snapshot.
- **Known ids**: all 9 hand-written `VaultPathSyncModelCatalog` loader lambdas → a defaulted generic loader
  (`CreateModel<T>` binds `VaultEntityGateway.LoadKnownIdsAsync(context, typeof(T), …)`).
- **API snapshots**: `DirectiveApiService.Clone` switch → gateway.

Semantics worth remembering:

- **Family-wide anchor lookups.** Querying an abstract anchor type spans its whole discriminated family
  (`Set<Directive>` reaches stellar + lunar rows). Identity discipline comes from PUCK tokenization *before*
  the DB is touched: a declaration only reaches the gateway with ids it can mint, and multi-declaration
  matches throw the resolution ambiguity guard rather than silently picking a type. Declarations are disjoint
  id-spaces — children do not "override" the base; they carve their own parse space.
- **Snapshots cover all mapped scalars + owned single references.** The hand-picked field lists are gone; the
  generic snapshot recurses into owned types (found the hard way: `PolarisCycle.Forecast` is `[Owned]` and a
  scalar-only clone would have silently dropped it). Reference-typed converted scalars (tag lists) copy by
  reference — safe because the codebase replaces them by assignment, never in-place mutation.

Covered by `EntityModelCatalogTests` (clone fidelity, owned recursion, family-anchor lookups, non-PUCK
rejection) with the pre-existing behavioural suite (reconcile flows, implicit boundaries, body preservation)
carrying the regression load.

---

## Phase 2 — Storage shape strategies ⏳

**Problem.** `MarkdownFileLocator` holds 9 per-entity `Get*FilePath` methods, 7 `Apply*CompositionFromPath`
helpers, and a 10-arm `entity switch` — yet the actual variety is ~3 physical shapes already declared by
`VaultStorage`: **SelfNamedDirectory**, **SingleFile**, and **partitioned-under-parent** placement, crossed
with the Quiet/Index PUCK-storage forms. `VaultMarkdownDiscoveryService.CreatePathComposedModel` mirrors the
same dispatch.

**Target.** An `IVaultStorageStrategy` (working name) per *shape*, parameterised entirely by the entity's
declared policy (via `VaultEntityModelCatalog`): canonical path composition, path→identity composition, and
candidate enumeration. `MarkdownFileLocator` becomes a thin façade that resolves the strategy for
`entity.GetType()`'s effective policy. Special placements that are genuinely per-model (executive orders
composing inside their owning onrush partition via composite PUCK `x{?}-o{I:2:1}`) become declared strategy
*parameters* or an explicit per-model hook — not a new switch arm.

**Untangles along the way (P2 parked item).** Entity→note association (`AssociatedNote`) never resolves for
directives because candidate enumeration is gated by the path-sync `IsCandidatePath` predicate, whose
directive instance is deliberately `false` to keep directives out of the generic watcher scan. The predicate
conflates *scan gating* with *path enumeration*; shape strategies separate the two concerns, and note
association falls out naturally (including lunar directives under `Moonlight`). The pinning test in
`DirectiveInitAndResolutionTests` flips from `Assert.Null` to a positive assertion then.

**Test gate (land these first, ~6–8 goldens in `FileLocatorTests` / `PathClassificationTests`):**
- fate/decree standalone-root paths and directive-partition paths (`Fates/`, `Decrees/` under a directive)
- executive order composite path inside its owning onrush partition
- lore page self-named path under the saga root
- Index vs Quiet filename forms per shape (polaris/onrush Index; objective/directive Quiet already covered)
- fate/decree path *classification* (foreign-container rejection between incentive kinds)
- nested directive composition already covered (`DirectiveStorageTests`); keep as regression anchors

**Risk.** Highest of all phases — path composition feeds watcher reconciliation, rename/relocation, and the
graveyard. Mitigation: goldens first; behaviour-preserving refactor; no policy changes smuggled in.

## Phase 3 — Polymorphic family descriptor ⏳

**Problem.** TPH families (directive: `Directive` ⊃ `StellarDirective`/`LunarDirective`; incentive:
`Incentive` ⊃ `Objective`/`Fate`/`Decree`) are implicit: family knowledge is re-derived at each consumer
(the path-sync model's hand-set `ConcreteType`, `IsDirectiveEntityTypeName` in discovery, discriminator
column disambiguation in `PlainfraContext`, per-sibling location overrides).

**Target.** The catalog models families first-class: anchor type, concrete members, per-member PUCK
declaration, per-member storage override, EF discriminator values. Consumers then derive:

- **Path-sync models generated from the registry** — the hand-written `VaultPathSyncModelCatalog`
  registration list (including the directive model's manual `concreteType:` argument) becomes a projection of
  catalog entries; per-model scan predicates remain declared behaviour (they are policy, phase 4's subject).
- **Family-aware discovery** — replaces name-list checks (`IsDirectiveEntityTypeName`) with catalog queries.
- **P3 parked item — lunar auto-discovery.** Today directives are reconciled only through explicit init and
  the API; lunar files authored under `./Moonlight` round-trip via the API. If lunar file auto-discovery is
  wanted, it lands here as a per-member path-sync model projected from `LunarDirective`'s own policy —
  *without* binding lunar files to `./Moonlight` (Freeform still means anywhere; concrete-type selection must
  come from identity, i.e. the `LUNA` declaration, not the directory).
- Directive family validation (per-member declarations coherent, discriminators unique) moves from ad-hoc
  knowledge into `Validate`.

**Test gate:** the directive family is already the fully-tested template (storage locations, init
materialization, per-kind resolution, discriminator migration). Add the incentive-family equivalents:
classification goldens per kind and a family-resolution matrix (every member declaration resolves to its
member; anchor spans the family).

**Risk.** Medium. The registry projection must reproduce today's 8 path-sync models byte-for-byte in
behaviour before adding anything new (assert model-list equivalence in a test during the transition).

## Phase 4 — Storage-mode policy objects ⏳

**Problem.** `VaultStorageMode` semantics are interpreted by scattered conditionals:
`Mode.IsIdentityDriven()`, freeform special-cases in discovery and `FreeformVaultStorageModePolicyService`,
implicit-boundary checks in the watcher and storage service, enforced/synced branches in reconciliation.

**Target.** One policy object per mode answering the questions the pipeline actually asks:
`CanCreateFromFile`, `DeletionIsAuthoritative`, `RequiresFrontmatterIdentity`, `MaterializesOnCreate`,
`BeginsBoundaryOnFirstFile`, relocation/old-id fallback rules. Pipeline code asks the policy; mode enums stop
leaking. `FreeformVaultStorageModePolicyService` becomes the Freeform policy object; Implicit boundary
begins/authority checks become the Implicit policy object.

**Test gate:** Implicit is already well covered (`ImplicitBoundaryTests`); Freeform via the directive init
tests. Add a per-mode behavioural matrix for Synced / Enforced / FileFirst / Optional when their branches are
touched — before, not after.

**Risk.** Medium. Mostly mechanical extraction, but authority rules (who wins on delete) are load-bearing;
matrix tests must pin them first.

## Phase 5 — API CRUD kit ⏳ (optional; may be dropped)

The 8 × (`IApi` + `ApiService` + `Module`) triplets repeat get/list/find/create/delete plumbing. A generic
`EntityApiService<TEntity, TUpdate>` + `MapEntityCrud<T>` could remove the mechanical parts, leaving domain
actions (workflow shifts, parenting, materialization, timeframes) explicit. **Do last or not at all** — this
is the layer where per-entity code is most legitimately domain-shaped, and the kind-split directive API is a
reminder that entity surfaces diverge on purpose. Revisit after phases 2–4 show what (if anything) is still
mechanical.

---

## Parked items — preconditions and notes

### P1 · Retire the base `A{S:6}` directive declaration 🅿️
Removing `[PuckEntity("directive")]` + `[PuckFormat("A{S:6}")]` (as a pair) from abstract `Directive` is
clean for activation, minting, storage, watcher sync, and family-wide known-id association — those key on
`VaultStorage` + database rows. It *only* disables the legacy id-space: 7-char `A` ids stop resolving
anywhere (API existence, note/banner resolution, discovery's existing-entity guard), while their rows remain
loadable and file-synced. Preconditions:
1. No legacy `A{S:6}` ids remain in vaults that matter — or ship an id re-mint migration (row ids +
   frontmatter `puck` rewrite + `PuckRegistryEntries` update). Current vaults **do** carry them: the sibling
   migration converted discriminators but deliberately kept ids.
2. Harden `PuckPathDiscriminabilityService.AreDistinguishable` (via the entity catalog) so a format-less type
   yields a proper validation outcome instead of `GetCompiled` throwing. Unreachable for `Directive` today,
   but a landmine if another self-named model ever scans the vault root.
3. Update the two pinning tests that intentionally encode today's behaviour (legacy-id resolution in
   `DirectiveInitAndResolutionTests`, anchor-kind assertion in `EntityModelCatalogTests`).

### P2 · Directive entity→note association 🅿️
Pre-existing gap, pinned by test, documented in `core/.DISCUSSION.md`. Resolved by phase 2's separation of
scan gating from path enumeration. Do not fix piecemeal before then — the current code is the refactor's
subject.

### P3 · Lunar directive file auto-discovery 🅿️
Deliberately bounded out of the sibling split. Lands (if wanted) as a phase 3 registry-projected model.
Identity-driven: concrete-type selection by `LUNA` declaration, never by directory.

---

## Decision log

| # | Decision | Where |
|---|---|---|
| D1 | Stellar keeps the committed `A{S:8}` / `stellar-directive` identity; base keeps its `A{S:6}` legacy declaration for now (see P1) | sibling split |
| D2 | Lunar directives get a dedicated `Moonlight` location key (default `./Moonlight`) as *their* declared policy; stellar inherit `Directives` from the anchor | sibling split |
| D3 | Directive reconciliation stays explicit-init/API-only; general watcher scan does not classify directive files | sibling split |
| D4 | Base-declaration lookups query the whole family; the defensive stellar-only filter was dropped — PUCK tokenization + the ambiguity guard carry the discipline; no resolution-scope hook until a real special behaviour needs one | phase 1 |
| D5 | Previous-state snapshots cover **all** mapped scalars + owned references, replacing hand-picked field lists | phase 1 |
| D6 | Init-from-path mints ids from the composed **instantiation type**, not the family anchor (`A{S:8}`, matching API creation) | phase 1 prep |

## Standing constraints

- Every phase lands with the full suite green (`dotnet test core.tests`); delicate-pipeline phases land their
  golden tests in a preceding commit.
- No phase changes observable storage policy behaviour; policy changes are their own proposals (PEP), not
  refactor side-effects.
- Keep `core/Vault/.GENESIS.md`, `core/.DISCUSSION.md`, and this document updated as phases land.
