# REFACTOR Alpha — Agnosticising the Vault / Markdown / API Core

The plan for retiring per-type "glue" from the core: the layers between the declarative attribute seam
(`PuckEntity` / `PuckFormat` / `MarkdownField` / `VaultStorage`) and the database re-encode model facts as
`typeof` chains, `entity switch` dispatch, and one-hand-written-method-per-entity families. This document
tracks what has landed, what remains, the test gates each phase must clear, and the decisions taken along the
way. Phases build on each other but each lands independently green.

**Status legend:** ✅ done · 🚧 in progress · ⏳ planned · 🅿️ parked (has preconditions)

**Progress context (2026-09-04).** Phases **0–4 are ✅** and **phase 5 §5a is ✅** (the policy-derived `begin`/`init`
actions — one `VaultEntityLifecycleService` each, dispatched through the phase-4 policy; `init` generalised to implicit
incentives). Phase 4's follow-ons #2 (dismiss + foreign-file) and #4 (PEP108 phase D) are since closed too, so the
watcher suite carries zero skips. Only **§5b (generic CRUD kit + collection registry)** remains — retained as the
final step toward total platform abstraction (see §5b), deliberately last because it is the most domain-shaped layer.
See the **Retrospective** section for the close-out assessment. Earlier snapshot below. Phases **0–3 are ✅** — the entity catalog, the generic gateway, the full
shape-strategy composer (forward *and* reverse path composition, including the P2 freeform note-association fix),
and the polymorphic family descriptor: first-class families, family-aware discovery, **identity-driven concrete-type
resolution** (the directive family no longer collapses to stellar — see D14), and the path-sync list validated as a
projection of the catalog (D15). Phases **4–5 are ⏳**. `dev/phase2a` has been merged in (a new Media domain, PEP105
directive icons/banners, timeframes) — none of it touches the refactor surface (see the merge note under decisions).

**Where the work lives (onboarding).** The refactor and the PEP108 operation-status system have been *waterfalled*
onto one branch, **`claude/refactor-alpha-continued`**, in this order: `0362720` (phase 2 forward) → PEP108 A/B/C/E
→ `99b82ed` (phase 3 families) → `d2f2053` (phase 2 reverse) → `ff0b9f2` (phase 2 P2) → `b1dabbb` (onboarding docs)
→ `a8aedd2` (merge `dev/phase2a`) → `2675485` (phase 3 identity-driven concrete type) → `ed51687` (phase 3 validated
projection). Each commit lands the suite green save one **wall-clock-flaky** test
(`DeclarativeEcosystemTests.Cycle_begin_materializes_proximity_eventives` — a fate at 23:00 + a 24h window;
pre-existing, being fixed separately). PEP108 is a *sibling* system with its own document
(`PEP108 - Operation Status System.md`); it depends on this refactor's phase 4 for its own phase D.

**Onboarding keypoints — the non-obvious load-bearing facts:**
- **The lifecycle system already exists.** PEP101's begin/finish is a *built*, declarative mechanism —
  `[LifecyclePhase]`/`[LifecycleStatus]` + `EntityLifecycleResolver` (+ an `ILifecyclePhaseSource` hook for
  temporal kinds like eventives) under `core/Orchestration/Lifecycle/`, consumed by PEP102's `DependencyGateService`.
  Don't reinvent it. (The PEP101 *document* is still `status: idea` even though the code shipped — a doc/impl gap,
  see D7.)
