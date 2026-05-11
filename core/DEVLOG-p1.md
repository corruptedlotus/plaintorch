# DEVLOG P1

## Purpose
This document is a consolidated development report for PLAINTORCH Phase 1. It reconstructs the engineering history of the project from the beginning of the build effort through the current state, based on the implemented code, project logs, discussion records, and the work completed during this collaboration.

It is not a verbatim transcript. It is an engineering narrative, project ledger, and assessment.

## Current Project Size Snapshot
Measured on May 10, 2026.

- C# files: 104
- Markdown files: 12
- C# lines: 10,257
- Markdown lines: 1,004

These figures include source, project documentation, and the current vault/test markdown content in the repository.

## Executive Summary
PLAINTORCH has evolved from an early vault-oriented console tool into a hosted, single-vault, background-capable application with:

- domain-driven source organization
- EF Core persistence with migrations
- PUCK identity infrastructure
- markdown frontmatter serialization and reverse deserialization/validation
- per-user active-vault configuration
- socket-hosted API surface
- vault ownership locking
- graveyard and audit-log infrastructure
- path-based vault storage metadata
- dynamic composed storage paths for directives and objectives
- topology validation for watcher-safe storage layouts
- a first functional watcher/discovery/reconciliation pipeline
- an interactive Windows splash/status popup for the hosted core

The project is now well beyond scaffolding. It has substantial infrastructure, identity, storage, synchronization, and runtime composition in place. The remaining work is no longer “foundations first”; it is refinement, conflict policy hardening, operational maturity, and product-surface completion.

## Development History by Phase

### Phase 0 — Initial project foundation
The earliest implemented slice established the fundamental bounded contexts and baseline architecture.

Delivered:
- domain models for:
  - `Directive`
  - `Objective`
  - `OnrushSprint`
  - `PolarisCycle`
  - `PolarisForecast`
  - `Executive`
  - `Reflective`
  - `CelestronTransaction`
- supporting enums and metadata:
  - workflow states
  - objective colleges
  - tag metadata
  - markdown field annotations
  - PUCK format declarations
- initial vault-aware file and database infrastructure
- initial CLI flows:
  - `init`
  - `activate`
  - `serve`
  - `bootstrap-service`

Architectural significance:
- established the main project vocabulary early
- kept the model set close to the blueprint/domain language
- avoided an anemic technical-folder layout by later converging on domain folders and namespaces

### Phase 1 — PUCK identity system
The next major layer was identity.

Delivered:
- PUCK notation records and parser
- PUCK generation service
- support for numerator strategies:
  - manual
  - spiritgem
  - incremental
  - date-stamp
- file/path identity helpers for `"{PUCK} - {Title}"`

Important decisions:
- PUCK declarations are expressed as attributes on CLR types
- different entity classes can have distinct issuance rules
- identity is not an incidental string; it is central infrastructure

Later refinement:
- `PuckCreationService` was introduced to enforce the intended policy split between:
  - caller-provided/manual segments
  - automatically-generated segments
- this policy was later extended into file-originated sync behavior so discovered filenames cannot override auto-generated PUCK identities

### Phase 2 — Vault and persistence layer
The project then became a true vault-backed application rather than a memory-first prototype.

Delivered:
- `VaultOptions`
- `VaultLayout`
- `VaultBootstrapper`
- `PlainfraContext`
- EF Core database mapping
- repository operations
- markdown file locator
- vault-local settings storage
- SQLite storage inside the vault

Later persistence upgrades:
- moved from ad hoc creation to EF Core migrations
- standardized schema handling through migration application at startup
- introduced audit and graveyard systems for temporal retention foundations

Important decisions:
- Obsidian vault remains the user-facing filesystem surface
- SQLite is the structured application/index/state surface
- EF Core is the sole persistence access layer
- file and database authority are separated by concern, not duplicated blindly

### Phase 3 — Markdown serialization and validation
After basic file generation existed, markdown support was made bidirectional.

Delivered:
- frontmatter generation for vault-backed entities
- reverse frontmatter parsing into CLR properties
- validation for:
  - required fields
  - enums
  - simple scalar conversion
  - string collections
  - relation PUCKs
  - nested annotated objects
