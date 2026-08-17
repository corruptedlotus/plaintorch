# REFACTOR Alpha — Agnosticising the Vault / Markdown / API Core

The plan for retiring per-type "glue" from the core: the layers between the declarative attribute seam
(`PuckEntity` / `PuckFormat` / `MarkdownField` / `VaultStorage`) and the database re-encode model facts as
`typeof` chains, `entity switch` dispatch, and one-hand-written-method-per-entity families. This document
tracks what has landed, what remains, the test gates each phase must clear, and the decisions taken along the
way. Phases build on each other but each lands independently green.

**Status legend:** ✅ done · 🚧 in progress · ⏳ planned · 🅿️ parked (has preconditions)

**Progress context (2026-08-10).** Phases 0–1 landed. Phase 2 is underway: its forward path-composition slice
landed on this date (the `entity switch` in `MarkdownFileLocator` is retired for a declared-policy shape-strategy
composer); its reverse-composition and candidate-enumeration slices, and phases 3–5, remain. When the later work
was re-verified against source at this date, the 8 hand-written path-sync models, the `IsDirectiveEntityTypeName`
name-list, and the scattered `Mode.IsIdentityDriven()` checks were all still present.
The intervening effort went to frontend and orbit/declarative work (PEP102 dependency-graph editor, PEP106
frontend repository, Polaris briefing, orbit materialisation, executive-order effective windows), not the
vault-core refactor — so the phase plan is **parked, not stale**. Two things that landed since do touch it and
are folded in below: the PEP106 change feed (a new declaration-driven consumer of family/type knowledge) and
the mode-policy service split (`VaultStorageModePolicyRouter`), which phase 4 now *extends* rather than invents.

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
   shapes are the domain — not glue. Do not genericise them. The line to hold: storage-policy *onboarding*
   (Implicit boundary-`begin`, Freeform/Implicit file-`init`) is **not** domain logic — it is mode-mechanical
   and is generalised in phase 5. "Materialization flows" here means the *domain consequences* of
   materialization (orbit proximity, state settlement), not the file boundary itself.

---

## Phase ledger

| Phase | Scope | Status |
|---|---|---|
| 0 | `VaultEntityModelCatalog` — declarative entity registry + activation validation | ✅ |
| 1 | `VaultEntityGateway` — retire `typeof`/DbSet dispatch, generic snapshots, known-id loaders | ✅ |
| 2 | Storage shape strategies — collapse `MarkdownFileLocator` / path composition per-type code | ✅ (candidate-enumeration folds into 3) |
| 3 | Polymorphic family descriptor — first-class TPH families; registry-generated path-sync models | 🚧 |
| 4 | Storage-mode policy objects — consolidate Freeform/Implicit/Synced/… conditionals | ⏳ |
| 5 | API kit — policy-derived actions (begin-boundary, init-from-file) + optional generic CRUD | ⏳ |
| P1 | Retire the base `A{S:6}` directive declaration | 🅿️ |
| P2 | Entity→note association for directives | ✅ (phase 2) |
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

## Phase 2 — Storage shape strategies ✅