- **Path composition is compose-then-correct.** The composer derives a *naive* parent id from the path (for a
  partitioned incentive it even reads the incentive's own file); `VaultMarkdownDiscoveryService.ApplyPathAuthorities`
  then **corrects** it via the partition-aware path policy. Phase 4's resolver consolidation must preserve *both*
  steps (D10).
- **Freeform/Implicit are identity-driven, not path-driven.** Belonging is by frontmatter PUCK, and the location is
  only a write-time default. Freeform note-association enumerates self-named files and matches frontmatter identity,
  contained to the resolver (D11). Getting freeform "clean" is largely phase 4's job — the Freeform policy object
  must own the *full* question set, not the five sampled below.
- **The mode-policy split already exists** (`VaultStorageModePolicyRouter`); phase 4 *extends* it, never invents.
- **A polymorphic family's concrete type is resolved by identity, never hard-coded.** `VaultFamilyInstantiationResolver`
  picks the member whose PUCK declaration mints the file's id (`A…`→stellar, `LUNA…`→lunar), with the model's
  `ConcreteType` as fallback. Lunar and stellar are the *same* mechanism differing only by declared identity +
  default location — **the north-star: changing a kind's behaviour is an attribute swap, no strings attached** (D14).
- **Type names + discriminators are a cross-repo contract** (the PEP106 SDK identity map keys on `{@type}:{id}`),
  gated by `EntityTypeNameContractTests` — treat renames as contract changes.

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
| 3 | Polymorphic family descriptor — first-class TPH families; identity-driven concrete type; catalog-projected path-sync list | ✅ (predicate/scan-root projection folds into 4) |
| 4 | Storage-mode policy objects — protocol-first watcher: the mode policy owns the pipeline's semantic questions | ✅ (bug class + protocol landed; #4 Phase D + #2 dismiss/foreign-file since closed) |
| 5 | API kit — policy-derived actions (begin-boundary, init-from-file) + generic CRUD | ✅ §5a (begin + init are one policy-derived action each); 🅿️ §5b (generic CRUD + collection registry) retained for total platform abstraction |
| P1 | Retire the base `A{S:6}` directive declaration | 🅿️ |
| P2 | Entity→note association for directives | ✅ (phase 2) |
| P3 | Lunar directives symmetric with stellar (was "lunar auto-discovery") | ✅ (phase 3, D14) |

---

## Retrospective — the arc so far (2026-09-04)

**What it did.** REFACTOR Alpha replaced *recognition-by-shape* with *declaration-by-attribute* across the vault core.
Before, the pipeline knew each entity by hardcoded `typeof`/DbSet chains, per-type path code, hand-kept family
name-lists, `enum`-tested storage behaviour, and English-string sniffing of reasons and exceptions. After, one entity
declares its identity and storage as attributes, and every consumer resolves the facts it needs from a queryable
catalog and a set of mode-policy objects. The realised north-star: **changing an entity's behaviour is now an attribute
swap** — a new mode's semantics live in one policy object; a new identity-driven entity gets `begin`/`init` for free.
The layers, bottom-up: the `VaultEntityModelCatalog` (0) and `VaultEntityGateway` (1) end type dispatch; the
shape-strategy composer (2) ends per-type path code, forward *and* reverse; first-class TPH families (3) make
polymorphic concrete-type selection identity-driven (lunar and stellar finally symmetric); the mode-policy protocol (4)
gives the watcher its semantic questions so the pipeline asks the protocol, never the enum; and the policy-derived
`begin`/`init` (5a) collapse the per-entity onboarding copies into one action each.

**What we achieved.** The north-star is real: **changing an entity's behaviour is now an attribute swap** — a new
mode's semantics live in one policy object; a new identity-driven entity gets `begin`/`init` for free by declaring its
mode. Three begin copies became one `BeginBoundaryAsync`; the directive-only init became one `InitializeFromFileAsync`
that now also serves implicit incentives. The watcher test suite carries **zero skips** (204/0) — the acceptance
criteria *were* skipped repros, and every one is green. Two sibling systems rode along on the new structure: PEP108
phase D (issue classification became a single typed `VaultSyncConcern`) and the dismiss feature (snooze-dismissible
statuses + a reusable `p7t-popover`).

**Issues solved.** The refactor was validated by turning a class of real bugs into pinned, now-green repros — the
watcher suite carries **zero skips** (204/0). Concretely: the watcher no longer destroys `prefix - ` user notes or
resurrects invalid-PUCK entities (identity is notation-gated, not string-shaped); an enforced root no longer
synthesizes a bogus `<root>/<root>.md`; note-resolution no longer binds to unrelated files in a folder; lore index 0
(a prologue / Chapter 0) is allowed; a child lore page can no longer begin before its parent (D17), the root cause of
the "hierarchy dislodging" weirdness; a boundary-begun file deleted while the daemon was offline is reconciled at
startup, so **startup and runtime reach the same state** (deletion parity); a foreign, unrecognised-PUCK file in a
non-exclusive root is now a dismissible *warning left in place*, not an error to purge — aggression is confined to
enforced (granted) territory. Two sibling systems rode along: **PEP108 phase D** made the watcher's issue
classification structural (one typed `VaultSyncConcern`, one classified reason), and the **dismiss feature** made a
status snooze-dismissible with a reusable `p7t-popover`. The through-line: **every string-heuristic in the pipeline is
gone** — exception classification is typed, policy classification is a typed concern, and the last `SuggestedReason`
sniff (directive manual-init) fell to phase 5a's structural test.

**Questions raised about the core.** The work surfaced, and left open, several genuine questions:
- **Where is the mechanical/domain boundary?** §5b exists to abstract "generic CRUD," but the refactor kept revealing
  that per-entity API code is often *legitimately* domain-shaped. The kit's real test is the collection→type registry:
  how much of the API surface is truly mechanical vs. a domain interface that only looks repetitive.
- **Startup does not populate the operation-status registry.** Only live watcher events report statuses; a foreign
  file or issue present at activation stays invisible until something touches it. Startup/runtime parity holds for the
  *database and vault* but not (yet) for *diagnostics*. Worth deciding deliberately.
- **The resolution↔policy DI cycle caps declarativeness.** The Freeform policy needs full PUCK resolution to confirm
  belonging, so two freeform behaviours can't move onto the policy object and stay documented helpers instead. Some
  behaviour is declarative-resistant by dependency shape, not by intent.
- **Is the mode-policy protocol accreting overlapping questions?** `CanCreateFromFile` currently coincides with
  `IsIdentityDriven`; they answer different questions but the same modes. As the protocol grows, are the semantic
  questions kept orthogonal, or do they drift into near-synonyms?
- **Validation issues are still untyped (field + English message).** The one narrow residual sniff
  (`IsMissingRequiredPuckInputIssue`) survives because `MarkdownValidationIssue` carries no typed reason. A typed
  issue vocabulary would finish what phase D started.
- **The legacy `A{S:6}` id-space (P1) still lingers**, kept alive by real vault rows; retiring it needs an id re-mint
  migration and a discriminability hardening, not just an attribute deletion.

The residual scope is **§5b (total platform abstraction)** and the parked **P1**; everything else in the plan has
landed. Details below.

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
(`MarkdownFileLocator` / catalog / `VaultWatcherPathPolicy`) is phase 4. *(Done 2026-09-28, D18.)* The composer is now a DI singleton
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

*(The original plan for this phase — the `IVaultStorageStrategy`-per-shape target, the P2 note-association
untangle, and the goldens-first test gate — is realized in the two **Landed** notes above and in decisions
D10–D11. The shape composer owns forward *and* reverse composition; the P2 flip shipped. The one piece that
moved out — turning the `IsCandidatePath` predicates into strategy-owned candidate enumeration — rides phase 3's
registry projection. Risk called out at the time, and honoured: path composition feeds watcher reconciliation,
rename/relocation, and the graveyard, so both slices were behaviour-preserving with goldens, no policy changes
smuggled in.)*

## Phase 3 — Polymorphic family descriptor ✅

**Landed (2026-08-16) — first-class families + family-aware discovery.** `VaultEntityModelCatalog` now models
TPH families first-class: `VaultEntityFamily` (anchor + concrete members), with `GetFamilies` / `TryGetFamily` /
`GetFamilyAnchor` / `IsFamilyMember`. Anchors are derived from the concrete members' inheritance (an abstract base
shared by *some but not all* members), so `Incentive` — which declares no entity attributes of its own and is not
a catalog model — still anchors its family. First consumer thinned: discovery's hand-kept `IsDirectiveEntityTypeName`
name-list is gone, replaced by `catalog.IsFamilyMember(typeof(Directive), name)` (behaviour-preserving). Covered by
`EntityModelCatalogTests` (both families enumerated, anchor round-trip, membership-by-name).

