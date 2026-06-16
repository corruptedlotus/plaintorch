---
status: draft
assignee: Copilot 🤖
---
# Definition
## Moonlight Directive
Moonlight directives are core directives created as a part of Project Moonlight (after v3). They are "everglow" in the sense that they will not start and end, instead they act more like laws, routines and requirements. Change in them directly means a change to the pillars of Project Moonlight.
## Decree
Moonlight directives can have regular objectives and sub-directives, but their main gimmick is decrees. Decrees are essentially enduring objectives that control the flow of routines, laws and keep track of requirements. Regular directives can have 
# Model Clarification
## Moonlight Directive
Moonlight directives are the same as regular directives, they even share the same table and can be used as an objective's directive. There are a few key distinctions however:
- PUCK format is `LUNA{S:3}`.
- Has no state and no start or end and no due.
- Stored separate from directives in the vault, in `./Moonlight` by default.
## Decrees
> [!Discussion]
> Can Decrees act as Events?

Decrees are also basically objectives and share the table with them. These are what distinguishes decrees from objectives:
- PUCK format is `r{S:8}`.
- Only has Standby, Active and Archived states.
- Cannot be added to onrush sprints.
- Can be added to Polaris cycles as executive.
- Celestron is rewarded every time it's executed.
- Can have "orbit" configuration, allowing for reflective selection.
# PODS Configuration
> Pleiades Orbits Declarative Schedule

PODS syntax example:
- `M[w{0}[d{3,6}]]+2`: Every 2 months, on the 4th and 7th day of the first week.
- `w[#^3]*60`: 3 random days of every week for a total of 20 times.
- `M{0~5}[d+6],M{6~11}[d+2]`: Every 6 days during the first half and every other day during the second half of the year.