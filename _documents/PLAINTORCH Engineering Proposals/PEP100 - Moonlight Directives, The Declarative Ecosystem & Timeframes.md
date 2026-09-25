---
status: implemented
patches:
  - Patch100.1 - Timeframe Auto-Inclusion, Icons & Affinity Surfaces
  - Patch100.2 - Exclusive & Active Timeframes, Availability & Auto Affinity
assignee: Copilot 🤖
phase: 2a
---
# Directives
## Moonlight Directives
Moonlight directives are core directives created as a part of Project Moonlight (after v3). They are "everglow" in the sense that they will not start and end, instead they act more like laws, routines and requirements. Change in them directly means a change to the pillars of Project Moonlight.
### State
Directives have a different state system than that of directive:
- On Hold
- Active
- Stale
### PUCK
Moonlight directives use the PUCK format `LUNA{S:3}`.
## Siblings to Stellar Directives
Moonlight directives live as siblings to Directives, which we will call Stellar Directives from here on out, on the same table, share the same base model, and have a discriminator for both.
# Declaratives
## Declarative: Fate
Fates are events, compared to objectives they cannot be done or undone, but only happen. They can however be missed or cancelled. Fates can be all-day, or have a start and end time (ref. CalDAV vEVENT). Fates can also specify scheduling using the Orbit scheduler syntax.

