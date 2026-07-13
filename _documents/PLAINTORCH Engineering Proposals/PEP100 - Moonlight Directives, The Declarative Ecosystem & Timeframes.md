---
status: proposed
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
### PUCK
Fates follow the PUCK notation format `e{S:8}`.
### State
Fates have the following states:
- Active
- Opt-out
- Cancelled
## Declarative: Decree
Decrees are essentially enduring objectives that control the flow of routines, laws and keep track of requirements. Their most important function is to specify Orbits that define routines. These routines will behave slightly differently based on whether or not they're part of a Moonlight directive hierarchy. (ref. [[#Reflectives]])
### PUCK
Decrees follow the PUCK notation format `r{S:8}`.
### State
Decrees have the following states:
- Active
- Abandoned
## Siblings to Objectives
Declaratives are siblings to Objectives, they share the same table and the same base class `Incentive`, each with a discriminator to identify them.
# Polaris Cycle Backlog
## Eventives
## Reflectives

![[structure-map.svg|657]]
