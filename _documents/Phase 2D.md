# Small Changes
- [x] Orbits/Agenda number warning on overdue
- [x] Improved Orbit human transcription + shortform
- [x] Show active timeframe
- [x] Pinch actions
- [x] [[PEP097 - Filename Constraints & Conversions]]
- [x] `${timebound(clock: '1s' | '60s' = '1s')}`: update on global tick
- [x] Respect timeframe Orbital restrictions
- [x] DepGraph: Nodes should be only moved when selected as opposed to not
- [x] DepGraph: Primary scroll should handle zoom
- [x] DateTime view granularity support
- [x] Entity Banner: Dependencies
# Special
- [x] Polaris briefing in-place insertion
- [x] Move parent/owner
- [x] Timeframes: Availability & active flare
# Views
- [x] Unify "extra view parts" as an extension to the note banner system (eg. what we currently have for Timeframes and ExecutiveOrders)
- [x] Executive unification
- [x] Attentive & Eventive modal
# Instance Management
- [x] Rework occurrence timing system
- [x] Attentive skipping (rescheduling) + Opt-in Orbit shift recalculation
# Bugs
- [x] LoreIndex getting retconned by repo
- [x] Circular reference when syncing objective edits
- [x] Watcher note creation and repositioning is still a mess
- [x] Single touch behaving like multi touch in graph
- [ ] editButton weird behaviour
- [x] Orbit humaniser doesn't use the preferred calendar
- [x] `*` or `@` wrong restraint + make humanised more readable
# Infrastructure
- [x] Enable polyfill and abstentions for Obsidian-dependent codepaths
- [x] Decouple the frontend from Obsidian and push the Electron client
- [x] [[PEP116 - User Preferences]]
- [x] Standalone: Status window