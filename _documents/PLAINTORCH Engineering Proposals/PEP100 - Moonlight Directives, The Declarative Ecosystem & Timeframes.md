---
status: implemented
assignee: Copilot 🤖
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