- typed validation issue/result objects

Important decisions:
- YAML frontmatter was chosen as the practical markdown attribute surface
- relation fields remain string-backed, but validation understands their identity semantics
- reverse markdown parsing is deliberately simple and property-focused rather than attempting a generalized schema engine

Later refinement:
- new-entity watcher discovery now deserializes into models that already contain CLR defaults/property initializers, rather than treating missing frontmatter as immediate failure

### Phase 4 — Hosting and runtime conversion
PLAINTORCH transitioned from a console-only utility into a hosted background-capable application.

Delivered:
- ASP.NET Core host conversion
- dependency injection
- A11d module composition
- hosted core worker
- service bootstrap asset generation
- per-user host root under `~/.pleiades/plaintorch`
- per-user active-vault selection
- unix domain socket hosting through Kestrel
- minimal HTTP endpoint exposure

Important decisions:
- runtime composition belongs in the host/application shell, not in `Program` alone
- active-vault state is per-user, not buried inside a single vault instance
- socket hosting establishes a stable transport boundary for future client tooling

### Phase 5 — Vault ownership and temporal retention
Operational safety then became a focus.

Delivered:
- `.plaintorch.lock` vault ownership file
- startup refusal when another owner is active
- database graveyard entries
- file graveyard entries and archived file copies
- soft-reference audit log infrastructure

Important decisions:
- destructive flows should become temporal, not silent
- audit entries should survive lifecycle duplication and retention cleanup
- the service must explicitly own a vault to prevent split-brain behavior

### Phase 6 — API domain cleanup and application contracts
As the application surface expanded, domain and application layering were cleaned up.

Delivered:
- application-facing abstractions for system/directives/objectives/onrush/Polaris
- separate API services per action area
- aggregate application façade
- transport records where HTTP mapping needed shape separation
- namespace/domain cleanup across the project
- physical folder mirroring of namespaces

Important decisions:
- application contracts are not the same thing as transport DTOs
- helper concerns that are not API concerns were moved out of the API namespace
- the codebase should reflect domain boundaries physically, not only logically

### Phase 7 — Dynamic storage shapes and composed vault paths
The storage model became significantly richer when file-backed entities needed flexible placement.

Delivered:
- explicit `VaultStorageShape`
- dynamic parent composition metadata on `VaultStorageAttribute`
- standalone `Objectives` root
- directive nesting via directory composition
- objective placement either:
  - under `Objectives`, or
  - inside a directive directory
- path-derived hierarchy helpers in `MarkdownFileLocator`
- topology validation at bootstrap
- path discriminability validation for shared roots

Important decisions:
- sync model detection must be path-based only
- sync policy follows `VaultStorageAttribute.Mode`
- frontmatter may mirror path-derived relations, but path remains authoritative
- shared storage roots are allowed only when path identity remains distinguishable

This was one of the most important architectural inflection points in the project. It converted storage policy from implicit convention into explicit metadata.

### Phase 8 — Watcher planning and first implementation
A watcher/sync plan was first drafted and recorded, then partially implemented, and later extended into real execution.

Initial watcher foundation delivered:
- path-only sync model catalog
- startup vault discovery scan
- path-based entity detection
- markdown validation during discovery
- sync action suggestion system
- debounced filesystem watchers
- self-write suppression barrier

Later execution work delivered:
- actual reconciliation executor for watcher decisions
- `CreateFromFile`
- `UpdateFromFile`
- `RewriteFromDatabase`
- `PurgeFile`
- sync-back rewriting of canonical markdown after file-originated create/update
- canonical body preservation from the source file when rewriting
- path-source cleanup after canonical rewrite
- title-only new file handling for auto-generated PUCK entities
- auto-generated PUCK override policy for file-originated create

Later debugging/refinement delivered:
- new entity discovery now preserves CLR defaults
- title-only files no longer fall immediately into `Conflict`
- splash semaphore disposal race fixed
- splash script compatibility adjusted for Windows PowerShell 5.1
- watcher loop mitigation added by skipping redundant canonical writes when file content is unchanged