Fates carry an event duration, specified either through their Orbit or through their own specification. That duration is what fills the eventives they materialize (ref. [[#Eventives]]).
### PUCK
Fates follow the PUCK notation format `e{S:8}`.
### State
Fates have the following states:
- Active
- Abandoned
## Declarative: Decree
Decrees are essentially enduring objectives that control the flow of routines, laws and keep track of requirements. Their most important function is to specify Orbits that define routines. These routines will behave slightly differently based on whether or not they're part of a Moonlight directive hierarchy. (ref. [[#Reflectives]])

Decrees can specify a default length, which seeds the time allocation of the attentives they materialize. That default can be overridden per attentive, either when the attentive is created through interaction/proximity or when it is added to a Polaris cycle (ref. [[#Attentives]]).

Decrees also predefine the Celestron reward their attentives grant (ref. [[#Attentives]]).
### PUCK
Decrees follow the PUCK notation format `r{S:8}`.
### State
Decrees have the following states:
- Active
- Abandoned
### Lunar Reflection
Decrees that live inside a Moonlight directive hierarchy can specify a `reflect` property. Setting it to true opts the decree into daily reflective generation (ref. [[#Reflectives]]). Decrees under Stellar directives cannot participate in Moonlight reflection; the property carries no meaning for them.
## Siblings to Objectives
Declaratives are siblings to Objectives, they share the same table and the same base class `Incentive`, each with a discriminator to identify them.
## Orbit Specification
Both declarative types can specify an Orbit notation to enable scheduling. The Orbit should always resolve to day-granularity.
## Incentive Parenting
As of this PEP, incentives gain a parent system:
- Objectives can specify a parent objective, which makes them subtasks.
- Objectives can also specify a Fate declarative as their parent.
- Fates can specify another Fate as their parent.
- Decrees are exempt from the parent system entirely: they neither parent nor get parented.
# Interaction & Proximity
Decrees are just like Fates in this regard: both are event-like entities. They are never acted on directly in day-to-day operation; each occurrence instead materializes an instance in the backlog record family — an attentive for a decree, an eventive for a fate. An objective's due date behaves the same way (ref. [[#Eventives]]).

An instance comes into existence through one of two triggers:
- **Interaction**: making a change to one single occurrence. Since changing an instance requires that instance to exist, the respective attentive or eventive is created first, and the change is then applied specifically to that instance.
- **Proximity**: getting close to the event time, or to the pre-orbit time of the upcoming Orbit occurrence.

Instances created through interaction or proximity are **not** placed inside a Polaris cycle. On the other hand, manually adding a decree to a Polaris cycle creates a Polaris-bound attentive.
# Polaris Cycle Backlog
## Backlog Record Structure
Reflectives, attentives and eventives share the same structure as executives, including the working time units of [[PEP098 - Executive Time Units]]. The key difference between them is in how that structure is filled, not in how it is used:
- **Executives** are filled by the user, as usual. Their creation from objectives is unaffected by this PEP.
- **Eventives** take their duration from the owning fate's event duration (from its Orbit or its specification).
- **Attentives** take their length from the owning decree's default length, overridable per instance as described above.
## Record States & Mobility
Each record kind resolves differently:
- **Executives** can only be done. They keep the whole time allocation system of [[PEP098 - Executive Time Units]], per-day and per-executive.
- **Eventives** can be missed or cancelled. They carry a time specification, and since an eventive is never bound to a Polaris cycle, it can also be moved.
- **Attentives** can have a time either way. When unbound, they can be done, skipped, or rescheduled (delayed). When Polaris-bound, they can only be done, skipped, or moved to another Polaris cycle.
- **Reflectives** can have a time, and can be done or not. The rest of their behavior is left to [[PEP104 - Reflective Generation Engine]].
## Eventives
When a Polaris cycle begins, any Fate that collides within 24h of the starting point has an eventive — created through proximity if it does not exist yet — which the cycle includes. Eventives are never Polaris-bound; the inclusion is always the non-structural kind (ref. [[#Inclusion of Unbound Items]]). Eventives are just like Executives structurally, but they record occurrences that happen rather than work that gets done: an occurrence can be missed or cancelled instead of being executed or left undone. Eventives carry no Celestron reward.

The due date of an objective also creates an eventive, again through proximity, exactly like fates and decrees.
## Attentives
Attentives are the per-occurrence instances of decrees. They are created unbound through interaction or proximity, or Polaris-bound by manually adding their decree to a Polaris cycle.

The Celestron reward of an attentive is predefined on its decree and cannot be overridden. The reward is granted on each attentive execution.
## Reflectives
Lunar-hierarchy decrees with `reflect: true` participate in daily reflective generation. Reflectives are
Polaris-bound schedules: the decree's orbit is resolved only to day granularity, against the day the Polaris
cycle started — if it falls within that day, it's a match, and the matching decree generates a cycle-bound
reflective when the cycle begins. The fuller generation engine (source pools, weighting, cooldowns, prompt
synthesis) belongs to [[PEP104 - Reflective Generation Engine]].

Reflectives are a special kind: they do not respect the reward of the decree that sets them. Instead, when all reflectives of a single Polaris cycle are done, that entire collection rewards the user with a fixed amount of Celestron.
## Inclusion of Unbound Items
A Polaris cycle still includes unbound (no-Polaris) items when they fall within 24h of its beginning. This inclusion is not structural: the cycle presents them alongside its own backlog, but does not relationally own them, and they stay unbound.
# Timeframes
> Originally drafted in [[PEP095 - Timeframes & Affinity System]], now folded into this PEP.

Timeframes are directive-level definitions of portions of time within a Polaris cycle — or within multiple Polaris cycles, depending on their Orbit definition. Timeframes specifically don't do anything on their own; they simply flag a portion of time within a day that can be used to clarify affinity.

Affinity is purely semantic and demands nothing engineering-wise. Executives can define a timeframe as their affinity, meaning that timeframe is preferred for their execution.
# Hierarchy Map
![[PEP100 - Moonlight Directives, The Declarative Ecosystem & Timeframes 2026-07-14 17.37.02.excalidraw]]
# Patches
## Patch100.1 - Timeframe Auto-Inclusion, Icons & Affinity Surfaces
Timeframes stay exactly what this PEP made them — inert, lunar-owned markers of a purely semantic affinity that enforces nothing. This patch does not change that; it adds ways for a timeframe to be *attached to*, and ways for it to be *seen and edited*. Three things: auto-inclusion, affinity on reflectives, and icons with the surfaces to manage them.

### Auto-inclusion
Until now affinity was set by hand, one executive at a time. A timeframe can now **auto-include** the workitems that flow into a Polaris cycle: when a matching workitem is created, it is affined to that timeframe on creation, with no manual step. Two creation paths are wired — an **executive built from an objective** (any planning mode carrying an objective; a title-only one-shot executive has no objective and so no auto-affinity), and a **reflective materialized from a decree** and bound to a cycle (the lunar-reflection path that runs at cycle begin, ref. [[#Reflectives]]).

The criterion, for now, is the **College**: a timeframe set to include a college affines every executive or reflective whose owning incentive — the objective behind the executive, the decree behind the reflective — carries that college. It is modelled as a deliberate extension point rather than a hardcoded rule. A timeframe stores an *inclusion kind* (`None` | `College`) beside its parameter; College is the first kind, and a further single-value criterion (a directive, a tag) slots in as a new kind paired with the column it reads, resolved by a new branch in the resolver rather than a new mechanism.

Affinity is single-valued — a workitem points at one timeframe — so when several timeframes include the same college the **lowest-id match wins**, deterministically. Auto-inclusion only ever *seeds* affinity at creation; a later manual change is never overridden by it. The resolution lives in one place (`TimeframeAffinityResolver`) that both the executive-planning and reflective-materialization paths go through, so the two stay in step.

### Affinity reaches reflectives
Affinity was an executive-only property. For auto-inclusion to reach both kinds of Polaris-level workitem, reflectives gain `AffinityTimeframeId` (+ navigation), mirroring the executive's existing affinity foreign key: removing a timeframe sets the reference back to null rather than cascading, since affinity is a soft pointer and losing the timeframe just un-affines the workitem.

### Icons
A timeframe can carry an **icon**, through the same media companion the directive icons use (ref. [[PEP105 - Directive Icons & Banners]]). Timeframes keep no asset folder of their own, so a glyph or lucide name and a vault-level (`vault:`) image resolve, while a `media:` self key has nowhere to live. The icon stands in for the **Celestron value**: on an executive affined to a timeframe, its objective item shows the timeframe's icon where the objective's Celestron reading would be. With no affinity the Celestron reading is kept, so nothing is lost when a timeframe is not in play.

### Affinity surfaces
- **Executive affinity selector.** The executive time-allocation modal gains a simple affinity selector beside the existing status selectors — the same edit-in-place control, showing the current timeframe's icon and title (or a muted "no affinity") and opening a picker over every lunar directive's timeframes, led by a "no affinity" choice so an executive is un-affined the same way it is affined. Auto-inclusion sets the initial value; this is how it is changed by hand.
- **Lunar directive editing modal.** Lunar directives get a dedicated editing modal, modelled on the onrush detail window (ref. [[PEP102 - Backlog Dependencies & Milestones|Patch102.5]]): the directive's own banner at the top — so a rename or moonlight-state shift reaches every other surface at once — with its timeframes listed and edited beneath, each row editing title, window, Orbit scoping, icon key, and the college it auto-includes, addable and removable. A lunar directive carries no measure of its own, so its otherwise-empty measure column in the entity grid hosts the button that opens the modal — the single place a lunar directive's timeframes are managed.

### Data model
- `Timeframe` gains `Icon` (`[Media]`-enriched to `IconMedia`), `AutoInclusion` (the inclusion kind, default `None`), and `AutoInclusionColleges` (the colleges it includes, a JSON list read only when the kind is `College`; it first shipped as a single `AutoInclusionCollege` column, and a follow-up migration turned it into the list, carrying any set college over).
- `Reflective` gains `AffinityTimeframeId` (+ set-null foreign key).
- A schema migration adds the three timeframe columns and the reflective affinity column, index, and foreign key.

### Held over
- **Abstract (clockless) timeframes** — a timeframe still carries a wall-clock window; the question of clockless, sprint-scoped timeframes is held separately.
- **Enforcement** — affinity stays purely semantic. Auto-inclusion decides *what a timeframe is attached to*, never *what the schedule does*.
- **Richer reflective UI** — reflective affinity is seeded and stored but has no dedicated editing surface here; the fuller reflective experience belongs to [[PEP104 - Reflective Generation Engine]].
## Patch100.2 - Exclusive & Active Timeframes, Availability & Auto Affinity
Patch100.1 let a timeframe attach itself to workitems by college. This patch works on both sides of that relation. On the timeframe side it adds **exclusivity**, and it moves the question "which timeframes are in play right now" from the client into the core, which now reads a timeframe's Orbit as well. On the directive side it adds **availability**: a directive picks the timeframe its work belongs in. At creation, a new **Auto** affinity makes the core's choice the default. Timeframes still enforce nothing. Each addition here decides what a timeframe *is attached to* or *is shown as*, never what the schedule does.

### Exclusivity
A timeframe can be marked **exclusive** (`Exclusive`, off by default, toggled in the lunar directive modal's timeframe editor). Exclusivity is judged among the timeframes that are active at the same moment. When any active timeframe is exclusive, only the active exclusive ones are reported (all of them, if there are several), and every active non-exclusive timeframe is dropped. An exclusive timeframe that is not active suppresses nothing. Exclusivity plays no part in auto-inclusion or affinity: a timeframe hidden behind an active exclusive one is still a valid affinity and still auto-includes.

The editor's toggle is a new general-purpose editable, `p7t-editable-toggle` (`EditableToggle`). It is a boolean editable that commits the moment it is pressed (click, Space or Enter). Its face is an on/off icon and text pair, or a custom face supplied through named slots, and it binds like every other editable. The decree banner's Lunar Reflection row, the pattern the toggle was drawn from, now uses it too.

### Per-cycle candidates & active timeframes in the core
Until now the client decided which timeframes were active by comparing each window with its own clock, and it ignored the Orbit entirely: a timeframe scoped to weekdays still showed on a weekend. The core now answers at `GET /api/timeframes/active`, in two stages.

**Candidates, per cycle.** When a Polaris cycle begins, the core works out which timeframes apply to it. That is every timeframe without an Orbit (no orbit means every day), plus every timeframe whose Orbit selects the cycle's day. The cycle's day is its local start day, the same day lunar reflection matches against. Orbits are read on the vault's default calendar (PEP116; Pleiadean unless changed). The set is fixed for the cycle's life, even when the cycle stays open past midnight. It is cached as timeframe ids only (`TimeframeCandidateCache`), so a renamed directive or an edited window never goes stale. The cache is warmed at the two moments a begun cycle can be found:
- when the cycle begins;
- at vault activation, for a cycle that had already begun before the core booted.

Both warms are best effort. A cache is only a shortcut, so a failed warm is logged and never fails the begin or the activation; the next read computes the set instead.

Any write that can change the set drops the cache, and the next read recomputes it. Those writes are:
- a timeframe added, edited or deleted;
- a cycle begun, ended or deleted, including through its note's frontmatter;
- a directive deleted, since its timeframes cascade away with it;
- a change to the default-calendar preference.

Releasing a vault purges the cache.

**Active, per request.** Only a strictly active cycle (begun and not ended) has active timeframes; with none, nothing is active. A candidate is active when the local wall-clock time, at minute precision, lies within its window, start inclusive and end exclusive. A window whose start is later than its end wraps midnight (22:00–06:00 is active at 23:00 and at 05:00). A window whose start equals its end is never active. Exclusivity is applied next, and the records come back shaped and ordered like the global timeframe listing (by directive title, then start time).

The active-timeframe chip now only fetches and draws what the core returns. It re-reads on the shared 60-second tick, so a chip can trail a window edge by up to a minute.

### Timeframe orbit schedule state
Reading a timeframe's Orbit against a day needs the same anchor a declarative's does: an interval orbit (`d%2`) must count from somewhere, and a random index (`{#n}`) needs a pinned seed. Timeframes therefore get persisted orbit state. Rather than growing a second, parallel mechanism, `OrbitScheduleState` expands into a hierarchy. The abstract `OrbitScheduleState` (id, snapshot JSON, last update) has two kinds, which share the one table through a discriminator:
- `IncentiveOrbitScheduleState` is the existing per-declarative state, unchanged in behaviour;
- `TimeframeOrbitScheduleState` is new, one per orbit-scoped timeframe.

Each kind is keyed uniquely by its owner and is deleted with it. A timeframe's state also goes when a lunar directive's delete cascades its timeframes away.

A timeframe's state follows the declarative reset policy. It rides the same save hook, so every write pathway behaves alike. Setting or changing the orbit re-anchors the state. Clearing the orbit removes it. A calendar change never resets it, just as it never resets a declarative's.

The anchor differs from a declarative's in one respect. A fixed `Z{…}` literal anchors at its own date, for both. Any other declarative orbit anchors at today. Any other timeframe orbit anchors at the earlier of today and the start day of the open cycle, if there is one. An orbit yields nothing before its anchor, so without this a timeframe orbit set after midnight could never select the day of a cycle begun the evening before and still open. A timeframe state is only previewed and never materializes anything, so anchoring it back has nothing to backfill. A cycle begun later but dated to an earlier day than an existing anchor still misses that timeframe.

Anchoring back is not free for an orbit with a phase. An interval (`%N`), a count limit (`*N`, `@N`) or a seeded random counts from the anchor, so one set while an older cycle is still open counts its phase from that cycle's day, not from the day it was set, however long that cycle has been open. This trade-off is accepted: every orbit set under an open cycle must be evaluable on that cycle's day, and the open cycle's day is the day the product is working in.

A timeframe's `Z{…}` literal names a date on the calendar its orbit is read on, the vault default (Pleiadean unless changed), so its anchor is that calendar date's civil day, the day the engine resolves the literal to. A Pleiadean `Z{3/3/40}` is the 40th day of the third month. A literal naming a day the calendar does not have anchors like any other orbit instead of failing the save. A declarative's literal is still read as a Gregorian date, which suits a fate (its one-offs are pinned to Gregorian) but gives a decree on the Pleiadean calendar a Gregorian-read anchor; that older edge is an open question.

A timeframe whose orbit predates timeframe states gets a state lazily, the first time its cycle candidates are computed. It is anchored like the incentive lazy path (at the day being read if that day is in the past, else today) and saved so the anchor sticks. If an orbit edit or a delete of that timeframe saves first, the lazy state is dropped and the listing still answers. Timeframes are only ever *previewed* against a day, so their state is never advanced. A stored orbit that cannot be read counts as not matching and is logged; it does not fail the listing.

### Availability
Auto-inclusion gains a third kind, **Availability**, beside `None` and `College`. The timeframe editor now picks the kind explicitly (None, College or Availability) instead of inferring it from whether any college is listed. The college chips appear only in College mode, and emptying the list no longer drops a timeframe back to None. An Availability timeframe has no parameter of its own; the choice lives on the directive. **Every directive**, stellar or lunar, can name one Availability-mode timeframe as its **availability**, from any lunar directive, and picks it from its banner.

A workitem is auto-affined to a directive's availability when its owning incentive belongs to that directive or to any of its descendants:
- **The nearest directive wins.** The walk starts at the incentive's own directive and climbs the parent-directive lineage. The first directive whose availability still names an Availability-mode timeframe decides. A hand-edited vault may loop a lineage, so the walk stops at the first repeat.
- **What does not count.** Availability does not inherit through a parent incentive. The owning lunar directive's moonlight status plays no part, as with college.
- **Availability takes precedence over College.** The college rule (lowest id wins) applies only when no directive in the lineage has an availability.

Both rules live in one resolver (`TimeframeAffinityResolver.ResolveAsync`), and every auto-assignment goes through it:
- executives planned from an objective;
- executives created by adding a decree to a cycle;
- the reflectives lunar reflection creates when a cycle begins.

Like college, availability only seeds at creation. Changing a directive's availability later never re-affines existing workitems.

Everywhere else, an Availability timeframe is an ordinary timeframe: it appears among the active timeframes and can still be picked as a manual affinity. When a timeframe leaves Availability mode, or is deleted (on its own or with its lunar directive), every directive naming it as its availability is cleared in the same save. The clear rides the state-policy save hook, so it holds on every write pathway and the change feed announces each cleared directive; the set-null foreign key is only the database's backstop. As defence in depth, the resolver also ignores an availability that does not point at an Availability-mode timeframe.

Availability is database-only. A timeframe id is a database row id, not a PUCK, so it has no place in a directive's frontmatter, and the watcher's note sync keeps it instead of clearing it.

### Auto affinity
The Polaris creation row's affinity picker now offers **Auto**, and Auto is the default. The affinity travels inside the create request on both paths: planning an executive (`PolarisExecutivePlan`) and adding a decree (`PolarisDecreeAdd`). It is tri-state:
- **Key omitted: Auto.** The resolver above decides. A title-only one-shot executive gets none, since it has no incentive to resolve from.
- **Explicit `null`: none,** even when auto-inclusion would match.
- **An id: that timeframe.** The timeframe must exist. An unknown id is refused before any cycle is started or objective created.

The executive comes back carrying its resolved affinity, so the creation row no longer patches the affinity on afterwards. Before this patch, leaving the picker empty silently meant auto, and choosing no affinity at creation was impossible.

### Data model
- `Timeframe` gains `Exclusive` (default `false`). `TimeframePlan`, `TimeframeUpdate` and the `DirectiveTimeframeRecord` listing carry it too.
- `TimeframeInclusion` gains `Availability`, appended as value 2. The enum is stored and sent as an integer, so members are only ever appended.
- `Directive` (the abstract base, so both kinds) gains `AvailabilityTimeframeId`, a set-null foreign key to `Timeframe`. It is database-only, and its navigation is never auto-included or served; clients resolve it from the global listing.
- `OrbitScheduleState` becomes an abstract TPH base in the `OrbitScheduleStates` table. Its kinds are `IncentiveOrbitScheduleState` (`IncentiveId`, unique, cascading) and `TimeframeOrbitScheduleState` (`TimeframeId`, unique, cascading). Rows are keyed by a new autoincrement `Id` instead of the incentive id.
- `PolarisExecutivePlan` gains a tri-state `Optional<long?> AffinityTimeframeId`, and `PolarisDecreeAdd`'s affinity changes from `long?` to the same type.
- New routes: `GET /api/timeframes/active`, and `PUT /api/directives/{id}/availability` with body `{ timeframeId }` (`null` clears).
- Two schema migrations:
  - `OrbitScheduleStateHierarchy` rebuilds the state table. Every existing state becomes an incentive state, with its incentive, snapshot and timestamp intact under a fresh id. Its Down drops the timeframe states, which the old table has no key for.
  - `TimeframeExclusiveAndDirectiveAvailability` adds the timeframe column and the directive column, index and foreign key.

  Both migrations are tested in both directions against rows written before them.

### Held over
- **Enforcement.** Still none. Exclusivity filters what is reported as active, never what may be scheduled or affined.
- **Push instead of poll.** Active timeframes are re-read on a one-minute tick instead of being pushed when a window opens or closes.
- **Retroactive assignment.** A new availability, or a timeframe moved into a mode, never re-affines workitems created before it. Re-resolving existing workitems would need its own ruling on what counts as a manual choice.
- **Serving the availability navigation.** A directive is served with its availability id only, so a client resolves the timeframe from the global listing.
- **Route-level tests.** The new wire shapes are pinned against the host's own serializer settings, but the two new routes are tested only through the services. Testing them over HTTP waits for an in-process host harness.
- **Abstract (clockless) timeframes** and **richer reflective UI** are still held, as in Patch100.1.