**Landed (2026-08-10) — forward path composition.** `VaultStoragePathComposer` + `IVaultStorageStrategy`
(`SelfNamedDirectoryStorageStrategy`, `SingleFileStorageStrategy`) now compose canonical paths from declared
policy (via `VaultEntityModelCatalog`): base name from `PuckStorage`, container from the resolved parent's own
directory + `PartitionUnder`, else the declared location root. `MarkdownFileLocator`'s `GetFilePath` `entity
switch` and every per-entity `Get*FilePath` body are gone — the methods are now thin façades over the composer
(public signatures kept for `PlaintorchEngine` / callers). Two declared-parameter/hook decisions: a new
`VaultStorageAttribute.RequiresParent` (set on `ExecutiveOrder`) preserves its "no owning sprint ⇒ throw"
guard instead of falling back to a location root; lore pages keep an explicit per-type strategy
(`LorePageStorageStrategy`) for their RelativePath-authoritative, `EffectiveIdentifier`-named, parent-ignoring
placement — a genuine per-model hook, not a switch arm. This also retires the PEP092-noted latent coupling
(onrush/polaris/lore path methods hardcoding the Index form). Pinned by 15 goldens in `FileLocatorTests`
(every entity × shape × PuckStorage form × parent nesting × partition), landed green *before* the refactor and
still green after; full suite 115/115.

**Landed (2026-08-16) — reverse composition + P2 note-association.** The `Apply*CompositionFromPath` helpers and
`VaultMarkdownDiscoveryService.CreatePathComposedModel`'s type switch are gone: the composer owns
`ApplyCompositionFromPath(entity, path)` (loose identity from the filename / lore segments; parent relation from
the declared `ParentEntityType`). Per the operator's call, the containing-owner resolution *delegates* to the
existing `MarkdownFileLocator.TryGetContaining*Id` helpers — consolidating the three duplicate resolvers
(`MarkdownFileLocator` / catalog / `VaultWatcherPathPolicy`) is phase 4. The composer is now a DI singleton
shared by `MarkdownFileLocator` and discovery. **P2 note-association is fixed**: it was broken for *all* Quiet
entities (the matcher read the filename PUCK, but Quiet/freeform keep it in frontmatter), and freeform directives
were additionally not enumerable. `PuckEntityResolutionService` now matches by frontmatter PUCK, anchors the
family model (lunar notes resolve too), and — per the operator's "derive from mode" call — enumerates freeform
entities by scanning self-named files (identity-driven, contained to the resolver so the shared `IsCandidatePath`
/ scan behaviour is untouched). The `DirectiveInitAndResolutionTests` pin flipped from `Assert.Null` to positive
(stellar *and* lunar). Pinned by `PathCompositionReverseTests`; full suite green (bar a pre-existing wall-clock
flake).

**Deferred to phase 3.** Turning the `IsCandidatePath` predicates themselves into strategy-owned *candidate
enumeration* rides the registry-projected path-sync models (phase 3), where that catalog is reorganised.

**Problem.** `MarkdownFileLocator` holds ~7 per-entity `Get*FilePath` methods, ~5 `Apply*CompositionFromPath`
helpers, and an 8-arm `entity switch` — yet the actual variety is ~3 physical shapes already declared by
`VaultStorage`: **SelfNamedDirectory**, **SingleFile**, and **partitioned-under-parent** placement, crossed
with the Quiet/Index PUCK-storage forms. `VaultMarkdownDiscoveryService.CreatePathComposedModel` mirrors the
same dispatch. (The fate/decree unification since this plan was drafted already collapsed their per-kind
methods into `GetIncentiveFilePath` / `ApplyIncentiveCompositionFromPath` — a partial down-payment on the
shape strategy, and evidence the per-kind methods never carried real per-kind variety.)

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
- executive order composite path inside its owning onrush partition (the order now also carries a validated
  `EffectiveFrom`/`EffectiveUntil` window — new fields, *not* a shape change; compose the current model)
- lore page self-named path under the saga root
- Index vs Quiet filename forms per shape (polaris/onrush Index; objective/directive Quiet already covered)
- fate/decree path *classification* (foreign-container rejection between incentive kinds)
- nested directive composition already covered (`DirectiveStorageTests`); keep as regression anchors

**Risk.** Highest of all phases — path composition feeds watcher reconciliation, rename/relocation, and the
graveyard. Mitigation: goldens first; behaviour-preserving refactor; no policy changes smuggled in.

## Phase 3 — Polymorphic family descriptor 🚧

**Landed (2026-08-16) — first-class families + family-aware discovery.** `VaultEntityModelCatalog` now models
TPH families first-class: `VaultEntityFamily` (anchor + concrete members), with `GetFamilies` / `TryGetFamily` /
`GetFamilyAnchor` / `IsFamilyMember`. Anchors are derived from the concrete members' inheritance (an abstract base
shared by *some but not all* members), so `Incentive` — which declares no entity attributes of its own and is not
a catalog model — still anchors its family. First consumer thinned: discovery's hand-kept `IsDirectiveEntityTypeName`
name-list is gone, replaced by `catalog.IsFamilyMember(typeof(Directive), name)` (behaviour-preserving). Covered by
`EntityModelCatalogTests` (both families enumerated, anchor round-trip, membership-by-name).

**Note on the phase-2 boundary.** Phase 2's forward path composition (the shape-strategy composer) is the landed
core. Its *remaining* concerns — path→identity (reverse) composition and candidate enumeration — turned out to be
entangled with the path-sync catalog (phase 3) and the watcher path policy (phase 4): the reverse `Apply*CompositionFromPath`
helpers depend on filesystem-scanning `TryGetContaining*Id` resolvers spread across `MarkdownFileLocator`,
`VaultPathSyncModelCatalog`, and `VaultWatcherPathPolicy`. So rather than force a standalone "phase 2 completion",
they are addressed here (registry-projected path-sync models) and in phase 4 (path policy), where they belong.

**Problem.** TPH families (directive: `Directive` ⊃ `StellarDirective`/`LunarDirective`; incentive:
`Incentive` ⊃ `Objective`/`Fate`/`Decree`) are implicit: family knowledge is re-derived at each consumer
(the path-sync model's hand-set `ConcreteType`, `IsDirectiveEntityTypeName` in discovery, discriminator
column disambiguation in `PlainfraContext`, per-sibling location overrides, and — new since this plan — the
PEP106 change feed (`PlaintorchChangeFeedInterceptor`) announcing entities by runtime type name and walking
`OwnersOf` over EF foreign-key metadata). The change feed already derives family/owner knowledge structurally
rather than from a name list, so it is mostly *aligned* with the target — but it carries one known-ambiguous
polymorphic edge: `Directive`/`LunarDirective` owner announcement (documented open edge in
`core/.DISCUSSION.md`, harmless today because no child record has a directive FK). A first-class family
descriptor is where that edge gets resolved rather than worked around.

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
member; anchor spans the family). Keep `EntityTypeNameContractTests` (which pins the concrete PUCK type names
on the wire) as a regression anchor — see the cross-repo constraint below.

**Cross-repo blast radius.** Concrete type names and discriminators are no longer core-only: the PEP106 SDK
identity map keys resolutions on `{@type}:{id}` and routes type names → repositories (`@model(...)`), so any
change here to a concrete type name or discriminator is a two-repo change gated by `EntityTypeNameContractTests`.
Treat renames as contract changes, not refactors.

**Risk.** Medium. The registry projection must reproduce today's 8 path-sync models byte-for-byte in
behaviour before adding anything new (assert model-list equivalence in a test during the transition).

## Phase 4 — Storage-mode policy objects ⏳

**Problem.** `VaultStorageMode` semantics are interpreted by scattered conditionals: `Mode.IsIdentityDriven()`
(discovery ×2, `VaultWatcherPathPolicy`), freeform special-cases in discovery, implicit-boundary checks in the
watcher and storage service, enforced/synced branches in reconciliation. A mode-policy split **already exists**
(`VaultStorageModePolicyRouter` over `Freeform`/`Enforced`/`Optional`/`Synced`/`FileFirst`/`Implicit` services,
with a shared `PathBoundVaultStorageModePolicyService` base) — but those services today answer only the
*watcher-decision* questions (`Decide`, `TryResolveWatchPath`, `BelongsToModelAsync`,
`ResolveRelocationOldIdFallback`); the *semantic* questions are still asked as scattered enum checks elsewhere.

**Target.** *Extend the existing router's policy objects* — do not invent a parallel hierarchy — so each mode
also answers the questions the rest of the pipeline asks: `CanCreateFromFile`, `DeletionIsAuthoritative`,
`RequiresFrontmatterIdentity`, `MaterializesOnCreate`, `BeginsBoundaryOnFirstFile`. Pipeline code asks the
policy; mode enums stop leaking. The Freeform and Implicit services (already registered) absorb their
special-case checks. These same policy objects are what phase 5's `begin`/`init` actions dispatch through, so
this phase is a hard dependency of phase 5a.

**Test gate:** Implicit is already well covered (`ImplicitBoundaryTests`); Freeform via the directive init
tests. Add a per-mode behavioural matrix for Synced / Enforced / FileFirst / Optional when their branches are
touched — before, not after.

**Risk.** Medium. Mostly mechanical extraction into an existing hierarchy, but authority rules (who wins on
delete) are load-bearing; matrix tests must pin them first.

## Phase 5 — API kit: policy-derived actions + generic CRUD ⏳

The 8 × (`IApi` + `ApiService` + `Module`) triplets (directive, objective, declarative, onrush, polaris, lore,
dependency, system) mix **three kinds of endpoint**, which must be told apart before anything is genericised:

- **Policy-derived actions** — mechanical consequences of a storage *mode*, currently hand-written per entity.
  This is the phase's real prize (§5a). Two families exist today:
  - *Boundary-`begin`* (Implicit): `ObjectiveApiService.BeginBoundaryAsync`,
    `DeclarativeApiService.BeginFateBoundaryAsync` / `BeginDecreeBoundaryAsync` — three copies of
    load-by-id → `Save{Type}Async(beginBoundary: true)` → audit `{kind}.begin-boundary`, differing only by type
    and the audit string.
  - *File-`init`* (Freeform; wanted for Implicit too): `DirectiveApiService.InitializeFromPathAsync`, directives
    only today. Its spine (validate path in-root → discovery candidate → `InitializeFromFileAsync` → load
    created → audit) is generic; the directive-specific parts are the `typeof(Directive)` gate, the
    `InspectDirectiveInitPathAsync` fallback, and the manual-no-PUCK override (`ShouldTreatAsManualFreeformInit`).
  Note `OnrushSprint.BeginAsync` / `PolarisCycle.BeginAsync` are *domain* workflow-begins, not file-boundary
  begins — they are not in scope.
- **Genuinely-domain actions** — workflow shifts, parenting, timeframes, orbit/proximity materialization. Stay
  explicit (principle 5). The kind-split directive API is the reminder that entity surfaces diverge on purpose.
- **Mechanical CRUD** — get/list/find/create/delete plumbing.

### §5a — Policy-derived actions (worth doing regardless of §5b)
Drive `begin`/`init` off the phase-4 mode policy objects instead of per-entity code. Keep the two actions
**distinct** — `begin` *adopts a file for an already-created entity*, `init` *creates an entity from an existing
file*; create-vs-adopt is a real semantic difference — but make both generic across every identity-driven mode:

- one `begin` action for any Implicit entity: the Implicit policy's `BeginsBoundaryOnFirstFile` plus the generic
  gateway snapshot/save replace the three hand-written copies; the audit string derives from the entity kind.
  Collapses objective/fate/decree begins into one.
- one `init` (create-from-file) action for any identity-driven entity — **Freeform *and* Implicit**, gated on the
  mode policy's `CanCreateFromFile`. This generalises init beyond directives (implicit-incentive init is new
  behaviour). The directive-only specifics become declared policy: the manual-no-PUCK override and the discovery
  fallback move onto the mode/entity policy object or an explicit named hook (principle 2), never a `typeof` arm.

Routes generalise to a per-collection shape (`POST /api/{collection}/{id}/begin`, `POST /api/{collection}/init`),
served only where the entity's mode supports the action.

**Test gate (§5a):** begin-boundary parity across objective/fate/decree through the one generic action (identical
file materialization + `BoundaryBegin` audit as today); `init` for a freeform directive *and* an implicit
incentive (the incentive init is new — pin it); the directive manual-no-PUCK override preserved through the
generic path; per-kind audit action strings unchanged.

**Risk (§5a).** Medium — `begin`/`init` feed real file materialization and the implicit boundary. Behaviour-preserve
the three begins and the directive init byte-for-byte *before* extending `init` to incentives.

### §5b — Generic CRUD kit (still optional; may be dropped)
A generic `EntityApiService<TEntity, TUpdate>` + `MapEntityCrud<T>` for the mechanical get/list/find/create/delete,
leaving domain actions explicit. **Do last or not at all** — this is the layer where per-entity code is most
legitimately domain-shaped. Revisit after §5a and phases 2–4 show what is still mechanical. Caveat for the kit:
`Dependency` and `Checkpoint` are DB-only and (for dependencies) not `IPuckNamedEntity`, so they do **not** fit
the entity-catalog/gateway shape the kit would assume — exclude or special-case them.

**Depends on** phase 4 (the mode policy objects own `BeginsBoundaryOnFirstFile` / `CanCreateFromFile` /
`MaterializesOnCreate`, which §5a dispatches through).

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
| D7 | The "declarations drive" principle now has a shipped instance *outside* the vault layer: the PEP106 change feed derives owners from EF foreign-key metadata (`OwnersOf`) and announces by runtime type name, not a hand-kept list. (Supersedes an earlier speculative D7 that attributed an `[LifecyclePhase]`/`EntityLifecycleResolver` design to "PEP101"; that design was never built — PEP101 is still an idea, and the dependency system that *did* ship is PEP102, with flat enum-typed endpoints and a recursive-CTE temporal-cycle probe.) | phase 1 / PEP106 |
| D8 | "Stealth" is the operator's informal name for the Implicit storage mode (Quiet frontmatter PUCK, no file on create, boundary-begun); the plan keeps the code identifier `Implicit` as canonical so it stays greppable against source | phase 5 scoping |
| D9 | Policy-derived onboarding endpoints (Implicit boundary-`begin`, Freeform/Implicit file-`init`) are mode-mechanical, not domain, and fold into phase 5 (§5a) dispatched through the phase-4 mode policy objects. `begin` (adopt a file for an existing entity) and `init` (create an entity from a file) stay **distinct** actions but both go generic across identity-driven modes; `init` generalises beyond directives to implicit incentives (new behaviour) | phase 5 scoping |

## Standing constraints

- Every phase lands with the full suite green (`dotnet test core.tests`); delicate-pipeline phases land their
  golden tests in a preceding commit.
- No phase changes observable storage policy behaviour; policy changes are their own proposals (PEP), not
  refactor side-effects. (The one deliberate *addition* is phase 5a's `init` for implicit incentives — a new
  surface, not a change to existing policy — pinned by its own test.)
- Concrete PUCK type names and discriminators are a cross-repository contract: the PEP106 SDK identity map keys
  on `{@type}:{id}` and routes type names → repositories, so phases 3 and 5 that touch type names or API shapes
  are two-repo changes gated by `EntityTypeNameContractTests`.
- Keep `core/Vault/.GENESIS.md`, `core/.DISCUSSION.md`, and this document updated as phases land.