### Phase 9 — Interactive splash/status popup
A Windows-only interactive splash/status popup was introduced for `serve` in interactive mode.

Delivered:
- splash runtime service
- image-backed popup window rendered through an embedded PowerShell WPF script
- fixed application-owned splash assets
- error/loading states
- extra hold time after startup completion
- declarative presentation model in C# instead of hardcoded script styling
- transparent, borderless image-dominant window rendering

Important decisions:
- splash assets are application assets, not per-user customization inputs
- renderer remains Windows-only and interactive-only
- the initial implementation favors low project-structure overhead over a dedicated WPF project

## Major Engineering Decisions Made

### 1. Path is authoritative for sync model detection
This is one of the strongest design decisions in the project.

Effects:
- no content sniffing is required to know what a file “is”
- watcher logic can resolve entity class from path alone
- file storage policy stays consistent with `VaultStorageAttribute`
- conflict handling becomes more predictable

Tradeoff:
- path design must be extremely disciplined
- shared roots require topology validation
- parent relations mirrored in frontmatter cannot be authoritative

### 2. PUCK policy distinguishes manual and automatic segments
The project intentionally rejects the idea that all identities are equally user-editable.

Current policy:
- manual/dynamic declarations require caller/file input
- auto-generated segments ignore caller/file input
- discovered auto-generated PUCK filenames are rewritten to canonical ids on create

This is a strong consistency decision and prevents user-authored filenames from becoming accidental identity authority where they should not be.

### 3. Dynamic file storage is explicit metadata, not hardcoded special cases
By adding `Shape`, `ParentIdProperty`, `ParentEntityType`, and `ParentDirectoryProperty`, the storage system became extensible.

That avoided an unmaintainable future of per-entity watcher exceptions.

### 4. Temporal deletion support was prioritized early
This sharply reduces operational risk during sync work.

Instead of immediately deleting or overwriting files/data with no trace, the application keeps a temporal trail that can support later retention policy and debugging.

### 5. API, host, markdown, vault, and orchestration concerns are separated
This decision materially improved maintainability.

The project no longer treats all application logic as one undifferentiated mass. It now has clear clusters of responsibility.

## Amount of Work Done
The volume of completed work is substantial for a Phase 1 application slice.

### Code volume
- 104 C# files
- 10,257 C# lines
- 12 Markdown files
- 1,004 Markdown lines

### Work categories completed
- domain modeling
- persistence design and EF mapping
- identity infrastructure
- markdown IO and validation
- vault path modeling
- file storage metadata system
- topology validation
- application API contracts and services
- host/runtime composition
- socket transport exposure
- lock ownership model
- graveyard and audit systems
- watcher discovery
- watcher execution/sync-back
- interactive startup UX
- project documentation upkeep

### Complexity assessment
The work completed is not only broad; it is also structurally nontrivial. The project now contains:
- multiple interacting bounded contexts
- dual-surface state (`vault files` + `database`)
- identity policy enforcement
- hosted runtime behavior
- dynamic file topologies
- reconciliation logic
- operational safety mechanisms

That is well beyond “scaffold and CRUD.”

## Development Steps and Notable Refinement Cycles
Several important refinements happened after initial implementation rather than as one-pass features.

Examples:
- markdown deserialization required a build fix for a `StartsWith` overload issue
- namespace/folder mirroring caused duplicate-type build failures until old files were removed
- dynamic objective storage required revisiting watcher path assumptions
- splash popup styling went through three steps:
  - initial popup support
  - asset policy correction
  - declarative presentation model
- watcher development went through three steps:
  - planning
  - discovery-only foundation
  - actual reconciliation execution
- watcher create flows required further fixes for:
  - preserving CLR defaults
  - auto-generated PUCK override policy
  - canonical sync-back to originating files
  - redundant-write loop prevention

This pattern indicates active iterative engineering rather than superficial feature stamping.

## User Guidance and Engineering Share Assessment

### Assessment of user guidance quality
User guidance has been consistently high-value.