**Landed (2026-08-20) — identity-driven concrete type + validated projection.** The directive path-sync model no
longer hard-codes `concreteType: typeof(StellarDirective)` as *the* type it materializes. `VaultFamilyInstantiationResolver`
selects the concrete member whose PUCK declaration mints the file's identity (`A…` → stellar, `LUNA…` → lunar) via the
pure notation gate (`PuckTokenizer`), falling back to the model's declared default when identity is absent or
ambiguous — so single-member models and brand-new files are byte-for-byte unchanged, and `ConcreteType` becomes an
honest fallback, not a family collapse. Wired into every path-composition site (discovery, `VaultLoader`,
watcher-sync id creation). This is where the long-standing lunar/stellar asymmetry is retired (D14). Separately,
`VaultPathSyncModelCatalog.ValidateAgainstCatalog` (run in `VaultBootstrapper`) makes the path-sync list a *validated
projection* of the catalog: every model targets a vault-stored catalog entity of the declared shape, and every
concrete vault-stored entity is discoverable through a model or a family anchor — a new entity added without a model
now fails activation fast (D15). Covered by `FamilyInstantiationResolverTests`, `PathSyncModelCatalogTests`, and a
lunar-composition test in `DirectiveInitAndResolutionTests`.

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

**Target — how each item resolved.** The catalog models families first-class (anchor type, concrete members,
per-member PUCK declaration, per-member storage override, EF discriminator values). Consumers derive:

- **Path-sync models projected from the registry** — ✅ *as a validated projection, not a generated list.* Mode,
  shape, and known-id loaders already derived from the declared storage attribute; `ValidateAgainstCatalog` now makes
  the entity catalog the **authority** the list must conform to (coverage + shape coherence, fail-fast). The manual
  `concreteType:` argument is retired as a collapse and demoted to a fallback (D14). What deliberately **did not**
  move: the per-model **scan predicates + roots**, and the family-vs-per-member **grouping** (directive = one model
  spanning both kinds; incentives = one model each). Those are storage *policy* — phase 4's policy objects own them,
  and generating the list from them before then would just churn (D15).
- **Family-aware discovery** — ✅ name-list checks (`IsDirectiveEntityTypeName`) replaced by catalog queries.
- **P3 — lunar/stellar symmetry** (was "lunar auto-discovery", a mis-framing). ✅ The only real asymmetry was the
  hard-coded stellar `concreteType`; identity-driven resolution retires it, so a `LUNA…`-identity file now composes to
  `LunarDirective` at every site, no `./Moonlight` binding (D14). Whether directives are *filesystem*-auto-discovered
  at all (the `_ => false` predicate) is a **separate** axis, unchanged, and orthogonal to kind symmetry.
- **Family / discriminator validation** — ✅ *already enforced*, not re-implemented. Sibling-declaration coherence and
  parse-space uniqueness live in `PuckRuntimeCompilationCatalog` (`ValidateDeclarationUniqueness` /
  `ValidateParseSpaceUniqueness`) — the latter is exactly what guarantees the resolver can always disambiguate a
  family — and discriminators are EF-default type names (inherently unique). Duplicating this in `Validate` would add
  nothing (D15).

**Cross-repo blast radius.** Concrete type names and discriminators are no longer core-only: the PEP106 SDK
identity map keys resolutions on `{@type}:{id}` and routes type names → repositories (`@model(...)`), so any
change here to a concrete type name or discriminator is a two-repo change gated by `EntityTypeNameContractTests`.
Treat renames as contract changes, not refactors. (Nothing in this phase renamed a type or discriminator.)

