---
status: draft
assignee: Copilot 🤖
---
# Definition
## Moonlight Directive
Moonlight directives are core directives created as a part of Project Moonlight (after v3). They are "everglow" in the sense that they will not start and end, instead they act more like laws, routines and requirements. Change in them directly means a change to the pillars of Project Moonlight.
## Declaratives: Decree
Decrees are essentially enduring objectives that control the flow of routines, laws and keep track of requirements. 
# Model Clarification
## Moonlight Directive
Moonlight directives are the same as regular directives, they even share the same table and can be used as an objective's directive. There are a few key distinctions however:
- PUCK format is `LUNA{S:3}`.
- Has a different state system than that of Stellar Directives:
	- Active
	- Stale
- Stored separate from directives in the vault, in `./Moonlight` by default.
## Decrees
> [!Discussion]
> Can Decrees act as Events?

Decrees are also basically objectives and share the table with them. These are what distinguishes decrees from objectives:
- PUCK format is `r{S:8}`.
- Only has Standby, Active and Archived states.
- Cannot be added to onrush sprints.
- Can be added to Polaris cycles as executive.
- 
- Can have "orbit" configuration, allowing for reflective selection.
# PODS Configuration
> Pleiades Orbits Declarative Scheduler

PODS syntax example:
- `M[w{1}[d{4,7}]]+2`: Every 2 months, on the 4th and 7th day of the first week.
- `w[#3]*20`: 3 random days of every week over 20 weeks.
- `M{1~6}[d+6],M{7~12}[d+2]`: Every 6 days during the first half and every other day during the second half of the year.
- `d+1`:  Every other day.
## Semantic Model
- 
## Type Model
- indexer
- scope
- scope specifier
- interval
- array
- quantifier
- range

```
Indexer[]
Quantifier
```

![[structure-map.svg]]