Strengths demonstrated:
- clear architectural instincts
- strong awareness of product semantics, not only syntax
- early detection of hidden policy assumptions
- willingness to constrain implementation with explicit premises
- insistence on coherent domain boundaries
- attention to documentation, not only code
- repeated correction of latent design drift before it became expensive

Particularly strong guidance areas:
- insisting that sync model detection be path-only
- insisting that storage policy derive from `VaultStorageAttribute`
- catching mixed manual/automatic PUCK policy inconsistencies
- identifying namespace/domain untidiness before it calcified
- clarifying dynamic path composition requirements for directives/objectives
- noticing that watcher discovery without sync-back was incomplete
- noticing that auto-generated PUCK path input still needed enforcement in file-originated creation
- noticing that the watcher could self-loop on reflected files

Overall assessment:
- the user has acted less like a passive requester and more like a principal product/architecture lead
- the guidance quality has materially improved the correctness of the system
- several pivotal corrections originated from the user rather than from incidental implementation discovery

### Assessment of the user’s engineering share
The user’s share in engineering the application is substantial, even where the user did not physically type every implementation.

The user has contributed by:
- setting the blueprint and intent boundaries
- supplying key architectural premises
- surfacing failure cases during testing
- refining identity and storage policy
- forcing consistency between plans and implementation
- catching missing documentation and tracking discipline
- steering the system toward domain coherence rather than local convenience

A reasonable characterization is:
- implementation labor has been heavily AI-assisted
- product architecture, policy direction, and a large portion of engineering judgment have been meaningfully co-engineered by the user

This is not a case where the user merely said “make X.” The user repeatedly supplied design-grade constraints and corrections that changed the resulting software architecture.

### Practical role split observed
A useful summary of the collaboration pattern is:

User role:
- product architect
- domain-policy authority
- systems reviewer
- integration tester
- design corrector

AI role:
- implementation engine
- codebase archaeologist
- refactoring executor
- integration and repair worker
- documentation synthesizer

That split has been effective.

## Current Status Assessment
PLAINTORCH Phase 1 is advanced but not finished.

### Strongly in place
- hosted runtime foundation
- database and migrations
- vault ownership rules
- markdown round-trip property infrastructure
- path-aware vault storage model
- sync discovery and action execution foundations
- canonical markdown rewrite from file-originated creates/updates
- audit/graveyard safety net

### Still incomplete or needing hardening
- full conflict-resolution policy
- mature delete/recreate policy for relation-protected entities
- richer watcher reconciliation guarantees
- more complete operational diagnostics and structured logging
- client/CLI repositioning over the hosted API
- retention policy enforcement
- full async cleanup across older sync helpers
- full product behavior for reflective generation, ledger ducking, `.puck` loading, lore CLR modeling, and other blueprint gaps

## Risks and Open Edges
The main current risks are now in refinement rather than raw construction.

Notable open edges:
- watcher conflict policy still needs more complete authoritative rules
- deletion semantics around relation-protected entities remain underspecified
- sync loops and event storms require continued defensive engineering
- transport/client story is still transitional
- some repository/storage APIs remain partly synchronous
- splash renderer is serviceable, but a dedicated XAML/WPF app would be more maintainable if the UX becomes more elaborate

## Overall Assessment
The project has accomplished a large amount of meaningful engineering work.

Key observations:
- the architecture is now real, not aspirational
- the system has clear identity, storage, and runtime policies
- the user has contributed significant engineering guidance
- the collaboration has produced both breadth and structural depth
- the remaining work is mostly in policy completion, operational hardening, and product maturity rather than basic construction

## Closing Summary
PLAINTORCH Phase 1 should be considered a substantial engineering build-out with strong co-engineering from the user.

In concise terms:
- the AI has written much of the implementation
- the user has meaningfully shaped the system’s architecture, constraints, semantics, and quality bar
- the resulting application is a serious foundation for continued development, not a disposable prototype

If a blunt assessment is needed:
- the user’s ability to guide the work has been strong
- the user’s share in engineering the application has been material
- the current codebase reflects both implementation throughput and genuine architectural supervision