**Risk — retired.** The byte-for-byte concern (reproduce today's 8 models) is pinned by `PathSyncModelCatalogTests`
(type/mode/shape set + directive fallback) and the full suite (153/153 save the known wall-clock flake).

## Phase 4 — Storage-mode policy objects ✅

**Landed.** The protocol-first watcher is real: `IVaultStorageModePolicyService` now owns the pipeline's semantic
questions (`IsIdentityDriven`, `MaterializesOnCreate`, `BeginsSyncBoundaryOnFirstFile`, `PurgesDesyncedFiles`) plus
the `ResolveWriteTargetPath` behaviour, and discovery / sync / storage / consistency ask the protocol
(`VaultStoragePolicyEngine.PolicyFor`) instead of testing the mode enum. The whole bug class the enum-leak caused is
fixed and green: notation-gated identity (`PuckIdentityGate`) kills the loose-PUCK misparse (Bug B) and coincidental
note-resolution (Bug C); the self-named fallback is shape-gated (#3); lore index 0 is allowed (Bug A) and the lore
`Beginning` is a hierarchical constraint (D17); and `ScanAsync`'s orphan pass gives startup/runtime deletion parity.
Only two skipped tests remain, both **outside** the mode-policy refactor: **#2** (a foreign file in a non-exclusive
root should be a *dismissible warning* — needs the dismiss feature's design + persistence) and **#4** (collapse the
three string-sniffed issue heuristics into one classified reason — **PEP108 phase D**). One recorded constraint: the
Freeform policy depends on `PuckEntityResolutionService`, so two Freeform-specific behaviours can't move onto the
policy without a DI cycle (see `core/Vault/Watcher/.GENESIS.md`).

**Problem (original).** `VaultStorageMode` semantics are interpreted by scattered conditionals: `Mode.IsIdentityDriven()`
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

**Absorbed from phase 3 (D15).** The per-model **scan predicates + roots** in `VaultPathSyncModelCatalog` (the
`IsIncentiveMarkdownFile` / `IsExecutiveOrderMarkdownFile` / self-named predicates and the `[…Root, VaultRoot]`
scan-root lists) are storage policy, and the family-vs-per-member **grouping** is too. They land here: a mode's
policy object should answer "is this path a candidate for me, and where do I scan" so the path-sync list becomes a
true projection (entity catalog × mode policy) with no imperative residue. The `_ => false` directive predicate — no
filesystem auto-discovery, init/API-only — is one such policy datum to make explicit here (and the place to
deliberately decide whether directives should auto-discover at all, a behaviour change held out of phase 3).

**Test gate:** the per-mode decision matrix **now exists** as a regression baseline — `StorageModeDecisionMatrixTests`
pins Enforced / Synced / FileFirst / Optional across the full fact matrix (36 decisions); Implicit via
`ImplicitBoundaryTests`, Freeform via directive init. Phase 4 must keep those green (or update them deliberately).
The `WatcherIsolationTests` and `StartupRuntimeParityTests` suites are the behavioural acceptance gate.

**Risk.** Medium. Mostly mechanical extraction into an existing hierarchy, but authority rules (who wins on
delete) are load-bearing; matrix tests must pin them first.

**Watcher philosophy + pinned gaps (steering).** The watcher is a **passive interface onto the user's vault** — it
is *instructed by* files and *reflects* the core back onto them, and its capacity for aggressive inference must be
**confined to declared territory** (see [`core/Vault/Watcher/.GENESIS.md`](../core/Vault/Watcher/.GENESIS.md) for the
full philosophy and the policy intent of each mode). Phase 4's policy objects are where this mindset becomes code.
The concrete gaps between today's code and that philosophy are already pinned as **skipped repro tests** — treat
them as this phase's acceptance criteria:
- **Notation-gate identity** — un-gated `" - "` loose-PUCK parsing turns ordinary notes into invalid-PUCK entities
  (`LoosePuckClassificationBugRepros`: watcher-destroys-`prefix - `, migration aftershock, note-resolution on
  unrelated files). A prefix/frontmatter value is a PUCK only if it tokenizes against the declared notation, as
  `VaultFamilyInstantiationResolver` already does.
- **Confine authority** — an enforced-root directory event synthesizing `<root>/<root>.md` (`PathClassificationTests`);
  a foreign file in a non-exclusive root raised as an Error rather than a dismissible warning (`WatcherStatusTests`).
- **Surgical, aftershock-free, state-aware reflection** — one cause raising three issues (`WatcherStatusTests`);
  no reflection onto a noteless entity and no hierarchy mutilation over invalid frontmatter (the latter still to be
  pinned for synced nested lore).
- **Startup/runtime parity** — a begun file deleted while offline orphans its entity at startup
  (`StartupRuntimeParityTests`); the startup sweep needs an orphan-reconciliation pass.

## Phase 5 — API kit: policy-derived actions + generic CRUD ⏳

The 9 × (`IApi` + `ApiService` + `Module`) triplets (directive, objective, declarative, onrush, polaris, lore,
dependency, system, and — since `dev/phase2a` — media) mix **three kinds of endpoint**, which must be told apart
before anything is genericised (the media triplet adds a fourth flavour: media-attachment mutations like
`SetIcon`/`SetBanner` over the new `[Media]` seam attribute — classify it here, D16):

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

### §5a — Policy-derived actions (worth doing regardless of §5b) ✅
**Landed (2026-09-04).** One `VaultEntityLifecycleService` owns both actions, dispatched through the phase-4 policy:
`BeginBoundaryAsync(type, id)` (gated on `BeginsSyncBoundaryOnFirstFile`) collapses the three objective/fate/decree
begins — each per-entity `Begin*BoundaryAsync` is now a one-line delegation, so their existing tests are the parity
gate. `InitializeFromFileAsync(type, path)` (gated on the new `CanCreateFromFile`) collapses the directive init spine
and generalises it: the `typeof(Directive)` arm became a type/kind check, the discovery fallback became
`InspectInitPathAsync(path, type)`, and the manual-no-PUCK override became **structural** (an ignore decision with no
path identity whose only issues are missing-required-PUCK-input) — deleting the `SuggestedReason` string-sniff. `init`
now serves implicit incentives too (a new `POST /api/objectives/init` + `IObjectiveApi.InitializeFromPathAsync`),
minting an identity for a puckless file exactly as directive init does. Audit actions derive from the catalog kind
(`{kind}.begin-boundary` / `{kind}.init`). The generic `POST /api/{collection}/…` route shape waits on a collection
registry (§5b); per-entity routes delegate for now. Suite 204/0. _Deferred:_ the objectives SDK gains no `init`/`begin`
method (it covers no lifecycle action today); a small follow-up if the frontend needs them.

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

### §5b — Generic CRUD kit 🅿️ (retained for total platform abstraction)
A generic `EntityApiService<TEntity, TUpdate>` + `MapEntityCrud<T>` for the mechanical get/list/find/create/delete,
leaving domain actions explicit — **and the collection→type registry that lets the routes generalise to the
`/api/{collection}/…` shape §5a's per-entity routes stand in for today** (begin/init included). The operator has kept
this in scope as the last step toward total platform abstraction; it is deliberately *last*, because this is the layer
where per-entity code is most legitimately domain-shaped, so §5a + phases 2–4 first show what is genuinely mechanical.
Companion follow-ups that ride with it: the objectives (and other) SDKs gain their `begin`/`init` lifecycle methods,
which they lack today. Caveat for the kit: `Dependency` and `Checkpoint` are DB-only and (for dependencies) not
`IPuckNamedEntity`, so they do **not** fit the entity-catalog/gateway shape the kit would assume — exclude or
special-case them.

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

### P2 · Directive entity→note association ✅ (phase 2)
Resolved (see phase 2's second Landed note and D11). The fix was broader than expected — it was broken for *all*
Quiet entities, and `PuckEntityResolutionService` now matches by frontmatter PUCK, anchors the family model, and
enumerates freeform entities by self-named-file scan (contained to the resolver). The `DirectiveInitAndResolutionTests`
pin is now a positive assertion for stellar *and* lunar.

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
| D7 | The "declarations drive; hooks stay available" principle already has **built** instances. **PEP101's begin/finish lifecycle is real, declarative code** — `[LifecyclePhase]`/`[LifecycleStatus]` + `EntityLifecycleResolver`, plus an `ILifecyclePhaseSource` hook for temporal kinds (eventives, whose "passing" is temporal not stateful) — under `core/Orchestration/Lifecycle/`, consumed by PEP102's `DependencyGateService`. **NB: the PEP101 *document* is still `status: idea` even though the mechanism shipped** (a doc/impl gap — don't reinvent the lifecycle system). A second instance sits outside the vault layer: the PEP106 change feed derives owners from EF FK metadata (`OwnersOf`) and announces by runtime type name, not a hand-kept list. *(Corrects an earlier D7 that wrongly claimed the lifecycle design was never built.)* | phase 1 / PEP101 / PEP106 |
| D8 | "Stealth" is the operator's informal name for the Implicit storage mode (Quiet frontmatter PUCK, no file on create, boundary-begun); the plan keeps the code identifier `Implicit` as canonical so it stays greppable against source | phase 5 scoping |
| D9 | Policy-derived onboarding endpoints (Implicit boundary-`begin`, Freeform/Implicit file-`init`) are mode-mechanical, not domain, and fold into phase 5 (§5a) dispatched through the phase-4 mode policy objects. `begin` (adopt a file for an existing entity) and `init` (create an entity from a file) stay **distinct** actions but both go generic across identity-driven modes; `init` generalises beyond directives to implicit incentives (new behaviour) | phase 5 scoping |
| D10 | Phase 2 reverse composition: the composer owns `ApplyCompositionFromPath` (identity + parent from the declared `ParentEntityType`) but **delegates** containing-owner resolution to the existing `TryGetContaining*Id` helpers rather than unifying the three duplicates now — that consolidation is phase 4. **Keypoint:** the composer's incentive `DirectiveId` is deliberately *naive* and is **corrected downstream** by `ApplyPathAuthorities` via the partition-aware path policy; phase 4 must preserve this compose-then-correct two-step | phase 2 (operator call) |
| D11 | Phase 2 note-association (P2) was broken for **all Quiet entities**, not just directives (the matcher read the filename PUCK; Quiet/freeform keep it in frontmatter). Fixed in `PuckEntityResolutionService` by frontmatter-PUCK matching + a family-anchored model lookup (lunar resolves too) + — per the operator's **"derive from mode"** call — enumerating freeform entities by scanning self-named files. Kept **contained to the resolver** so the shared `IsCandidatePath`/scan/storage/migration behaviour is untouched; turning `IsCandidatePath` itself into strategy-owned candidate enumeration is deferred to phase 3 | phase 2 (operator call) |
| D12 | Phase 3 families are derived from the concrete members' **inheritance** (an abstract base shared by *some but not all* members), not from catalog membership — so `Incentive`, which declares no entity attributes of its own and is not a catalog model, still anchors its `{Objective, Fate, Decree}` family | phase 3 |
| D13 | Phase 2's leftover reverse/candidate-enumeration work was **not** forced into a standalone "phase 2 completion": the reverse `Apply*` helpers entangle with the path-sync catalog (phase 3) and the triplicated containing-owner resolvers in the path policy (phase 4), so they land there. Phase 2 is ✅ for the shape composer; phase 3's registry projection clears the rest | phase 2/3 boundary |
| D14 | The directive family's concrete type is resolved **by identity, not hard-coded**: `VaultFamilyInstantiationResolver` tokenizes the file's PUCK against each member's declaration (`A…` → stellar, `LUNA…` → lunar), falling back to the model's declared default (`ConcreteType`) only when identity is absent/ambiguous — so single-member models and brand-new files are byte-for-byte, and a `LUNA…` file now composes to `LunarDirective` at every site (discovery, loader, watcher-sync id creation). This retires the lunar/stellar asymmetry. **Operator framing:** lunar and stellar are the *same* mechanism differing only by declared identity + default location; making lunar behave differently later (e.g. `Synced`/`Implicit`) is an **attribute swap with no strings attached**, which is the whole point. The `./Moonlight` folder is a usage convention, never a system rule. *(This reframes and closes the old P3 "lunar auto-discovery" item, which was a mis-naming.)* | phase 3 (operator call) |
| D15 | Phase 3's "registry-projected path-sync models" landed as a **validated projection, not a generated list**: mode/shape/known-ids already derived from the storage attribute, so `ValidateAgainstCatalog` (in `VaultBootstrapper`) makes the entity catalog the *authority* (coverage + shape coherence, fail-fast) rather than churning the list. Per-model **scan predicates + roots** and the family-vs-per-member **grouping** are storage policy → deferred to phase 4's policy objects (generating them now would just move them twice). The plan's "family/discriminator validation into `Validate`" is **already enforced** by `PuckRuntimeCompilationCatalog` (`ValidateDeclarationUniqueness`/`ValidateParseSpaceUniqueness`, the latter guaranteeing the resolver can disambiguate) + EF type-name discriminators — not duplicated | phase 3/4 boundary |
| D16 | `dev/phase2a` merged into the refactor branch (`a8aedd2`) after assessment: the new Media domain is a non-persisted sidecar (`[Media]` marshals out, never a stored entity), PEP105 directive icons/banners are plain `[MarkdownField]`s, and timeframes/reflectives are DB-only — **none add a vault-stored/path-sync/family member**, and dev/phase2a touched none of the refactor's core files, so no resync was needed. Note for phase 5: there is now a **9th** API triplet (Media: `IMediaApi`/`MediaApiService`/`MediaModule`) and a new `[Media]` seam attribute to fold into the endpoint taxonomy | merge |
| D17 | **Lore `Beginning` is a hierarchical constraint** (operator call): a child lore page's `Beginning` must fall **within its parent's span** — `>=` the parent's `Beginning` and before the parent's next-sibling boundary. This is the root-cause fix for the reproduced hierarchical-beginning weirdness (`LoreIndex.MarkActivePages` dragging a not-yet-begun ancestor into the active set via a past-begun child): with the invariant enforced at the lore write-path, an un-begun ancestor can never contain a begun child. Acceptance test: `LoreHierarchyTests.A_child_beginning_before_its_parent_is_rejected` (skipped until implemented). Lands in phase 4 / the lore write-path | lore write-path (operator call) |
| D18 | **Containment is one declaration-driven resolver** (closes the D10/D13 consolidation). A repro matrix showed the per-kind resolvers were not a harmless over-approximation: the directive walk accepted *any* note's PUCK in a folder, so an objective note moved into an arbitrary folder of its directive lost its parent, and the write path's reparent check — reading containment differently from discovery — moved it out of that folder on the next edit. `VaultWatcherPathPolicy.EnumerateContainingParentIds(entityType, path)` now answers containment for every kind from declarations alone (child: `ParentEntityType`, shape, `PartitionUnder`, identity-driven reach; parent: self-named shape, `PuckStorage`, family notation gate; territory: catalog-derived roots), and the directive/onrush/freeform-directive resolvers and the composer's and storage service's `typeof` dispatch are gone. DB-aware callers (discovery, write path, loader, consistency pass) take the nearest *known* candidate; the composer stays naive and is corrected by discovery, preserving D10's compose-then-correct | containment (operator: "policy-level, never model-specific") |
| D19 | **Folders and identities are read by policy, never by name or title** (follows D18). A self-named folder is never a partition, and a partition that cannot be used (parent folder of its name, a self-named folder or a file of its name) places children directly in the parent's folder (`IsPartitionUsable`); the freeform assertion territory is one declaration-driven rule for every identity-driven kind (`TryGetAssertionViolation`), replacing two directive-only copies; the incentive-only init placement step (dead since the Implicit policy's `ResolveWriteTargetPath`) is removed, so placement is the mode policy's alone; and note resolution reads only the identity a kind's storage puts in the path (notation-gated) or the frontmatter — the title-based resolvers are gone. Remaining model-specific: vanished-note delete ordering walks `ParentDirectiveId` | policy (operator: "no model-specific patches") |

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
