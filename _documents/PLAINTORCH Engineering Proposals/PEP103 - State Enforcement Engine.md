---
status: accepted
patches:
  - Patch103.1 - Lifecycle States and Enforcement Levels
  - Patch103.2 - The State Interceptor
assignee: Copilot 🤖
phase: 3a
---

# Patches
## Patch103.1 - Lifecycle States and Enforcement Levels
State enforcement is being rewritten from scratch, and this first patch lays the base declarations the engine stands on: the lifecycle every backlog kind is read in, the one place that reads it, the schedules that place it in time, and the policy that decides what happens when a change would break a lifecycle rule. Nothing new is enforced yet. The old rules still run, re-expressed in the new states, until the engine replaces them.

### Four lifecycle states
`LifecycleState` has four values: Deferred (−1) → Open (0) → Started (1) → Finished (2).
- **The main line** runs Open → Started → Finished. The values are ordered along it, so a later state compares greater.
- **Deferred** sits below Open as the side exit for an item set aside. A deferred item has neither started nor finished and satisfies no dependency it is the source of. It keeps its place on the main line until it resumes.
- **Every status member declares its state** (`[LifecycleState]`), so no rule names a status to learn what it means.
- **Two kinds have no plain status**, so they supply their state through a source:
  - an eventive: a resolution that sets it aside defers it; otherwise its time window opens, starts and finishes it
  - a checkpoint: finished once unlocked, deferred when still locked past its due

| Kind | Deferred | Open | Started | Finished |
|---|---|---|---|---|
| Objective | **Failed** | **Standby**, Blocked, Onboard | **Polaris** | **Done**, Archived |
| Stellar directive | **Failed** | **Planned**, Committed | **Active** | **Fulfilled**, Over |
| Fate | **OptOut**, Cancelled | — | **Active** | — |
| Decree | **Abandoned** | — | **Active** | — |
| Lunar directive | **OnHold**, Stale | — | **Active** | — |
| Attentive | **Skipped** | **Pending** | — | **Done** |
| Eventive | **Missed**, Cancelled, OptOut | **Pending**, before its start | Pending, after its start | Pending, after its end |
| Checkpoint | locked once its due has passed | locked | — | unlocked |

The status in bold is the state's default. It is the member with the lowest value among those that declare the state, and the core sets it when it moves an item to that state of its own accord.

The user ruled on the following:
- Set-aside outcomes are Deferred. Blocked stays Open, since a blocked objective is meant to go on.
- Decrees, lunar directives and attentives gained a lifecycle in this patch.
- Fates, decrees and lunar directives never finish.
- A checkpoint can finish or be deferred, but never starts. It fails one way only: its due passes before it is finished (satisfied, toll paid, condition met). Paying the toll it then owes still finishes it late.

### Reading a lifecycle
`EntityLifecycleResolver` is the one place the core reads a lifecycle:
- `StateOf` gives the current state of an entity or a status. `StatesOf` gives the states a kind passes through.
- `NextState` gives the next state up the order. `NextGatedState` gives the next of Started and Finished that a dependency can hold back; a deferred item counts as before Open.
- `Transition` reads a move as a `LifecycleTransition`:
  - the progress states the move enters: a jump from Open to Finished enters Started and Finished
  - the progress states it leaves going back
  - whether it defers or resumes

  A shift between two statuses of one state is no transition, and a deferral enters and leaves nothing on the main line.
- `DefaultStatus` translates a state to a status, as above.

`LifecycleDeclarations` reads the enums. Activation validates every declaration:
- each lifecycle status enum is complete and has a main-line state
- the status enum of a state source declares only states that source passes through
- no kind has both a status property and a source

### Schedules
Plans place Started or Finished, and only a state the owner's kind reaches. A checkpoint has no start to plan, and a fate, a decree or a lunar directive has no finish.

Records track every state but Open (user ruling):
- Reaching Started or Finished records it.
- Pulling the item back along the main line withdraws the records of the states it leaves.
- Deferring keeps the Started and Finished records, so failing a started objective keeps its start, and adds a Deferred record.
- Resuming withdraws the Deferred record. The move is read from the place the kept records show, so resuming to where the item was changes nothing else.

The `LifecycleStates` migration made three changes to stored schedules:
- It renamed the stored states: Begin became Started, and Finish became Finished.
- It turned a cancelled fate's finish records into deferred records.
- It dropped fates' finish plans.

Nothing is backfilled. Records the old failing rule withdrew are gone. Items already set aside, as well as decrees and lunar directives, start recording at their next change.

### Dependencies and the gate
Dependencies attach to Started and Finished as before (`OnBegin`/`OnFinish`, `ToBegin`/`ToFinish`). The gate now judges every state a shift enters:
- A jump straight to a finish meets the start gate as well as the finish gate.
- A move back is not gated, so reopening a finished objective into Polaris no longer re-checks its start.
- A shift from one finished status to another (Fulfilled → Over) still re-checks the finish gate, as it always has, because Over is the finish that tolerates no open child.

The mapping changes what satisfies an edge:
- Missed, cancelled and opted-out occurrences no longer satisfy finish edges, and neither does a cancelled fate.
- A fate never finishes, so an edge from a fate can no longer be satisfied at all (I-191).

### Backlog state enforcement level
The `backlog.state-enforcement` preference (BSEL) decides what happens when a change would break a lifecycle rule:

| Level | What happens |
|---|---|
| Ignore | The change goes through. |
| Auto | The core compensates by moving the other items the requested state needs. |
| Warn (default) | The user is asked: they cancel the change or force the compensations. |
| Block | The change is refused. |

A systemic conflict, one no state change could resolve, is refused at every level.

`StateDecisionService` is the one place the level is applied.
- **What goes in:** the conflicts a change causes, each with the compensations that would resolve it. The engine will find these; nothing calls the service yet.
- **What comes out:** proceed, compensate, wait for confirmation, or refuse.
- **When it refuses under Auto and Warn alike:** a conflict that offers no compensation, or compensations that would move one item to two different states.

Warn relies on a request-wide API flag:
- When a decision stops a change, the API answers 409 Conflict with the decision (`StateEnforcementProblem`).
- The client then cancels, or repeats the call with `X-Plaintorch-Enforcement: auto` to force the compensations.
- Any other value of that header is refused with 400.
- A change nobody can be asked about, from a note edit or a background pass, is refused under Warn.

### Ledger
- **Struck:** I-019, I-020, I-025, I-030.
- **Trimmed to what still holds:** I-029, I-057, I-070, I-073, I-188.
- **Added:** I-191, which this patch's states create, and I-192, which this patch's Warn flow runs into.

### Open edges
- **Warn with nobody to ask.** A note edit or background pass that meets a Warn conflict is refused, which matches the watcher's "revert the note and raise an issue". The alternative is to compensate there as under Auto. Decide before the watcher pathway joins the engine.
- **Over's rule lives in the gate.** It is a re-check on finished-to-finished shifts, not an invariant declared on Over itself ("Over has no open child").
- **Edge sides ignore the kind's states.** An edge's trigger and constraint are not checked against the states its endpoints reach, and a null trigger means OnFinish even for a fate, which never finishes (I-191). Open: what a fate's default trigger should be, or whether finish edges from fates are refused.
- **Clashing compensations.** Two compensations that would move one item to different states are refused rather than reconciled.
- **Checkpoint deferral goes unrecorded.** A checkpoint becomes deferred by time alone, so its deferral is not recorded until the engine has a clock (I-070).
- **The client can't prompt yet.** The SDK drops every non-OK response, so a client cannot build the Warn prompt (I-192). This must land with the first engine rule that can return Warn.
- **On Hold's reach.** What On Hold should suspend beyond reflection is still undecided (I-024).

## Patch103.2 - The State Interceptor
The engine starts here. Every piece of state logic now runs in one save interceptor, and every decision a rule calls for goes through the backlog state enforcement level. The rules themselves are the ones the core already had, moved rather than rewritten, with three exceptions: the gate now reaches every pathway, the Over rule is declared on Over, and reopening an Over directive became a compensation.

### One interceptor
`StateInterceptor` replaces `PlaintorchStatePolicyInterceptor` and `BacklogScheduleInterceptor`. It runs the `StateEngine` before anything is written, on every save of a request context:
- asynchronous and synchronous saves alike (the policy used to skip synchronous ones)
- every pathway: the API, the watcher's note edits, activation and background passes

It is registered first, so the change feed and the caches see the save the engine leaves behind.

The logic the API services held moved in with it. The services now only set what the caller asked for:
- the gate on objective, stellar directive and fate status changes
- the resume fast-forward of fates and decrees
- the onrush promotion of a waiting objective that joins a sprint
- the release of a deleted checkpoint's edges

Three things stay outside:
- the freeze of a locked fate's materialisation (LC-11), a generation policy that decides what is created rather than judging a save
- the services' own input checks
- the sprint and cycle verbs

`DependencyGateService` keeps only its readings: the lock view, `GatesNextTransition`, and the freeze.

### Four phases
| Phase | Class | Rules |
|---|---|---|
| 1. Structure | `StructureReactions` | Work kept on delete (SH-01, SH-02), boundary ends (SH-03), availability (SH-04), cursor resets and the resume fast-forward (SH-05, SH-06, DE-08), onrush promotion (OB-04), hardening (SH-17), edge release on delete, checkpoints included (SH-18, DP-12), purge (SH-19) |
| 2. Judgement | `StateJudge`, `StateDecisionService`, `StateCompensator` | Exclusivity (SH-08 to SH-10), the gates (LC-09, LC-10), the Over rules (SH-20) |
| 3. Outcomes | `OutcomeReactions` | Next occurrences (SH-07), forecast supersession (SH-11), settlements and rewards (SH-12 to SH-16, SH-29), the reconciler (SH-21, SH-22) |
| 4. Records | `BacklogScheduleRecorder` | Records and slots (SH-25, SH-26) |

After the commit, `StateNoteSyncService` deletes the notes of superseded forecasts (SH-23) and rewrites the notes of the items the engine moved, which generalises SH-24.

Phase 3 runs on the final state, so a compensated item is settled, rewarded, reconciled and recorded like a requested one.

A resume that also changes the orbit now resets the cursor first and then seeks the fresh one. Both reach the same state row, tracked or stored, so the save no longer inserts two (I-026).

### The rules the judge applies
**Systemic rules.** These are refused at every level:
- one planning placeholder sprint
- one active onrush sprint
- one active Polaris cycle

They are read on the sprints and cycles as the save leaves them, so ending one and starting another in a single save is allowed.

**Lifecycle rules.** These are judged on every state a save moves an objective, a stellar directive or a fate into, on every pathway:

| Rule | Judged when | Holds when |
|---|---|---|
| `dependency.start-gate` | An item enters Started | Every edge constrained to the start (`ToBegin`, the default) is satisfied |
| `dependency.finish-gate` | An item enters Finished | Every edge constrained to the finish is satisfied |
| `directive.open-children` | A stellar directive enters Finished or becomes Over | Every direct child is finished or set aside |
| `directive.over-open-work` | Open work arrives under a directive that was Over and stays Over: created there, moved there, reopened from a finish or taken up again from a deferral | The directive is not Over |

A move back, a deferral and a shift between two statuses of one state are never gated.

Compared with the old gate:
- **It runs on every pathway** (I-001): a frontmatter `status:` edit, init-from-file, planning an objective into Polaris, any other writer.
- **Over's rule is declared on Over.** Becoming Over needs the children finished. A shift between two finished statuses no longer re-checks the explicit finish edges, so Done → Archived goes through even when a finish edge was added after the objective finished.
- **The Over reopening is a compensation**, so the level decides it rather than happening silently. Taking a set-aside child up again counts as open work arriving (I-032).

**Messages.** A message says what blocks a change, never which items do: "Objective 'Tower' cannot start: it is blocked by 2 dependencies." The compensations name the items that confirming would move.

### Compensations
Each lifecycle conflict carries the moves that would resolve it:
- a dependency's source moved to the state its trigger needs
- an open child finished
- an Over parent reopened to Fulfilled

Each item takes the default status of its new state (Done, Fulfilled, Polaris, Active). A move is judged in turn, as a requested one is, so the moves cascade: finishing a directive finishes its children, then theirs.

**Pass-through.** A checkpoint held only by its own edges passes the moves through to their sources.

**Nothing to move.** A conflict carries no compensation, and so is refused under Auto and Warn alike, when it is blocked by:
- a source that is set aside
- an occurrence, which time moves
- a fate asked to finish
- a checkpoint held by its toll or its condition
- an endpoint with nothing behind it

**Merging.** Two moves of one item along its main line now merge into the furthest, since a finish passes through the start. Only a deferral against a main-line move still clashes, and no rule proposes a deferral yet.

**Applying them.** Compensations commit with the change in the same save. Each one goes on the audit record (`state.compensate`), and the conflicts that Ignore lets through are recorded too (`state.ignore`). Each moved item gets a reconcile intent in the same transaction, and its note is rewritten once the save commits, unless its caller writes that note already.

### How each pathway answers
**API.** A stopped change is answered with 409 Conflict and the decision.
- The SDK hands it to the client's `stateEnforcement` handler.
- When the core asks for confirmation, the handler is asked. If it agrees, the SDK repeats the call with `X-Plaintorch-Enforcement: auto`, and a call that was already forced is never put again.
- A refusal is told to the handler.
- A 409 of another kind falls through as before.

**SIPA, changing a state.** Every status change goes through the state bubble (`StateBubble`), which replaces the old status picker in the banners, the backlog grid, the briefing and Moonlight rows and the executive modal. The bubble works like this:
- **The row.** It lists every status of the item's kind in a row. The main line is joined by thin arrows that shorten first when the row is crowded, and the statuses that set the item aside sit after a divider. The status the item is in carries the accent text on a slight accent backdrop.
- **Greying.** Where the client holds the dependency graph, which is most of the time, a status the item's dependencies would hold back is greyed but still offered, whatever the level. A status is held back when an unmet edge is constrained to a state the move enters, or when a directive would become Over with an open child.
- **Picking.** Picking a status sends the change through the item's reference and awaits the core's answer for that one commit, with a thin ribbon running along the top meanwhile. Nothing changes on screen until the core accepts.
- **Hold for confirmation.** The drawer folded under the row opens with the message, the items going ahead would also change, and Cancel or Go ahead. Go ahead repeats the change, forced.
- **Refusal or failure.** The drawer shows the message for two seconds.

Two pieces back it:
- **The awaited commit.** `EntityRef.awaitCommit` runs the commit in an SDK state enforcement scope (`client.withStateEnforcement`). The answer comes back to the caller with the outcome instead of reaching the client-wide hook, and no failure toast is raised. A scope covers what is sent before the run's first `await`, so it holds exactly the one SDK call it wraps.
- **The served lifecycles.** The core serves each kind's lifecycle as declared (`GET /api/system/lifecycles`, `repos.lifecycles`): its states, and each status with its state and whether it is the default. The client reads which statuses are aside, and which states a move enters, the way the core does (`enteredStates`) instead of keeping a copy of the declarations.

**SIPA, everything else.** `provideCore` installs the UI's handler for every other write unless the host installed its own, so both the Obsidian plugin and the standalone shell get it. `StateEnforcementModal` shows:
- the message
- the items going ahead would also change
- Go ahead and Cancel

A refusal is shown as a warning toast.

**Watcher.** Nobody can be asked about a note edit, so under Warn it is refused (user ruling). The note keeps its edit and the database its state. The note carries a standing `state-refused` issue (Error) whose detail says what blocks the change. The issue is not retried on a timer: an edit of the note, or the next sweep once the block is settled, applies it.

**Background and activation passes.** A refusal fails the pass's save, as any other failure does.

### User rulings
- **Warn with nobody to ask.** The change is refused and the note keeps its edit. The warning says which reasons block it, not which entities or dependencies: "…blocked by 2 dependencies".
- **The client answers Warn in this patch.** The SDK and SIPA support were built alongside the engine.
- **A state change has a bubble of its own.** It lists the item's statuses, greys the ones its dependencies hold back without disabling them, runs a loader ribbon while the change is sent, opens a built-in Warn confirmation on a 409, and shows a refusal for two seconds. Every other write keeps the modal.

### Ledger
- **Struck:** I-001, I-004, I-026, I-032.
- **Trimmed to what still holds:** I-013, I-014, I-031, I-039, I-055, I-057, I-058, I-063, I-064, I-071, I-072, I-073, I-120, I-186, I-190, I-192.
- **Added:** I-193, which this patch's watcher answer creates.

### Open edges
**Closed from Patch103.1:**
- Warn with nobody to ask (ruled)
- Over's rule (declared on Over)
- clashing compensations (main-line moves merge)
- the client that could not prompt (the SDK and SIPA answer state enforcement)

**Still open:**
- **Set-aside sources are never compensated.** Taking a deferred item up again is left to the user, so a conflict it causes is refused under Auto and Warn. The alternative is to resume it to the state the edge needs.
- **Occurrences are not judged.** The gate covers objectives, stellar directives and fates. An eventive enters its states by time, and its resolution and reschedule are not gated. The freeze stays a materialisation policy.
- **A refused note waits for its note or a sweep** (I-193), even when what blocked it clears on its own.
- **A compensation only ever takes the default status.** It never moves an item to Archived, Over or Onboard, nor sets one aside.
- **Edge sides ignore the kind's states** (I-191).
- **Checkpoint deferral goes unrecorded** (I-070).
- **On Hold's reach** (I-024).

## Patch103.3 - Onboard and the Moves on Joining
The objective status Onrush is renamed Onboard, and the engine now moves an objective of its own accord when it joins the active onrush sprint or the active Polaris cycle. Both moves used to live elsewhere: the engine promoted a waiting objective that joined any sprint, and planning set Polaris on any objective it touched, in any cycle. Now one reaction does both, on every pathway, and only where nothing blocks the move.

### Onboard
- `ObjectiveStatus.Onrush` is renamed `Onboard`. It keeps its value (2) and its Open state.
- **Database.** Objectives store their status by name, so the `OnboardStatus` migration rewrites the rows that read Onrush.
- **Notes.** Vault v7 (`ObjectiveOnboardStatusMigration`) renames the value on each note's frontmatter `status` line in place, keeping the key, any quotes and comment, and the rest of the note as written. It looks at the objectives stored as Onboard.
- **Old notes.** A note the migration never sees, such as a template or a restored copy, still reads: `[MarkdownEnumAlias("Onrush")]` reads the old name as Onboard, and the note is written with the new one next time.
- **Assign-all** follows the name: `AssignAllOnboardObjectivesToSelfAsync`, `POST /api/onrush/{id}/assign-onboard`, and `assignOnboardObjectives` in the SDK.
- The SDK enum and SIPA's status descriptor carry the new name. The glyph is still the onrush one.

### Joining
`MembershipReactions` runs last in phase 1, on the memberships the save leaves, so the judge sees the statuses it sets.

| Joins | When | Moves to |
|---|---|---|
| The active onrush sprint | The save creates an objective in it or moves one into it. Or the save begins a sprint: it creates an active one (the placeholder's begin among them) or gives a start to one that had none, which brings every member on board. | Onboard |
| The active Polaris cycle | The save creates an executive in it, moves one into it or points one at another objective. Or the save begins a cycle, which brings every objective its executives work at. | Polaris |

What moves:
- An objective that is open short of the target moves to it: Standby or Blocked, and Onboard on its way to Polaris.
- So does a finished one (Done, Archived).
- One already started (Polaris, for the sprint) or set aside (Failed) keeps its status.
- An objective the save creates moves only from an open status.
- A status the save sets itself is never overridden.
- When both moves apply in one save, the sprint's comes first and the cycle's takes the objective further.

What does not count as joining:
- joining the planning placeholder, a forecast, or a sprint or cycle that has ended
- a sprint or cycle taken up again by clearing its end, which does not begin it again, so its finished members are not reopened

### Only where nothing blocks it
A move is made only where the rules allow it as the save stands (`StateJudge.AllowsAsync`): the move must meet no rule the item does not meet already.
- A held start gate keeps an objective out of Polaris.
- A finished objective is not taken up again under a directive that is Over.

This holds at every level. The engine never compensates for its own moves nor asks about them, and the join itself always goes ahead:
- Planning a blocked objective into Polaris adds it under Block.
- Under Auto it moves none of the objective's prerequisites.

A move that unblocks another is followed, for example one objective's start meeting the next one's start gate: the blocked moves are tried again until a round moves nothing. Each move goes on the record (`state.join`), and the objective's note follows once the save commits, as a compensation's does.

### Leaving
Taking an objective out of a sprint or a cycle, or ending or deleting either, keeps every status (user ruling). Neither verb moved it back before either.

### The API
- **Planning sets no status.** `PlanExecutiveAsync` leaves the status to the engine.
  - FromObjective writes the objective through the write queue, so its note follows the planned college, Celestron value and sprint, and whatever status the engine sets.
  - Standalone and FromDirective create the objective on Standby, and the engine moves it as it joins.
- **Adding to an onrush sprint is unchanged.** It sets the sprint, and the engine does the rest.

### User rulings
- **Onrush becomes Onboard.**
- **Coming on board.** Adding to the onrush sets Onboard only on an objective that is pre-onrush or finished, and keeps any other as it is.
- **Only the active sprint.** Joining it triggers the move, so a sprint becoming active triggers it on its members.
- **Polaris.** Adding to Polaris can also, if nothing blocks it, set the objective to Polaris.
- **Leaving moves nothing.** Removing from an onrush sprint or a Polaris cycle triggers no state change.

### Calls made without a ruling
- **Polaris works like the sprint.** Only the active cycle moves objectives, and a forecast moves its objectives once it begins.
- **Which statuses Polaris takes.** Open and finished objectives move to Polaris, as for the sprint; Failed and Polaris stay. A Blocked objective counts as open: "blocked" in the ruling is read as held back by the rules, not as the Blocked status.
- **The sprint's move asks nothing either.** Onboard is an Open state, so only the Over rule can hold it back. It is skipped there as Polaris's is, not asked about or refused.
- **Created and asked statuses win.** A created objective moves only from an open status, and a status the save sets itself is kept.
- **Clearing an end is not a begin.**
- **A blocked objective waits for the user.** One left out of Polaris because it was blocked is not moved when the block clears later. Only a join moves.
- **A finished member is taken up again when its sprint begins.** An objective finished while its sprint was still being planned becomes Onboard when the sprint begins, and its settlement is revoked. That follows the ruling, but it may surprise.

### Ledger
- **Struck:** I-014.
- **Trimmed to what still holds:** I-013 (the status name), I-055, I-057, I-074.

### Open edges
Every open edge of Patch103.2 still stands.

# Inventory: state enforcement in the core
Every piece of core logic that changes state the caller did not directly ask to change (**auto-mutation**), or that refuses, skips or silently drops an operation because of a state rule (**rejection**). Taken on 2026-10-02, and brought up to date on 2026-10-04 at `b1697b9`: the PEP101 state polish and the intrinsic dependencies, rebased onto `dev/phase3a`. Line numbers refer to that commit. Rules that dev/phase3a added are marked *(dev/phase3a)*. IDs are stable: a new rule takes the next free number in its prefix, wherever its row sits.

§1–§4 cover the state rules, and §5–§12 the systems around them, from the watcher to identity and the domain model. The problems the audit found follow the inventory, under *Issues as of Development Phase 3A*.

Not listed: pure argument guards (null or blank ids), plain "was not found" lookups, and transport or DTO plumbing. The audit counted those per file; there are about 500 across the core.

**How to read the tables**
- **Kind**:
  - **M**: the core mutates on its own.
  - **R**: the core refuses, skips or drops.
  - **M+R**: both.
- **Reach**, meaning which write pathways meet the rule (§1 maps the pathways):
  - **Hook**: the state-policy save hook. It runs on every asynchronous EF save of a request context: API services, the watcher, activation passes, background saves.
  - **Hook\***: an interceptor that also hooks synchronous saves.
  - **DB**: a SQLite constraint, which every pathway meets, raw SQL included.
  - **API**: only the named API operation.
  - **Watcher**: only the file→database sync.
  - **Activation**: vault activation.
  - **Background**: a timed pass.
  - **Once**: a one-shot migration.
- **Where**: paths are under `core/`. A bare file name is unique in the core.

## 1. Where enforcement sits
### The save interceptors, in order
| # | Interceptor | Sync saves | Async saves | Role |
|---|---|---|---|---|
| 1 | `PlaintorchStatePolicyInterceptor` | — | ✔ (plus post-commit work) | The state policy, §2 |
| 2 | `BacklogScheduleInterceptor` | ✔ | ✔ | Backlog records and slots (SH-25, SH-26) |
| 3 | `PlaintorchChangeFeedInterceptor` | — | ✔ | Announces changes. Not enforcement |
| 4 | `LoreActiveCacheInterceptor` | — | ✔ | Drops the active-lore cache (SH-27) |
| 5 | `TimeframeCandidateCacheInterceptor` | ✔ | ✔ | Drops the timeframe-candidate cache (SH-28) |

Registered in `PlaintorchModule.cs:64-78`. `PlainfraContext` has no `SaveChanges` override of its own. Two connection interceptors run on every connection, bare contexts included, and neither enforces state: `SqlitePragmaConnectionInterceptor` (WAL and the busy timeout, `PlainfraStoreContextFactory.cs:40`), and `SearchFoldingConnectionInterceptor` (dev/phase3a), which registers the search-folding function (`PlainfraContext.cs:199-203`).

### The pathways
| Pathway | Hook (1, 3, 4) | Hook\* (2, 5) | API rules | Watcher rules | DB |
|---|---|---|---|---|---|
| API services | ✔ | ✔ | only the operation's own | — | ✔ |
| Watcher sync of a note (frontmatter edits, moves, deletes) | ✔ | ✔ | ✘ | ✔ | ✔ |
| Activation passes (outbox drain, consistency pass, vault migrations) | ✔ | ✔ | ✘ | partly | ✔ |
| Background and infrastructure saves (rolling materialisation, diagnostics flush, preferences, audit writes, outbox clears) | ✔ | ✔ | ✘ | — | ✔ |
| Legacy `PlaintorchRepository` upserts (sync; no callers) | ✘ | ✔ | ✘ | ✘ | ✔ |
| Bare contexts (`PlainfraStoreContextFactory`; PUCK issuance only) | ✘ | ✘ | ✘ | ✘ | ✔ |
| `ExecuteUpdate` and raw SQL (lore renumbering; EF data fixes) | ✘ | ✘ | ✘ | ✘ | ✔ |

The CLI has no domain commands (`Program.cs:77-85`: init, activate, deactivate, serve, bootstrap-service), so it is not a pathway of its own.

## 2. The save hook
### The state policy (`PlaintorchStatePolicyProcessor.ApplyAsync`)
The policy is one fixed pass in this order (`PlaintorchStatePolicyProcessor.cs:44-60`), with no fixpoint. A rule never sees what a later rule does in the same save.

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| SH-01 | Deleting an objective, fate or decree keeps its executives in ended cycles, with the reference cleared, and removes the others. | M | Hook | `PlaintorchStatePolicyProcessor.cs:294-320` |
| SH-02 | Deleting a decree keeps its reflectives in ended cycles, with the reference cleared, and removes the others. Removals feed SH-16, so a delete can grant the collection reward. | M | Hook | `PlaintorchStatePolicyProcessor.cs:322-337` |
| SH-03 | Deleting a PUCK-named entity ends its sync boundary (stages a `BoundaryEnd` row). | M | Hook | `PlaintorchStatePolicyProcessor.cs:344-353`; `VaultImplicitBoundaryService.cs:126-167` |
| SH-04 | A directive's availability reference is cleared when its timeframe is deleted, leaves `Availability`, or goes with its lunar directive. | M | Hook | `PlaintorchStatePolicyProcessor.cs:367-415` |
| SH-05 | A fate or decree created with an orbit, or whose orbit changes, gets a fresh schedule state. It is anchored at today, or at a lone `Z{…}`'s date read on the declarative's own calendar; a date that calendar lacks falls back to today. An orbit that will not build — including a `y/M/d` bound naming a date the calendar lacks — fails the whole save. | M+R | Hook | `PlaintorchStatePolicyProcessor.cs:427-466`, `498-499`; `PlaintorchOrbitService.cs:123-143` |
| SH-06 | A timeframe created with, or changing, its orbit gets a fresh state, anchored no later than the open cycle's day; a lone `Z{…}` is read on the vault default calendar. | M | Hook | `PlaintorchStatePolicyProcessor.cs:446-451`, `469-489`, `506-531` |
| SH-07 | Every fate or decree in the save gets its cached `NextOccurrence` recomputed, read on its own zone's clock. It is null unless the item has an orbit and is an Active or OptOut fate, or an Active decree. | M | Hook | `PlaintorchStatePolicyProcessor.cs:533-565`; `PlaintorchOrbitService.cs:252-261` |
| SH-08 | At most one planning placeholder sprint (`x0000`): "A planning onrush sprint already exists." | R | Hook | `PlaintorchStatePolicyProcessor.cs:610-630` |
| SH-09 | At most one active onrush sprint (started, not ended, not the placeholder): "Another onrush sprint is already active." | R | Hook | `PlaintorchStatePolicyProcessor.cs:632-658` |
| SH-10 | At most one active Polaris cycle: "Another Polaris cycle is already active." | R | Hook | `PlaintorchStatePolicyProcessor.cs:663-692` |
| SH-11 | While a cycle is active, every unstarted forecast dated on or before it is deleted. Its executives and reflectives cascade, with no graveyard entry. | M | Hook | `PlaintorchStatePolicyProcessor.cs:694-712` |
| SH-12 | An objective entering Done, Archived **or Failed** earns its Celestron value once, doubled in the active onrush. Leaving those statuses revokes it. | M | Hook | `PlaintorchStatePolicyProcessor.cs:715-773`, `:1042-1045` |
| SH-13 | An attentive carries `ResolvedOn` only while it is Done; entering Done stamps it. | M | Hook | `PlaintorchStatePolicyProcessor.cs:798-816` |
| SH-14 | An attentive turning Done grants its decree's `ActiveCelestron` once per occurrence; leaving Done revokes it. | M | Hook | `PlaintorchStatePolicyProcessor.cs:818-860` |
| SH-15 | Executing a decree-backed executive grants the decree's reward once per cycle; un-executing revokes it. Deleted executives are ignored. | M | Hook | `PlaintorchStatePolicyProcessor.cs:870-930` |
| SH-29 | Executing a decree-backed reflective books the decree's `ActiveCelestron` once per decree and cycle ("PLAINTORCH reflective execution (cycle …)"); un-executing removes it. A reflective without a decree, or a reward of 0, books nothing. Deleted reflectives are ignored, so the entry outlives them. *(dev/phase3a, PEP104; runs between SH-15 and SH-16)* | M | Hook | `PlaintorchStatePolicyProcessor.cs:938-997` |
| SH-16 | A cycle whose reflectives are all executed earns the `reflection.daily-reward` preference (default 4; was a constant 10); otherwise the reward is revoked. A reward of 0 or less grants nothing. | M | Hook | `PlaintorchStatePolicyProcessor.cs:999-1061` |
| SH-17 | A dependency added in the save hardens each occurrence it names into an `Eventive` row. The gate is bypassed and failures are swallowed. | M | Hook | `PlaintorchStatePolicyProcessor.cs:585-602`; `OccurrenceHardeningService.cs:105-144` |
| SH-18 | Deleting a stellar directive, objective or fate deletes every stored edge naming it (audit `dependency.release`). Checkpoints are not covered. | M | Hook | `PlaintorchStatePolicyProcessor.cs:73-103` |
| SH-19 | A new child, a reparent, or **any** status change of a stellar directive or objective purges two kinds of stored edge (audit `dependency.purge`; planned by LC-15):<br>• the edges the structure now contradicts<br>• the edges an intrinsic edge restates | M | Hook | `PlaintorchStatePolicyProcessor.cs:115-192` |
| SH-20 | An `Over` stellar directive that receives an open child is bumped back to `Fulfilled`. The child counts if it is created under the directive, moved in, or reopened from a finish. The bump records a reconcile intent. | M | Hook | `PlaintorchStatePolicyProcessor.cs:201-283` |
| SH-21 | Every stored edge's `Satisfied` is recomputed from its source: finished for OnFinish (the default), begun for OnBegin. An absent or failed source satisfies nothing. | M | Hook | `DependencyReconciler.cs:22-60` |
| SH-22 | A checkpoint is unlocked exactly when all of these hold, iterated with checkpoint-sourced edges to a fixpoint (at most 256 rounds):<br>• its incoming edges are satisfied<br>• its toll is absent, paid or not yet due<br>• its condition is not false | M | Hook | `DependencyReconciler.cs:62-97` |

### After the commit (async saves only)
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| SH-23 | The notes of forecasts removed by SH-11 are deleted from disk with a direct `File.Delete`. There is no outbox intent and no graveyard copy. | M | Hook | `PlaintorchStatePolicyFileSyncService.cs:20-36` |
| SH-24 | The note of a directive bumped by SH-20 is rewritten on a fresh scope. That scope's intent-clear save runs the whole policy again. | M | Hook | `PlaintorchStatePolicyFileSyncService.cs:45-55`; `VaultWriteQueue.cs:129-161` |

### Backlog records (`BacklogScheduleInterceptor`, after the policy)
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| SH-25 | A `Record` schedule stands exactly while its phase holds. Entering a phase adds one stamped with the save's local time; leaving it withdraws the record. The comparison is the stored row against the current entity, through the lifecycle resolver. | M | Hook\* | `BacklogScheduleRecorder.cs:41-96` |
| SH-26 | An owner holds one schedule per type and phase. A new one replaces the slot's old one, the later of two additions wins, and a withdrawn slot is emptied. | M | Hook\* | `BacklogScheduleRecorder.cs:101-149` |

### Caches
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| SH-27 | A save touching a lore page drops the active-lore cache after commit. | M | Hook | `LoreActiveCacheInterceptor.cs:25-69` |
| SH-28 | These saves drop the timeframe-candidate cache:<br>• a save touching a timeframe<br>• a save touching a cycle's start, end or existence<br>• a directive delete<br>• a change to the default-calendar preference | M | Hook\* | `TimeframeCandidateCacheInterceptor.cs:40-123` |

## 3. Lifecycle, gate and dependency services
These are readings and checks that other rules call (`core/Plaintorch/State`, `core/Orchestration/Lifecycle`, `DependencyRules`, `DependencyOrdering`).

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| LC-01 | Begin, finish and failed are declared on status members. A finish implies begun; `Failed` is neither begun nor finished; an unmarked status has not begun. The declarations follow this table. | — | everywhere | `EntityLifecycleResolver.cs:33-131` |
| LC-02 | An eventive has begun once its start has passed. It has finished once its end has passed, or when it is Missed, Cancelled or OptOut. A timed eventive with a zone the host knows compares its real instant; a floating or all-day one still compares its civil `Epoch.Moment` as UTC, which skews it by the host's offset (I-157). | — | everywhere | `EventiveLifecyclePhaseSource.cs:23-54`; `Epoch.cs:76` |
| LC-03 | A checkpoint is one-shot: finished, and therefore begun, exactly while it is unlocked. | — | everywhere | `CheckpointLifecyclePhaseSource.cs:17-26` |
| LC-04 | Each kind passes through the phases its members or its phase source declare (`GetPhases`). `Failed` is never one of them. | — | everywhere | `EntityLifecycleResolver.cs:72-101` |
| LC-05 | Lifecycle declarations are validated at activation. Each status-driven kind needs a finish member, and a begin member unless a source supplies one. No member may be both failed and begun or finished. | R | Activation | `EntityLifecycleResolver.cs:143-191`; `VaultBootstrapper.cs:59` |
| LC-06 | Intrinsic edges: finishing a stellar directive depends on finishing each direct child, stellar or objective. The edge is switched off when either end has failed. Edges are derived on read and never stored (`Id` 0). | M | everywhere | `IntrinsicDependencyRules.cs:29-164`; `Dependency.cs:113-128` |
| LC-07 | An endpoint counts only when a live entity backs it. A directive endpoint resolves to stellar directives only; an eventive endpoint to the exact owner and slot. | R | everywhere | `DependencyEndpoints.cs:47-149` |
| LC-08 | Default sides: a null trigger reads as OnFinish, a null constraint as ToBegin, and a checkpoint's sides are empty. The same defaults are restated in five places. | — | everywhere | `DependencyRules.cs:62-88`; copies in `DependencyReconciler.cs:58`, `DependencyGateService.cs:64-67,121-123`, `DependencyApiService.cs:175-176`, `DependencyOrdering.cs:249-262` |
| LC-09 | **Gate.** A shift into a begin status is refused while an unmet ToBegin (or null) edge targets the item. A shift into a finish status is refused while an unmet ToFinish edge does: "…is blocked by unmet dependencies and cannot begin\|finish…". The status is classified raw, so a jump from un-begun to a finish skips the begin edges (I-030, since resolved). | R | API | `DependencyGateService.cs:20-75` (called from `ObjectiveApiService.cs:146`, `DirectiveApiService.cs:285`, `DeclarativeApiService.cs:107`) |
| LC-10 | **Gate.** A stellar directive cannot shift into a finish while an intrinsic child edge is unmet: "…still has open children…". | R | API | `DependencyGateService.cs:37-41`, `:82-91` |
| LC-11 | **Freeze.** A fate with an unmet begin edge on itself, or on an occurrence's slot, does not materialise that occurrence. The rolling pass skips it, an owner-and-slot interaction is refused, and a manual eventive is refused on a whole-fate lock. An interaction that names a hardened eventive by row id skips the check. | R | Background, API | `DependencyGateService.cs:168-182` (called from `ProximityMaterializationService.cs:282`, `OccurrenceHardeningService.cs:160`, `DeclarativeApiService.cs:386`) |
| LC-12 | `GatesNextTransition` is stamped on every edge the API serves: whether this unmet edge holds the target's next transition. | M | API | `DependencyGateService.cs:99-152` |
| LC-13 | Shared readings, all served to clients:<br>• **Active onrush**: started, not ended, not the placeholder.<br>• **Active Polaris cycle**: started and not ended, *else today's cycle while it has not ended* (dev/phase3a; PO-02's "current" still takes an ended one).<br>• **Banked Celestron**: the ledger sum, truncated. | — | API | `PlaintorchStateService.cs:29-90` |
| LC-14 | **Temporal cycle rule.** An edge is impossible when the graph (stored and intrinsic edges, plus each node's begin→finish) already orders the target's constrained side before the source's triggering side. Checkpoints are dead ends. | R | API | `DependencyOrdering.cs:36-106` |
| LC-15 | **Purge planning.** Contradicted stored edges give way one per round: the preferred set first, then the most resolving, then the newest. Redundant edges go all at once. Intrinsic edges never go. | M | Hook, Once | `DependencyOrdering.cs:118-203` |

The lifecycle declarations (LC-01):

| Kind | Begin | Finish | Failed | No phase |
|---|---|---|---|---|
| Objective | Polaris | Done, Archived | Failed | Standby, Blocked, Onrush |
| Stellar directive | Active | Fulfilled, Over | Failed | Planned, Committed |
| Fate | Active, OptOut | Cancelled | — | — |
| Eventive (source) | start passed | end passed; Missed, Cancelled, OptOut | — | — |
| Checkpoint (source) | — (one-shot) | unlocked | — | — |
| Decree, lunar directive, attentive | no lifecycle | | | |

## 4. API rules
These run only for the named operation. The save hook (§2) and the database (§7) apply on top of them. Unless a row says otherwise, a frontmatter edit of the same field never meets the rule (the issues list each case under *Rules that skip pathways*).

### 4.1 Objectives (`ObjectiveApiService`)
| ID | Rule | Kind | Where |
|---|---|---|---|
| OB-01 | `POST /api/objectives` without a directive routes to the standalone create, which takes no sprint. A requested `onrushSprintId` is dropped. | R | `ObjectiveModule.cs:44-46` |
| OB-02 | Update applies only the values present:<br>• a blank title is ignored<br>• the directive is tri-state<br>• the sprint can be set but not cleared<br>• status cannot change here<br>• no existence check, so a missing id fails at the FK as a raw error | M+R | `ObjectiveApiService.cs:101-130` |
| OB-03 | A workflow shift calls the gate (LC-09) and then applies the status. There is no transition table: any status may follow any other. | R | `ObjectiveApiService.cs:146-151` |
| OB-04 | Joining a sprint promotes Standby or Blocked to Onrush. Leaving never demotes. Neither the sprint's state nor the objective's finish is checked. | M | `ObjectiveApiService.cs:171-194` |
| OB-05 | Delete stages a graveyard snapshot and a remove intent. The hook releases executives, edges and the boundary (SH-01, SH-18, SH-03). | M | `ObjectiveApiService.cs:213-217` |
| OB-06 | Beginning a sync boundary is possible only for an Implicit-mode kind (objectives, fates, decrees). It writes the note directly, outside the outbox. | M+R | `VaultEntityLifecycleService.cs:44-62` |
| OB-07 | Init adopts an existing note for an objective or directive only if all of these hold:<br>• the mode allows it<br>• the path is inside the vault<br>• the type matches<br>• the note is valid<br>• the decision is CreateFromFile<br>• the id is new<br>A PUCK-less note is adopted with a freshly minted id. | M+R | `VaultEntityLifecycleService.cs:81-152`; `VaultWatcherSyncService.cs:114-136` |

### 4.2 Directives and timeframes (`DirectiveApiService`)
| ID | Rule | Kind | Where |
|---|---|---|---|
| DI-01 | Stellar-only actions refuse a lunar id and lunar-only actions a stellar one: update, workflow, onrush join/leave, timeframe creation. There are six wordings. | R | `DirectiveApiService.cs:128-131`, `229-232`, `280-283`, `338-339`, `503-506`, `530-533` |
| DI-02 | A blank codename becomes null on update (not on create). Uniqueness is left to the DB index (DB-10), which raises a raw error. | M+R | `DirectiveApiService.cs:141`, `242` |
| DI-03 | A new parent must exist, be of the same family, and be neither the directive itself nor a descendant. An unchanged parent is not re-checked. Creation checks only that the parent exists. | R | `DirectiveApiService.cs:181-219` |
| DI-04 | A stellar workflow shift calls the gate (LC-09, LC-10). A lunar shift has no rule at all. | R | `DirectiveApiService.cs:285-289`, `508-510` |
| DI-05 | Delete is refused while subdirectives or incentives remain: "Directive cannot be deleted while it still has subdirectives or incentives." Otherwise the directive goes to the graveyard and the hook releases its references (SH-04, SH-18, SH-03). | M+R | `DirectiveApiService.cs:352-364` |
| DI-06 | An icon or banner change must supply a reference or clear it. Replacing or clearing a `media:` key archives the old file before the save. | M+R | `DirectiveApiService.cs:795-834`; `VaultMediaService.cs:115-141` |
| DI-07 | Availability may only name a timeframe whose inclusion is `Availability`. | R | `DirectiveApiService.cs:441-451` |
| DI-08 | Stellar onrush join and leave set membership only. A directive has no status to promote. | M | `DirectiveApiService.cs:300-325` |
| DI-09 | Timeframe create:<br>• the owner must be lunar<br>• the orbit must be valid at day granularity<br>• the icon is trimmed<br>• colleges are dropped unless inclusion is College<br>Start and end are not validated. | M+R | `DirectiveApiService.cs:530-550` |
| DI-10 | Timeframe update:<br>• the orbit is normalised and validated<br>• the icon is trimmed<br>• leaving College clears colleges, but supplied colleges are re-applied afterwards (create drops them) | M+R | `DirectiveApiService.cs:693-728` |
| DI-11 | Deleting a timeframe clears its references: availability through SH-04 (tracked), affinities through the DB's SET NULL (untracked, not announced). | M | `DirectiveApiService.cs:746-747` |
| DI-12 | Active timeframes:<br>• only a strictly active cycle has any<br>• a timeframe is active within [start, end) by the local clock, wrapping midnight<br>• exclusive timeframes suppress the rest | R | `DirectiveApiService.cs:583-623` |

### 4.3 Onrush sprints and executive orders (`OnrushSprintApiService`)
| ID | Rule | Kind | Where |
|---|---|---|---|
| ON-01 | Planning always creates the placeholder `x0000`, so a second plan meets SH-08. | M+R | `OnrushSprintApiService.cs:32-42` |
| ON-02 | Every sprint the API plans, begins or starts gets a milestone checkpoint, in a save of its own. With nothing gating it, the milestone unlocks at once and records its finish. | M | `OnrushSprintApiService.cs:57-75` |
| ON-03 | Begin does nothing for an active sprint. Otherwise the start becomes the given date, else the stored one, else today; SH-09 refuses a second active sprint. An ended sprint stays ended. | M+R | `OnrushSprintApiService.cs:88-94`, `144-147` |
| ON-04 | Beginning the placeholder replaces it with a minted sprint and moves its objectives, directives, checkpoints and milestone across. The member notes are not rewritten (I-074). | M | `OnrushSprintApiService.cs:96-141` |
| ON-05 | Start-new always creates a sprint started today; SH-09 refuses it while one is active. The minted id stays issued. | M | `OnrushSprintApiService.cs:154-166` |
| ON-06 | End sets the end date unconditionally. An unstarted sprint, the placeholder, or an end before the start are all accepted. | M | `OnrushSprintApiService.cs:177-178` |
| ON-07 | The generic update applies the title and tri-state dates. It can start, end or reopen a sprint, with only SH-09 applying. | M | `OnrushSprintApiService.cs:336-351` |
| ON-08 | Delete:<br>• archives the orders<br>• removes the milestone in a first save, without releasing its edges (I-039)<br>• archives the sprint in a second save<br>• frees the members, whose status stays Onrush | M | `OnrushSprintApiService.cs:184-244` |
| ON-09 | Assign-all pulls every objective whose status is Onrush into the sprint, with a plain save that rewrites no note (I-074). | M | `OnrushSprintApiService.cs:379-388` |
| ON-10 | No executive orders on the placeholder. Ended sprints still accept orders. | R | `OnrushSprintApiService.cs:409-412` |
| ON-11 | An order's effective-from may not be after its effective-until. A blank summary becomes null. | M+R | `OnrushSprintApiService.cs:465-468`, `524-530` |
| ON-12 | An order id needs the sprint's numeric PUCK part. | R | `OnrushSprintApiService.cs:516-522` |
| ON-13 | Deleting an order stages a graveyard snapshot and a remove intent. | M | `OnrushSprintApiService.cs:493-497` |

### 4.4 Polaris cycles, executives and reflectives (`PolarisCycleApiService`)
| ID | Rule | Kind | Where |
|---|---|---|---|
| PO-01 | A forecast cannot be planned for a day before its reference; a horizon of 0 (today) is allowed. A day that already has a cycle is refused before anything is minted: "… already has a Polaris cycle ('…')." A day id still registered from a deleted cycle is reused rather than minted again. | R | API | `PolarisCycleApiService.cs:33-52`, `324-332`; `PolarisCycleLifecycle.cs:19-43` |
| PO-02 | "Current" for begin, end and inclusions means the strictly active cycle, else today's cycle in any state, an ended one included. Nothing is created: "No Polaris cycle is available to begin/end." | R | API | `PolarisCycleApiService.cs:838-858` |
| PO-03 | Begin does nothing for an active cycle. Otherwise it:<br>• stamps the start<br>• drops the forecast<br>• keeps any end (a re-begun ended cycle stays ended, with start after end)<br>• materialises the cycle's reflectives by the PEP104 selection (MA-03)<br>SH-10 and SH-11 apply. | M+R | API | `PolarisCycleApiService.cs:77-92`; `PolarisCycleLifecycle.cs:44-58` |
| PO-04 | Start-new ensures an active cycle: it returns the active one, activates today's planned one, returns today's ended one untouched, or creates today's (reusing a registered day id). | M | API | `PolarisCycleApiService.cs:130-165` |
| PO-05 | Planning an executive or adding a decree against "current" auto-starts a cycle first. That happens even when the rest of the request is then refused. | M | API | `PolarisCycleApiService.cs:860-870` (called at `342`, `708`) |
| PO-06 | A cycle that has not started cannot finish. Re-ending overwrites the end; the end is never checked against the start. | R | API | `PolarisCycleApiService.cs:187-198`; `PolarisCycleLifecycle.cs:60-76` |
| PO-07 | Planning an objective executive puts the objective in Polaris (its begin), whatever its status, without the gate. A new objective is created in Polaris; an existing one is saved without rewriting its note (I-074). | M | `PolarisCycleApiService.cs:353-412`, `872-888` |
| PO-08 | One executive per incentive per cycle: "Incentive '…' is already an executive of Polaris cycle '…'." The DB index (DB-08) backs it. | R | `PolarisCycleApiService.cs:790-798` |
| PO-09 | An omitted affinity means auto (MA-04). An explicit id is used as given, any mode accepted. | M | `PolarisCycleApiService.cs:754-770` |
| PO-10 | Time allocations: a missing estimation adopts the minimum (else the maximum) and is clamped into [min, max]. Inverted or negative values are accepted, and the elapsed time is never clamped. | M | `TimeAllocations.cs:35-54` (called at `PolarisCycleApiService.cs:409`, `500`, `538`, `733`) |
| PO-11 | An executive's incentive can be reassigned but not cleared, with no existence or kind check. Its cycle can be moved, to any state. Uniqueness is kept. | M+R | `PolarisCycleApiService.cs:427-502` |
| PO-12 | Moving to the next Polaris goes to the day after the executive's cycle, planning a one-day forecast if needed (reusing a registered day id). Tracked minutes become the new envelope and tracking restarts. | M | API | `PolarisCycleApiService.cs:514-594` |
| PO-13 | Removing an executive never rolls its objective back. This is deliberate. | — | `PolarisCycleApiService.cs:600-604` |
| PO-14 | A decree executive needs an Active decree. Its estimation defaults to the decree's default length. | M+R | `PolarisCycleApiService.cs:708-736` |
| PO-15 | *Removed on dev/phase3a:* the manual reflective draw is gone; only a beginning cycle creates reflectives (MA-03). | — | — | — |
| PO-16 | A reflective's execution drives two rewards: the decree's per-run reward (SH-29) and the cycle's collection reward (SH-16). Reflectives in ended cycles stay editable. | M | API | `PolarisCycleApiService.cs:801-831` |
| PO-17 | Only a cycle that has not begun can be deleted: "…has begun; only a cycle that has not begun can be deleted." Its executives and reflectives go with it; the objectives and decrees behind them keep their state. The cycle is graveyarded and its note removed through the outbox. *(dev/phase3a)* | M+R | API | `PolarisCycleApiService.cs:278-322` |

### 4.5 Declaratives and occurrences (`DeclarativeApiService`, `OccurrenceHardeningService`)
| ID | Rule | Kind | Where |
|---|---|---|---|
| DE-01 | A fate's or decree's directive must exist; any family is accepted. The watcher nulls a dangling one instead (WA-23). | R | `DeclarativeApiService.cs:628-640` |
| DE-02 | A one-off fate window has no end without a start and no end before the start. | R | `DeclarativeApiService.cs:704-715` |
| DE-03 | A one-off fate plan folds into a `Z{…}` orbit. An explicit orbit wins. | M | `DeclarativeApiService.cs:69-71`, `660-676` |
| DE-04 | Calendar pinning. Create pins a one-off `Z{…}` fate to Gregorian and leaves any other orbit on the default. On update, a one-off keeps the fate's own calendar and is pinned to Gregorian only when it has none; a recurrence replacing a Gregorian-pinned one-off returns to the default; any calendar the user chose is kept. | M | API | `DeclarativeApiService.cs:81`, `125-132`, `528-543`, `684-688` |
| DE-05 | Orbits must validate:<br>• a fate's must parse<br>• a decree's must be granular, and day-granular when it reflects<br>• a timeframe's must be day-granular<br>Bounds parse only as ISO or `y/M/d[Th:m[:s]]` (dev/phase3a). Validation only parses, so an orbit that parses but cannot build fails later, in SH-05. | R | API | `PlaintorchOrbitService.cs:76-121`; `OrbitDays.cs:60-132` |
| DE-06 | A blank orbit unschedules the declarative. | M | `DeclarativeApiService.cs:123`, `262` |
| DE-07 | A fate status change calls the gate (LC-09). A decree status change has no rule. | R | `DeclarativeApiService.cs:105-109`, `247-250` |
| DE-08 | Resuming a paused fate or decree fast-forwards its cursor to now, read on its zone's clock, so elapsed occurrences are skipped rather than back-filled. | M | API | `DeclarativeApiService.cs:145-150`, `293-298`; `PlaintorchOrbitService.cs:184-192` |
| DE-09 | Interacting with an eventive by owner and slot hardens its occurrence; an inexact slot snaps to the day's first occurrence. Refused for a cancelled fate, a day with no occurrence, or a frozen fate (LC-11). An eventive named by row id (a manual one, or any hardened one) is loaded directly, skipping all three refusals. | M+R | API | `DeclarativeApiService.cs:411-453`, `612-627`; `OccurrenceHardeningService.cs:39-58`, `146-181`, `205-225` |
| DE-10 | Interacting with an attentive hardens its occurrence. Refused for a non-Active decree (which freezes its existing attentives too) or a day with no occurrence. | M+R | API | `OccurrenceHardeningService.cs:65-96`, `205-225` |
| DE-11 | Rescheduling an occurrence rewrites its epoch:<br>• no time makes it all-day<br>• a time makes it minute-granular<br>• an end becomes a span<br>• an end not after the start is dropped silently | M | API | `DeclarativeApiService.cs:421-435`, `469-478`; `OccurrenceDurations.cs:55-56` |
| DE-12 | Deleting a fate or decree stages a graveyard snapshot and a remove intent; SH-01, SH-02, SH-03 and SH-18 follow. The ledger rows stay. | M | `DeclarativeApiService.cs:170-174`, `316-320` |
| DE-13 | A manual eventive (`POST /api/eventives`) is a fate eventive with no RECURRENCE-ID, always Pending, OptOut fates included. It is refused for a Cancelled fate ("…takes no eventives.") and for a fate frozen by an unmet whole-fate begin edge; no slot lock can name it. *(dev/phase3a)* | M+R | API | `DeclarativeApiService.cs:369-410` |
| DE-14 | A manual epoch must have a defined granularity, a valid Orbit duration and a zone the host knows. A day-or-finer moment is cut to the start of its unit; a week, month or year moment is kept as given; the moment is stored as civil time. *(dev/phase3a)* | M+R | API | `DeclarativeApiService.cs:568-611`; `OccurrenceDurations.cs:99-102` |
| DE-15 | A fate's or decree's time zone must be one the host knows: "'…' is not a known time zone." Blank means floating. A frontmatter `timezone:` is stored unchecked, and an unknown id silently reads as floating. *(dev/phase3a)* | R | API | `DeclarativeApiService.cs:511-527` (used at `142`, `287`, `582`); `OccurrenceZones.cs:17-23` |
| DE-16 | Every occurrence an orbit generates — rolling pass, interaction and dependency hardening, projection — is stamped with its declarative's zone, and seeking, the fast-forward and `NextOccurrence` read "now" on that zone's clock. A hardened occurrence keeps its stamp; a zone change neither restamps occurrences nor resets the cursor. *(dev/phase3a)* | M | Background, API, Hook | `PlaintorchOrbitService.cs:60-75`, `166-192`, `252-261`; `ProximityMaterializationService.cs:265`, `298`; `OccurrenceHardeningService.cs:91`, `174` |
| DE-17 | When an update changes a declarative's resolved calendar and its orbit is a lone `Z{…}`, the literal is rewritten into the new calendar so it keeps its day, and refused when that calendar cannot write it. A frontmatter `calendar:` edit leaves the literal and the cursor, so the one-off then names another day. *(dev/phase3a)* | M+R | API | `DeclarativeApiService.cs:544-557` (called at `137`, `282`); `OrbitDays.cs:174-198` |

### 4.6 Dependencies and checkpoints (`DependencyApiService`, `DependencyRules`)
Edges and checkpoints have no notes, so this API is their only writer.

| ID | Rule | Kind | Where |
|---|---|---|---|
| DP-01 | A directive endpoint must be a stellar directive. | R | `DependencyApiService.cs:371-377` |
| DP-02 | An eventive endpoint needs a slot and a fate or objective owner. `DependencyRules`' own comment says a slot may be omitted. The slot is never checked against the orbit. | R | `DependencyApiService.cs:387-400` |
| DP-03 | No self-link: same kind and id, case-insensitive, whatever the slots. | R | `DependencyRules.cs:35-39` |
| DP-04 | No duplicate, including an edge an intrinsic edge already implies. | R | `DependencyRules.cs:90-115` |
| DP-05 | A checkpoint side carries no trigger or constraint. | R | `DependencyRules.cs:43-51` |
| DP-06 | Only eventive endpoints carry a slot. | R | `DependencyRules.cs:117-130` |
| DP-07 | No logically impossible edge (LC-14). The refusal names the contradicting path. | R | `DependencyApiService.cs:42-47` |
| DP-08 | A checkpoint toll cannot be negative. The check is written twice. | R | `DependencyApiService.cs:184-187`, `234-237` |
| DP-09 | Clearing a toll resets `TollPaid`, with no refund. Changing a paid toll's amount keeps it paid. | M | `DependencyApiService.cs:241-246` |
| DP-10 | The condition switch refuses a checkpoint with no external condition. The generic update sets the condition freely. | R | `DependencyApiService.cs:350-353` |
| DP-11 | A sprint's milestone cannot be deleted on its own. | R | `DependencyApiService.cs:278-283` |
| DP-12 | Deleting a checkpoint deletes its edges. This is not in the save hook, and no graveyard entry or refund is made. | M | `DependencyApiService.cs:285-292` |
| DP-13 | Paying a toll needs a toll; it is a no-op once paid. It needs a banked balance ≥ the toll, and it debits the ledger before marking the toll paid. The balance check and the debit are separate statements. | M+R | `DependencyApiService.cs:310-333` |

### 4.7 Backlog schedules (`BacklogScheduleApiService`)
| ID | Rule | Kind | Where |
|---|---|---|---|
| BS-01 | A schedule places a begin or a finish; `Failed` is refused. | R | `BacklogScheduleApiService.cs:47-52` |
| BS-02 | Records are the core's: adding or deleting one by hand is refused. | R | `BacklogScheduleApiService.cs:54-57`, `86-89` |
| BS-03 | A plan names a phase the owner's kind passes through: a checkpoint has no begin, and a kind with no lifecycle takes either. | R | `BacklogScheduleApiService.cs:59-66` |
| BS-04 | The owner id must name exactly one incentive, directive or checkpoint. | R | `BacklogScheduleApiService.cs:106-120` |
| BS-05 | A plan's time is stored as local civil time (a UTC time is converted). | M | `BacklogScheduleApiService.cs:128-133` |
| BS-06 | Only incentives, directives and checkpoints own schedules. The recorder applies this too. | R | `BacklogSchedule.cs:99-126` |

### 4.8 Lore (`LorePageApiService`)
| ID | Rule | Kind | Where |
|---|---|---|---|
| LO-01 | A new page's level follows its parent, and a Phase cannot have children. | M+R | `LorePageApiService.cs:75`, `326-341` |
| LO-02 | A new page gets the next index in its scope, a parent-composed id, the parent's indices and a RelativePath. | M | `LorePageApiService.cs:76-98`, `343-425` |
| LO-03 | D17: a page's beginning must fall within its parent's span. It is checked on create and update only, never when a parent or sibling moves. | R | `LorePageApiService.cs:303-324` |
| LO-04 | Renaming a page moves its folder. Descendants' stored paths are not rebased. | M | `LorePageApiService.cs:135-147` |
| LO-05 | A page with children cannot be deleted. | R | `LorePageApiService.cs:163-167` |
| LO-06 | A renumber index must be ≥ 0 with no sibling collision, and the level must have a discriminator. The collision scope (per parent) differs from the numbering scope (global for Era and Chapter). | R | `LorePageApiService.cs:196-222` |
| LO-07 | Renumbering re-keys the subtree with `ExecuteUpdate` and deferred FKs. That is outside every interceptor and the outbox, and the notes are written directly. | M | `LorePageApiService.cs:213-291` |
| LO-08 | Every lore response is stamped with `IsActive` from the cached spine (DM-14). | M | `LoreActiveEndpointFilter.cs:22-52` |

### 4.9 Media, preferences and system
| ID | Rule | Kind | Where |
|---|---|---|---|
| SY-01 | An upload must have a safe single file name and an image extension, and be non-empty and ≤ 8 MiB. A same-named file is overwritten without an archive. | M+R | `VaultMediaService.cs:29-43`, `73-93`, `292-310` |
| SY-02 | Removing or replacing a self media asset archives it. Shared `vault:` files are never reclaimed. | M | `VaultMediaService.cs:115-141` |
| SY-03 | Each `[Media]` property needs a `…Media` companion. This is checked lazily on first use, not at activation. | R | `VaultMediaService.cs:264-290` |
| SY-04 | A preference write must name a catalog key and carry a value of its kind (→ 400). A stored value that cannot be read falls back to the default. There are no range rules. | R | `PreferenceApiService.cs:36-39`; `PreferenceDescriptor.cs:51-58`; `UserPreferenceSerializer.cs:29-45` |
| SY-05 | A preference write updates the database and memory. A default-calendar change drops the timeframe cache, re-reading every unpinned orbit with no cursor reset. | M | `UserPreferenceService.cs:26-103` |
| SY-06 | A fatal watcher issue cannot be dismissed. An Instance dismissal needs the status active and lapses when its fingerprint changes. | R | `SystemApiService.cs:266-270`; `OperationStatusRegistry.cs:139-252` |
| SY-07 | An unknown dismissal scope is treated as Instance. | M | `SystemApiService.cs:294-304` |
| SY-08 | Client paths must stay inside the vault root. There are three hand-written copies. | R | `SystemApiService.cs:83-89`, `335-347`; `VaultEntityLifecycleService.cs:91-99` |

### 4.10 The endpoint layer
| ID | Rule | Kind | Where |
|---|---|---|---|
| EP-01 | No exception mapping exists. Every refusal in this inventory (`InvalidOperationException`, `ArgumentException`, a raw `DbUpdateException`) reaches the client as HTTP 500, bodiless in Production. Only three cases map to 400:<br>• preference validation<br>• a blank lore PUCK<br>• an unknown directive kind<br>Not-found is 404 on reads and 500 on writes. | R | `PlaintorchHostFactory.cs:70-72`; `PreferenceModule.cs:36-44`; `LorePageModule.cs:46-82`; `DirectiveModule.cs:37-40` |

## 5. Watcher (file → database)
A frontmatter edit passes the intake filter and the 3 s write barrier. It is then classified by its path (territory, identity, family), checked for syntax and given its path authorities, and the storage mode picks an action. An update copies every mapped scalar of a freshly hydrated model over the row (`SetValues`), restores the guarded database-only fields, nulls dangling references and drops the orbit cursor on an orbit change, then saves once through the full save hook (§2). The note is rewritten only if it was invalid or had no frontmatter, and no §4 rule runs on this path. (DE-01's placeholder pointer WA-08 should read WA-23.)

| Kind | Mode | Fields a note edit writes | Guarded (database-only) | Applied with no rule that the API would enforce |
|---|---|---|---|---|
| Stellar directive | Freeform: any folder (canonically `Directives/`) | `codename`, `tags`, `icon`, `banner`, `status`, `due`, `onrush` (nulled if the sprint is unknown); title from the file name; parent from the nearest self-named folder that asserts a known directive | `AvailabilityTimeframeId` | `status:` skips the gate (LC-09, LC-10)<br>• a folder move skips DI-03 (no family, self or descendant check), so a stellar folder under a lunar one is accepted<br>• an unknown `onrush:` is nulled, not refused<br>• `icon:`/`banner:` replace media without archiving (DI-06) |
| Lunar directive | Freeform, chosen by `LUNA…` notation (canonically `Moonlight/`) | `codename`, `tags`, `icon`, `banner`, `status` (lunar; On Hold pauses reflection, MA-03a); title; parent as above | `AvailabilityTimeframeId` | a folder move skips DI-03<br>• `icon:`/`banner:` skip DI-06 |
| Objective | Implicit | `status`, `college`, `starfire`, `onrush` (nulled if unknown), `due` (owned, copied by hand); title; directive from the containing directive folder (none under `Objectives/`, nulled if unknown) | — | `status:` skips the gate (LC-09)<br>• an `onrush:` join skips the Standby/Blocked → Onrush promotion (OB-04)<br>• a missing sprint or directive is nulled, not refused |
| Fate | Implicit | `status`, `orbit`, `calendar`, `timezone`; title; directive from the folder (nulled if unknown) | — (`NextOccurrence` is wiped, then recomputed by SH-07) | `status:` skips the gate (DE-07) and the resume fast-forward (DE-08), so the paused span is back-filled<br>• `orbit:` is neither validated (DE-05) nor pinned to Gregorian (DE-04); an unbuildable one fails every save<br>• `calendar:` leaves a one-off literal naming another day (DE-17)<br>• `timezone:` is not validated (DE-15); an unknown zone floats<br>• a missing directive is nulled (DE-01) |
| Decree | Implicit | `status`, `orbit`, `defaultLength`, `activeCelestron`, `reflect`, `college`, `calendar`, `timezone`; title; directive | — (`NextOccurrence` as for fates) | `orbit:`/`reflect:` skip DE-05, so a reflecting decree may carry a sub-day orbit<br>• a `status:` resume back-fills (DE-08)<br>• `calendar:` (DE-17), `timezone:` (DE-15) and a missing directive (DE-01) as for fates |
| Onrush sprint | Enforced (`Onrush/`) | `startDate`, `endDate`; title | `MilestoneCheckpointId`, `GraphLayout` | `startDate:` on `x0000` starts the placeholder in place, with no re-identity (ON-04) |
| Polaris cycle | Enforced (`Journal/`) | `startTime`, `endTime`; title. A `forecast:` edit is never applied (owned type, WA-22) | — | `startTime:` gets no lifecycle start or forecast drop, no reflectives (MA-03) and no candidate warm (PO-03)<br>• `endTime:` with no start is accepted (PO-06) |
| Executive order | Synced (a sprint's `ExecutiveOrders/`) | `summary`, `effectiveFrom`, `effectiveUntil`; title; sprint from the folder (it must exist, else the note is reverted or held in conflict) | — | the window order (ON-11)<br>• a note in `x0000`'s folder becomes an order on the placeholder (ON-10)<br>• a note filed or moved under another sprint keeps an id numbered for its old one (ON-12) |
| Lore page | Synced (`Saga/`) | `overrideIdentifier`, `beginning`; id, parent (nulled if unknown), level, `RelativePath`, era, chapter, act, phase and `IndexedUtc` from the path; title | — | `beginning:` skips D17, by design (LO-03) |

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| WA-01 | Reconciliation runs only while the coordinator has marked a vault session ready, and a session generation is never re-entered. An exception that escapes every inner guard raises the fatal `watcher.process`/`fatal` ("The watcher stopped after an unexpected failure…") and parks the watcher on Standby until the next activation. | R | Watcher | `VaultWatcherService.cs:90-136`; `ActiveVaultSession.cs:71-125`; `WatcherStatusReporter.cs:86-87` |
| WA-02 | Each session starts and ends with an empty in-memory issue set. It loads the vault's durable dismissals before the startup sweep re-raises issues; a failed load is only logged. | M | Watcher | `VaultWatcherService.cs:144-154`, `201-207`, `517-530`; `OperationStatusRegistry.cs:129-136`, `158-178`; `OperationStatusDismissalService.cs:16-20` |
| WA-03 | While the vault root or an existing entity root cannot be enumerated, nothing is reconciled: the fatal `watcher.vault-access`/`vault-inaccessible` stands, health reads Standby and the observers are dropped. The watcher re-probes every 5 s (every 2 s mid-span) and resumes with a full sweep. | R | Watcher | `VaultWatcherService.cs:47`, `215-252`, `307-316`, `358-364`, `442-449`, `471-476`; `WatcherStatusReporter.cs:96-108`; `VaultWatcherPathPolicy.cs:48-97` |
| WA-04 | Every live span starts with a whole-vault reconcile through the live reconciler, in priority order (update and rewrite, create, purge, conflict, ignore), then the vanished-note deletes (WA-09), plus one `discovery/startup-scan` audit row. A non-structural sweep failure is the fatal `scan-failed`, and the span is retried on a 5 s → 60 s backoff. A file the sweep cannot inspect is skipped with a log line and no issue. | M+R | Watcher | `VaultWatcherService.cs:259-276`, `296-324`, `538-547`; `VaultWatcherReconciler.cs:35-71`, `304-316`; `VaultMarkdownDiscoveryService.cs:48-106` |
| WA-05 | An observer overflow raises `root-error` (Critical) and re-sweeps the vault at the next 2 s maintenance step. Any other observer error restarts the span with a sweep and fresh observers: at once after a span of 5 min or more, else on the backoff. Roots that cannot be resolved are the fatal `roots-unresolved`. | M | Watcher | `VaultWatcherService.cs:55`, `182-192`, `372-381`, `421-464`, `554-638`; `WatcherStatusReporter.cs:32-61` |
| WA-06 | Only events inside the vault, outside `_`/`.` segments and the attachment folder, and for a markdown file, a folder or a gone path are queued; a folder maps to its self-named primary note. Each path is debounced 500 ms and drained every 250 ms. Every other event is dropped silently. | R | Watcher | `VaultWatcherService.cs:38-39`, `646-693`, `699-776`, `918-928`; `VaultPathSyncModelCatalog.cs:188-230`; `VaultWatcherPathPolicy.cs:102-130` |
| WA-07 | For 3 s after the core writes, moves, archives or purges a path, events on it and on its parent folder are discarded, not deferred. This applies on intake, on the relocation fast path and on retry re-queue. The barrier is keyed on the path, not the content. | R | Watcher | `VaultWatcherWriteBarrier.cs:11-59`; `VaultWatcherService.cs:398`, `488`, `672-683`, `856-859`; `PlaintorchMarkdownStorageService.cs:311-317`, `493`, `722-728`, `898`; `VaultWatcherSyncService.cs:625`; `PlaintorchStatePolicyFileSyncService.cs:28`; `VaultMediaService.cs:90`, `139` |
| WA-08 | A rename whose new path keeps the same identity is synced once at the new location instead of as a delete plus a create. This is usually update-from-file, so it reparents or retitles, and the old path's reconcile flags resolve. The old id is a loose filename parse, or the new id for a directive; on an exception both paths fall back to plain inspection. | M | Watcher | `VaultWatcherService.cs:711-740`, `799-911`; `FreeformVaultStorageModePolicyService.cs:138-148`; `WatcherStatusReporter.cs:63-80` |
| WA-09 | An identity-driven entity that is expected to have a note (every Freeform one; an Implicit one whose boundary stands) is deleted, dependents first, when no note anywhere in the vault asserts its PUCK and no write intent is pending. The pass runs after every drained batch holding a note, folder or gone path, last in every sweep, and when an identity-keyed `sync-failed` is due. If any folder or note is unreadable, it judges nothing. | M+R | Watcher | `VaultWatcherService.cs:367-370`, `707-709`, `755`, `772-797`; `VaultWatcherReconciler.cs:61-70`, `118-186`; `VaultMarkdownDiscoveryService.cs:121-349`; `WatcherStatusReporter.cs:202-282` |
| WA-10 | The live issue set is the retry queue. Operational per-path issues (`discovery-failed`, `permission-denied`, `file-in-use`, `sync-failed`, unknown codes) are re-inspected forever, on a 2 s → 60 s backoff per status. Content issues and `delete-blocked` are never retried on a timer; a content issue whose file vanished gets one re-inspection. | M+R | Watcher | `VaultWatcherService.cs:43`, `366-370`, `483-515`; `WatcherRetryScheduler.cs:23-108` |
| WA-11 | Sweep and live events share one path (inspect report → execute → outcome report), so both raise the same issues. A throwing inspection applies nothing and is classified `markdown-invalid` (Error), `file-in-use` (Warning) or `permission-denied`/`discovery-failed` (Critical). A path that is not a candidate passes all nine reconcile reasons. | R | Watcher | `VaultWatcherReconciler.cs:78-109`, `193-260`; `WatcherStatusReporter.cs:136-146`, `345-356`, `388-389`, `416-429` |
| WA-12 | When more than one file asserts the same identity-driven PUCK, `watcher.identity`/`duplicate-identity` (Error, "Asserted by … files: …") is raised; it resolves once a single file is left. Nothing is refused: every duplicate is still synced, so the last one reconciled sets the row. A loose filename-prefix match counts as an assertion. | R | Watcher | `VaultWatcherReconciler.cs:53`, `198-201`, `268-302`; `VaultMarkdownDiscoveryService.cs:357-413`; `WatcherStatusReporter.cs:117-128` |
| WA-13 | A candidate's typed concern becomes exactly one issue (`markdown-invalid`, `puck-violation`, `policy-violation`, `foreign-file`); raw validation issues raise `markdown-invalid` only when no concern is set.<br>• A content problem the watcher enforced (rewrite, purge) is a Warning; one left in the file is an Error.<br>• A successful sync clears the operational reasons, and clears the content reasons only if it enforced them; `foreign-file` stands.<br>• An unknown reason code is Critical. | R | Watcher | `WatcherStatusReporter.cs:152-200`, `295-300`, `358-385`; `WatcherOperations.cs:66-106`; `VaultSyncConcern.cs:11-46` |
| WA-14 | A sync that throws is `sync-failed` (Critical, retried). A refused delete (`VaultEntityDeleteBlockedException`) is a standing `delete-blocked` Error ("…cannot be removed while other records still reference it…") that retries leave alone; it is fingerprinted by the entity and its blocking keys. The scope is the path, or the identity for a vanished note. A state-policy refusal is not told apart from a technical failure. | R | Watcher | `WatcherStatusReporter.cs:227-254`, `309-339`; `VaultWatcherReconciler.cs:174-185`, `242-259`; `VaultEntityDeleteBlockedException.cs:22-32` |
| WA-15 | The path, never the frontmatter, decides a note's model:<br>• incentives in their standalone root or under a hosting directive (identity-driven kinds anywhere in it; partition folders tell sibling kinds apart)<br>• orders in a sprint's `ExecutiveOrders/`; sprints and lore pages as self-named primaries; cycles under `Journal/`<br>• directives anywhere, the most specific root winning<br>Identity-driven modes also need a frontmatter `puck:` that resolves to no other type. A note outside every territory is ignored, apart from a `watcher/ignored-path` audit row per live event. | R | Watcher | `VaultPathSyncModelCatalog.cs:57-87`, `157-249`, `310-454`; `VaultMarkdownDiscoveryService.cs:429-443`, `552-590`; `FreeformVaultStorageModePolicyService.cs:39-95`; `ImplicitVaultStorageModePolicyService.cs:78-110` |
| WA-16 | Only Index storage reads an identity from the file name. A flat one that does not tokenize against the declared PUCK is discarded, leaving the note identity-less. A family's concrete type is the member whose PUCK mints the id (`LUNA…` → lunar), falling back to stellar when none or several do. A freeform directive note asserting an existing non-directive id is not a candidate. | M+R | Watcher | `VaultMarkdownDiscoveryService.cs:592-631`, `675-682`, `820-834`; `VaultFamilyInstantiationResolver.cs:34-84`; `VaultStoragePathComposer.cs:153-168` |
| WA-17 | A note is valid only if:<br>• every mapped key parses (enum, date, integer, related-PUCK format)<br>• a known identity-driven entity carries every non-nullable key<br>• a caller-input PUCK is present<br>• an order's sprint resolves<br>• a freeform directive sits in a folder it may own<br>An invalid note is never applied field by field: a known entity's frontmatter is reverted whole (WA-26). The check is syntactic only. A new or path-bound entity hydrates with defaults, so a removed nullable key clears its field. | R | Watcher | `VaultMarkdownDiscoveryService.cs:633-641`, `663-673`, `687-692`, `727-752`, `778-797`; `VaultWatcherPathPolicy.cs:143-194` |
| WA-18 | Location is authoritative:<br>• a directive's parent is the nearest self-named ancestor whose primary note asserts a known directive PUCK of either family<br>• a hosted incentive's directive is its containing directive, and an incentive in its standalone root has none<br>• a lore page's `RelativePath` is its real path<br>• every kind takes its title from the file name<br>Moving a note or folder reparents or retitles the entity on the next update. | M | Watcher | `VaultMarkdownDiscoveryService.cs:642-661`, `684`, `842-935`; `VaultStoragePathComposer.cs:181-201` |
| WA-19 | Every sync runs under `PlaintorchChangeOrigin.Critical()`, so clients apply its changes over edits in flight. A candidate that throws clears the shared change tracker, so the next candidate's save flushes nothing it staged. | M | Watcher | `VaultWatcherSyncService.cs:40-61`, `119` |
| WA-20 | An unknown or title-only Synced note (an order or a lore page) becomes an entity once its dangling references are nulled.<br>• An auto-generated PUCK is always minted by the core, ignoring the file's id; a caller-input PUCK keeps the path id.<br>• An Implicit boundary begins.<br>• The note is rewritten canonically if it had no id, was invalid or lacked frontmatter.<br>Identity-driven modes never create an entity from a file on their own. | M | Watcher, API | `VaultWatcherSyncService.cs:144-243`, `253-264` |
| WA-21 | For a known, valid note the file is the authority. Every mapped scalar of a freshly hydrated model overwrites the tracked row through `SetValues`: frontmatter values, path-derived values, and CLR defaults for the rest. The file-name title wins, and one save runs through the full save hook (`sync/update-from-file`). The note is not rewritten. | M | Watcher | `VaultWatcherSyncService.cs:251-399` |
| WA-22 | Database-only fields survive a note sync only if restored by hand after `SetValues`: `OnrushSprint.MilestoneCheckpointId`, `OnrushSprint.GraphLayout` and `Directive.AvailabilityTimeframeId`. An objective's owned `due` is copied by hand. Any other owned value (a cycle's `forecast:`) is never applied. Any other database-only scalar resets to its default; `NextOccurrence`, for example, is then recomputed by SH-07. | M | Watcher | `VaultWatcherSyncService.cs:279-303`, `501-505` |
| WA-23 | A reference to a missing entity is silently dropped from the row with only a log warning; the note keeps naming it and no issue is raised. This covers an objective's directive and sprint, a stellar `onrush:`, a fate's or decree's directive and a lore page's parent. The API refuses the same input (DE-01). | M | Watcher | `VaultWatcherSyncService.cs:147-169`, `270-285`, `305-312`, `411-494` |
| WA-24 | A frontmatter `orbit:` change removes the fate's or decree's schedule state in the sync's own unit of work. SH-05 re-creates it in the same save, so this step is redundant. A `calendar:` or `timezone:` change resets nothing. | M | Watcher | `VaultWatcherSyncService.cs:310-314`, `507-528` |
| WA-25 | A sync that modified no scalar or owned value leaves the row alone (`sync/update-skipped-noop`). A note without frontmatter, or a missing file, is still rewritten with the canonical scaffold (`sync/update-rewrite-scaffold`). | M+R | Watcher | `VaultWatcherSyncService.cs:321-366`, `370-374`, `401-409`, `550-565` |
| WA-26 | When the file cannot be trusted, the note is regenerated from the row, keeping its body, possibly moving it and reverting the edit (`sync/rewrite-from-database`). This covers invalid frontmatter for a known entity in any mode, and an Enforced note seen deleted live while its entity exists. The rewrite is skipped (`rewrite-skipped`) when the entity is gone. | M | Watcher | `VaultWatcherSyncService.cs:573-615`, `766-797` |
| WA-27 | In Enforced territory (`Journal/`, `Onrush/`), a note that maps to no live entity is archived to the file graveyard behind the write barrier (`sync/purge-file`). That is an unknown id, a title-only name other than an `Untitled` placeholder, or missing caller input. | M | Watcher | `VaultWatcherSyncService.cs:623-654` |
| WA-28 | A note's disappearance deletes its entity for Freeform, boundary-begun Implicit and Synced kinds. The delete commits with its database-graveyard entry, and the hook releases work, boundary and edges. A delete that a restricting foreign key would refuse is refused up front, writing only a `sync/delete-blocked` audit row. The check is a raw `COUNT` per restricting key, skipping the executive and reflective keys that SH-01 and SH-02 release. | M+R | Watcher | `VaultWatcherSyncService.cs:662-745`; `VaultEntityGateway.cs:71-134` |
| WA-29 | Ignore and Conflict write nothing but a `sync/watcher-ignore` or `sync/watcher-conflict` audit row, on every event; the content issue stands. Ignore covers no identity, an unrecognised assertion, an unbegun boundary and an `Untitled` placeholder. Conflict covers an invalid or unknown Synced note and missing caller input. | R | Watcher | `VaultWatcherSyncService.cs:82-107` |
| WA-30 | An API init forces the type's model and relaxes the ignore and belonging policies, then reuses create-from-file (WA-20). Its refusals are OB-07. | M+R | API | `VaultWatcherSyncService.cs:114-136`; `VaultMarkdownDiscoveryService.cs:475-535` |
| WA-31 | An Implicit entity stays database-only until its note first exists: the watcher sees it, or a write begins the boundary. This records a `boundary/BoundaryBegin` audit row. Until then canonical writes are skipped and a missing note is ignored; afterwards a missing note deletes the entity (WA-09). The begin is coded in both the sync service and the storage service. | M+R | Watcher, API | `VaultWatcherSyncService.cs:193`, `266`, `533-548`; `PlaintorchMarkdownStorageService.cs:269-276`, `322-332`, `339-354`; `VaultMarkdownDiscoveryService.cs:131-140`, `694-700`; `VaultImplicitBoundaryService.cs:44-118` |
| WA-32 | Each report is diffed against the live set: a failing check raises or re-grades, a passing one resolves, and an absent reason is left alone. Transitions go to the durable sink (MA-10) and to a ring of the last 200 resolutions. Health is the worst non-dismissed severity (Fatal → Standby, Critical → Critical, Error or Warning → Issues) unless a lifecycle override is set. | M | Watcher | `OperationStatusRegistry.cs:15`, `28-53`, `100-122`, `258-321`; `OperationStatusReporter.cs:14-21` |
| WA-33 | A fatal status is never hidden. An Instance dismissal matches while the operation, scope, reason and fingerprint match, so it also holds across a resolve and re-raise. File and Reason dismissals stand until restored. Dismissed statuses are left out of the health rollup, and dismissal rows are never cleaned up. The API refusals are SY-06 and SY-07. | R | API, Watcher | `OperationStatusRegistry.cs:139-143`, `187-252`; `OperationStatusDismissal.cs:55-91`; `OperationStatusDismissalService.cs:26-76` |

## 6. Storage modes, paths and markdown
How a note maps to an entity. A mode's `Decide` governs only file→database. Database→file is governed by write placement, the capability flags and the outbox (§8), and API writes never consult `Decide`. The watcher pipeline that acts on the decisions is in §5, and the activation consistency pass is in §10.

| Mode | Entities | Behaviour | File→DB | DB→file |
|---|---|---|---|---|
| Enforced | OnrushSprint, PolarisCycle | Path-bound. The core owns these notes (every note under Journal, every self-named sprint note under Onrush), and each must be a live entity in canonical shape. | A valid known file updates the row. Title-only and unknown files are purged to the graveyard. An invalid or deleted known file is rewritten. No file ever deletes a row. | A note is written on create, always at the canonical path. |
| Synced | LorePage, ExecutiveOrder | Path-bound and two-way: lore pages' self-named folders under Saga, and orders in a sprint's ExecutiveOrders partition. | Title-only and unknown files create entities. An invalid unknown file is a Conflict. A deleted known file deletes the row. An invalid known file is rewritten. | A note is written on create, at the canonical path (a lore page at its RelativePath). |
| Freeform | StellarDirective, LunarDirective | Identity-driven: the frontmatter `puck` decides, and a note may sit anywhere in the vault. | A note needs a `puck`. An unknown one is left in place as a foreign file. A vanished note deletes the row. An invalid known note is rewritten. | A note is written on create. Later writes keep the authored folder and refresh the name, fall back to a numbered canonical slot under another type's managed root, and go canonical on a reparent. |
| Implicit | Objective, Fate, Decree | Identity-driven, but detected only under the kind's roots and a containing directive. Database-first until a boundary begins. | As Freeform, except that a missing note deletes the row only once the boundary stands. | No note until the boundary begins. After that the note keeps its folder, refreshes its name, and goes canonical on a reparent. |
| FileFirst | none (registered, unused) | Path-bound. Files originate entities. | Title-only and unknown files create entities. Any invalid file is a Conflict. A deleted known file deletes the row. | A note is written on create, at the canonical path. |
| Optional | none (registered, unused) | Path-bound. Documented as "removals will not be synced by either side". | Files never create entities. An invalid known file is rewritten. A deleted known file deletes the row, against the documentation. | A note is written on create, at the canonical path. |

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| SM-01 | A path belongs to at most one model. Candidates are tried longest scan root first, and the first whose mode policy accepts the path wins; any other path is not inspected.<br>• Each mode resolves to one policy, and the last one registered wins. An unregistered mode throws "No storage-mode policy service is registered for mode '…'".<br>• A decision takes `RequiresCallerInput` from PUCK creation. `BoundaryBegun` is true outside Implicit. | R | Watcher, API | `VaultStoragePolicyEngine.cs:21-45`, `50-98`; `VaultStorageModePolicyRouter.cs:6-23`; `VaultSyncDecisionService.cs:24-41`; `VaultStorageModeDecisionContext.cs:13-21`; `PlaintorchModule.cs:150-157` |
| SM-02 | Path-bound modes own only files under their scan roots that fit the model's shape. A folder event stands for `<dir>/<dir>.md` only for a self-named model. A title-only file named "Untitled" or "Untitled 2" (and so on) is ignored: it is neither adopted nor purged. Path-bound writes resolve to the canonical path, so a moved note returns there on its next write that carries `previous` (SM-25). | M+R | Watcher, API, Activation, Once | `PathBoundVaultStorageModePolicyService.cs:30-81`, `92-118` |
| SM-03 | Enforced:<br>• a title-only file is purged: "…enforced storage disallows unresolved files." (a PUCK violation when caller input is missing)<br>• an unknown id is purged: "Unknown file is disallowed by enforced storage policy."<br>• an invalid known file, or a deleted one, is rewritten from the row; a valid known file updates it<br>No file ever deletes a row. Purges go to the file graveyard (PS-03). | M+R | Watcher | `EnforcedVaultStorageModePolicyService.cs:16`, `19-73` |
| SM-04 | FileFirst is registered but declared by no entity. A title-only or unknown file creates an entity, missing caller input or any invalid file is a Conflict, and a deleted known file deletes the row. | M+R | Watcher | `FileFirstVaultStorageModePolicyService.cs:15-62`; `PlaintorchModule.cs:155` |
| SM-05 | Optional is registered but declared by no entity. Files never create entities, an invalid known file is rewritten, and a deleted known file deletes the row, although the mode is documented as "removals will not be synced by either side". | M+R | Watcher | `OptionalVaultStorageModePolicyService.cs:15-69`; `VaultStorageAttribute.cs:16-22`; `PlaintorchModule.cs:153` |
| SM-06 | Synced: a title-only or unknown file creates an entity (a Conflict when the PUCK needs caller input). An invalid unknown file is a Conflict, an invalid known file is rewritten, and a deleted known file deletes the row (a lore page with children is refused, DB-09). Synced is the `[VaultStorage]` default mode. | M+R | Watcher | `SyncedVaultStorageModePolicyService.cs:15-67`; `VaultStorageAttribute.cs:119` |
| SM-07 | Freeform: any `.md` in the vault is a candidate. It belongs only if its `puck` is unknown or resolves to a member of the model's family; a missing file always belongs, and a note without a `puck` does not (only init adopts one).<br>• unknown `puck`: left in place as a foreign file<br>• a vanished known note: DeleteFromDatabase (DB-17 may block it)<br>• invalid known: rewrite; valid known: update<br>A relocated directive with no old id falls back to its new path's id. | M+R | Watcher, API | `FreeformVaultStorageModePolicyService.cs:39-56`, `59-95`, `98-135`, `138-148` |
| SM-08 | A freeform write keeps the authored location and renames the file (and its self-named folder) from the current identity. An authored location under the Metadata, Objectives, Fates, Decrees, Onrush, Journal or Saga root falls back to the canonical path, suffixed " (n)" until free; for a directive, so does a location directly beneath one of those roots. Partition folders and entity roots are not checked here. | M | API, Watcher, Activation, Once | `FreeformVaultStorageModePolicyService.cs:151-174`, `183-206`, `208-252`, `254-273` |
| SM-09 | Implicit: a note is detected only in the kind's standalone root, or under a containing directive outside a sibling kind's partition. It belongs only if its `puck` is unknown or resolves to exactly the model's type; a note without a `puck` does not belong (only init adopts one).<br>• unknown `puck`: left as a foreign file<br>• a missing known note deletes the row only once the boundary stands, else "Implicit entity has no begun synchronization boundary, so a missing file is not authoritative."<br>• invalid known: rewrite; valid known: update | M+R | Watcher, API | `ImplicitVaultStorageModePolicyService.cs:77-110`, `112-157`; `VaultPathSyncModelCatalog.cs:67-74`, `310-353` |
| SM-10 | An implicit entity gets no note until its boundary stands. A write is skipped unless a note already exists, a boundary was recorded, or the caller begins one. Once a note exists it keeps its folder, and only the file name follows the title. | M+R | API, Watcher, Activation, Once | `ImplicitVaultStorageModePolicyService.cs:30`, `40-56`; `PlaintorchMarkdownStorageService.cs:269-276`, `339-354` |
| SM-11 | The pipeline asks the policy's flags instead of the mode enum: IsIdentityDriven (Freeform, Implicit), MaterializesOnCreate (all but Implicit), BeginsSyncBoundaryOnFirstFile (Implicit), PurgesDesyncedFiles (Enforced), CanCreateFromFile (Freeform, Implicit). Init in any other mode is refused: "Storage mode '…' for '…' does not support initialization from a file." | R | API, Watcher, Activation | `IVaultStorageModePolicyService.cs:15-46`; `PathBoundVaultStorageModePolicyService.cs:13-28`; `VaultEntityLifecycleService.cs:83-86` |
| SM-12 | The watcher never reads a path outside the vault, under Obsidian's attachment folder, or under any segment starting with "_" or ".". A note there asserts no identity, so the vanished-note pass counts it as gone. Discovery and live events apply these rules; the loader, the consistency pass and the write pipeline do not. | R | Watcher | `VaultWatcherPathPolicy.cs:99-130`, `241-252`, `310-352`; `VaultMarkdownDiscoveryService.cs:61`, `238-252`, `552` |
| SM-13 | A freeform directive note may not own the vault root, an entity root or a partition folder (any folder named Objectives, Fates, Decrees or ExecutiveOrders). It also may not sit under, or directly beneath, a non-directive managed root ("Freeform directive cannot assert ownership of a partition directory." and similar). The violation is a validation issue: a known directive is rewritten, an unknown one is ignored, and init is refused. | R | Watcher, API | `VaultWatcherPathPolicy.cs:132-194`, `278-301`, `354-362`; `VaultMarkdownDiscoveryService.cs:663-673` |
| SM-14 | A note's containing directive is the nearest enclosing folder whose self-named primary note, or any top-level note, carries a frontmatter `puck`. The walk skips the vault root, entity roots, partition folders and anything under a non-directive managed root. It sets an incentive's directive on read (SM-21), decides a reparent on write (SM-26), and bounds incentive detection and the loader's territory. | — | Watcher, API, Once | `VaultWatcherPathPolicy.cs:196-232`, `265-276`; `MarkdownFileLocator.cs:257-345` |
| SM-15 | If the vault root, or an existing entity root (Directives, Objectives, Fates, Decrees, Onrush, Journal, Saga, the data folder), cannot be enumerated, syncing stops and the watcher sleeps and re-probes. Moonlight is not probed. | R | Watcher | `VaultWatcherPathPolicy.cs:41-97`; `VaultWatcherService.cs:217`, `237`, `358`, `473` |
| SM-16 | A rewrite emits the model's declared fields from the row, with `puck:` first for Quiet storage. A declared key belongs to the model even when null, so a cleared value is dropped rather than revived from the old note; undeclared keys are carried over. Values holding `:`, `#`, `[` or `]` are JSON-quoted, and dates, enums and lists (as JSON arrays) are normalised. | M | API, Watcher, Activation, Once | `MarkdownFrontMatterSerializer.cs:37-88`, `287-334` |
| SM-17 | Retired keys are dropped on every rewrite. The declaration is inherited and matched ignoring case; today it covers the stellar directive's `startDate` and `endDate`. A fate's dropped one-off keys (`date`, `startTime`, `endTime`, `eventDuration`) are not declared retired, so they survive as undeclared keys. | M | API, Watcher, Activation, Once | `StellarDirective.cs:22-23`; `RetiredMarkdownFieldAttribute.cs:13-20`; `MarkdownFrontMatterSerializer.cs:65-73`, `90-102` |
| SM-18 | A declared field becomes an issue instead of a value unless it:<br>• converts to its type, and names a defined value for an enum<br>• is present (strict mode) and non-empty, unless nullable<br>• tokenizes against the related type's PUCK, for a foreign key<br>Then the mode rewrites, conflicts, purges or ignores, and init is refused ("Field is required.", "Field cannot be empty.", "Value '…' is not a valid … PUCK: …"). Strict mode applies to known identity-driven notes; new and path-bound notes keep defaults. Checks are by type only: a date field accepts a datetime and truncates it, and any string passes as `timezone:` (DE-15). | R | Watcher, API | `MarkdownFrontMatterSerializer.cs:163-207`, `336-504`, `556-582`, `630-685`; `VaultMarkdownDiscoveryService.cs:636-639` |
| SM-19 | An objective's `due:` reads as a date, a datetime, or a datetime followed by a space and a zone. Anything else is refused: "'…' is not a valid due: expected a date, a datetime, or a datetime with a trailing zone." A due at midnight is written date-only, any other to the minute (or the second). | M+R | Watcher, API, Activation, Once | `Due.cs:14`, `60-104`; `MarkdownFrontMatterSerializer.cs:298`, `475-491` |
| SM-20 | For Quiet storage, the frontmatter `puck` is the identity. A present `puck` overwrites the path-derived id, an empty one is "Field cannot be empty.", and every write emits the id as `puck:`. Its validity is checked downstream (§12). | M+R | Watcher, API, Activation, Once | `MarkdownFrontMatterSerializer.cs:28`, `209-254` |
| SM-21 | The path is authoritative on read. The file name gives the title (the whole name for Quiet storage, PEP097-decoded) and, for Index storage only, the id. The folder gives the parent:<br>• a directive: the nearest self-named folder whose note names a known directive<br>• an incentive: its containing directive (SM-14), or none in the kind's own root<br>• an order: the self-named sprint folder above its ExecutiveOrders partition<br>• a lore page: its RelativePath<br>Moving or renaming a note therefore reparents or retitles its entity. | M | Watcher, API | `VaultStoragePathComposer.cs:117-201`; `MarkdownFileLocator.cs:219-247`; `VaultMarkdownDiscoveryService.cs:684`, `842-935` |
| SM-22 | The canonical location follows the declaration. The container is the parent's own folder plus PartitionUnder, or else the location root. The name is "{token} - {title}" (Index) or "{title}" (Quiet), PEP097-encoded. The shape is a self-named folder or a single file. A child goes under its parent's real folder when the parent has a note. Refused: "'…' requires a resolved parent entity to compose its storage path." (ExecutiveOrder), and a PartitionUnder that is not one safe segment. | M+R | API, Watcher, Activation, Once | `VaultStoragePathComposer.cs:79-104`, `203-247`, `283-340`; `MarkdownFileLocator.cs:45-70`; `PlaintorchMarkdownStorageService.cs:607-635` |
| SM-23 | A lore page's RelativePath decides its file while the path stays inside the vault; otherwise the page goes to `Saga/{identifier} - {title}/…`. The parent's folder is ignored. | M | API, Watcher | `VaultStoragePathComposer.cs:342-377` |
| SM-24 | A note that cannot be read is not synced. A sharing violation is retried up to four attempts (50, 100 and 200 ms apart); after that, "Vault file access failed (…) for '…'." is thrown for the watcher to grade and requeue. Only discovery and the system API read this way. | R | Watcher, API | `VaultFileAccess.cs:21-57`, `63-98`; `VaultMarkdownDiscoveryService.cs:409`, `584`, `916`; `SystemApiService.cs:132` |
| SM-25 | Every canonical write puts the note where its mode says. An identity-driven note is located by identity and kept where it is (SM-08, SM-10). A path-bound one goes to the canonical path. A child goes under its parent's real folder, and a self-named folder moves whole. The file name follows the title, except that a write given neither `previous` nor a source keeps an existing note's path and name. | M | API, Watcher, Activation, Once | `PlaintorchMarkdownStorageService.cs:229-320`, `356-412`, `467-503` |
| SM-26 | A note whose folder implies a different parent than the row moves to the canonical path under the new parent, or to the location root, overriding keep-where-authored. The test reads the file's own location, so no `previous` is needed. | M | API, Watcher, Activation, Once | `PlaintorchMarkdownStorageService.cs:282-292`, `422-465` |
| SM-27 | A rewrite owns only the frontmatter. The body is carried over from the previous, source or current file, and a byte-identical result is not written. | M | API, Watcher, Activation, Once | `PlaintorchMarkdownStorageService.cs:309-318`, `730-801` |
| SM-28 | When several files assert an entity's identity, a write takes the preferred path if it is among them, else the most recently written one, and leaves the others alone. A file matches by a loose filename id or by its frontmatter `puck`, searched across the model's whole territory. | M | API, Watcher, Activation, Once | `PlaintorchMarkdownStorageService.cs:514-605` |
| SM-29 | Once a note lands on a new path, its previous file and the source file are deleted outright (their content was copied), and empty folders are pruned up to the storage root. | M | API, Watcher, Activation, Once | `PlaintorchMarkdownStorageService.cs:319-320`, `818-865`, `923-945` |
| SM-30 | Lore save hook: a lore page whose parent changed moves its folder under the new parent's folder and rewrites its RelativePath. A vertical move is refused ("…is vertical and not supported by the static nesting pattern…", "…cannot be reassigned vertically…", "Lore parent '…' was not found during reassignment sync."). No API write changes a parent; a watcher scaffold rewrite can. | M+R | Watcher | `LorePageSaveHook.cs:24-92`; `IEntitySaveHook.cs:9-58`; `PlaintorchMarkdownStorageService.cs:294-306`; `VaultWatcherSyncService.cs:326`, `373` |

## 7. Database constraints and delete behaviour
SQLite enforces these rules on every pathway, raw SQL included. EF adds the model's rules (owned types, conversions, and client-side nulling and cascades of loaded rows) on every EF save and read. Every rule was checked against `PlainfraContextModelSnapshot.cs` and `PlainfraContext.cs` at HEAD.

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| DB-01 | There is at most one stored edge per source kind, id and slot, target kind, id and slot, trigger and constraint. SQLite counts each NULL as distinct, so a duplicate with a NULL trigger, constraint or slot passes, and the API's check on resolved values is the authority (DP-04). Endpoints are loose ids with plain indexes and no foreign key. | R | DB | `PlainfraContext.cs:334-356`; `PlainfraContextModelSnapshot.cs:188-193`; `20260923220349_OccurrenceRecurrenceId.cs:115-119` |
| DB-02 | A backlog schedule names exactly one owner (`CK_BacklogSchedules_SingleOwner`). There is one row per owner, type and phase (a unique index per owner column), and a schedule is deleted with its owner (cascade). These constraints back the recorder (SH-25, SH-26). | M+R | DB | `PlainfraContext.cs:358-390`; `PlainfraContextModelSnapshot.cs:80-92`, `1158-1180`; `20260930054809_BacklogSchedules.cs:22-75` |
| DB-03 | Deleting an onrush sprint:<br>• deletes its executive orders (required key, cascade)<br>• sets to null its tracked checkpoints, its milestone link and its member stellar directives<br>• is refused by the store while an objective names the sprint. That key still has no ON DELETE action, and only the API's Include lets EF null the members first, although the comments say the sprint frees them<br>The sprint↔milestone pair is a mutual key, so deleting both takes two saves (ON-08). | M+R | DB | `PlainfraContext.cs:392-412`; `PlainfraContextModelSnapshot.cs:1184-1187`, `1301-1305`, `1321-1324`, `1400-1403`, `1410-1412`; `20260714231514_DeclarativeEcosystem.cs:336-341`; `Objective.cs:30` |
| DB-04 | Deleting a fate or an objective deletes its eventives (cascade). Both owner keys are optional and no CHECK requires exactly one, so an eventive with both owners or none is accepted. | M | DB | `PlainfraContext.cs:444-453`; `PlainfraContextModelSnapshot.cs:1231-1239` |
| DB-05 | Deleting a timeframe sets to null the affinity of executives, reflectives and attentives and the availability of directives, also when a lunar directive's delete cascades its timeframes. The hook clears availability first as a tracked change (SH-04). The store clears the affinity of rows the context has not loaded, unseen (DI-11). | M | DB | `PlainfraContext.cs:455-478`; `PlainfraContextModelSnapshot.cs:1113-1116`, `1215-1218`, `1277-1280`, `1356-1359` |
| DB-06 | Required children are deleted with their parent by EF convention: a decree's attentives, a Polaris cycle's executives and reflectives, and a lunar directive's timeframes (and with them their orbit states, DB-07). No graveyard entry is written for them. | M | DB | `PlainfraContextModelSnapshot.cs:1118-1122`, `1286-1290`, `1365-1369`, `1380-1384`; `Attentive.cs:23`; `Executive.cs:23`; `Reflective.cs:28`; `Timeframe.cs:29` |
| DB-07 | There is one orbit state per incentive and one per timeframe (unique indexes). Each is deleted with its owner (cascade), and every row names its kind (NOT NULL discriminator). | M+R | DB | `PlainfraContext.cs:484-508`; `PlainfraContextModelSnapshot.cs:404-429`, `1038-1063`, `1438-1458`; `20260925001601_OrbitScheduleStateHierarchy.cs:37-74` |
| DB-08 | A Polaris cycle holds at most one executive per incentive. The unique index is filtered to a non-null incentive, so one-shot and released executives are exempt. PO-08 checks first. | R | DB | `PlainfraContext.cs:535-540`; `PlainfraContextModelSnapshot.cs:312-314` |
| DB-09 | Restricting keys (EF ClientSetNull, no ON DELETE action in the store): a directive's subdirectives and incentives, an incentive's executives, a decree's reflectives, a sprint's objectives (DB-03), and a lore page's children. The store refuses the delete while such rows exist, but EF first nulls the dependents the caller loaded, so the outcome depends on what was included.<br>• executives and reflectives: released by the hook first (SH-01, SH-02)<br>• directives and lore pages: refused by the API first (DI-05, LO-05)<br>• file-driven deletes: probed first (DB-17) | M+R | DB | `PlainfraContextModelSnapshot.cs:1220-1222`, `1282-1284`, `1312-1314`, `1361-1363`, `1391-1393`, `1410-1412`; `Directive.cs:41`; `Incentive.cs:23`; `Executive.cs:39`; `Reflective.cs:53` |
| DB-10 | No two directives, stellar or lunar, share a non-null codename (a unique index declared by attribute). No application check exists, so a clash from the API or from a note's `codename:` is a raw DbUpdateException. The match is case-sensitive. | R | DB | `Directive.cs:24`, `33-34`; `PlainfraContextModelSnapshot.cs:235-236` |
| DB-11 | Each graveyard entry has a unique key. Keys are fresh GUIDs, so the index never fires. Entity ids are deliberately not unique, so repeated lifecycles are kept. | R | DB | `DatabaseGraveyardEntry.cs:9`; `FileGraveyardEntry.cs:9`; `PlainfraContextModelSnapshot.cs:726-727`, `774-775` |
| DB-12 | Natural keys coalesce rows: one write intent per entity type and id, one dismissal and one preference per key, one registry entry per issued id, and one sequence per key. The outbox, preference and dismissal upserts and PUCK issuance rely on them. Directive and incentive ids live in different tables, so their uniqueness across families rests on the PUCK registry (§12). | R | DB | `VaultWriteIntent.cs:14`; `OperationStatusDismissalRecord.cs:17-18`; `UserPreferenceRecord.cs:14-15`; `PlainfraContextModelSnapshot.cs:564`, `577`, `807`, `879`, `956` |
| DB-13 | Owned values live in the owner's row. An occurrence always has an Epoch with a granularity (a required navigation and a NOT NULL column). A due (objective, checkpoint) and a cycle's forecast are optional and read back as null when all their columns are null. Only EF prevents a due without a moment; the `Due_*` columns are nullable. | M+R | DB | `PlainfraContext.cs:422-428`, `437-442`; `PolarisForecast.cs:7`; `PlainfraContextModelSnapshot.cs:1124-1155`, `1189-1206`, `1241-1268`, `1331-1349`, `1414-1431` |
| DB-14 | Shared tables name each row's kind (a NOT NULL discriminator), and one that matches no type fails on read. Siblings map same-named properties to their own columns (Status, LunarStatus, FateStatus, DecreeStatus; Objective_College), while fate and decree share Orbit. A derived type's required columns are nullable in the store, so their presence rests on the CLR type. | R | DB | `PlainfraContext.cs:257-262`, `269-275`, `286-314`, `484-488`; `PlainfraContextModelSnapshot.cs:212-215`, `356-359`, `410-413`, `961-1109` |
| DB-15 | Required columns are NOT NULL, so a save without them fails. Only null is refused, so an empty title passes. Since dev/phase3a, `Eventives.RecurrenceId` is nullable (a manual eventive has none), while `Attentives.RecurrenceId` stays required by explicit configuration. | R | DB | `PlainfraContextModelSnapshot.cs:29-38`, `161-178`, `212-229`, `259-264`, `330-339`, `518-543`, `603-623`; `PlainfraContext.cs:430-435`; `20260930141546_ManualEventives.cs:21-30` |
| DB-16 | Value conversions apply on every read and save. Enums are stored by name and parsed on read, and an unknown name throws. A retired severity "Suspended" reads as Warning. A JSON `null` tag or college list reads as empty, and invalid JSON throws. No CHECK backs any enum column. Colleges are numbers inside their JSON, and a decree's college is an INTEGER where an objective's is a name. | M+R | DB | `PlainfraContext.cs:189-192`, `213-255`, `259-332`, `365-366`, `414-428`, `510-512`; `PlainfraContextModelSnapshot.cs:1016-1018`, `1072-1073` |
| DB-17 | Before a file-driven delete, the watcher counts the stored rows behind each restricting single-column key with a raw `SELECT COUNT(*)`. Cascade, set-null and ownership keys are skipped, and executives and reflectives are exempt because the hook releases them. If any rows remain, it refuses: audit `delete-blocked`, then "… was not removed: it is still referenced by …". Nothing is written. | R | Watcher | `VaultEntityGateway.cs:71-134`; `VaultWatcherSyncService.cs:690-718`; `WatcherMessages.resx:95-100` |

| Behaviour | Relationships |
|---|---|
| Cascade, declared | backlog schedule → incentive, directive, checkpoint; eventive → fate, objective; orbit state → incentive, timeframe |
| Cascade, by convention (required key) | attentive → decree; executive and reflective → Polaris cycle; timeframe → lunar directive; executive order → onrush sprint |
| Set null | checkpoint → onrush sprint; sprint milestone → checkpoint; stellar directive → onrush sprint; executive, reflective and attentive affinity → timeframe; directive availability → timeframe (cleared first by SH-04) |
| Restrict (ClientSetNull: no store action; EF nulls loaded dependents) | directive → parent directive; incentive → directive; objective → onrush sprint (the comments say set null); lore page → parent page; executive → incentive and reflective → decree (released first by SH-01, SH-02) |
| Restrict, unmapped | `Incentives.ParentIncentiveId` → incentive, always null, on databases that ran the rewritten nesting removal (MG-20) |
| No foreign key | dependency endpoints (released only by SH-18 and DP-12); the ledger's `SourcePuck`; write intents; audit, boundary and graveyard rows |
| Same row (owned) | occurrence Epoch; objective and checkpoint Due; cycle Forecast |

## 8. Persistence side effects
These are the writes the persistence layer makes around an entity change: boundaries, graveyards, audit rows and the note outbox. Boundary end is SH-03.

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| PS-01 | An implicit entity's boundary begins the first time its note exists: when the watcher syncs it, or when a canonical write lands it (the API's begin-boundary and the v1→v2 step included). The begin is one audit row, `boundary/BoundaryBegin`, written only if the latest Begin/End row is not already Begin, and saved at once. That latest row is the boundary's state. It gates implicit deletion (SM-09), releases withheld writes (SM-10) and lists the notes the vanished-note pass expects. The boundary ends with the entity (SH-03). | M | Watcher, API, Activation, Once | `VaultImplicitBoundaryService.cs:41-117`; `VaultWatcherSyncService.cs:533-548`; `PlaintorchMarkdownStorageService.cs:322-354`; `VaultMarkdownDiscoveryService.cs:131-140`, `694-700` |
| PS-02 | A user- or watcher-driven delete stages a JSON snapshot of the entity (type, id, title, payload, reason, actor, time) without saving, so it commits with the removal and a refused delete leaves none. Each call site opts in: the fate, decree, directive, lore page, objective, sprint and order deletes; since dev/phase3a, the forecast delete (the cycle only); and the watcher's DeleteFromDatabase. | M | API, Watcher | `VaultTemporalDataService.cs:20-51`; `DeclarativeApiService.cs:170`, `316`; `DirectiveApiService.cs:358`; `LorePageApiService.cs:170`; `ObjectiveApiService.cs:213`; `OnrushSprintApiService.cs:198`, `222`, `493`; `PolarisCycleApiService.cs:299`; `VaultWatcherSyncService.cs:722` |
| PS-03 | A file or folder the core removes is moved into the data folder's `graveyard/files/{time}_{key}/{path}`, then a graveyard row is added and saved. A missing path is a no-op. Callers: the watcher's purge, entity note deletes (PS-08), the startup remove recovery, the activation consistency pass and media removal. | M | API, Watcher, Activation | `VaultTemporalDataService.cs:53-110`; `VaultWatcherSyncService.cs:623-633`; `PlaintorchMarkdownStorageService.cs:867-902`; `VaultWriteQueue.cs:232-252`; `VaultAutoGeneratedPuckConsistencyService.cs:337`; `VaultMediaService.cs:140` |
| PS-04 | `VaultAuditLogService.WriteAsync` stores its entry by saving the shared scoped context, so it commits whatever else the caller has pending and runs every save interceptor, the whole state policy included. `Stage` adds an entry without saving; the hook and the v5→v6 step use it. | M | API, Watcher, Activation, Once | `VaultAuditLogService.cs:21-40`, `46-91` |
| PS-05 | The legacy repository upserts by id, merging only some fields, with a synchronous `SaveChanges` (tags, directives, sprints, objectives, cycles), and appends executives, reflectives and ledger rows. A sync save meets only SH-25, SH-26 and SH-28: no state policy, change feed, note intent or graveyard. Nothing calls it; only `GetCounts` is used. | M | Hook\* | `PlaintorchRepository.cs:27-42`, `74-175`, `182-222`; `PlaintorchEngine.cs:65` |
| PS-06 | Note outbox: an API change that must reach a note records one intent row per entity in the same save as the change. The row holds the type's full name and the id; Remove overrides Reconcile, and a remove captures the note's last-known path. A drain then rewrites or archives the note and deletes the row. At activation, the startup drain finishes what a crash left: a reconcile rewrites the note from the row, and a remove archives the last-known path (`startup-remove-recovery`). While a row is pending, the vanished-note pass does not delete that entity (matched on the id alone). | M | API, Hook, Activation | `VaultWriteIntent.cs:14-40`; `PlainfraContext.cs:233-236`; `VaultWriteQueue.cs:42-117`, `178-252`; `PlaintorchStatePolicyProcessor.cs:250`; `PlaintorchEngine.cs:36`, `54`; `VaultMarkdownDiscoveryService.cs:156-170` |
| PS-07 | A reconcile drain is bounded by `NoteQueueTimeout` (2000 ms by default; zero or less means no bound). Past the timeout, the request is marked pending (`VaultWriteReadiness`, `noteReady` false) and the drain finishes in the background on the same scope. A fault is only logged, and the row waits for the next activation. Removes drain synchronously. | M | API, Hook | `VaultWriteQueue.cs:107-176`, `217-225`; `VaultWriteReadiness.cs:10-17`; `WatcherPreferences.cs:13-20`; `PlaintorchStatePolicyFileSyncService.cs:45-55` |
| PS-08 | Deleting an entity archives its note through PS-03: the note found by identity (else the canonical path), or the whole folder when the note's name equals its folder's, non-entity files included. Empty parents are then pruned. API deletes go through the outbox; the sprint delete archives its orders' notes and its own folder directly. A watcher delete starts from a missing note, and a raw delete leaves the note behind. | M | API | `PlaintorchMarkdownStorageService.cs:48-56`, `505-512`, `867-921`; `VaultWriteQueue.cs:111-117`; `OnrushSprintApiService.cs:196-201`, `231` |

## 9. Materialisation and background passes
Besides the watcher, the only timed passes are the rolling harden-on-time pass and the status-transition flush; no background job rolls Polaris or onrush cycles. Referenced-occurrence hardening is SH-17, orbit validation DE-05, the resume fast-forward DE-08 and the interaction refusals DE-09 and DE-10; they are not repeated here.

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| MA-01 | Rolling harden-on-time runs 15 s after host start, then hourly. It is not session-gated, and `TimeIsInteraction` is hard-coded true.<br>• Active orbit fates (plus OptOut ones when `agenda.auto-materialise-optout` is on) and Active orbit decrees other than lunar reflect-decrees are sought through now on their zone clock, and each crossed occurrence hardens (an OptOut fate's stamped OptOut).<br>• A due objective that is not Done, Archived or Failed and has no eventive gets an all-day eventive on its due date.<br>• The cached `NextOccurrence` rolls forward.<br>It makes one save through the hook, with a `daemon/rolling.materialize` audit row when anything was created. | M | Background | `RollingMaterializationService.cs:18-63`; `ProximityMaterializationService.cs:144-304`; `MaterializationPolicyOptions.cs:19`; `AgendaPreferences.cs:17` |
| MA-02 | The rolling pass applies the LC-11 freeze only after its cursor has passed the occurrence, so a frozen occurrence is skipped for good and never hardened once the gate opens. An interaction is refused ("Fate '…' occurrence on … is blocked by unmet dependencies and cannot be materialized."). A dependency reference hardens it anyway (SH-17). Decrees and objectives have no gate here. | R | Background, API | `ProximityMaterializationService.cs:169-172`, `277-285`; `OccurrenceHardeningService.cs:156-163`; `DependencyGateService.cs:168-182` |
| MA-03 | Cycle-begin reflective generation. When a cycle begins (begin, start-new, or the auto-start of planning an executive or adding a decree), reflectives are generated for the host-local day of its start, each affined per MA-04. It makes one save and writes an `api/polaris.materialize-proximity` audit row. Re-beginning an ended cycle re-runs it for the new start day. A cycle begun through the Journal note's `startTime:` gets none. | M | API | `ProximityMaterializationService.cs:47-136`; `PolarisCycleApiService.cs:55-93`, `130-164`, `334-342`, `700-708`, `860-869` |
| MA-03a | Lunar pause. A reflect-decree yields a reflective only while it is Active and every lunar directive up its lineage is Active; a lunar directive starts On Hold, and a decree with no lunar ancestor never yields one. A paused orbit decree is skipped without seeking, so the next live begin consumes the days it missed without generating them. | R | API | `ProximityMaterializationService.cs:56-73`, `81-90`; `PlaintorchOrbitService.cs:298-327`; `LunarDirective.cs:32` |
| MA-03b | Pool draw. An orbit decree is taken when one of its occurrences covers the start day. The live reflect-decrees with no orbit form a pool. Each begin draws `reflection.base-daily-reflectives` of them at random (default 3, a negative value read as 0), minus pool decrees already in the cycle; a smaller pool is taken whole. | M | API | `ProximityMaterializationService.cs:75-99`; `ReflectionPreferences.cs:15` |
| MA-03c | A decree gets at most one reflective per cycle, so re-running begin adds nothing. The check reads the cycle's rows once, and no unique index backs it. | R | API | `ProximityMaterializationService.cs:60-64`, `101-107` |
| MA-04 | An executive or reflective created without an explicit affinity is affined to the nearest directive availability up its lineage that still names an Availability-mode timeframe; failing that, to the lowest-id College-mode timeframe that includes its college; otherwise to none. The lunar directive's status is ignored. The affinity is seeded at creation only and never re-resolved. | M | API | `TimeframeAffinityResolver.cs:47-137`; `PolarisCycleApiService.cs:754-770`; `ProximityMaterializationService.cs:117` |
| MA-05 | Touching a projected occurrence finds or adds its row in the caller's unit of work, without saving. The row is keyed by owner and RECURRENCE-ID, including eventives added earlier in the same unit of work, and the occurrence is resolved in preview mode, so the cursor never moves. An orbit-less owner, or an objective, accepts any slot. An OptOut fate's occurrence hardens stamped OptOut. | M | API | `OccurrenceHardeningService.cs:39-96`, `146-241` |
| MA-06 | The agenda unions projected occurrences with the hardened rows; a hardened row overrides its twin by owner and RECURRENCE-ID, and manual eventives stand alone. The projected occurrences come from Active and OptOut orbit fates (OptOut stamped), Active orbit decrees other than lunar reflect-decrees, and due objectives that are not Done, Archived or Failed. Windows are on the host clock. Polaris inclusions are this projection cut to the 24 h after the cycle's start. Previewing an orbit with no state adds one to the request's context. | — | API | `AgendaProjectionService.cs:38-186`; `InclusionWindow.cs:15-78`; `PolarisCycleApiService.cs:615-638`; `PlaintorchOrbitService.cs:198-243`, `334-351` |
| MA-07 | A begun cycle's timeframe candidates are the orbit-less timeframes plus those whose orbit selects its host-local start day on the vault default calendar. They are fixed for the cycle's life, and a malformed orbit reads as non-matching (logged). Candidates are warmed at activation and at begin, and recomputed lazily on a miss; that read saves any lazily created orbit state. A recompute overtaken by an invalidation is discarded. An unbegun cycle refuses: "Polaris cycle '…' has not begun, so it has no timeframe candidates." | M+R | Activation, API | `TimeframeCandidateService.cs:30-100`; `TimeframeCandidateCache.cs:70-133`; `PlaintorchOrbitService.cs:409-496`; `PlaintorchEngine.cs:78-102`; `PolarisCycleApiService.cs:105-123` |
| MA-08 | Each scheduled fate, decree or timeframe has one cursor. It is:<br>• reset when its orbit is set or changed (SH-05, SH-06; WA-24)<br>• advanced only by seeking (MA-01, MA-03)<br>• fast-forwarded on an API resume (DE-08) but not on a frontmatter one, so the next rolling pass back-fills the paused span<br>• created lazily on first resolution, anchored at today for a seek and at the earlier of the day and today for a preview<br>The calendar is the declarative's own, else the vault default; timeframes always use the default. | M | Hook, Background, API | `PlaintorchOrbitService.cs:42-52`, `123-208`, `334-361` |
| MA-09 | Zone stamping (DE-16). Every orbit-born occurrence (rolling pass, interaction and reference hardening, projection) is stamped with its declarative's zone. Seeking, the fast-forward and `NextOccurrence` read "now" on that zone's clock, and an unknown zone counts as floating. A hardened occurrence keeps its stamp, and a zone change neither restamps it nor resets the cursor. | M | Hook, Background, API | `PlaintorchOrbitService.cs:54-68`, `166-192`, `252-261`; `ProximityMaterializationService.cs:265`, `298`; `OccurrenceHardeningService.cs:91`, `174`; `AgendaProjectionService.cs:101`, `152`; `OccurrenceZones.cs:17-25` |
| MA-10 | Every 2 s, and once at shutdown, buffered status transitions are inserted as `OperationStatusEvent` rows through the full save hook. The flush is not session-gated. A batch is dequeued before its save, so a failed save loses it. While idle no context can be built, so the buffer waits for the next activated vault. | M | Background | `OperationStatusPersistenceWorker.cs:17-50`; `OperationStatusEventBuffer.cs:19-46` |

## 10. Activation
The activation order:
1. `serve` probes its profile's endpoint and steps aside if a core answers (AC-02).
2. Every 2 s, and on any config.json change, the coordinator resolves the vault to serve (AC-01): `PLAINTORCH_VAULT_PATH`, else `ActiveVaultPath`. An uninitialized folder waits; a path refused for any other reason is skipped.
3. The layout binds and loads `.plaintorch` (AC-07), and the vault lock is taken (AC-05).
4. Bootstrap:
   - creates the folder skeleton and default settings (AC-06)
   - validates the entity catalog (AC-08), the path-sync catalog (AC-09), the lifecycle declarations (LC-05) and the storage topology (AC-10)
   - compiles the PUCK declarations (AC-11)
   - refuses a newer database (AC-12)
   - applies the EF migrations (AC-13)
5. The vault migrations run (AC-14).
6. The write-intent outbox drains (AC-15).
7. The unknown-id consistency pass runs (AC-16).
8. Preferences load (AC-17).
9. The active cycle's timeframe candidates are warmed, best effort (MA-07).
10. The host reports Active (sweeping), the session is marked ready and a 30 s sweep backstop is set. The watcher then resets its issue set and loads dismissals (WA-02), probes access (WA-03) and sweeps, deleting vanished notes last (WA-04, WA-09).
11. A failure in steps 3–9 releases the vault and reports Failed. Release marks the session idle, deletes the lock file, purges the PUCK catalog and the candidate cache and unbinds the layout; the preference store is kept.

CLI `init` runs steps 4–9 on its directory without the coordinator or the lock (AC-04).

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| AC-01 | The core serves one vault: `PLAINTORCH_VAULT_PATH` (set by `serve --vault`), else config.json's `ActiveVaultPath`, re-read every 2 s and on every config.json change.<br>• A folder without `.plaintorch` stays idle ("The configured vault is not initialized for PLAINTORCH."). It is re-checked on every pass and activated once initialized (dev/phase3a).<br>• A vault refused for its lock, a newer database or any other activation failure stays refused until the configured path changes or the core restarts.<br>• On release, the session goes idle, the lock file is deleted, the PUCK catalog and the candidate cache are purged and the layout is unbound. | M+R | Activation | `PlaintorchCoreService.cs:33-36`, `80-154`, `164-220`, `263-278`; `Program.cs:324-332` |
| AC-02 | A second `serve` for a profile exits with code 3 (an `already-running` event in spawn mode) when a core answers `/healthz` on the profile's pipe or socket. A probe error reads as "not running". | R | Activation | `Program.cs:28`, `48-56`, `266-282`; `PlaintorchInstanceProbe.cs:33-78` |
| AC-03 | CLI `activate` writes config.json (atomically, keeping the shell's keys) only for a folder with `.plaintorch`; otherwise it fails with "Vault '…' is not initialized for PLAINTORCH. Run 'init' in that directory first." (exit 1). The shell writes config.json itself, so AC-01 checks again. | R | Activation | `PlaintorchUserConfiguration.cs:212-220`, `244-270`; `Program.cs:120-141` |
| AC-04 | A command line with no command token (`--help` included) runs `init` in the working directory. That runs the whole bootstrap and repair chain (steps 4–9) there without the vault lock; a newer database exits 1. | M | Activation | `PlaintorchLaunchArguments.cs:67-74`, `110-113`; `Program.cs:79`, `92-115` |
| AC-05 | Only one serving core owns a vault, through an exclusive `.plaintorch.lock` with owner metadata; otherwise activation fails with "Vault '…' is already owned by another PLAINTORCH service instance.". The file is deleted on release. CLI `init` takes no lock. | R | Activation | `PlaintorchVaultLockService.cs:16-48`, `69-82`; `PlaintorchCoreService.cs:171-172` |
| AC-06 | Before any check, bootstrap creates the required folders (vault, metadata, graveyards, Directives, Moonlight, Onrush, Objectives, Fates, Decrees, Journal, Saga) and writes a missing `.plaintorch` with defaults. A vault refused later in the chain has therefore already been changed on disk. | M | Activation | `VaultBootstrapper.cs:42-53`; `VaultLayout.cs:145-171` |
| AC-07 | Missing location keys resolve to defaults. A missing, unreadable or unparsable `.plaintorch` loads as a fresh v1 with the default roots. So a new vault is stamped v1 and runs every vault migration, and a corrupt one re-runs them all and loses its custom roots at the next version write. | M | Activation | `VaultSettings.cs:15`, `27-44`, `79-91`; `VaultVersionService.cs:17-31`; `VaultLayout.cs:178-186` |
| AC-08 | Activation fails when a PUCK format declares no kind, a vault-stored type is not PUCK-named, an abstract anchor has no concrete member, a type is missing from the EF model, or a kind is declared twice. | R | Activation | `VaultEntityModelCatalog.cs:142-180`; `VaultBootstrapper.cs:57` |
| AC-09 | Each path-sync model must match its entity's declared storage shape, and every concrete vault-stored entity must be covered by a model or a family anchor ("Vault-stored entity '…' has no path-sync model and would be undiscoverable…"). Otherwise activation fails. | R | Activation | `VaultPathSyncModelCatalog.cs:103-140`, `261-286`; `VaultBootstrapper.cs:58` |
| AC-10 | Storage declarations must be coherent: a `[VaultStorage]`, parent-id and parent-directory properties that exist and are strings, a parent type for a parent id, and a `PartitionUnder` that is one safe segment under a declared parent. Same-shape models that share a scan root must differ in their first static discriminator ("Vault activation denied because '…' and '…' share scan root '…'…"); a discriminator on one side only counts as different. | R | Activation | `VaultStorageTopologyValidator.cs:17-100`; `PuckPathDiscriminabilityService.cs:11-34`; `VaultBootstrapper.cs:60` |
| AC-11 | Every `[PuckFormat]` type is compiled. Activation fails on two declarations with the same signature or the same zero-filled sample ("PUCK declaration uniqueness validation failed…", "PUCK parse-space uniqueness validation failed…"), or on a retired syntax ("PUCK discriminator boundary syntax '(...)' is no longer supported."). A lazy compile outside activation skips both uniqueness checks. | R | Activation | `PuckRuntimeCompilationCatalog.cs:19-53`, `69-75`, `123-154`, `177-216`; `PuckNotationParser.cs:32`, `40`; `VaultBootstrapper.cs:63-66` |
| AC-12 | A database whose migration history names a migration this core lacks is refused before anything writes to it ("The database of vault '…' was upgraded by a newer PLAINTORCH core (migration … is unknown to this core)…"). The host reports Failed, which sticks (AC-01), and `init` exits 1. | R | Activation | `PlainfraContextInitializer.cs:29-33`, `39-52`; `VaultDatabaseAheadOfCoreException.cs:18-23`; `PlaintorchCoreService.cs:199-211`; `Program.cs:104-108` |
| AC-13 | Every pending EF migration, including its one-time SQL data fixes, is applied before any other work. | M | Activation | `PlainfraContextInitializer.cs:32` |
| AC-14 | Vault conventions advance one registered step at a time from the `.plaintorch` version (5 steps, v1 → v6), and the version is written only after a step completes.<br>• With no registered step: "No vault migration is registered from version … toward current version …".<br>• A failing step records history `failed` and rethrows.<br>• A version above the engine's only logs a warning, and activation continues.<br>• `completed-with-conflicts` still advances. | M+R | Activation | `VaultMigrationRunner.cs:48-115`, `125-151`; `VaultSchema.cs:28`; `PlaintorchModule.cs:167-171` |
| AC-15 | Every write intent left between a database commit and its file write is completed before the file → database passes. A reconcile intent rewrites the note from the row, or is cleared if the entity is gone. A remove intent archives the file or folder at its last known path (`startup-remove-recovery`). | M | Activation | `VaultWriteQueue.cs:183-252`; `PlaintorchEngine.cs:54` |
| AC-16 | A file in declared territory whose name carries a mintable PUCK the database does not know is handled by mode:<br>• in Enforced roots it is purged (`desync-purge`)<br>• elsewhere it is renamed to its title-only name (`strip-unknown-id`), or archived instead on a name clash<br>Emptied folders are deleted. Title-only names, prefixes that do not tokenize, known ids, lore pages and files outside territory are left alone, and no issue is raised. | M | Activation | `VaultAutoGeneratedPuckConsistencyService.cs:30-156`, `215-380`; `PlaintorchEngine.cs:55` |
| AC-17 | The vault's stored preference overrides replace the in-memory store and drop the candidate cache. This runs after steps 5–7, and the store is not cleared at release. The write and validation rules are SY-04 and SY-05. | M | Activation | `UserPreferenceService.cs:26-32`; `UserPreferenceStore.cs:20-21`; `PlaintorchEngine.cs:56` |
| AC-18 | With no vault bound, every layout path, and so every vault-scoped context, throws `VaultNotActiveException` ("No PLAINTORCH vault is currently active…"). The background passes meet it as a logged failure. | R | Activation, Background | `VaultLayout.cs:173`; `VaultNotActiveException.cs:16-19`; `PlainfraStoreContextFactory.cs:33-38` |

## 11. One-shot migrations
Vault steps advance `.plaintorch`'s SchemaVersion one step at a time at activation, after the EF migrations (the runner and its version gate are in §10). The engine emits v6 (`VaultSchema.cs:28`), and the steps are registered at `PlaintorchModule.cs:167-171`. EF data fixes run inside `Migrate` as raw SQL, outside every interceptor. Only EF migrations with data-moving SQL, or with a hand-written rebuild-avoidance, are listed.

| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| MG-01 | Re-canonicalisation base. Load the target types under the source version's conventions, then, for each item:<br>• import it when the row is absent (`context.Add` plus a save, unvalidated)<br>• copy the legacy file once to `graveyard/files/migration-v{from}-to-v{to}/…` (no graveyard row)<br>• re-emit it in the current conventions (the row wins, the body is kept) and audit `recanonicalize`<br>An IOException or InvalidOperationException counts as a conflict (`recanonicalize-conflict`) and the loop goes on; any other exception fails the step. | M | Once | `VaultRecanonicalizationMigration.cs:47-117`, `119-141` |
| MG-02 | Loader. It loads only files inside the model's own (non-vault-root) scan roots, or in a folder whose containing directive exists. A flat Index id that does not tokenize leaves the file untouched. A Quiet id comes from `puck`, and the family member is chosen by identity. Fields are hydrated with defaults kept, and their validation issues are discarded. The baseline convention set (objectives as Index and Synced) is returned for any version below current. | M+R | Once | `VaultLoader.cs:38-141`, `143-229`; `VaultConventionSetFactory.cs:30-52` |
| MG-03 | v1→v2 (`20260710_0001_ObjectiveQuietCanonicalization`): objective notes named "{id} - Title.md" become "Title.md" with `puck:`, file-only objectives are imported, and each begins its boundary. Every new vault is stamped v1 (§10), so it runs this step on any matching files already there. | M | Once | `ObjectiveQuietCanonicalizationMigration.cs:15-42` |
| MG-04 | v2→v3 (`20260905_0001_OnrushPlanningPlaceholderRename`): the planning sprint "0" becomes "x0000". The step inserts the new row, re-points objectives and tracked checkpoints, hands over the milestone and removes "0" (one save), re-keys the registry entry (a second save), moves the folder through the storage service, and audits `onrush-planning-rename`. Member stellar directives are not re-pointed; none can name "0". | M | Once | `OnrushPlanningPlaceholderRenameMigration.cs:46-144` |
| MG-05 | v3→v4 (`20260922_0001_FateOrbitOnly`): every fate's note is rewritten from its row, which MG-14 folded into an orbit, so the note carries `orbit:` and `calendar:`. Implicit write rules still apply, and the old date keys survive as undeclared keys (SM-17). Audit `fate-orbit-only`. | M | Once | `FateOrbitOnlyMigration.cs:39-69` |
| MG-06 | v4→v5 (`20260930_0001_DirectiveStartEndRetirement`): every stellar directive note still carrying `startDate` or `endDate` is rewritten from its row, which drops them (SM-17). A directive without a note gets none. Audit `directive-start-end-retirement`. | M | Once | `DirectiveStartEndRetirementMigration.cs:42-87` |
| MG-07 | v5→v6 (`20260930_0002_IntrinsicDependencyPurge`): stored edges that the intrinsic directive-children edges contradict or restate are removed in one save, each with a staged `dependency.purge` audit row. Among contradicted edges, the most resolving one goes each round, the newest on a tie, with no preference for a moved subtree. The same planner runs on every save (SH-19, LC-15). | M | Once | `IntrinsicDependencyPurgeMigration.cs:39-68` |
| MG-08 | `LunarStellarDirectiveSiblings`: legacy rows with Discriminator 'Directive' become 'StellarDirective'. Down reverses it. | M | Once | `20260722010046_LunarStellarDirectiveSiblings.cs:13-16`, `30` |
| MG-09 | `AddDependencySystem`: each eventive's original slot is set to its current one (best effort; collapsed later by MG-17). | M | Once | `20260724170105_AddDependencySystem.cs:27-29` |
| MG-10 | `TimeframeAutoInclusionColleges`: a timeframe's single college becomes a one-element JSON array (default `'[]'`). Down drops the data. | M | Once | `20260826003857_TimeframeAutoInclusionColleges.cs:13-28`, `31-43` |
| MG-11 | `AttentiveRecurrenceId`: each attentive's original slot is set to its current one (collapsed later by MG-17). | M | Once | `20260908224915_AttentiveRecurrenceId.cs:27-30` |
| MG-12 | `UniqueExecutiveObjectivePerCycle`: deletes every executive but the earliest per cycle and objective, then builds the unique index (the origin of DB-08). The duplicates' executed and elapsed data is lost with no graveyard entry, and Down does not restore it. | M | Once | `20260915154002_UniqueExecutiveObjectivePerCycle.cs:17-34` |
| MG-13 | `OccurrenceEpoch`: an occurrence's date and time fold into its Epoch. The moment is the date plus the time or midnight, and the granularity is Minute or Day. An eventive's window becomes "Nm", with no guard against an overnight window going negative. Down loses the end time. | M | Once | `20260922011112_OccurrenceEpoch.cs:24-27`, `36-43`, `52-71` |
| MG-14 | `FateOrbitOnly`: a fate with a date and no orbit gets `Z{y/M/d}` or `Z{y/M/dTh:m}`, plus "=Nm" from a positive end-time window or else the event duration. Fates with no calendar are pinned to Gregorian. The one-off columns are dropped, and Down does not unfold them. | M | Once | `20260922092435_FateOrbitOnly.cs:14-42` |
| MG-15 | `BoundAttentivesToExecutives`: Polaris-bound attentives become decree-backed executives, one per cycle and decree: executed if any attentive was Done, with the maximum allocations and affinity, and elapsed 0. The bound attentives are deleted, and Down does not restore them. | M | Once | `20260922115705_BoundAttentivesToExecutives.cs:13-26`, `42-61` |
| MG-16 | `DueMoment`: a date-only due becomes a midnight moment. Down keeps the date. | M | Once | `20260922124840_DueMoment.cs:29-32`, `62-65` |
| MG-17 | `OccurrenceRecurrenceId`: every slot becomes one datetime (a slot without a time becomes midnight) on attentives, eventives and both dependency ends; NULL stays NULL. A super-day attentive's period becomes a duration in whole days, and the dependency unique index is rebuilt on the new columns. Down turns midnight back into all-day. | M | Once | `20260923220349_OccurrenceRecurrenceId.cs:51-73`, `115-119`, `181-206` |
| MG-18 | `OrbitScheduleStateHierarchy`: existing states become incentive states through the new column's default. Down first deletes every timeframe state. | M | Once | `20260925001601_OrbitScheduleStateHierarchy.cs:33-43`, `80-81` |
| MG-19 | `BacklogSchedules`: a stellar directive's StartDate and EndDate become Plan/Begin and Plan/Finish schedules at midnight, before the columns are dropped (a rebuild of `Directives`). No records are backfilled. Down writes the plans back as dates, losing times and every other schedule. | M | Once | `20260930054809_BacklogSchedules.cs:77-93`, `111-123` |
| MG-20 | `RemoveIncentiveNesting`, rewritten on this branch: it empties `ParentIncentiveId` and drops its index, but leaves the column and its self-referencing key in place, unmapped. `Incentives` is therefore not rebuilt, and a later column (`DeclarativeTimeZone`'s) survives an out-of-order run; the next rebuild of the table drops the leftover column. Down only recreates the index. | M | Once | `20260930131323_RemoveIncentiveNesting.cs:10-38` |
| MG-21 | `ManualEventives` (dev/phase3a): Up makes `Eventives.RecurrenceId` nullable (a rebuild that copies every row). Down first pins each manual eventive's moment as its RECURRENCE-ID, then makes the column required again. | M | Once | `20260930141546_ManualEventives.cs:13-49` |

EF migration order at HEAD, last five:

| # | Migration | Origin | Table rebuild |
|---|---|---|---|
| 1 | `20260930054809_BacklogSchedules` | PEP101 state polish | `Directives` (drops StartDate, EndDate) |
| 2 | `20260930131323_RemoveIncentiveNesting` | intrinsic dependencies; rewritten on this branch | none |
| 3 | `20260930141546_ManualEventives` | dev/phase3a | `Eventives` |
| 4 | `20260930144431_AddStellarDirectiveOnrush` | intrinsic dependencies (Patch102.10) | `Directives` (adds the SET NULL key); its designer was regenerated to carry the nullable RECURRENCE-ID |
| 5 | `20260930230346_DeclarativeTimeZone` | dev/phase3a | none (adds `Incentives.TimeZone`) |

A dev/phase3a database already has 3 and 5 applied, and meets 1, 2 and 4 out of order. That is safe: 1 and 4 rebuild only `Directives`, which dev/phase3a does not touch, and 2 no longer rebuilds `Incentives` (`.DISCUSSION.md:1787-1805`).

## 12. Identity and the domain model
Identity covers PUCK issuance, formats, gating and resolution, and the Pleiadean calendar under them (`core/Puck`, `core/Calendar`). The domain-model rows are what entities, epochs, orbit schedules and lore pages enforce or derive on their own (`core/Orchestration`, `core/Orbit`, `core/Saga`). Line numbers are at `b1697b9`; rules that §2–§4 already hold are referenced, not repeated.

### 12.1 PUCK identity
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| ID-01 | A system-minted id draws a spiritgem numerator of `width` random digits, retried while a registry entry equals it. The retry compares the bare digits with registry ids that carry their discriminator, so it never fires and a clash reaches the registry key as a raw `DbUpdateException` (defect, by reading). | M+R | API, Watcher | `PuckIdService.cs:71`, `120-130`, `186-189`; `Checkpoint.cs:17`; `LunarDirective.cs:19`; `StellarDirective.cs:21`; `Objective.cs:14`; `Fate.cs:18`; `Decree.cs:17` |
| ID-02 | Every minted id is registered on a bare context in its own transaction, committed before the caller saves the entity, so an id whose entity is never saved stays issued. Registering an id twice fails at the registry key (raw error), and rows are never released.<br>• Ids the core assigns without minting are never registered: the `x0000` placeholder, every lore id, and caller-input ids the watcher adopts (ID-05).<br>• Polaris day ids are the one reuse (ID-08). | M+R | API, Watcher, DB | `PuckIdService.cs:10-16`, `57-58`, `79-83`, `191-198`; `PuckRegistryEntry.cs:9-13` |
| ID-03 | An incremental segment issues its seed first and stores seed + 1. A first-segment counter is keyed by its declaration and a nested one by the segments rendered before it, so executive orders count per sprint. There is no overflow check: a value of 10^width renders wider and then fails the exact-width rule (ID-10). | M | API | `PuckIdService.cs:72`, `138-162`; `OnrushSprintApiService.cs:100`, `157`, `416` |
| ID-04 | A date-stamp segment encodes the given date, by default today in UTC, as Gregorian `yyyyMMdd` or Pleiadean `yyyMdd`; parsing requires a real date on that calendar. Only Polaris (`{D:p}`) uses it, and every caller passes a local date. | M+R | API, Watcher | `PuckIdService.cs:70`, `110`; `PuckDateStampCodec.cs:14-46`; `PuckTokenizer.cs:171-174`, `242-244`; `PolarisCycle.cs:12` |
| ID-05 | A declaration with a dynamic discriminator or a manual numerator needs caller input covering its segments: "Dynamic discriminator value missing…", "Manual numerator missing…", "Type '…' requires caller-provided PUCK input…", "Requested PUCK '…' does not provide enough segments…".<br>• The API passes an order's sprint segment; lore ids are composed by the API, never minted.<br>• The watcher flags an id-less note (a Synced conflict) and keeps an id a note already carries, unregistered and without advancing the counter. | M+R | API, Watcher | `PuckIdService.cs:63-69`; `PuckCreationService.cs:48-57`; `PuckRuntimeCompilationCatalog.cs:113-114`; `VaultMarkdownDiscoveryService.cs:689-692`; `SyncedVaultStorageModePolicyService.cs:30-35`; `VaultWatcherSyncService.cs:186-189`; `LorePageApiService.cs:77` |
| ID-06 | For a declaration that needs no caller input, a requested id is ignored and a fresh one minted: the `id` on objective, stellar-directive, checkpoint, fate and decree creates is dropped without an error. The watcher's create path likewise re-mints a system type and discards any path id. | M | API, Watcher | `PuckCreationService.cs:43-46`, `81-102`; `ObjectiveModule.cs:45-46`; `DirectiveModule.cs:54-55`; `DependencyModule.cs:60`; `DeclarativeApiService.cs:76`, `217`; `VaultWatcherSyncService.cs:181-185` |
| ID-07 | An id can be composed without minting only when every segment is deterministic: "PUCK id for '…' cannot be composed ahead of minting: segment {i} is non-deterministic (…)." Polaris composes a day's id to look the day up before minting; nothing forces the lookup. | R | API | `PuckIdService.cs:92-118`; `PuckCreationService.cs:68-79`; `PolarisCycleApiService.cs:39`, `144`, `327`, `558`, `852` |
| ID-08 | A cycle's id is its day. Planning, today's cycle and the next-day forecast look the day's cycle up first; minting then registers the id only the first time, and a day whose cycle was deleted (a taken-back plan, a superseded forecast) reuses its registered id. The PolarisCycles key is the only backstop. | M | API | `PolarisCycleApiService.cs:38-45`, `144-160`, `319-331`, `558-565` |
| ID-09 | A cycle's day is read back off its id (none when it is not a Pleiadean date stamp): `PolarisCycle.Day` serves the by-day listing, and the hook's forecast supersession parses the id again with an identical copy. The next-day move reads another day: the start's local date, else the forecast target, else today. Nothing keeps the two equal (DM-07). | — | Hook, API | `PolarisCycle.cs:44-74`; `PlaintorchStatePolicyProcessor.cs:694-705`, `1110-1124`; `PolarisCycleApiService.cs:247-252`, `521`, `578-592` |
| ID-10 | A PUCK must tokenize against its declaration:<br>• no empty segment and no trailing separator ("PUCK contains an empty segment.", "PUCK cannot terminate with a nesting separator.")<br>• no more segments than declared unless a terminal repetition allows them; fewer are accepted ("PUCK contains more segments than the declaration allows.")<br>• a static discriminator prefixes its segment, compared case-insensitively; a dynamic one is non-digits then digits<br>• a spiritgem or incremental numerator has exactly `width` digits ("…must be exactly {w} numeric characters."); a manual one is digits or empty; a date stamp is a real date | R | Watcher, API, Activation | `PuckTokenizer.cs:68-157`, `189-246`; `PuckEntityResolutionService.cs:86-97`; `VaultFamilyInstantiationResolver.cs:67-84`; `MarkdownFrontMatterSerializer.cs:494-504`; `PuckCreationService.cs:53`; `OnrushSprintApiService.cs:516-522` |
| ID-11 | A filename-derived Index identity is trusted only if it tokenizes against the type's declaration; otherwise it is dropped and the note is identity-less, which an Enforced root purges. Lore's folder-composed ids and Quiet frontmatter ids are not gated. | R | Watcher, Activation, Once | `PuckIdentityGate.cs:15-32`; `VaultMarkdownDiscoveryService.cs:615-630`; `VaultAutoGeneratedPuckConsistencyService.cs:92-98`; `VaultLoader.cs:152-162` |
| ID-12 | An id resolves registry-first, else through every declaration that tokenizes it; more than one existing match is refused: "PUCK '…' resolved to multiple entity types (…)." A filename prefix counts only for Index storage, and a locked file is a non-match. The watcher drops a freeform directive note whose PUCK resolves to a non-directive, and the Freeform and Implicit policies judge an asserted `puck` by its resolved type. | R | API, Watcher | `PuckEntityResolutionService.cs:27-84`, `154-214`; `SystemApiService.cs:155`, `175`; `VaultMarkdownDiscoveryService.cs:675-682`; `FreeformVaultStorageModePolicyService.cs:83-91`; `ImplicitVaultStorageModePolicyService.cs:103-107` |
| ID-13 | Index filenames are `{id} - {title}`, split at the first " - ", so an id can never contain one. Thirteen characters that break the filesystem or Obsidian are written as look-alikes and decoded on read; a look-alike typed by hand comes back as the plain character. The strict parse that throws on a missing separator has no caller. | M | API, Watcher | `PuckNamedIdentity.cs:11-48`, `55-155`; `PuckFileNameCodec.cs:25-89`; `VaultStoragePathComposer.cs:153-167`, `230-239`, `365`; `MarkdownFileLocator.cs:165-216`, `246`; `LorePage.cs:203-206` |
| ID-14 | An executive order's id is `x{sprint number, unpadded}-o{NN}`, counted per sprint from 01, and an order needs a parent sprint (none on the placeholder, ON-10). The watcher checks that the note's folder sprint exists, never that the id's sprint segment names it. | M+R | API, Watcher | `ExecutiveOrder.cs:11-25`; `OnrushSprintApiService.cs:409-416`, `516-522`; `MarkdownFileLocator.cs:223-247`; `VaultMarkdownDiscoveryService.cs:729-744` |
| ID-15 | A Pleiadean date has months 1–6 of 61 days, the sixth 60 or 61 by the Persian leap year: "Month must be between 1 and 6." / "Day must be between 1 and {n}.". A date before the epoch (2023-03-21) cannot be converted: the constructor refuses its day. This bounds Polaris ids and the default Polaris and onrush titles. | R | API, Watcher | `PleiadeanCalendar.cs:10-12`, `37-78`, `131-198`, `219-243`; `PuckDateStampCodec.cs:38-46`; `PlaintorchDefaultTitleFactory.cs:30-34` |

### 12.2 Defaults and statuses
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| DM-01 | A new entity starts in a policy-bearing status unless the writer sets one: stellar directive Planned, objective Standby, fate Active, decree Active, lunar directive On Hold. API creates rely on it, and the watcher keeps it for fields a new note lacks.<br>• Active is a fate's begin, so every fate is begun at birth and records its begin in its creating save, before anything could gate it.<br>• A new decree materialises at once; a new lunar directive pauses its subtree's reflection until it is activated (DM-03). | M | API, Watcher | `StellarDirective.cs:31`; `Objective.cs:55`; `Fate.cs:33`; `Decree.cs:31`; `LunarDirective.cs:32`; `VaultMarkdownDiscoveryService.cs:636-639`; `BacklogScheduleRecorder.cs:74-83` |
| DM-02 | Other construction defaults: occurrence resolutions Pending; objective and decree college Unspecified; tag colour Light; timeframe inclusion None, not exclusive; a ledger row a new id and the UTC instant (schedules and epochs are civil); a lore page's level "Lore". An occurrence's default epoch has granularity 0, which is no unit, at `DateTime.MinValue`: it reads as all-day and ends at its start, and every creator overwrites it. | M | API, Watcher, Hook, Background | `Attentive.cs:37`; `Eventive.cs:49`; `Objective.cs:48`; `Decree.cs:68`; `TagDefinition.cs:29`; `Timeframe.cs:86`, `104`; `CelestronTransaction.cs:15`, `20`; `OrbitScheduleState.cs:38`; `LorePage.cs:68`; `Occurrence.cs:32` |
| DM-03 | A lunar directive's status gates reflection only: a reflect-decree yields reflectives while every lunar directive up its lineage is Active, and On Hold or Stale pauses the subtree without seeking (MA-03a). Nothing else reads it: the directive's timeframes, the availability picked from them, its non-reflect decrees' attentives and its decrees' cached next occurrence ignore it. Its transitions have no rule, by API (DI-04) or frontmatter. | R | API | `LunarDirectiveStatus.cs:8-19`; `PlaintorchOrbitService.cs:292-327`; `ProximityMaterializationService.cs:68-73`; `DirectiveApiService.cs:496-518` |
| DM-04 | An attentive resolves Pending, Done or Skipped, and any may follow any, through the API only (attentives have no notes). Only Done carries meaning (SH-13, SH-14); Skipped is inert. `ResolvedOn` is a UTC instant beside a civil epoch, and the reward is keyed by the RECURRENCE-ID cut to the minute ("attentive {date}[T{HH:mm}]"). | — | API, Hook | `AttentiveResolution.cs:6-20`; `Attentive.cs:34-43`; `DeclarativeApiService.cs:480-483`; `PlaintorchStatePolicyProcessor.cs:806-816`, `1061-1073` |
| DM-05 | An eventive takes its fate's OptOut only at spawn (rolling pass, interaction, projection): a later fate status change restamps nothing, and a manual eventive is always created Pending (DE-13). Any resolution may follow any other, through the API only. OptOut is documented as a reversible hide "not hardened by time passage", yet it reads as finished (LC-02) and hardens when auto-materialise-optout is on. | M | Background, API | `EventiveResolution.cs:22-27`; `Eventive.cs:45-49`; `OccurrenceHardeningService.cs:176`; `ProximityMaterializationService.cs:159-168`; `AgendaProjectionService.cs:94`; `DeclarativeApiService.cs:396`, `437-440` |

### 12.3 Cycles, sprints and orders
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| DM-06 | A planned cycle is titled for its day ("Polaris {d} of {month} {y}") and carries Forecast {reference, "+Nd"}; its id comes from the caller and is not checked against the target. "Forecast" means a forecast and no start (`IsForecast`), restated by the hook's supersession and the forecast listing. Only the next-day move reads "+Nd" back, and any other text reads as today. | M | API | `PolarisCycleLifecycle.cs:19-39`; `PolarisCycle.cs:38-42`; `PlaintorchStatePolicyProcessor.cs:699-701`; `PolarisCycleApiService.cs:237`, `585-591`; `PlaintorchDefaultTitleFactory.cs:22-34` |
| DM-07 | Neither begin nor end looks at the cycle's day: any cycle, a far forecast included, can be begun on any day, so its id day and its start day can differ (ID-09). Finish keeps the forecast that Start drops. | M | API | `PolarisCycleLifecycle.cs:44-76`; `PolarisCycleApiService.cs:55-61`, `77-93`, `838-845` |
| DM-08 | The planning sprint takes the reserved id `x0000`, an onrush PUCK below the sequence seed (100): it passes the identity gate, yet is assigned, never minted or registered. Beginning it re-keys it to the next incremental id (ON-04), not the "final date-based identity" its doc names; the start date passed to the mint is ignored. A legacy placeholder `0` is renamed once (vault v2→v3). | M | API, Once | `OnrushSprint.cs:20-26`; `OnrushSprintApiService.cs:34`, `96-101`; `OnrushPlanningPlaceholderRenameMigration.cs:31-37`, `75` |
| DM-09 | An executive order is in effect on a day inside its window; each unset bound falls back to its sprint's start or end, and an order with no bounds is onrush-bound. "Active" reads the host's today. The values are only served: nothing in the core acts on them. An unloaded sprint leaves its bound open, and an inverted window (frontmatter skips ON-11) is never in effect. | — | API | `ExecutiveOrder.cs:58-83` |

### 12.4 Lore
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| DM-10 | A lore note's frontmatter `puck`, trimmed, replaces the id composed from its path; a blank one is ignored. The path still supplies the parent id, the level and the numbers. | M | Watcher, Activation | `LorePage.cs:30-45`; `VaultAutoGeneratedPuckConsistencyService.cs:163-178` |
| DM-11 | A lore note counts only when it lies under the saga root, is self-named, and every folder up to the root carries an id. Id, parent id, level, path, the era/chapter/act/phase numbers and `IndexedUtc` are then derived from the folder chain; a number comes from the first segment whose name starts with the level's prefix. The watcher nulls a parent id that names no page; the API composes all of this itself (LO-02). | M+R | Watcher, Activation, API | `LorePage.cs:147-243`; `MarkdownFileLocator.cs:252-254`; `VaultWatcherSyncService.cs:147-154`, `270-277` |
| DM-12 | A page's folder and file are named by `overrideIdentifier`, trimmed, when set, else by its id's last segment. The override is never validated, and path reads take it back as the id segment (DM-11). | M | API, Watcher | `LorePage.cs:47-51`, `113-125`; `VaultStoragePathComposer.cs:365`; `LorePageApiService.cs:408-410`; `LorePageSaveHook.cs:39` |
| DM-13 | A page ends (exclusive) at the earliest later beginning among its siblings; a page without a beginning has no end, and siblings that begin on the same day do not end each other. It is the upper bound of D17 (LO-03), which the watcher deliberately leaves alone. | — | API | `LorePage.cs:127-145`; `LorePageApiService.cs:303-324`; `VaultMarkdownDiscoveryService.cs:747-751` |
| DM-14 | The active lore page is the one with the latest beginning on or before today in UTC, the deeper level winning a tie; it and its ancestors form the active spine. A page without a beginning is never active, and the walk stops at a parent id that names no page. One definition serves every surface; the response stamp (LO-08) reads it from a per-session cache that async lore saves drop (SH-27). | — | API | `LoreIndex.cs:34-53`; `LoreActiveCache.cs:26-55`; `LorePage.cs:105-111`, `297-304`; `LorePageApiService.cs:51-57`; `SystemApiService.cs:624-632` |

### 12.5 Occurrences, epochs and zones
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| DM-15 | An occurrence is its owner's id plus its RECURRENCE-ID: the slot it materialised at, which a reschedule never moves (an eventive's owner is its fate, else its objective). An existing identity suppresses re-materialisation in the projection, the rolling pass, interaction and dependency hardening; a manual eventive has none and is never deduplicated.<br>• No unique index backs the identity, and no check constraint gives an eventive exactly one owner.<br>• An objective's eventive is hardened at whatever slot the caller names, never checked against its due. | M+R | Background, API, Hook | `Occurrence.cs:34-61`; `Eventive.cs:41-43`; `Attentive.cs:30-32`; `AgendaProjectionService.cs:181-186`; `ProximityMaterializationService.cs:211-218`, `253-260`, `287-293`; `OccurrenceHardeningService.cs:78-86`, `182-198`, `226-241`; `DependencyEndpoints.cs:163-191`; `PlainfraContext.cs:430-452` |
| DM-16 | An orbit occurrence's epoch takes its date, time and granularity. A span stores its minutes as the duration, a week/month/year occurrence its whole period in days (exact on its own calendar), and every one is stamped with its declarative's zone (DM-22). An occurrence with no orbit occurrence behind it is day-granular at midnight and minute-granular otherwise. A due objective's eventive is always all-day: the due's time and zone are dropped. | M | Background, API, Hook | `Epoch.cs:115-153`; `ProximityMaterializationService.cs:225-226`, `265`, `298`; `OccurrenceHardeningService.cs:91`, `174`, `193`; `AgendaProjectionService.cs:101`, `125`, `152` |
| DM-17 | An epoch ends (exclusive) at its moment plus its nominal duration, or plus one granularity unit when it has none. Months and years add as Gregorian months and years, and an unparseable duration adds nothing, so the occurrence ends at its start. Day-or-coarser is all-day; dues and slots use another test, a midnight time. | — | everywhere | `Epoch.cs:48-62`, `151-153`; `OccurrenceDurations.cs:58-93`; `Due.cs:35` |
| DM-18 | A null or non-positive minute length stores no duration, so the occurrence fills one granularity unit; stored durations use days, hours and minutes only. A time-of-day span whose end is not after its start is no span (DE-11 drops it, DE-02 refuses it at fate creation), and a hand-given duration must be well-formed positive notation (DE-14). | M+R | Background, API | `OccurrenceDurations.cs:15-56`, `95-108` |
| DM-19 | A timed epoch anchored in a zone this host knows names an absolute instant: a skipped wall time takes the offset before the gap, a repeated one its earlier reading. A floating, unknown-zone or all-day epoch names none. An eventive's begin and finish compare this instant with `UtcNow` (LC-02); without one they compare the civil moment as UTC. | — | everywhere | `Epoch.cs:64-76`, `99-113`; `EventiveLifecyclePhaseSource.cs:41-55` |
| DM-20 | On this host's clock an epoch reads as its instant converted to the host zone, else as its civil moment (`LocalMoment`, `LocalDate`, `LocalEndMoment`). The agenda window, overdue attentives, the Polaris agenda and the inclusion window decide on this reading, after widening their database filters by two days (`ReachDays`). | — | API | `Epoch.cs:78-97`; `OccurrenceZones.cs:9-14`; `AgendaProjectionService.cs:46-77`, `159-161`; `PolarisCycleApiService.cs:655-692`; `InclusionWindow.cs:52-77` |
| DM-21 | A zone id this host does not know, like a blank one, reads as floating, silently. The API refuses an unknown zone (DE-15), but a frontmatter `timezone:` is stored unchecked, and a stamped zone is looked up again on every read. | R | everywhere | `OccurrenceZones.cs:16-25`; `Declarative.cs:24-32`; `DeclarativeApiService.cs:507-520` |
| DM-22 | A declarative's zone is the clock its orbit runs on: every occurrence its orbit yields is stamped with it, and seeking, the resume fast-forward and `NextOccurrence` read "now" on that clock. A hardened occurrence keeps its stamp; a zone change neither restamps nor resets the cursor, as the reset watches the orbit only. The anchors still read the host's day: the reset's (SH-05) and the lazy states of the cycle-begin seek and of previews. | M | Background, API, Hook | `PlaintorchOrbitService.cs:54-68`, `145-192`, `245-261`; `PlaintorchStatePolicyProcessor.cs:459-466`, `549`, `567-577`; `OccurrenceHardeningService.cs:91`, `174` |

### 12.6 Orbit schedules
| ID | Rule | Kind | Reach | Where |
|---|---|---|---|---|
| DM-23 | The validators behind DE-05 parse and walk the syntax tree; they never build an engine, so a notation only the engine refuses passes: a malformed span (DM-24) or a bound date the calendar lacks (DM-25). A number too long for an `int` escapes as a raw `OverflowException`, not "Invalid orbit notation…". Frontmatter orbits are never validated, so a reflecting decree can carry a sub-day orbit. | R | API | `OrbitDays.cs:57-132`; `PlaintorchOrbitService.cs:70-117`; `OrbitParser.cs:344-358` |
| DM-24 | Building a span-format engine refuses:<br>• a top-level operand without exactly one duration on its spine ("Span operand is missing a duration…" / "A span operand may carry only one duration.")<br>• span and granular operands mixed ("Cannot mix span and granular sub-expressions…")<br>• a set lifted from under a `*` or `@` node ("A span set operation cannot sit under a node with a * or @ limit…")<br>An engine is first built when the save hook resets the schedule state, so the refusal fails that save, from the API or the watcher. | R | Hook | `OrbitEngine.cs:167-173`, `706-740`, `769-789`, `810-860`; `OrbitDays.cs:268-283` |
| DM-25 | A `<t`/`>t` bound is an ISO timestamp, read as a UTC instant on the engine's civil timeline (an explicit offset shifts it), or a `y/M/d[Th:m[:s]]` date composed in the orbit's calendar; any other token fails to parse. A `y/M/d` date the calendar lacks is refused only when the engine is built ("The bound '…' names a date its calendar does not have."), failing the save; an ISO-shaped date that does not exist (`2026-02-30`) parses and is dropped silently. The latest `>t` is the origin: `%N` phases and top-level lives count from it. | R | API, Hook | `OrbitParser.cs:360-414`; `OrbitEngine.cs:159-164`, `927-951`, `1759-1833` |
| DM-26 | A schedule state is a snapshot anchored at an epoch day, with a seed:<br>• a lone `Z{y/M/d}` anchors at its own day, read on the declarative's resolved calendar (the vault default for a timeframe); a day that calendar lacks falls back to the reset's anchor (SH-05, SH-06)<br>• a missing seed is drawn at random, so every orbit edit draws a new one<br>• a span schedule keeps only notation, epoch and seed plus a hand-kept cursor, and is recognised by a `=` in its notation<br>• a declarative or timeframe without a state gets one on first read, anchored where that read stands (today, the previewed day, or 400 days before a preview window) | M | Hook, Watcher, API, Background | `OrbitDays.cs:134-283`, `371-410`; `OrbitEngine.cs:157`; `PlaintorchOrbitService.cs:123-143`, `334-351`, `369-400`, `471-487`; `PlaintorchStatePolicyProcessor.cs:427-499`; `VaultWatcherSyncService.cs:511-528` |
| DM-27 | Seeking consumes every occurrence before its bound and persists the advanced cursor, so nothing is emitted twice. A stream is exhausted when no candidate remains, a `<t` bound stops it, or 100,000 skips pass, and stays exhausted until the orbit is edited. A resumed snapshot must be version 1 with valid instants ("Unsupported OrbitSnapshot version: …", "Orbit snapshot epoch '…' is not a valid instant."); one carrying legacy counters re-derives its exhaustion. A fate occurrence blocked when sought is consumed without hardening (LC-11). | M+R | Background, API | `OrbitDays.cs:285-317`, `371-390`; `OrbitEngine.cs:222-261`, `867-951`; `ProximityMaterializationService.cs:85`, `169`, `196`, `281-285`; `DeclarativeApiService.cs:149`, `297` |
| DM-28 | The cached `NextOccurrence` is the earliest occurrence at or after "now" on the orbit's clock within a 400-day preview, else null, so a rarer schedule reads as having none. It previews from the epoch, ignoring the cursor and exhaustion. The hook (SH-07) and the rolling pass compute it; the rolling pass skips lunar reflect-decrees, whose value then moves only when they are saved. | M | Hook, Background | `OrbitDays.cs:331-356`; `OrbitEngine.cs:475-499`; `PlaintorchOrbitService.cs:245-261`; `ProximityMaterializationService.cs:174-180`, `189-205` |
| DM-29 | A schedule matches a day when an occurrence's period covers it (date ≤ day < period end), looking back 400 days, so a longer period never matches; previews never advance the state. It decides timeframe candidacy (a malformed orbit reads as not matching) and the interaction slot: "{Kind} '…' has no orbit occurrence on {day}.", an inexact slot snapping to the day's first occurrence. Reflective selection restates the test. Interaction matches the zone's civil date; the agenda files a timed zoned occurrence under its host-local day. | R | API | `OrbitDays.cs:358-369`; `PlaintorchOrbitService.cs:194-243`, `402-496`; `OccurrenceHardeningService.cs:200-220`; `ProximityMaterializationService.cs:86` |

# Issues as of Development Phase 3A
Every problem the audit found in the core, as of `b1697b9`. `dev/phase3a` has since moved to `6e525f2`; its one core change widens a search and touches no rule. This is PEP103's backlog:
- Each time a part of the engine is strapped in, the entries it satisfies are deleted. Its patch lists them under *Ledger*.
- Numbers are never reused or shifted, so references stay valid.
- Whatever is left once the engine is complete is systematic and needs work of its own.

At `b1697b9` the list holds 190 entries, merged from 264 findings of four audits. By the audit's reckoning:
- 75 should clear with the engine.
- 2 should clear in part.
- 112 should remain.
- 1 is already settled elsewhere.

Each entry ends with that expectation. *Engine N* names the point of the engine (below) expected to clear it, *Systematic* names the system it belongs to. The expectation is a forecast: an entry leaves the list only when a step actually satisfies it.

**Markers**
- *(defect, by reading)*: a defect traced through the code, not reproduced.
- *(unverified)*: suspected but not traced.
- Four by-reading defects were checked a second time and hold: I-088, I-089, I-090 and I-016.
- *(new in dev/phase3a)*, *(changed by dev/phase3a)* and *(made worse by dev/phase3a)* show what the rebase did. An entry with none of these stands as it did at `402eb4e`.
- *Rows* point to the inventory above.

## The engine the expectations assume
1. **Rules declared once, on state.** Every state rule is declared once, on entity state and transitions rather than on API operations. That covers:
   - lifecycle phases and transition legality
   - gates (dependency and intrinsic edges)
   - invariants: exclusivity, value ranges, valid references, and structural rules such as the directive family and nesting
   - reactions: bumps and promotions, settlements and rewards, records, releases, purges, cursor resets, and derived fields
2. **Every pathway.** The engine runs at the save boundary for every write pathway: the API, the watcher, background passes and activation passes, sync and async saves alike. Bulk updates and raw SQL against domain tables go through it or are forbidden.
3. **A phased fixpoint.** Reactions run first, then validation of the final state, then effects (ledger rows, records, audit, note intents), all in the same transaction. Evaluation is scoped to what changed.
4. **Typed violations.** Each pathway surfaces a violation its own way:
   - the API as 409 or 422 with the message
   - the watcher by refusing the edit, which the note keeps, and raising an issue that says what blocks it (user ruling, Patch103.2)
   - a background pass as an issue
5. **A clock.** Time-dependent rules (eventive phases, checkpoint dues, cycle and sprint windows) are re-evaluated on a clock trigger, not on the next unrelated save.
6. **Database-backed exclusivity.** Exclusivity and idempotency rest on database constraints or in-transaction checks.
7. **Note intents.** A note-write intent is recorded whenever a note-backed field changes, so every pathway's change reaches its note.

Points 6 and 7 stretch "enforcement" toward persistence. If they are built outside the engine, the entries that expect them move with them.

## State rules

### Rules that skip pathways
A frontmatter edit is saved through the hook only, so it skips every §4 rule on the same field; §5 has the per-kind table (`VaultWatcherSyncService.cs:251-399`; `VaultMarkdownDiscoveryService.cs:684`, `898-935`). Init-from-file, planning and the other non-API writers skip some of them too. Each entry below is one rule that runs on fewer pathways than it should.
- **I-002 · The directive family rule guards one operation.** Only the API reparent checks family and loops. Elsewhere:
  - both create paths accept a parent of the other family
  - an unchanged parent is never re-checked
  - a folder move adopts any containing directive
  - objectives, fates and decrees may sit under either family

  *Engine 1 and 2: a structural invariant on the final state, checked on every pathway.*
  Rows: OB-02, DI-03, DE-01. Where: `DirectiveApiService.cs:108-118`, `181-219`, `469-479`; `VaultMarkdownDiscoveryService.cs:842-857`, `898-933`; `ObjectiveApiService.cs:70-89`, `106-110`; `DeclarativeApiService.cs:628-640`.
- **I-003 · Orbit validation only parses, and only on the API.** *(made worse by dev/phase3a: `y/M/d` bounds add a build-time refusal)* The validators parse without building, and a frontmatter orbit is not validated at all (a span on a decree, a sub-day orbit on a reflecting decree). An orbit that parses but cannot build then throws in SH-05's `CreateState` and fails every save that touches its declarative: a raw 500 on the API, a retried `sync-failed` on the watcher. Such orbits include:
  - span and granular operands mixed (`d=2h + d`)
  - a bound naming a date its calendar lacks (`d>2026/2/30`, new with dev/phase3a's bounds)
  - a number too long for an `int`, which escapes as a raw `OverflowException` instead of "Invalid orbit notation…"

  A lone `Z{…}` naming a date its calendar lacks silently anchors at today and never occurs. *Engine 1 and 2: an "orbit builds on its calendar" invariant on the final state, on every pathway.*
  Rows: SH-05, DI-09, DI-10, DE-05, DM-23, DM-24, DM-25. Where: `PlaintorchOrbitService.cs:70-117`, `139`; `OrbitDays.cs:57-132`, `143-144`, `163`, `268-270`; `OrbitEngine.cs:167-173`, `857-860`, `1801-1833`; `PlaintorchStatePolicyProcessor.cs:498-499`; `OrbitParser.cs:357`; `DeclarativeApiService.cs:72`, `124`, `213`, `291`; `DirectiveApiService.cs:535`, `696`; `VaultWatcherSyncService.cs:269`, `313`.
- **I-005 · Calendar reactions are API-only.** *(changed by dev/phase3a)* Only API create pins a one-off `Z{…}` fate to Gregorian, and only API update rewrites a lone literal when the calendar changes (DE-17). A note-authored one-off reads on the default calendar. A frontmatter `calendar:` edit leaves the literal and the cursor, so the one-off names another day. *Engine 2: reactions on the calendar field fire for every writer.*
  Rows: SH-05, DE-04, DE-17. Where: `DeclarativeApiService.cs:81`, `127-138`, `528-556`, `684-687`; `Declarative.cs:21-22`.
- **I-006 · Time zones are validated in two API places only.** *(new in dev/phase3a)* Fate, decree and manual-epoch zones are checked, through the API only. Frontmatter validation checks types only, so a frontmatter `timezone:` is stored unchecked, and an unknown id then silently reads as floating. Objective and checkpoint `Due` zones are never checked, even through the API. *Engine 1 and 2: a valid-zone invariant on every pathway.*
  Rows: DE-14, DE-15, SM-18. Where: `DeclarativeApiService.cs:140-143`, `285-288`, `511-520`; `OccurrenceZones.cs:17-18`; `Declarative.cs:31-32`; `ObjectiveApiService.cs:127-130`; `DependencyApiService.cs:254-258`; `MarkdownFrontMatterSerializer.cs:411-492`.
- **I-007 · Polaris begin and end rules are API-only.** A frontmatter `startTime:` begins a cycle without dropping the forecast, materialising reflectives, warming candidates or auditing. An `endTime:` on an unstarted cycle is accepted. A `forecast:` edit never reaches the row (I-078). *Engine 1 and 2: transitions and reactions declared on the cycle.*
  Rows: SH-11, PO-03, PO-06. Where: `PolarisCycleApiService.cs:77-93`, `187-198`; `PolarisCycleLifecycle.cs:44-76`; `PolarisCycle.cs:23-36`; `VaultWatcherSyncService.cs:269`.
- **I-008 · Sprint rules live in the begin and plan verbs.** A frontmatter `startDate:`, or the generic update, starts, ends or reopens a sprint with no milestone and no placeholder materialisation. Dating `x0000` leaves a dated placeholder that never counts as active. *Engine 1 and 2: reactions on the sprint's begin transition, on every pathway.*
  Rows: ON-02, ON-04, ON-07. Where: `OnrushSprintApiService.cs:57-75`, `96-142`, `327-354`; `VaultWatcherSyncService.cs:269`, `287-295`.
- **I-009 · Executive-order rules are API-only.** The effective window and the placeholder refusal never apply to a note; discovery checks only that the sprint exists. A frontmatter edit can therefore store an inverted window, which then never reads as in effect. *Engine 1 and 2: order invariants on every pathway.*
  Rows: ON-10, ON-11, DM-09. Where: `OnrushSprintApiService.cs:405`, `409-412`, `480`, `524-530`; `VaultMarkdownDiscoveryService.cs:726-743`; `ExecutiveOrder.cs:58-83`.
- **I-010 · Lore renumbering bypasses every interceptor.** `SetIndexAsync` re-keys the subtree with `ExecuteUpdate` under deferred FKs and writes notes directly. That means:
  - no state policy and no change feed
  - no active-lore cache drop
  - no outbox intent
  - other PUCK-keyed rows keep the old ids

  *Engine 2: bulk updates go through the engine or are forbidden.*
  Rows: SH-27, LO-07. Where: `LorePageApiService.cs:265-291`; `BacklogScheduleInterceptor.cs:24-27`; `LoreActiveCacheInterceptor.cs:25-69`.
- **I-011 · Schedule and checkpoint rules are API-only (latent).** The recorder accepts any tracked schedule (a Failed phase, a hand-added record, a begin plan on a checkpoint) and lets it replace a slot. The reconciler trusts `TollPaid`, `ExternalCondition` and `Due` from any writer. Today only the API writes them. *Engine 1 and 2: invariants on the final state cover any writer.*
  Rows: SH-22, SH-26, DP-13, BS-01, BS-02, BS-03. Where: `BacklogScheduleApiService.cs:47-66`, `86-89`; `BacklogScheduleRecorder.cs:101-149`; `DependencyReconciler.cs:75-85`.
- **I-012 · The lore nesting rule lives in a file hook.** An Era has no parent, a Cha sits under an Era, an Act under a Cha and a p under an Act. That rule is checked only inside a canonical write, when a page's parent changes. No API operation reparents a page, so the API never reaches it; when a watcher canonical rewrite does, the refusal becomes a retried sync failure. *Engine 1 and 2: a structural invariant declared on lore pages and run on every pathway with a typed violation; placing the folder stays a storage job.*
  Rows: SM-30. Where: `LorePageSaveHook.cs:15-20`, `66-92`; `VaultWatcherSyncService.cs:326`, `373`.

### Lifecycle and transitions
- **I-013 · No transition legality.** Any status may follow any other (Done → Standby, Failed → Done, any lunar or decree status), though the GENESIS orders read Planned → Committed → Active → Fulfilled → Over and Standby → [Blocked] → Onboard → Polaris → Done → Archived. A same-status shift rewrites the note and reruns the hook. *Engine 1: the engine declares lifecycle transitions.*
  Rows: LC-09, OB-03, DI-04, DE-07. Where: `ObjectiveApiService.cs:138-154`; `DirectiveApiService.cs:273-297`, `496-518`; `DeclarativeApiService.cs:105-109`, `247-250`; `Orchestration/.GENESIS.md:51-60`.
- **I-015 · Begin and end act on ended sprints and cycles.** *(changed by dev/phase3a)* Re-beginning an ended sprint or cycle rewrites its start, possibly to after its end, and leaves it ended. A re-begun cycle re-runs the PEP104 selection on the new start day, adds reflectives to the ended cycle (which revokes a collected reward) and skips the warm. Re-ending overwrites the end. *Engine 1: declared phase transitions refuse or reopen consistently.*
  Rows: SH-16, ON-03, ON-06, PO-03, PO-06, MA-03. Where: `OnrushSprintApiService.cs:88-94`, `144-147`, `177`; `PolarisCycleApiService.cs:77-93`, `105-112`, `187-198`; `PolarisCycleLifecycle.cs:44-55`; `ProximityMaterializationService.cs:47-55`; `PlaintorchStatePolicyProcessor.cs:1031-1055`.
- **I-016 · Forecast supersession deletes plans it should not.** Any save of the active cycle deletes every unstarted forecast dated on or before the active cycle's id day, with its executives and notes. That catches plans it should not:
  - Begin accepts any cycle on any day, so beginning D+3's forecast today deletes the plans for today, D+1 and D+2 (defect, by reading).
  - Planning accepts any reference date, and next-Polaris from an old cycle plans a past forecast, which the next edit of the active cycle (even a title change) deletes.

  An id that is not a date stamp silently disables supersession. *Engine 1: supersession declared on the begin transition, a begin bound to its cycle's day, and a valid-date invariant.*
  Rows: SH-11, SH-23, PO-01, PO-03, PO-12, ID-09, DM-07. Where: `PolarisCycleApiService.cs:33-46`, `55-61`, `77-93`, `555-572`; `PlaintorchStatePolicyProcessor.cs:661-713`.
- **I-017 · Onrush and Polaris verbs disagree.** The differences:
  - start-new returns the active cycle, but always creates a sprint, which SH-09 refuses while one is active, wasting the minted id
  - end refuses an unstarted cycle but accepts anything for a sprint
  - Polaris has a "current" with a today fallback and auto-start; onrush has neither

  *Engine 1: one lifecycle declaration per container kind.*
  Rows: ON-03, ON-05, ON-06, PO-02, PO-04, PO-06. Where: `PolarisCycleApiService.cs:64-70`, `130-164`, `187-198`; `PolarisCycleLifecycle.cs:60-66`; `OnrushSprintApiService.cs:152-181`.
- **I-018 · "Current cycle" means three things.** *(made worse by dev/phase3a)* The three:
  - `GET /api/polaris/current` takes the running cycle, else today's while not concluded.
  - Begin, end, inclusions, start-new and add-to-current take today's cycle even when ended, so "current" planning lands in a concluded cycle.
  - Active timeframes take only a strictly active cycle.

  *Engine 1: one declared reading of the current cycle.*
  Rows: LC-13, DI-12, PO-02, PO-04, PO-05. Where: `PlaintorchStateService.cs:58-77`; `PolarisCycleApiService.cs:130-153`, `838-854`; `DirectiveApiService.cs:579-591`.
- **I-021 · Ended sprints and cycles keep accepting work.** Ended sprints take orders, joins, assign-all and new checkpoints. Ended cycles accept new executives by explicit id, and their executives and reflectives stay executable, movable and removable, re-settling rewards after the fact. *Engine 1: phase-dependent legality declared on cycles and sprints.*
  Rows: OB-04, DI-08, ON-09, ON-10, PO-05, PO-11, PO-12, PO-13, PO-16. Where: `OnrushSprintApiService.cs:370-388`, `407-412`; `ObjectiveApiService.cs:165-172`; `DirectiveApiService.cs:306-311`; `DependencyApiService.cs:189-193`; `PolarisCycleApiService.cs:425-511`, `514-549`, `595-612`, `801-831`, `860-866`.
- **I-022 · "Active" ignores the calendar.** A sprint or cycle is active as soon as it has a start and no end. So a future start already blocks other sprints and doubles settlements, and a future end date ends a sprint at once. *Engine 1 and 5: window rules re-evaluated on a clock.*
  Rows: SH-09, SH-10, SH-12, LC-13, ON-03, ON-06. Where: `PlaintorchStatePolicyProcessor.cs:1095-1103`; `PlaintorchStateService.cs:29-39`, `58-64`; `OnrushSprintApiService.cs:94`, `177`; `PolarisCycleApiService.cs:60`, `193`.
- **I-023 · Initial states are policy in property initialisers.** *(changed by dev/phase3a: On Hold now pauses reflection)* A fate is begun at birth and records its begin before anything could gate it, a decree materialises at once, and a lunar directive starts On Hold and so pauses reflection under it; the lifecycle declarations never see these choices. *Engine 1: initial states declared with the lifecycle.*
  Rows: LC-01, DM-01. Where: `Fate.cs:33`; `Decree.cs:31`; `LunarDirective.cs:32`; `StellarDirective.cs:31`; `Objective.cs:55`.
- **I-024 · Lunar status means more on paper than in code.** *(changed by dev/phase3a: reflection pause added)* On Hold is documented as "not currently enforced", but only reflection reads it. The directive's timeframes and availability, its non-reflect decrees' attentives and a paused reflect-decree's cached next occurrence (SH-07) carry on, and nothing governs its transitions. What On Hold should suspend is a spec decision. *Engine 1: the engine declares once what On Hold suspends, once the spec decides.*
  Rows: SH-07, DI-04, DM-03. Where: `LunarDirectiveStatus.cs:8-19`; `PlaintorchOrbitService.cs:298-327`; `PlaintorchStatePolicyProcessor.cs:560-563`; `DirectiveApiService.cs:496-518`; `VaultWatcherSyncService.cs:269`.

### Schedules and occurrences
- **I-027 · A zone change leaves occurrences and the cursor on the old zone.** *(new in dev/phase3a)* Changing a fate's or decree's zone neither restamps hardened occurrences nor resets the cursor, because SH-05 watches only the orbit. `NextOccurrence`, meanwhile, is recomputed on the new zone's clock. *Engine 1: a cursor reaction on the zone field; whether to restamp is a time-model choice.*
  Rows: SH-05, SH-07, DE-16. Where: `DeclarativeApiService.cs:140-143`, `285-288`; `PlaintorchStatePolicyProcessor.cs:567-577`; `PlaintorchOrbitService.cs:60-68`, `166-176`.
- **I-028 · Occurrence refusals depend on the address.** *(changed by dev/phase3a)* By owner and slot, a cancelled or frozen fate's already-hardened eventive cannot be edited, because the refusals run before the existing-row lookup. By row id it can, and a manual eventive always can. Abandoning a decree freezes its existing attentives, so they cannot be resolved late. *Engine 1: refusals declared on occurrence state, not on the call.*
  Rows: LC-11, DE-09, DE-10, DE-13. Where: `DeclarativeApiService.cs:612-626`; `OccurrenceHardeningService.cs:65-86`, `146-169`.
- **I-029 · Resolutions have no transition rules.** Attentive and eventive resolutions take any value from any value, through the API only, and a fate's status change never restamps the eventives it already has. *Engine 1: transition legality for resolutions.*
  Rows: SH-21, LC-02, DM-04, DM-05. Where: `AttentiveResolution.cs:6-20`; `EventiveResolution.cs:7-28`; `DeclarativeApiService.cs:437-440`, `480-483`; `EventiveLifecyclePhaseSource.cs:30-39`.

### Gates and dependencies
- **I-031 · Fulfilled with open children is both refused and created.** The gate refuses entering Fulfilled while a child is open. Yet reopening an Over directive that receives open work (SH-20, a compensation since Patch103.2) creates exactly that state, and a child created or moved under a Fulfilled parent is accepted with neither reopening nor refusal. *Engine 1: one declared invariant for Fulfilled.*
  Rows: SH-20, LC-10. Where: `DependencyGateService.cs:37-41`, `82-91`; `PlaintorchStatePolicyProcessor.cs:194-199`, `244`, `274-277`.
- **I-033 · Satisfaction can be withdrawn after dependants move on.** Two triggers make edges unsatisfied after the fact:
  - a source is un-finished
  - a toll-free checkpoint re-locks at the first save after its due, which also withdraws its finish record

  Dependants that already began or finished are neither reverted nor flagged, and un-begin and un-finish shifts are never gated. *Engine 1: a declared reaction for withdrawn gates.*
  Rows: SH-21, SH-22, SH-25, LC-03, LC-09. Where: `DependencyReconciler.cs:55-60`, `78-84`; `DependencyGateService.cs:20-35`; `BacklogScheduleRecorder.cs:84-87`; `CheckpointLifecyclePhaseSource.cs:17-26`.
- **I-034 · Dependency loops through a checkpoint are accepted.** The ordering walk treats a checkpoint as a dead end, so `X.finish → checkpoint → X.begin` is not refused, and it deadlocks X. *Engine 1: edge legality declared over checkpoints too.*
  Rows: LC-14, DP-07. Where: `DependencyOrdering.cs:45-51`, `86-88`.
- **I-035 · An eventive edge can name a slot that never resolves.** The service requires a slot but never checks it against the owner's orbit. When the slot is inexact, SH-17 hardens the day's first occurrence while the edge keeps the requested slot, and endpoint matching is exact. SH-17 also swallows a cancelled fate or an off-orbit day. Either way the edge can never be satisfied. *Engine 1: a valid-reference invariant on edges refuses it up front.*
  Rows: SH-17, LC-07, DP-02. Where: `DependencyApiService.cs:387-400`; `OccurrenceHardeningService.cs:105-144`, `156-157`, `219`; `DependencyEndpoints.cs:182-191`.
- **I-036 · A milestone counts as finished from creation.** A milestone has no edges, toll or condition, so the save that creates it unlocks it and records its finish. The sprint reads as complete from day one, and anything gated on the milestone is released. *Engine 1: the milestone's unlock declared on its sprint's completion.*
  Rows: SH-22, SH-25, LC-03, ON-02. Where: `OnrushSprintApiService.cs:47-75`; `DependencyReconciler.cs:75-85`; `BacklogScheduleRecorder.cs:76-83`.
- **I-037 · The condition switch and update disagree.** The switch refuses a checkpoint with no external condition. The generic update sets or clears it freely, including requiring and meeting it in one call. *Engine 1: one transition rule for the condition.*
  Rows: DP-10. Where: `DependencyApiService.cs:249-252`, `344-353`.
- **I-038 · The lock view mis-reports.** `GetLockAsync` has three flaws:
  - it matches `TargetId` without `TargetKind`, so an occurrence lock shows as its fate's or objective's lock
  - it counts a checkpoint's incoming edges as `blockedBegin`, though a checkpoint never begins
  - it sets its flags without regard to whether each edge gates the next transition

  *Engine 1: a lock view read from the declared gates.*
  Rows: LC-08, LC-12, DP-05. Where: `DependencyApiService.cs:163-178`.
- **I-039 · A raw delete leaves dangling edges.** Since Patch103.2 one release reaction covers every endpoint kind, a sprint's milestone included, on every save. But endpoints have no foreign key, so a delete by raw SQL still leaves dangling edges that the reconciler reads as unsatisfied forever. *Systematic: schema, or bulk writes forbidden on domain tables (engine point 2).*
  Rows: SH-18, ON-08, DP-12, DB-01. Where: `OnrushSprintApiService.cs:207-216`; `PlaintorchStatePolicyProcessor.cs:73-103`; `DependencyReconciler.cs:87-91`; `DependencyApiService.cs:285-292`; `PlainfraContextModelSnapshot.cs:149-196`.
- **I-191 · An edge can hang off a state its kind never reaches.** *(new in Patch103.1)* Since fates stopped finishing, an edge from a fate is never satisfied: a null trigger means OnFinish, and nothing checks an edge's trigger or constraint against the states its endpoints pass through, neither when it is created nor for the edges already stored. Its target stays gated for good. *Engine 1: edge sides declared against the states each kind passes through, with defaults that fit the kind.*
  Rows: LC-08, DP-05. Where: `DependencyApiService.cs:33-62`; `DependencyRules.cs:43-51`, `62-74`; `FateStatus.cs:9-27`.

### Rewards and settlements
- **I-040 · Failed still settles.** `IsSettlementStatus` includes Failed. Failing an objective therefore books its Celestron value (doubled in the active onrush), though the lifecycle declares Failed neither begun nor finished. *Engine 1: a settlement reaction declared on the finish phase.*
  Rows: SH-12, LC-01. Where: `PlaintorchStatePolicyProcessor.cs:735-765`, `1105-1108`; `ObjectiveStatus.cs:45-46`.
- **I-041 · Moving or reassigning an executed decree executive double-pays.** The reward is keyed by the executive's current decree and cycle and is touched only when `Executed` flips. A move or reassign therefore leaves the row under the old key; un-executing then revokes nothing, and re-executing grants a second reward. *Engine 1: a reward reaction keyed on record state and re-evaluated on every change.*
  Rows: SH-15, PO-11, PO-12. Where: `PlaintorchStatePolicyProcessor.cs:888-893`, `908-911`; `PolarisCycleApiService.cs:438-459`, `532-538`.
- **I-042 · Removed work keeps its rewards.** *(made worse by dev/phase3a)* SH-12, SH-14, SH-15 and SH-29 read only added and modified rows; only SH-16 reads deletes. So these keep settlement, attentive, decree-executive and per-reflective rewards in the ledger:
  - removing an executed executive
  - the cascades of the forecast delete and of supersession
  - SH-01 and SH-02 removals
  - an objective delete

  Removing work can even earn a reward: when a decree is deleted, SH-02 removes its unexecuted reflective in a live cycle, and SH-16 then grants the cycle's collection reward from the reflectives left. *Engine 1: reward existence declared on record state sees deletes too.*
  Rows: SH-01, SH-02, SH-11, SH-12, SH-14, SH-15, SH-16, SH-29, OB-05, PO-13, PO-17. Where: `PlaintorchStatePolicyProcessor.cs:318`, `322-337`, `707-710`, `717-719`, `786-788`, `872-874`, `940-942`, `1001-1048`; `PolarisCycleApiService.cs:296-297`, `603`; `ObjectiveApiService.cs:213-216`.
- **I-043 · Zero amounts follow no single rule.** *(made worse by dev/phase3a: SH-29 repeats SH-15)* SH-12 books a 0-amount settlement row, while SH-14, SH-15 and SH-29 skip a 0 reward and SH-16 skips one of 0 or less. Because SH-15 and SH-29 read the decree's current `ActiveCelestron`, a reward lowered to 0 can no longer be revoked: un-executing leaves the earlier grant in place, where SH-14 revokes regardless. A toll of 0 still blocks until a 0 payment is made. *Engine 1: one declared amount rule; a reward row exists exactly while its work is executed, whatever the current amount.*
  Rows: SH-12, SH-14, SH-15, SH-16, SH-22, SH-29, DP-13. Where: `PlaintorchStatePolicyProcessor.cs:759-764`, `828-859`, `897-906`, `958-967`, `1039`; `DependencyReconciler.cs:82`; `DependencyApiService.cs:310-333`.
- **I-044 · Attentive rewards are keyed to the minute.** The ledger key cuts the RECURRENCE-ID to minutes, so two attentives of one decree in the same minute (a seconds orbit passes the granular validator) share one reward: the second Done pays nothing, and un-doing either revokes it (defect, by reading). *Engine 1: the reward reaction, declared once, keys each grant to its occurrence.*
  Rows: SH-14, DM-04. Where: `PlaintorchStatePolicyProcessor.cs:776-781`, `823-826`, `1061-1073`; `OrbitDays.cs:66-78`.
- **I-045 · A paid toll follows no single rule.** The cases:
  - clearing a paid toll resets `TollPaid`, with no refund
  - changing its amount keeps it paid, with no top-up or refund
  - deleting the checkpoint never refunds
  - paying inside the due window debits a toll not yet owed

  *Engine 1: settlement reactions declared on the toll.*
  Rows: SH-22, DP-09, DP-12, DP-13. Where: `DependencyApiService.cs:230-247`, `272-301`, `315-333`; `DependencyReconciler.cs:81-82`.
- **I-046 · Toll payment races.** The balance read, the check, the debit and the `TollPaid` flip are separate statements outside a transaction. Two concurrent payments of one toll both debit, and payments of two tolls can overdraw. *Engine 6: in-transaction checks.*
  Rows: DP-13. Where: `DependencyApiService.cs:315-333`; `PlaintorchStateService.cs:82-90`.

### Invariants and references
- **I-047 · Missing references have three outcomes.** The API refuses a missing directive, sprint or parent, with checks copied across five services. The watcher drops it from the row with only a log line, while the note keeps naming it and no issue shows. Paths that check nothing (objective update, FromObjective planning, executive reassign, objectives created by planning) fail at the foreign key with a raw error. *Engine 1 and 4: reference validity declared once; a note that breaks it is reverted with an issue.*
  Rows: OB-02, PO-07, PO-11, DE-01, LO-01, WA-23. Where: `DeclarativeApiService.cs:628-640`; `ObjectiveApiService.cs:73-86`, `106-115`; `DirectiveApiService.cs:111-115`, `197-200`, `306-309`; `OnrushSprintApiService.cs:373-377`; `DependencyApiService.cs:189-193`; `LorePageApiService.cs:68-72`; `PolarisCycleApiService.cs:381-384`, `438-442`, `872-886`; `VaultWatcherSyncService.cs:147-169`, `270-285`, `305-312`, `411-494`.
- **I-048 · Executives can be reassigned to any incentive.** An executive works at an objective or a decree, but reassignment accepts any incentive id: a fate, or an Abandoned decree, though the hook's comment says a fate is never worked. *Engine 1: a reference-kind invariant.*
  Rows: PO-11, PO-14. Where: `PolarisCycleApiService.cs:436-442`; `PlaintorchStatePolicyProcessor.cs:286-287`; `Executive.cs:30-37`; `Incentive.cs:30-35`.
- **I-049 · `reflect` outside Moonlight is accepted and inert.** Any decree accepts `reflect`, which the docs call "only meaningful" inside a Moonlight hierarchy; outside one it only tightens the API's orbit check to day granularity. *Settled: ruled on 2026-10-04 that every hierarchy may reflect. The change is being made on `claude/lunar-reflection-stellar-hierarchy-0a86cd` and has not reached dev/phase3a.*
  Rows: DE-05, DM-03. Where: `Decree.cs:54-62`; `PlaintorchOrbitService.cs:88-91`; `ProximityMaterializationService.cs:56-73`, `191`; `DeclarativeApiService.cs:628-640`.
- **I-050 · End before start has four answers.** The answers:
  - Fate creation refuses it, even when an explicit orbit discards the window, but accepts start == end as a spanless one-off.
  - The order window refuses it.
  - An eventive reschedule silently drops an end that is not after the start.
  - Sprints and cycles never check: end, generic update, re-end.

  *Engine 1: one declared range invariant.*
  Rows: ON-06, ON-07, ON-11, PO-06, DE-02, DE-11, DM-07, DM-18. Where: `DeclarativeApiService.cs:65`, `432-435`, `660-676`, `704-715`; `OccurrenceDurations.cs:51-56`; `OnrushSprintApiService.cs:170-181`, `341-349`, `524-530`; `PolarisCycleLifecycle.cs:44-76`.
- **I-051 · Entity value ranges are missing.** Missing ranges:
  - decree `DefaultLength` and `ActiveCelestron`, and objective `CelestronValue`, take any sign, so negative rewards and settlements book negative rows, the per-reflective reward included
  - allocations accept inverted bounds and negatives, and `Elapsed` is never clamped
  - timeframe start and end, and plan order and dates, are unchecked
  - a timeframe stores any `media:` key, though its icon has nowhere to resolve one, and an update re-applies supplied colleges after a mode change, though colleges are ignored for other inclusion kinds
  - the one range that exists (toll ≥ 0) is written twice

  *Engine 1: value-range invariants.*
  Rows: SH-12, SH-14, SH-15, SH-29, DI-09, DI-10, PO-10, DP-08, BS-03, BS-05. Where: `DeclarativeApiService.cs:208-224`, `265-273`; `ObjectiveApiService.cs:122-125`; `TimeAllocations.cs:35-54`; `PolarisCycleApiService.cs:477-480`; `DirectiveApiService.cs:536-547`, `683-691`, `700-721`; `BacklogScheduleApiService.cs:39-70`; `DependencyApiService.cs:184-187`, `234-237`; `Timeframe.cs:56-64`, `88-96`.
- **I-052 · D17 is checked in one direction only.** A page's beginning is checked against its parent on create and update. Children are never re-checked when:
  - a parent's beginning moves or is cleared
  - a new or moved sibling shortens the parent's span

  *Engine 1: an invariant on the final state of every page affected.*
  Rows: LO-03. Where: `LorePageApiService.cs:74`, `126-133`, `303-324`; `LorePage.cs:130-145`.
- **I-053 · Tags are never checked against the registry.** `TagDefinition` is the place for valid tags, but directive updates and frontmatter store any string; whether to enforce it is still open (`.DISCUSSION.md:175-181`). *Engine 1: a valid-reference rule, once the spec decides whether tags are enforced.*
  Where: `DirectiveApiService.cs:156-159`, `257-260`; `Orchestration/.GENESIS.md:45-49`.
- **I-054 · "Every sprint has a milestone" holds only on the happy path.** The milestone is minted and saved after the sprint, so a failed mint (a checkpoint id clash) leaves the sprint without one, and nothing restores it. *Engine 3: the milestone becomes a reaction inside the sprint's own transaction.*
  Rows: ON-02, ID-01. Where: `OnrushSprint.cs:64-68`; `OnrushSprintApiService.cs:40-42`, `57-75`.
- **I-055 · Onboard status and sprint membership drift apart.** Onboard means "on board an onrush sprint". Since Patch103.3 the engine brings an objective on board when it joins the active sprint, or when its sprint begins, on every pathway, and leaving moves nothing back (user ruling). So the two still drift:
  - A shift or a frontmatter `status:` sets Onboard with no sprint.
  - An objective that leaves keeps Onboard, so assign-all later pulls it into the sprint that calls it, wherever it sits: even out of the active sprint, when assign-all is called on the planning one.

  *Engine 1: whether Onboard needs a sprint is a spec question; leaving stays free of moves (user ruling, Patch103.3).*
  Rows: OB-02, OB-03, OB-04, ON-04, ON-08, ON-09, PO-07. Where: `ObjectiveApiService.cs:70-89`, `112-115`, `171-194`; `OnrushSprintApiService.cs:107-110`, `218-229`, `379-386`; `PolarisCycleApiService.cs:381-384`; `VaultWatcherSyncService.cs:269`; `ObjectiveStatus.cs:22-25`.

### One rule, several implementations
- **I-056 · Cycle and sprint phases are hand-written everywhere.** *(made worse by dev/phase3a)* Cycles and sprints have no lifecycle source:
  - "active Polaris" is written ten times
  - "active onrush" five times, with two orderings
  - ended, not-begun, forecast and today's-cycle readings are further copies; phase3a added "not begun" for delete and "today unless concluded"

  *Engine 1: cycle and sprint phases declared and read once.*
  Rows: SH-01, SH-02, SH-09, SH-10, SH-11, LC-13, DI-12, PO-02, PO-17. Where: `PlaintorchStatePolicyProcessor.cs:311`, `328`, `521`, `649-652`, `684-686`, `700`, `1089`, `1095-1103`; `PlaintorchStateService.cs:33-35`, `62`, `73-76`; `PolarisCycleApiService.cs:79`, `109`, `135`, `141-153`, `237`, `288`, `846`, `852-853`; `OnrushSprintApiService.cs:88`; `DirectiveApiService.cs:585`; `PlaintorchEngine.cs:90`; `ProximityMaterializationService.cs:50`.
- **I-057 · Status knowledge is hardcoded past the lifecycle.** *(made worse by dev/phase3a)* These rules name statuses directly instead of reading the lifecycle declarations:
  - the objective "closed" set (Done, Archived, Failed), for settlement and again for due hardening
  - the generating statuses (fate Active and, by preference, OptOut; decree Active; objectives not closed; lunar Active up the lineage), written into `NextOccurrence`, resume, hardening, the rolling pass, the projection, reflective selection, decree executives and manual eventives. A non-reflect decree under an On Hold lunar directive keeps generating attentives (unverified whether intended)
  - Over in the Over rules (SH-20); the Fulfilled that reopening sets now comes from the default translation
  - Onboard and Polaris as the targets of the moves on joining, and Onboard for assign-all (which statuses move is read by state since Patch103.3)
  - attentive Done
  - the unlock formula: incoming edges, toll or due, and the external condition, combined by hand

  *Engine 1: declared phases, read once.*
  Rows: SH-07, SH-12, SH-13, SH-14, SH-20, SH-22, OB-04, ON-09, PO-14, DE-08, DE-09, DE-10, DE-13, MA-01, MA-03a, MA-05, MA-06. Where: `PlaintorchStatePolicyProcessor.cs:233`, `244`, `554-563`, `800-805`, `1105-1108`; `ProximityMaterializationService.cs:58`, `160-168`, `185-194`, `211-219`; `DeclarativeApiService.cs:147`, `295`, `379`; `OccurrenceHardeningService.cs:71-74`, `151-154`, `176`; `PolarisCycleApiService.cs:711`; `PlaintorchOrbitService.cs:298-327`; `AgendaProjectionService.cs:90`, `113-117`, `137-144`; `ObjectiveApiService.cs:191-194`; `OnrushSprintApiService.cs:380`; `DependencyReconciler.cs:75-85`.
- **I-058 · Default edge sides are restated five times.** Null trigger means OnFinish, null constraint means ToBegin, and checkpoint sides are empty. Each consumer re-derives this by hand, and Patch103.2's judge is one more (`StateJudge`'s needed state and default constraint), in place of the gate check it took over. *Engine 1: declared once on the edge.*
  Rows: LC-08. Where: `DependencyRules.cs:62-74`; `DependencyReconciler.cs:58`; `DependencyGateService.cs:64-67`, `121-123`; `DependencyApiService.cs:175-176`; `DependencyOrdering.cs:249-262`.
- **I-059 · Delete restriction is decided in four places.** Four mechanisms each decide part of whether a delete may go:
  - the database's restricting foreign keys
  - the watcher's probe, which counts restricting keys with raw SQL and skips a hand-kept exemption list (`ReleasedOnDelete`) mirroring the async-only hook releases
  - per-API checks: directive children (DI-05), lore children (LO-05), the milestone (DP-11)
  - the hook's releases (SH-01, SH-02)

  EF nulls only the dependents a caller loaded, so the outcome depends on the Include, and the probe sees stored rows only. *Engine 1: one declared delete rule per relationship.*
  Rows: SH-01, SH-02, SH-18, DI-05, DP-11, LO-05, WA-14, WA-28, DB-03, DB-09, DB-17. Where: `VaultEntityGateway.cs:70-134`; `DirectiveApiService.cs:352-355`; `LorePageApiService.cs:163-167`; `DependencyApiService.cs:280-283`; `PlaintorchStatePolicyProcessor.cs:73-103`, `294-338`; `VaultWatcherSyncService.cs:690-718`.
- **I-060 · The orbit-state reset runs twice.** *(changed by dev/phase3a: zones and calendar-aware one-offs added; the reset still ignores both)* On an `orbit:` change the watcher removes a fate's or decree's schedule state in its own unit of work, and SH-05 removes and re-adds it in the same save; the watcher's doc ("lazily rebuilt on the next seeking resolution") is stale. Neither reset reacts to `calendar:` (I-005) or `timezone:` (I-027). *Engine 1: one cursor reaction at the save boundary.*
  Rows: SH-05, DE-17, WA-24, MA-08, MA-09. Where: `VaultWatcherSyncService.cs:310-314`, `507-528`; `PlaintorchStatePolicyProcessor.cs:427-489`, `567-577`.
- **I-061 · NextOccurrence has two implementations that disagree.** *(changed by dev/phase3a: both now read the zone clock)* SH-07 refreshes every fate or decree in a save. The hourly pass refreshes only what it seeks. An OptOut fate (without the auto-materialise preference) and a lunar reflect-decree therefore keep a cached value that never rolls forward between saves. *Engine 1 and 5: one derived-field reaction, re-evaluated on a clock.*
  Rows: SH-07, DE-16, MA-01, MA-09. Where: `PlaintorchStatePolicyProcessor.cs:533-565`; `ProximityMaterializationService.cs:160-205`.
- **I-062 · Contradicted edges are handled three ways.** They are refused at creation, purged on save, and purged again by a migration that has its own copy of the planning. *Engine 1: one edge-legality rule with one reaction.*
  Rows: SH-19, LC-14, LC-15, DP-07. Where: `DependencyApiService.cs:40-47`; `PlaintorchStatePolicyProcessor.cs:115-178`; `IntrinsicDependencyPurgeMigration.cs:41-58`.

### Evaluation and effects
- **I-063 · One fixed pass, no fixpoint.** *(changed by dev/phase3a)* Since Patch103.2 the engine runs in phases and its judgement runs to a fixpoint, each compensation judged in turn. The reactions within and across the phases still run once in a fixed order and never see later reactions' changes:
  - SH-03 runs before SH-11's removals, so forecasts get no BoundaryEnd (harmless while cycles are Enforced)
  - SH-02's removals feed SH-16
  - SH-17 hardens before SH-19 purges

  Phase3a slotted SH-29 into the order. *Engine 3: evaluation to a fixpoint.*
  Rows: SH-01 to SH-22, SH-29. Where: `PlaintorchStatePolicyProcessor.cs:40-65`.
- **I-064 · Swapping the active sprint or cycle across two notes depends on their order.** Since Patch103.2 the single-active checks read the sprints and cycles as the save leaves them, so one save may end A and start B. On the watcher, ending A and starting B in two notes still depends on candidate order: started first, B is refused and waits for its note or a sweep (I-193). *Systematic: watcher ordering.*
  Rows: SH-09, SH-10. Where: `PlaintorchStatePolicyProcessor.cs:646-658`, `681-692`.
- **I-065 · Prior state is read two ways.** *(changed by dev/phase3a)* SH-12, SH-13/14, SH-15, SH-29 and SH-20 read `OriginalValues`; SH-25 reads the stored row. An entity attached rather than loaded would be misread by the former (latent). *Engine 3: one evaluation model.*
  Rows: SH-12, SH-13, SH-14, SH-15, SH-20, SH-25, SH-29. Where: `PlaintorchStatePolicyProcessor.cs:279-282`, `731-733`, `800-802`, `888-889`, `951-952`; `BacklogScheduleRecorder.cs:71-75`.
- **I-066 · Every save runs the whole policy over the whole graph.** The policy runs on infrastructure saves too: audit writes, outbox clears, diagnostics flushes, preference and dismissal writes, and an empty materialisation save. These and the two or three saves of each API call each reload every edge and checkpoint. The endpoint resolver also pulls matched entities and their eventives into the tracker. Any status change re-derives and re-plans the whole graph round by round, and may purge contradicted or redundant edges unrelated to the save. *Engine 3: evaluation scoped to what changed.*
  Rows: SH-19, SH-21, SH-22, LC-15. Where: `VaultAuditLogService.cs:38`; `VaultWriteQueue.cs:45`, `217-225`; `OperationStatusEventBuffer.cs:44`; `UserPreferenceService.cs:72`, `85`; `ProximityMaterializationService.cs:122`; `DependencyReconciler.cs:26`, `34`; `DependencyEndpoints.cs:108-111`, `134-136`; `PlaintorchStatePolicyProcessor.cs:130`, `156-161`, `184-192`; `DependencyOrdering.cs:152-190`; `DependencyGateService.cs:89`.
- **I-067 · Exclusivity and idempotency are check-then-act.** *(made worse by dev/phase3a)* Every check runs in `SavingChanges`, before the write transaction, with no database backstop:
  - one active sprint, one active cycle, one placeholder
  - ledger idempotency: no unique key on source and description
  - one reflective per decree per cycle: no unique index on `(PolarisCycleId, DecreeId)`, though SH-29's key relies on it, so a begin racing an auto-start can duplicate a reflective
  - edge uniqueness: the index includes the nullable slot, trigger and constraint columns, so a NULL defeats it and the API's check on resolved values (DP-04) is the only dedupe

  *Engine 6: database constraints or in-transaction checks.*
  Rows: SH-08, SH-09, SH-10, SH-12, SH-14, SH-15, SH-16, SH-29, DP-04, DB-01, MA-03c. Where: `PlaintorchStatePolicyProcessor.cs:604-692`, `743-753`, `824-833`, `909-924`, `970-984`, `1033-1047`; `ProximityMaterializationService.cs:60-64`, `101-120`; `PlainfraContext.cs:334-356`; `PlainfraContextModelSnapshot.cs:149-196`.
- **I-068 · Occurrence identity has no database backstop.** *(changed by dev/phase3a: manual eventives carry no RECURRENCE-ID)* One row per owner and RECURRENCE-ID is check-then-insert in four places (the projection, the rolling pass, interaction and dependency hardening), with no unique index on attentives or eventives, and no CHECK gives an eventive exactly one owner. The RECURRENCE-ID's immutability rests on a public setter no code happens to call. Since dev/phase3a a manual eventive has no RECURRENCE-ID at all, and the ManualEventives Down can pin one onto a slot an orbit-born row already holds. *Engine 6: declared invariants backed by database constraints.*
  Rows: DB-04, DB-15, MG-21, DM-15. Where: `PlainfraContextModelSnapshot.cs:43-49`, `266-272`; `20260930141546_ManualEventives.cs:35-38`; `Occurrence.cs:34-47`; `ProximityMaterializationService.cs:211-218`, `253-260`, `287-293`; `OccurrenceHardeningService.cs:78-86`, `182-198`, `226-241`; `PlainfraContext.cs:414-452`.
- **I-069 · Store cascades and set-nulls go unseen.** *(changed by dev/phase3a)* For rows the context has not loaded, these all happen inside SQLite: eventive, attentive, schedule and orbit-state cascades, a cycle's executives and reflectives, a lunar directive's timeframes, and timeframe affinity clears. The change feed, the graveyard, the audit and the backlog records never see them; only availability is cleared first (SH-04). A begun cycle deleted outside the API would take its worked executives with it; since dev/phase3a, the API refuses that delete (PO-17). *Engine 1 and 3: declared reactions turn store cascades into tracked removals and releases, with their effects.*
  Rows: DB-02, DB-04, DB-05, DB-06, DB-07. Where: `PlainfraContext.cs:444-478`; `PlainfraContextModelSnapshot.cs:1118-1122`, `1286-1290`, `1365-1369`, `1380-1384`.
- **I-070 · Time-driven state waits for an unrelated save.** Eventive phases, checkpoint dues and re-locks, and the `NextOccurrence` of declaratives the hourly pass skips are persisted only by the next save of anything, a diagnostics flush included. The gate reads persisted `Satisfied` flags while deriving intrinsic edges live. Records carry the time of whichever save noticed the change. A checkpoint whose due passes is Deferred (Patch103.1), yet no save records it: the recorder compares the stored and the current state read at the same moment, so a change made by time alone is never seen. *Engine 5: time-dependent rules re-evaluated on a clock.*
  Rows: SH-07, SH-21, SH-22, SH-25, LC-02, LC-09, LC-11, LC-12. Where: `DependencyReconciler.cs:22-97`; `DependencyGateService.cs:56-75`, `82-91`, `99-127`; `EventiveLifecyclePhaseSource.cs:23-39`; `RollingMaterializationService.cs:19`; `BacklogScheduleRecorder.cs:47`.
- **I-071 · Post-commit work escapes the transaction.** SH-23 and SH-24 run after the commit, on synchronous saves too since Patch103.2. A failure there throws for a save that already committed; for example, an IO error deleting a forecast aborts the begin before reflectives are created. Forecasts are handled first, so such a throw also skips the note drains. Since Patch103.2 the notes of the items the engine moved are safe, their reconcile intents recorded inside the transaction for the startup drain to recover; the forecast deletions are not (I-075). *Engine 3 and 7: effects and note intents inside the transaction.*
  Rows: SH-23, SH-24. Where: `PlaintorchStatePolicyInterceptor.cs:33-54`; `PlaintorchStatePolicyFileSyncService.cs:20-55`; `PolarisCycleApiService.cs:87-88`.
- **I-072 · Orbit build failures on the watcher are retried forever.** Since Patch103.2 a note edit the state engine refuses stands as a typed `state-refused` issue, not retried (I-193). An orbit that fails to build in SH-05 is no state refusal: it becomes a Critical `sync-failed`, re-run every ≤ 60 s for good. The note keeps the refused value and the row the old one, no domain issue says why, and each retry adds a `watcher/candidate-discovered` audit row. *Engine 1 and 4: an "orbit builds" invariant (I-003) refused as a typed violation like any other.*
  Rows: SH-05, SH-08, SH-09, SH-10, WA-10, WA-14. Where: `VaultWatcherReconciler.cs:242-258`; `VaultWatcherSyncService.cs:53-60`; `WatcherStatusReporter.cs:309-339`; `WatcherRetryScheduler.cs:36-46`, `95-108`; `VaultMarkdownDiscoveryService.cs:445-460`.
- **I-193 · A refused note waits for its note or a sweep.** *(new in Patch103.2)* A `state-refused` issue is a content status, never retried on a timer. When what blocked the edit clears on its own — the blocking item finished through the API, or another note synced first (I-064) — the edit stays unapplied until the note is edited again or the next sweep runs. The retried `sync-failed` it replaces applied it within a minute, at the cost of a retry and an audit row every minute for good (I-072). *Systematic: watcher re-inspection; the engine could name the notes a change unblocks.*
  Where: `WatcherRetryScheduler.cs`; `WatcherStatusReporter.cs`; `VaultWatcherReconciler.cs`.
- **I-073 · Service refusals reach clients as bodiless 500s.** Since Patch103.2 every refusal of the state engine — the gates, the Over rules, exclusivity — is a `StateEnforcementException`, answered with 409 and its decision. No handler maps the services' own refusals (`InvalidOperationException`, `ArgumentException`, a raw `DbUpdateException`) or an orbit that fails to build, and the host runs as Production, so those reach clients as bodiless 500s and their messages stay in the server log. *Engine 4: typed violations mapped to 409 and 422.*
  Rows: EP-01. Where: `PlaintorchHostFactory.cs:70-73`.

### Notes written outside the outbox
- **I-074 · API writes that skip the note are reverted by the watcher.** Two flows save note-backed fields with a plain save and no reconcile intent: assign-all, and the placeholder begin's member moves. The startup sweep and any later edit re-apply the note over the row. Since Patch103.3 planning an existing objective into a cycle writes its note, and the begin rewrites the notes of the members it brings on board; the members it leaves as they are (started, set aside, already on board) and its directives keep `onrush: x0000`, which no longer exists, so they are detached, or join a newer placeholder. *Engine 7: a note intent whenever a note-backed field changes.*
  Rows: ON-04, ON-09, PO-07, PS-06, PS-08. Where: `OnrushSprintApiService.cs:107-115`, `137`, `379-388`; `PolarisCycleApiService.cs:366-388`, `412`; `VaultWatcherReconciler.cs:35-70`; `VaultWatcherSyncService.cs:269`, `411-470`.
- **I-075 · Superseded forecast notes are hard-deleted.** After commit, SH-23 `File.Delete`s each superseded forecast's note at its computed path. There is no remove intent and no graveyard copy. A crash between commit and delete leaves an orphan note, and a note elsewhere survives. *Engine 7: a remove intent recorded in the transaction.*
  Rows: SH-11, SH-23. Where: `PlaintorchStatePolicyFileSyncService.cs:20-36`; `PlaintorchStatePolicyInterceptor.cs:42-45`.
- **I-076 · A sprint delete writes notes outside the outbox.** Order notes are archived before the first commit, and the sprint note is deleted directly with no remove intent. A failed save leaves orders without notes, and a crash leaves the sprint note unrecovered. *Engine 7: remove intents in the transaction.*
  Rows: ON-08, ON-13. Where: `OnrushSprintApiService.cs:196-201`, `216`, `231`.

## Watcher
- **I-077 · The no-op skip rarely fires.** `SetValues` copies every mapped scalar, so restored or recomputed fields still read as modified. This hits sprints with a milestone or layout, directives with an availability, fates and decrees with a cached `NextOccurrence` (wiped, then recomputed by SH-07), and every lore page, whose `IndexedUtc` is stamped at each read. Every sweep re-saves them through the full hook, adds a `sync/update-from-file` row for each and announces critical changes. *Systematic: watcher write mechanics.*
  Rows: WA-21, WA-22, WA-25. Where: `VaultWatcherSyncService.cs:269`, `287-303`, `322`, `501-505`; `LorePage.cs:191`; `.DISCUSSION.md:1177-1179`.
- **I-078 · `forecast:` edits never reach the database.** `PolarisForecast` is an owned type, `SetValues` skips owned references, and only an objective's `due` is copied by hand. A Journal note's `forecast:` edit therefore reads as a no-op, and the note and the row disagree until the next rewrite (defect, by reading). *Systematic: a local defect in watcher read mechanics.*
  Rows: WA-21, WA-22. Where: `VaultWatcherSyncService.cs:269`, `279-285`, `501-505`; `PolarisForecast.cs:7`; `PolarisCycle.cs:23-24`.
- **I-079 · The database-only guard list is kept by hand.** A new database-only scalar on a note-backed entity is reset to its default by the next note sync unless it is added to the restore list, and nothing checks the list against the model. *Systematic: watcher mechanics.*
  Rows: WA-22. Where: `VaultWatcherSyncService.cs:287-303`.
- **I-080 · One bad key reverts the whole note.** An invalid frontmatter key on a known entity rewrites the note from the row, discarding every other key edited in the same save. *Systematic: markdown validation mechanics.*
  Rows: WA-17, WA-26. Where: `VaultMarkdownDiscoveryService.cs:702-703`; `VaultWatcherSyncService.cs:573-615`.
- **I-081 · Relocation fast-path gaps.** The fast path skips the duplicate-identity check and the identity-asserted settle, and reads the old id with the loose parser (I-154). Its `relocation-failed` reason is catalogued but never raised. *Systematic: watcher mechanics and dead code.*
  Rows: WA-08. Where: `VaultWatcherService.cs:849-911`; `WatcherOperations.cs:40`, `91`; `WatcherStatusReporter.cs:63-65`.
- **I-082 · The write barrier drops genuine edits.** For 3 s after a core write, move, archive or purge, events on the path and its parent folder are discarded, not deferred. A user save in that window waits for a later event or sweep. *Systematic: watcher read mechanics.*
  Rows: WA-07. Where: `VaultWatcherWriteBarrier.cs:11-44`; `VaultWatcherService.cs:672-683`; `PlaintorchMarkdownStorageService.cs:311-317`.
- **I-083 · Path-bound notes that go without a per-file event are never reconciled.** The sweep enumerates only existing files, the vanished-note pass covers only identity-driven kinds, and a gone folder resolves to no model. As a result (defect, by reading):
  - an order or lore note deleted while the core is off keeps its row
  - a sprint or cycle note deleted while the core is off is not restored
  - a sprint or lore folder deleted or moved as one event leaves its rows, and a moved lore subtree is created again under new ids beside the old ones

  *Systematic: watcher sweep mechanics.*
  Rows: WA-04, WA-06, WA-09, WA-26, WA-28. Where: `VaultMarkdownDiscoveryService.cs:56-97`, `129`; `VaultPathSyncModelCatalog.cs:188-204`; `SyncedVaultStorageModePolicyService.cs:41-49`; `EnforcedVaultStorageModePolicyService.cs:45-53`.
- **I-084 · Sweep inspection failures are only logged.** A file the sweep cannot inspect is skipped with a warning and no issue, while the same failure on a live event raises a retried issue. Such a file waits for its next event. *Systematic: watcher sweep mechanics.*
  Rows: WA-04, WA-11. Where: `VaultMarkdownDiscoveryService.cs:67-83`; `.DISCUSSION.md:1169-1170`.
- **I-085 · Blocked deletes re-run on every batch.** Each vanished-note check runs after any batch with a note, folder or gone path, and in every sweep. Each time it re-reads every note's frontmatter in the vault and re-attempts every blocked delete, adding a `sync/delete-blocked` audit row. The blueprint's "recreate the file" is still an open decision. *Systematic: watcher mechanics.*
  Rows: WA-09, WA-28. Where: `VaultWatcherService.cs:755`, `772-775`; `VaultMarkdownDiscoveryService.cs:215-262`; `VaultWatcherSyncService.cs:694-718`; `.DISCUSSION.md:204-205`.
- **I-086 · One unguarded exception stops file sync for the whole activation.** An exception that escapes the session's inner guards raises the fatal `watcher.process`/`fatal`, and the watcher then only waits for a new session generation; nothing retries before re-activation. *Systematic: watcher hosting.*
  Rows: WA-01. Where: `VaultWatcherService.cs:99-134`.
- **I-087 · The watcher deliberately skips D17.** A frontmatter `beginning:` is applied unchecked by design, to avoid rewrite churn. *Systematic: a deliberate watcher policy; the engine could report a violation without rewriting the note.*
  Rows: LO-03. Where: `VaultMarkdownDiscoveryService.cs:746-750`.

## Storage, paths and media
- **I-088 · Deleting a note can archive a whole folder.** The archive target is chosen by name: a note whose name equals its folder's is treated as a self-named entity, whatever the entity's shape (defect, by reading). Deleting an objective titled "Objectives" from the standalone root archives the whole `Objectives` root, with every standalone note in it, and the vanished-note pass then deletes those objectives. A fate titled "Fates" or a decree titled "Decrees" does the same to its root or partition. *Systematic: storage mechanics.*
  Rows: PS-03, PS-08. Where: `PlaintorchMarkdownStorageService.cs:505-512`, `904-921`; `VaultMarkdownDiscoveryService.cs:121-183`.
- **I-089 · Fate and decree notes outside their partition never sync.** Routing gives a path to the first model whose watch check accepts it, longest scan root first. All three incentive models accept a note placed directly in a directive folder or in a plain subfolder of it, so the objective model (the longest root) always takes the note. Its belonging test then wants the exact type, so a fate or decree note there is dropped: its edits never reach the row, while core writes keep the note there (defect, by reading). *Systematic: storage path routing.*
  Rows: SM-01, SM-09. Where: `VaultStoragePolicyEngine.cs:27-40`; `VaultPathSyncModelCatalog.cs:67-74`, `341-352`; `ImplicitVaultStorageModePolicyService.cs:103-106`; `VaultMarkdownDiscoveryService.cs:559-590`.
- **I-090 · Any note's `puck` makes its folder a directive.** The containing-directive lookup takes the first frontmatter `puck` it finds in a folder, of any kind (defect, by reading). An incentive note in a plain subfolder of its directive therefore makes that subfolder "the directive", with the note's own id. Discovery sets DirectiveId to that id, the watcher nulls it as a missing directive (detaching the incentive), and the write path sees a reparent and moves the note to a canonical path. `ObjectiveSurvivalMatrixTests` covers this placement but asserts only that the objective survives. *Systematic: path policy.*
  Rows: SM-14, SM-21, SM-26. Where: `MarkdownFileLocator.cs:262-345`; `VaultWatcherPathPolicy.cs:199-232`; `VaultMarkdownDiscoveryService.cs:874-888`; `VaultWatcherSyncService.cs:411-427`; `PlaintorchMarkdownStorageService.cs:429-463`.
- **I-091 · Titles starting with `_` or `.` lose their entity.** The PEP097 codec maps neither a leading `_` nor a leading `.`, so such a title gives the note, or its folder, a name the watcher ignores. The watcher never reads the note, which then asserts no identity, and the vanished-note pass deletes the Freeform entity (or the Implicit one once its boundary stands) while the note stays on disk (defect, by reading). *Systematic: path and storage policy.*
  Rows: WA-06, WA-09, SM-07, SM-09, SM-12, SM-22. Where: `VaultWatcherPathPolicy.cs:127-129`; `PuckFileNameCodec.cs:25-40`; `VaultMarkdownDiscoveryService.cs:143-149`, `170-176`, `238-252`.
- **I-092 · Ignore rules bind discovery only.** Neither candidate enumerator applies them (defect, by reading). The loader can import a legacy note from an `_archive` folder under a directive. The write path's identity search reads `.trash` and `_…` folders, so a copy there can win SM-28 and be rewritten while the real note goes stale. An unreadable `.obsidian/app.json` silently turns the attachment rule off. *Systematic: path policy.*
  Rows: SM-01, SM-12, SM-28, MG-02. Where: `VaultStoragePolicyEngine.cs:70-88`; `VaultPathSyncModelCatalog.cs:170-179`; `VaultLoader.cs:62-73`; `PlaintorchMarkdownStorageService.cs:560-584`; `VaultWatcherPathPolicy.cs:318-351`.
- **I-093 · Duplicate identities are flagged on read, but a winner is picked on write.** When several files assert one identity, the watcher raises `duplicate-identity` yet applies every one, so the last file reconciled sets the row. A write takes the most recently modified match and leaves the rest. The match uses the loose parser (I-154), and each write rescans and reads every candidate note in the territory several times (the whole vault for a directive), so a write's cost grows with the vault. *Systematic: identity and storage mechanics.*
  Rows: WA-12, SM-28. Where: `VaultWatcherReconciler.cs:226-236`, `268-302`; `PlaintorchMarkdownStorageService.cs:233-267`, `514-605`, `631-635`.
- **I-094 · Pruning climbs past the storage root.** The prune stops only at the entity's storage root. For a note kept elsewhere (a freeform directive outside `Directives`), it therefore removes every empty ancestor folder, user-made ones included. Old and source files are hard-deleted, not archived. *Systematic: storage mechanics.*
  Rows: SM-29. Where: `PlaintorchMarkdownStorageService.cs:818-842`, `923-945`.
- **I-095 · The frontmatter parser reads lines, not YAML.** A block list (`tags:` followed by `- item` lines) or any multi-line value reads as an empty value (defect, by reading). A declared list such as a directive's `tags:` then fails "Field cannot be empty.", and the whole note is reverted from the row. An undeclared block list is mangled on the next core rewrite: item lines without a colon are dropped, and those with one become keys. A repeated key keeps its last value. *Systematic: markdown mechanics.*
  Rows: SM-16, SM-18. Where: `MarkdownFrontMatterSerializer.cs:65-79`, `109-140`, `421-433`; `Directive.cs:57-58`.
- **I-096 · A fate's retired keys never leave its notes.** `date`, `startTime`, `endTime` and `eventDuration` are not declared retired, so v3→v4 and every later rewrite carry them on as undeclared keys. *Systematic: markdown mechanics.*
  Rows: SM-17, MG-05. Where: `Fate.cs:17-26`; `MarkdownFrontMatterSerializer.cs:65-73`; `.DISCUSSION.md:1765-1766`.
- **I-097 · Partition names match anywhere.** Any folder named Objectives, Fates, Decrees or ExecutiveOrders counts as a partition, so a directive titled like one is flagged on every read and its note edits are reverted from the row. Freeform write placement checks neither partitions nor entity roots, so it keeps writing where SM-13 flags; a rewrite loop is possible (unverified). *Systematic: path policy.*
  Rows: SM-08, SM-13. Where: `VaultWatcherPathPolicy.cs:177-180`, `297-301`; `FreeformVaultStorageModePolicyService.cs:208-232`.
- **I-098 · Moonlight is not an entity root.** It is not probed for access and is not an ownership boundary. A lunar note placed directly in it is not flagged as owning an entity root, unlike one in `Directives`. *Systematic: path policy.*
  Rows: SM-13, SM-14, SM-15. Where: `VaultWatcherPathPolicy.cs:87-97`, `172-175`, `265-276`.
- **I-099 · Synced files behave differently at activation.** At activation, the consistency pass strips an unknown but mintable id from a Synced file name, so the file is then re-created under a new id. At runtime, the same file is adopted with its own id. *Systematic: activation versus storage policy.*
  Rows: SM-06. Where: `SyncedVaultStorageModePolicyService.cs:61-66`; `VaultAutoGeneratedPuckConsistencyService.cs:113-153`.
- **I-100 · Typed reads stop at discovery.** The write pipeline and the loader read notes with plain `File.ReadAllTextAsync`, so a note an editor holds open fails a write, a drain or a migration step with a raw IOException. The failure classification knows Windows HResults only. *Systematic: storage mechanics.*
  Rows: SM-24. Where: `PlaintorchMarkdownStorageService.cs:595`, `735`, `760`, `779`; `VaultLoader.cs:149`; `VaultFileAccess.cs:23-25`, `46-56`.
- **I-101 · The "Untitled" exemption is English-only.** Only "Untitled", "Untitled 2" and so on escape an Enforced purge, so a new note from an Obsidian set to another language would be purged from Journal or Onrush at once (unverified). *Systematic: storage policy.*
  Rows: SM-02, SM-03. Where: `PathBoundVaultStorageModePolicyService.cs:92-106`; `EnforcedVaultStorageModePolicyService.cs:29-42`.
- **I-102 · One storage fact, several sources.** The non-directive managed-root list is kept twice. "Identity-driven" is both a policy flag and an enum extension, and watch-root selection and detection scope still read the enum. Implicit belonging compares the exact type name, where Freeform uses family membership. *Systematic: duplicated storage policy.*
  Rows: SM-07, SM-08, SM-09, SM-11. Where: `FreeformVaultStorageModePolicyService.cs:91`, `234-244`; `VaultWatcherPathPolicy.cs:31`, `254-263`; `VaultStorageAttribute.cs:82-85`; `VaultPathSyncModelCatalog.cs:469-474`; `ImplicitVaultStorageModePolicyService.cs:106`.
- **I-103 · Dead and misdocumented storage modes.** FileFirst and Optional are registered but declared by no entity. Optional deletes rows although it is documented to sync removals on neither side. The attribute defaults to Synced, so a new type silently gets file-driven deletes. *Systematic: dead code and storage policy.*
  Rows: SM-04, SM-05, SM-06. Where: `PlaintorchModule.cs:153`, `155`; `OptionalVaultStorageModePolicyService.cs:40-46`; `VaultStorageAttribute.cs:16-22`, `119`.
- **I-104 · The vault-root containment check is written three times.** *Systematic: path policy.*
  Rows: OB-07, SY-08. Where: `SystemApiService.cs:83-89`, `335-347`; `VaultEntityLifecycleService.cs:91-99`.
- **I-105 · A lore rename leaves descendants' paths stale.** Renaming recomposes only the page's own `RelativePath` and moves its folder; descendants keep the old path (`SetIndexAsync` rebases them). Lore placement trusts `RelativePath`. Until the watcher re-reads the moved notes, an API write to a descendant, or a child created under one, targets the old folder (by reading). *Systematic: path and storage policy.*
  Rows: LO-04. Where: `LorePageApiService.cs:135-147`, `240-263`; `VaultStoragePathComposer.cs:343-357`.
- **I-106 · A lore override breaks the page's path identity.** Once a page's folder carries its `overrideIdentifier` (an API rename composes it), path reads take the override as an id segment: the page's level and number are re-derived from it (an override such as "Prologue" makes the level "Prologue", after which the page "cannot receive children"), and its children get a parent id that names no page, which the watcher nulls on every sync (defect, by reading). *Systematic: path and storage policy.*
  Rows: DM-11, DM-12. Where: `LorePage.cs:113-125`, `179-191`, `214-243`; `VaultWatcherSyncService.cs:147-154`, `269-277`; `LorePageApiService.cs:142`, `333-340`, `408-410`.
- **I-107 · A lore page's id and path can part.** The frontmatter `puck` keeps the id while the parent, level and numbers come from the path, so a folder moved by hand keeps an id that names its old parent, and pages created under it later get a path parent that names no page (unverified). *Systematic: path policy.*
  Rows: DM-10, DM-11. Where: `LorePage.cs:30-45`, `179-191`; `LorePageApiService.cs:77`.
- **I-108 · Self-media archiving is API-only.** A frontmatter `icon:` or `banner:` change orphans the old self image, with no archive and no trim. *Systematic: media file mechanics; the engine could host it as an effect.*
  Rows: DI-06, SY-02. Where: `DirectiveApiService.cs:785-835`; `VaultWatcherSyncService.cs:269`.
- **I-109 · Directive media is archived before the save.** The replaced self image goes to the graveyard before `WriteAsync`. If the save fails, the file is archived while the stored key still points at it. *Systematic: media file mechanics.*
  Rows: DI-06, SY-02. Where: `DirectiveApiService.cs:391-393`, `413-415`, `817-835`.
- **I-110 · Uploads overwrite same-named media.** A same-named asset is replaced with no graveyard copy, unlike media removal. *Systematic: media mechanics.*
  Rows: SY-01. Where: `VaultMediaService.cs:73-93`, `119-141`.
- **I-111 · Media companions are checked lazily.** A `[Media]` property without its `…Media` companion fails at first use, not at activation. *Systematic: media model wiring.*
  Rows: SY-03. Where: `VaultMediaService.cs:264-290`.

## Persistence
- **I-112 · Timed-out drains outlive their context.** Past `NoteQueueTimeout` a drain keeps running on a context it does not own:
  - A request's drain keeps using the request's scoped `PlainfraContext` while the caller carries on with it: each API flow writes its audit row right after `WriteAsync`, and the sprint delete loops `WriteAsync` over its members. Two operations can then run on one DbContext, which EF refuses with an InvalidOperationException after the change has committed (defect, by reading). When the scope ends first, the drain fails on a disposed context and the note waits for the next activation.
  - SH-24's reopened-directive drain marks its throwaway scope's readiness rather than the request's, and the scope is disposed while the drain still runs. A failure is only logged and left to the activation drain.

  *Systematic: outbox mechanics.*
  Rows: SH-24, PS-07. Where: `VaultWriteQueue.cs:129-176`; `ObjectiveApiService.cs:132-133`; `OnrushSprintApiService.cs:226-229`; `PlaintorchStatePolicyFileSyncService.cs:45-55`.
- **I-113 · Audit and archive writes commit pending work.** The audit write (`WriteAsync`), the note archive (`ArchivePathAsync`) and the boundary begin each save the shared context through the whole hook. Each commits whatever else is pending and runs the full state policy for one row, and the reconciler reloads every edge each time. In the sprint delete, the first order's archive commits that order's removal before the milestone and sprint saves. An archive moves the file before its row is saved, so a failed save leaves an archived file with no row. The watcher writes such an audit row for every inspection, ignore, conflict, no-op, scaffold rewrite and blocked delete, per event and per retry, so a stuck note grows the audit log without bound. The boundary begin is coded twice, in the sync and the storage service. *Systematic: persistence mechanics; engine point 3's scoping would remove the cost of running the policy, not the early commit.*
  Rows: WA-10, WA-19, WA-25, WA-29, WA-31, PS-01, PS-03, PS-04. Where: `VaultAuditLogService.cs:21-40`; `VaultMarkdownDiscoveryService.cs:437-460`; `VaultWatcherSyncService.cs:82-107`, `334-363`, `533-548`, `704-711`; `PlaintorchMarkdownStorageService.cs:322-332`; `VaultImplicitBoundaryService.cs:67-92`; `VaultTemporalDataService.cs:83-108`; `OnrushSprintApiService.cs:196-214`.
- **I-114 · Crash recovery cannot replay a rename.** The startup drain rewrites a pending entity without `previous`, and such a write keeps the existing note's path and name. A rename cut short by a crash therefore comes back under the old file name, and the next sweep reads that old title back over the row (defect, by reading). *Systematic: storage mechanics.*
  Rows: SM-25, PS-06. Where: `VaultWriteQueue.cs:190-201`; `PlaintorchMarkdownStorageService.cs:250-258`, `278-281`; `VaultMarkdownDiscoveryService.cs:595-613`.
- **I-115 · A stuck intent is silent and shields its entity.** A faulted or orphaned drain is retried only by the next activation's drain, and no issue is raised. Meanwhile its row keeps the vanished-note pass from deleting the entity, matched on the id alone. *Systematic: persistence mechanics.*
  Rows: PS-06, PS-07. Where: `VaultWriteQueue.cs:169-176`; `VaultMarkdownDiscoveryService.cs:156-170`.
- **I-116 · Status transitions can be lost or land in another vault.** The buffer is drained before its save, so a failed save loses the batch. While idle the context cannot be built, so buffered transitions wait and are written into whichever vault activates next. Flush failures are only logged, and the log is never reloaded into the registry. *Systematic: persistence mechanics.*
  Rows: MA-10. Where: `OperationStatusEventBuffer.cs:28-46`; `OperationStatusPersistenceWorker.cs:38-50`; `.DISCUSSION.md:1171-1172`.
- **I-117 · Deletes keep history unevenly.** *(changed by dev/phase3a: the forecast delete snapshots only the cycle)* Fates, decrees, objectives, directives, lore pages, orders and cycles get a graveyard snapshot and a remove intent. Checkpoint, timeframe, dependency, schedule and executive deletes, hook removals (SH-01, SH-02, and SH-11's superseded forecasts with their executives) and store cascades leave no snapshot, and dev/phase3a's forecast delete snapshots the cycle but not the executives and reflectives it removes. There is no restore path and no retention. *Systematic: graveyard policy; an engine effect phase could stage a snapshot on every delete.*
  Rows: SH-11, SH-23, DI-11, DP-12, DB-02, DB-06, PS-02. Where: `DependencyApiService.cs:148-160`, `272-301`; `DirectiveApiService.cs:740-755`; `PlaintorchStatePolicyProcessor.cs:707-710`; `VaultTemporalDataService.cs:20-51`; `PolarisCycleApiService.cs:295-300`.
- **I-118 · The boundary lives in the audit table.** The implicit boundary is load-bearing state kept in a soft-reference audit table, so pruning audit rows would change deletion authority. Its begin is check-then-act with no unique row, and it is written by two implementations, keyed by model name and by CLR name (equal today). An entity deleted before BoundaryEnd existed keeps a standing boundary. Its end rides the async hook only (SH-03), so a sync or raw-SQL delete leaves it standing; the engine's every-pathway run would cover that part. *Systematic: persistence mechanics.*
  Rows: PS-01. Where: `VaultImplicitBoundaryService.cs:44-88`, `119-167`; `VaultWatcherSyncService.cs:543-547`; `PlaintorchMarkdownStorageService.cs:327-331`.
- **I-119 · Begin-boundary writes its note outside the outbox.** It recanonicalises the note directly, with no durable intent and no `X-Note-Ready`, and writes a fresh `…begin-boundary` audit on every call. *Systematic: outbox mechanics.*
  Rows: OB-06. Where: `VaultEntityLifecycleService.cs:57-62`.
- **I-120 · The engine's services bind to the scope's context.** Lazily resolved services (graph, hardening, orbit, audit, write queue) use the scope's context, not the context the save hands the engine; Patch103.2 kept the pattern. The two coincide only because a scope has one context. *Systematic: DI wiring.*
  Rows: SH-05, SH-07, SH-17, SH-18, SH-19, SH-20. Where: `PlaintorchStatePolicyProcessor.cs:20-25`, `92`, `130`, `240-241`, `458`, `548`, `596`.

## Database schema
- **I-121 · The objective→sprint key refuses instead of freeing.** At HEAD it still has no ON DELETE action, in both the snapshot and the DDL, while `PlainfraContext` and the sprint delete say a deleted sprint frees its objectives. Only the API's Include lets EF null them, so any other delete of a sprint that still has objectives fails in the store. Stellar members and checkpoints are SET NULL. *Systematic: schema; a declared release reaction would cover the app pathways only.*
  Rows: DB-03, DB-09. Where: `PlainfraContextModelSnapshot.cs:1410-1412`; `20260714231514_DeclarativeEcosystem.cs:336-341`; `PlainfraContext.cs:407`; `OnrushSprintApiService.cs:219-221`.
- **I-122 · Codename uniqueness is store-only and case-sensitive.** No application check exists, so a clash from the API or from a note's `codename:` surfaces as a raw DbUpdateException: a bare 500 (EP-01) or a retried sync failure. "Alpha" and "alpha" do not clash. *Partly engine 4: a clash would surface as a typed violation; the store-only, case-sensitive rule is schema.*
  Rows: DB-10. Where: `Directive.cs:24`; `PlainfraContextModelSnapshot.cs:235-236`.
- **I-123 · Required and enum columns rest on the CLR model.** A derived type's required status columns and the `Due_*` columns are nullable in the store, and discriminator lengths are not enforced. No CHECK backs any enum column, and a decree's college is a number where an objective's is a name. Raw SQL can therefore write rows that EF then fails to read, and empty titles pass. *Systematic: schema.*
  Rows: DB-13, DB-14, DB-15, DB-16. Where: `PlainfraContextModelSnapshot.cs:212-215`, `965-968`, `1016-1018`, `1072-1073`, `1086-1089`, `1103-1106`; `20260722010046_LunarStellarDirectiveSiblings.cs:18-24`.

## Migrations
- **I-124 · Stored orbits changed meaning in dev/phase3a, with no migration.** *(new in dev/phase3a)* dev/phase3a changed the orbit notation without migrating stored orbits or their states:
  - It narrowed the bound syntax. A stored orbit that no longer parses fails everything that re-parses it, and only timeframe matching catches the failure: the whole rolling pass every hour (so nothing is hardened for anyone), the agenda read, every save touching it (SH-07), and a cycle begin, which by then has committed its start but not its note (defect, by reading; whether such orbits exist depends on the data).
  - Implicit cycles for top-level indices, `%1` stepping and `>t` as the origin now apply to existing notations, so a state persisted as exhausted under the old reading (a top-level `d{2,4,6,8}@2`) resumes exhausted and never hardens again, while previews keep projecting it (unverified).

  *Systematic: an orbit data migration, plus per-item isolation in the passes.*
  Rows: MA-01, MA-03, MA-06, MA-08, DM-26, DM-27. Where: `OrbitEngine.cs:150-164`, `222-261`, `867-872`; `OrbitParser.cs:399-414`; `OrbitDays.cs:304-317`, `392-405`; `ProximityMaterializationService.cs:166-206`; `RollingMaterializationService.cs:42-63`; `PlaintorchOrbitService.cs:471-495`; `PolarisCycleApiService.cs:85-89`; `OrbitCycles.cs:35-52`.
- **I-125 · Vaults that ran the purge as v4→v5 skip the retirement.** *(changed by dev/phase3a)* On the intrinsic branch, the purge was the v4→v5 step. Such a vault runs it again as v5→v6 (it finds nothing) and never runs the start/end retirement, so its directive notes drop those keys only at their next rewrite. *Systematic: migration numbering.*
  Rows: MG-06, MG-07. Where: `DirectiveStartEndRetirementMigration.cs:30-36`; `IntrinsicDependencyPurgeMigration.cs:27-33`; `.DISCUSSION.md:1782-1786`.
- **I-126 · The nesting removal left two schemas.** *(changed by dev/phase3a)* Databases that ran the rewritten RemoveIncentiveNesting keep an always-null `ParentIncentiveId`, with its self-referencing key, until the next rebuild of `Incentives`. Databases that ran the earlier version have neither, under the same history row, and cannot run its Down. One `.DISCUSSION.md` passage still says the migration drops the column. *Systematic: migration mechanics.*
  Rows: DB-09, MG-20. Where: `20260930131323_RemoveIncentiveNesting.cs:10-38`; `20260714231514_DeclarativeEcosystem.cs:329-334`; `.DISCUSSION.md:379-381`, `1799-1805`.
- **I-127 · EF data fixes lose data.** The executive dedupe deletes the duplicates' executed and elapsed data with no graveyard entry. The epoch fold does not guard against an overnight window, which yields a negative duration. The Downs lose end times, bound attentives, timeframe orbit states, schedule times and every non-directive schedule, the difference between all-day and midnight slots, and colleges. Two edges that differed only by a NULL time versus 00:00 would collide on the rebuilt index (unverified). *Systematic: migration mechanics.*
  Rows: MG-10, MG-12, MG-13, MG-15, MG-17, MG-18, MG-19. Where: `20260915154002_UniqueExecutiveObjectivePerCycle.cs:20-27`; `20260922011112_OccurrenceEpoch.cs:40-43`, `52-71`; `20260922115705_BoundAttentivesToExecutives.cs:42-61`; `20260925001601_OrbitScheduleStateHierarchy.cs:80-81`; `20260930054809_BacklogSchedules.cs:111-123`; `20260923220349_OccurrenceRecurrenceId.cs:60-65`, `181-197`; `20260826003857_TimeframeAutoInclusionColleges.cs:31-43`.
- **I-128 · A legacy one-off on another calendar can move.** *(changed by dev/phase3a)* FateOrbitOnly composes a Gregorian `Z{…}` literal from the old date, but pins only fates with no calendar. Since dev/phase3a, a lone `Z{…}` is read on the fate's own calendar (SH-05, DE-17). A legacy one-off fate that already named another calendar may therefore land on another day, or fail to build, when an old vault meets this migration (unverified). *Systematic: the time model meeting a one-shot migration.*
  Rows: MG-14. Where: `20260922092435_FateOrbitOnly.cs:18-42`.
- **I-129 · A refused import fails or poisons the step.** Imports are not validated (defect, by reading). A store refusal (a DbUpdateException) is not caught as a conflict, so it fails the step and the activation. A hook refusal leaves the entity Added, so the conflict's own audit save retries it and fails too. *Systematic: migration mechanics.*
  Rows: MG-01. Where: `VaultRecanonicalizationMigration.cs:63-71`, `94-112`.
- **I-130 · The loader knows only the baseline.** `BuildForVersion` returns the baseline set for any version below current, which a future step from v2 or later would misread. Quiet titles are not PEP097-decoded. Its territory logic duplicates the consistency pass's. *Systematic: migration mechanics.*
  Rows: MG-02. Where: `VaultConventionSetFactory.cs:30-35`; `VaultLoader.cs:113-141`, `204-212`.
- **I-131 · The v2→v3 step spans several commits.** The v2→v3 step spreads over several saves and a folder move, so a crash between them leaves a "0 - …" folder for Enforced to purge. Legacy snapshot copies get no graveyard row. *Systematic: migration mechanics.*
  Rows: MG-01, MG-04. Where: `OnrushPlanningPlaceholderRenameMigration.cs:104-141`; `VaultRecanonicalizationMigration.cs:122-141`.
- **I-132 · Migration-runner gaps.** The history save for a failing step can commit what the step staged. `completed-with-conflicts` advances the version, so conflicts are never retried. The version lives in `.plaintorch` and the history in the database, and nothing reconciles the two. *Systematic: migration mechanics.*
  Rows: AC-14. Where: `VaultMigrationRunner.cs:84-101`, `125-151`.
- **I-133 · The version and settings guards disagree.** A newer EF history refuses activation, but a `.plaintorch` version above the engine's only logs a warning. An unreadable `.plaintorch` silently loads as v1 with default roots, re-running every vault migration and overwriting custom roots at the next version write. `Vault/.GENESIS.md:72` says a fresh vault is stamped at the current version; it is stamped v1. *Systematic: persistence and migration mechanics.*
  Rows: AC-07, AC-12, AC-14. Where: `VaultMigrationRunner.cs:59-63`; `VaultSettings.cs:27-44`; `VaultVersionService.cs:26-31`; `PlainfraContextInitializer.cs:39-52`.

## Materialisation and derived reads
- **I-134 · Some occurrences are never hardened.** Two ways an occurrence is never hardened:
  - The rolling pass seeks an orbit's occurrences, advancing the cursor, before the LC-11 freeze check, so an occurrence frozen by an unmet edge is consumed and never hardened after the gate opens.
  - Any eventive of an objective suppresses its due. An eventive hardened at a slot the caller named (never checked against the due), or at a due since moved, leaves the current due unhardened for good (defect, by reading).

  *Systematic: materialisation cursor mechanics; the engine only supplies the freeze rule.*
  Rows: LC-11, DE-09, MA-01, MA-02, DM-27. Where: `ProximityMaterializationService.cs:166-172`, `208-229`, `277-285`; `PlaintorchOrbitService.cs:166-176`; `OccurrenceHardeningService.cs:182-198`.
- **I-135 · Reads write.** Computing timeframe candidates on an API read saves lazily created orbit states, and a lost race detaches them with only a log line. A malformed timeframe orbit silently reads as non-matching. Previewing an orbit with no state adds a state that persists only if the request happens to save. *Systematic: materialisation mechanics.*
  Rows: SH-05, SH-06, MA-06, MA-07. Where: `TimeframeCandidateService.cs:74-97`; `PlaintorchOrbitService.cs:334-351`, `471-496`.
- **I-136 · The active lore spine is computed three ways and cached without its date.** The cached spine stamps lore responses, the briefing computes it fresh, and an unmapped `ListActiveAsync` computes it again. The cache is keyed by session generation and dropped only on async lore saves, though the spine depends on today's date, so `IsActive` stamps go stale at a day rollover until the next lore write; the `ExecuteUpdate` re-key bypasses the drop too (I-010). Today is the UTC date (I-159). *Systematic: a cached read projection; engine point 5's clock could drive the drop.*
  Rows: SH-27, LO-07, LO-08, DM-14. Where: `LoreActiveCache.cs:26-55`; `SystemApiService.cs:624-632`; `LorePageApiService.cs:51-57`, `268-283`; `LoreIndex.cs:36-38`; `LoreActiveCacheInterceptor.cs:25-69`.

## Activation and hosting
- **I-137 · A malformed config.json stops the host.** The coordinator reads config.json every 2 s with no guard. A JSON error (a hand edit, or a read racing another writer) escapes `ExecuteAsync`, and by default a faulted background service stops the host (defect, by reading). *Systematic: activation and hosting.*
  Rows: AC-01. Where: `PlaintorchCoreService.cs:60-74`, `121-122`; `PlaintorchUserConfiguration.cs:195-205`.
- **I-138 · CLI `init` bypasses the lock and is the default command.** `init` runs the whole repair chain (migrations, drain, consistency pass) without the vault lock, so it can rewrite a vault that a live core serves. A command line with no command token (`--help`) runs it in the working directory. *Systematic: activation and hosting.*
  Rows: AC-04, AC-05. Where: `Program.cs:79`, `92-115`; `PlaintorchLaunchArguments.cs:70`.
- **I-139 · Activation rewrites notes before the watcher looks, and reports nothing.** Three passes run before the startup sweep: the vault migrations, of which v1→v2, v3→v4 and v4→v5 re-emit notes; the outbox drain, which rewrites notes from rows; and the consistency pass, which purges and renames. A field edited while the core was off can therefore be overwritten, and the effects show only in the audit log. *Systematic: activation and migration mechanics.*
  Rows: AC-14, AC-15, AC-16, MG-03, MG-05, MG-06. Where: `PlaintorchEngine.cs:50-57`; `VaultWriteQueue.cs:183-215`; `VaultAutoGeneratedPuckConsistencyService.cs:113-152`.
- **I-140 · Preferences load late and are never cleared.** *(changed by dev/phase3a: the reflection preferences are now among what these passes read)* The store is loaded only after the migrations, the drain and the consistency pass. Their saves run the hook, which reads `AgendaPreferences` and `ReflectionPreferences` (cursor calendars and anchors, `NextOccurrence`, the daily reward), so they get the previous vault's values or the defaults. Release never clears the store (defect, by reading). *Systematic: activation ordering.*
  Rows: MA-08, AC-17. Where: `PlaintorchEngine.cs:50-56`; `UserPreferenceService.cs:26-32`; `PlaintorchCoreService.cs:263-278`; `PlaintorchOrbitService.cs:42-52`; `PlaintorchStatePolicyProcessor.cs:28`, `55`.
- **I-141 · Background passes ignore the vault session.** The rolling pass (15 s after host start, then hourly) and the status flush (every 2 s) build a context whenever the layout is bound, not when a session is ready:
  - they can run during activation, before the migrations finish or the preferences load
  - while idle every run throws `VaultNotActiveException`, so the flush logs an error every 2 s
  - a vault activated after the first tick waits up to an hour for its first pass, and a vault switch triggers none

  *Systematic: hosting and activation.*
  Rows: MA-01, MA-10, AC-18. Where: `RollingMaterializationService.cs:18-34`; `OperationStatusPersistenceWorker.cs:17-50`; `PlaintorchCoreService.cs:167-178`; `PlainfraStoreContextFactory.cs:33-38`.
- **I-142 · Most activation refusals stick until the vault is reconfigured.** *(changed by dev/phase3a: the uninitialized case is now retried)* A vault refused for its lock, a newer database or any init failure is skipped by every later pass until the configured path changes or the core restarts. A lock freed by a stopped core is therefore never retried; only "not initialized" is re-checked. *Systematic: activation and hosting.*
  Rows: AC-01, AC-05, AC-12. Where: `PlaintorchCoreService.cs:108-111`, `199-219`.
- **I-143 · A refused vault is still written to.** Bootstrap creates the folder skeleton and a default `.plaintorch` before any declaration check or the database guard, so a vault refused later in the chain has already changed on disk. *Systematic: activation mechanics.*
  Rows: AC-06. Where: `VaultBootstrapper.cs:42-53`.
- **I-144 · A hung core lets a second one start.** The single-instance probe reads any probe error as "not running". A core that does not answer in time therefore lets a second `serve` go ahead and fail on the pipe or socket bind, as the class remarks describe. *Systematic: hosting.*
  Rows: AC-02. Where: `PlaintorchInstanceProbe.cs:11-15`, `48-56`.

## Identity (PUCK)
- **I-145 · Spiritgem collisions are never caught.** The retry compares bare digits with discriminated registry ids, so it never fires; a clash fails the create with a raw registry-key error. Lunar directives (`LUNA{S:3}`, even odds of a clash by about the 38th) and checkpoints (`c{S:4}`, about the 118th; every sprint mints a milestone) will meet it, and a milestone clash leaves the already-saved sprint without its milestone (defect, observed: a full test run on 2026-10-06 met a lunar clash in `DirectiveAvailabilityTests`). *Systematic: identity/PUCK, a local defect.*
  Rows: ID-01, ID-02. Where: `PuckIdService.cs:120-130`, `186-189`; `OnrushSprintApiService.cs:40-42`, `57-75`.
- **I-146 · Counters overflow into ids the tokenizer refuses.** `D{width}` renders 10^width wider, so a sprint's 100th order is `x…-o100` and the 9,901st sprint `x10000`. Both register and save, then fail the exact-width rule, so the identity gate drops their notes' identity and resolution cannot find them. Deleted orders still consume numbers. *Systematic: identity/PUCK, a local defect.*
  Rows: ON-12, ID-03, ID-10, ID-14. Where: `PuckIdService.cs:145-161`; `PuckTokenizer.cs:234-240`; `ExecutiveOrder.cs:16`; `OnrushSprintApiService.cs:416`.
- **I-147 · Adopted order ids bypass the registry and the counter.** The watcher keeps the id an order note carries without registering it or advancing the sprint's counter, so the API's next order for that sprint can mint the same id: the registry insert passes and the order insert fails at its key (defect, by reading). *Systematic: identity/PUCK mechanics.*
  Rows: ID-02, ID-05, ID-14. Where: `VaultWatcherSyncService.cs:186-189`; `SyncedVaultStorageModePolicyService.cs:66`; `PuckIdService.cs:145-162`; `OnrushSprintApiService.cs:416`.
- **I-148 · Polaris day ids are check-then-mint.** *(new in dev/phase3a)* MintDayIdAsync reads the registry on the request context and mints on a bare one, after a separate "already has a Polaris cycle" lookup, so two concurrent first creates for a day end in a raw key error; the reuse also ends "each id issued once" for cycles, the entry keeping its first `IssuedUtc`. *Systematic: identity mechanics; the PolarisCycles key already backs one cycle per day.*
  Rows: ID-02, ID-08. Where: `PolarisCycleApiService.cs:38-45`, `319-331`.
- **I-149 · Ids minted before validation stay issued.** *(changed by dev/phase3a: the Polaris plan now checks before minting)* PUCK issuance commits on its own context. A start-new refused by SH-09, or an objective or directive create that later fails, leaves the id registered. *Systematic: PUCK identity.*
  Rows: ON-05, PO-07. Where: `PuckIdService.cs:57-58`; `OnrushSprintApiService.cs:155-163`; `ObjectiveApiService.cs:236-245`; `DirectiveApiService.cs:761-770`.
- **I-150 · Partial ids tokenize.** Every declaration accepts fewer segments than declared, so `x0100` tokenizes as both a sprint and an order, and `x-o01` passes with an empty sprint segment. *Systematic: identity/PUCK format rules.*
  Rows: ID-10, ID-11. Where: `PuckTokenizer.cs:104-117`, `227-232`.
- **I-151 · Id comparisons mix case rules.** Placeholder detection, edge release, intrinsic derivation and the gate compare ids case-sensitively, while endpoint resolution, the reconciler and slot settling compare them case-insensitively. Discriminators and the watcher's known-id sets ignore case, while database keys, indexes and the registry lookup do not, so a note whose id differs only in case counts as known and then finds no row (unverified). *Systematic: an identity convention; the engine covers only the rules it re-declares.*
  Rows: SH-08, SH-18, SH-26, LC-06, LC-07, SM-01, SM-03, ID-10, ID-12. Where: `PlaintorchStatePolicyProcessor.cs:84-86`, `611`, `623`; `IntrinsicDependencyRules.cs:45`, `63`; `DependencyGateService.cs:60-61`; `DependencyEndpoints.cs:90`, `184-190`; `DependencyReconciler.cs:40-41`; `BacklogScheduleRecorder.cs:151-158`; `VaultEntityGateway.cs:202-221`; `PuckTokenizer.cs:208-217`; `VaultMarkdownDiscoveryService.cs:633-636`; `PuckEntityResolutionService.cs:70-81`.
- **I-152 · Order ids are not tied to their sprint.** The id embeds its owning sprint, but the watcher only checks that the note's folder sprint exists, so `x999-o01` filed under `x0180` becomes an order of `x0180`. *Systematic: identity/PUCK; the engine could carry "an order's id names its sprint" as a declared invariant.*
  Rows: ID-14. Where: `ExecutiveOrder.cs:11-16`; `VaultMarkdownDiscoveryService.cs:729-744`.
- **I-153 · Renumbering can duplicate a chapter or act number.** The collision check looks only for the parent-scoped id. Era and Chapter numbers are global and Act numbers are per Era. *Systematic: lore identity and numbering policy.*
  Rows: LO-06. Where: `LorePageApiService.cs:213-222`, `343-361`.
- **I-154 · Identity is still read loosely off file names.** The loose path reader is neither storage-aware nor notation-gated, so a Quiet title that starts with something id-shaped and " - " reads as an id. It still has six callers: two in the system API, PUCK resolution, discovery, the relocation fast path (I-081) and the write path's identity search (I-093). *Systematic: identity mechanics.*
  Rows: SM-21, SM-28. Where: `SystemApiService.cs:98`, `524`; `PuckEntityResolutionService.cs:173`; `VaultMarkdownDiscoveryService.cs:403`; `VaultWatcherService.cs:909`; `PlaintorchMarkdownStorageService.cs:588`.
- **I-155 · The consistency pass reads identities without storage awareness.** It loose-parses every file name, so a Quiet note whose title starts with a mintable PUCK and " - " is renamed to the rest of its title. Its lore branch is dead (lore is skipped), the territory host check resolves directive ids only, and emptied folders are hard-deleted. *Systematic: identity/PUCK and storage mechanics.*
  Rows: AC-16. Where: `VaultAutoGeneratedPuckConsistencyService.cs:44-47`, `158-181`, `243-244`, `369-380`.
- **I-156 · Declaration-check gaps.** The checks run on PUCK declarations have gaps:
  - A lazy compile outside activation skips both uniqueness checks.
  - The zero-fill check misses truncation overlaps such as `x0000` vs `x0-o00` (unverified).
  - Discriminability counts a discriminator on one side only as distinct.
  - `ParentDirectoryProperty` is validated but never declared.
  - The abstract `Directive` still declares `A{S:6}` beside `StellarDirective`'s `A{S:8}`.

  *Systematic: identity/PUCK.*
  Rows: AC-10, AC-11. Where: `PuckRuntimeCompilationCatalog.cs:69-75`, `177-216`; `PuckPathDiscriminabilityService.cs:28-31`; `VaultStorageTopologyValidator.cs:73-82`; `Directive.cs:17`; `StellarDirective.cs:21`.

## Time, calendars and orbits
- **I-157 · Civil moments are compared on the wrong clock.** *(changed by dev/phase3a)* Three time bases meet in one epoch: a zoned timed eventive begins at its instant, a floating or all-day one when UTC reaches its civil moment, and the agenda shows both on the host's clock. At +03:30 a floating 10:00 eventive is listed at 10:00 but begins and finishes at 13:30 local, and so do the edge satisfactions and records it drives. A checkpoint `Due` is compared as host-local time, and its `TimeZone` is ignored. *Systematic: the time model; engine point 5 decides only when these rules re-run.*
  Rows: SH-21, SH-22, LC-02, DM-19, DM-20. Where: `EventiveLifecyclePhaseSource.cs:41-55`; `Epoch.cs:76`, `78-85`; `DependencyReconciler.cs:70`, `81`; `Due.cs:23-27`.
- **I-158 · A cycle's day has three readings.** *(made worse by dev/phase3a: `PolarisCycle.Day` added beside the older reading)* Supersession and the day listing read the id's date stamp (`PolarisCycle.Day`, `TryParseCycleDate`). Reflective generation, timeframe candidates, the inclusion window and next-Polaris read the host-local day of `StartTime`. A forecast reads its reference plus its target. A cycle begun on another day than its id's (a forecast begun early, a re-begun cycle, a begin after midnight) gets a different day from each: reflectives are generated for the start day, and next-Polaris can skip a day or target the same cycle, which is refused ("Incentive '…' is already an executive of Polaris cycle '…'.") or moves a standalone executive into its own cycle with its tracking reset (defect, by reading). `PolarisCycle.Day`'s doc says the readings agree. *Systematic: which day a cycle is belongs to its PUCK identity; the engine covers only the readings it re-declares.*
  Rows: SH-06, SH-11, PO-03, PO-12, MA-03, MA-06, MA-07, ID-09, DM-07. Where: `PolarisCycle.cs:44-74`; `PlaintorchStatePolicyProcessor.cs:694-705`, `1110-1123`; `TimeframeCandidateService.cs:45-51`; `PolarisCycleApiService.cs:514-549`, `578-592`, `790-797`; `ProximityMaterializationService.cs:55`; `InclusionWindow.cs:15-20`.
- **I-159 · Time bases are mixed.** *(made worse by dev/phase3a)* Four time bases are in use:
  - UTC: the ledger, `ResolvedOn`, lore "today", and the PUCK date-stamp default (latent: every caller passes a local date)
  - host-local civil time: records, plans, sprint dates, dues, the agenda
  - civil time on each declarative's zone: schedules and `NextOccurrence`
  - Polaris times, which default to `UtcNow`

  *Systematic: the time model.*
  Rows: SH-07, SH-13, SH-25, DE-16, BS-05, LO-08, ID-04. Where: `CelestronTransaction.cs:20`; `PlaintorchStatePolicyProcessor.cs:549`, `811`; `LoreIndex.cs:36`; `PuckIdService.cs:70`, `110`; `BacklogScheduleRecorder.cs:47`; `BacklogScheduleApiService.cs:128-133`; `PlaintorchOrbitService.cs:252-261`; `OnrushSprintApiService.cs:94`, `154`, `177`; `PolarisCycleApiService.cs:60`, `132`, `193`.
- **I-160 · Zone anchors read the host's day.** *(new in dev/phase3a)* A reset anchors the cursor at the host's today while seeking reads the zone's clock: a zone ahead of the host hardens its previous day on the next rolling pass, and one behind skips the rest of its current day (defect, by reading); the cycle-begin seek and previews also anchor lazy states on the host's today. *Systematic: time model; a clock trigger does not decide which clock.*
  Rows: SH-05, DM-22, DM-26. Where: `PlaintorchStatePolicyProcessor.cs:459-466`; `PlaintorchOrbitService.cs:153`, `170-171`, `202-203`, `233-237`.
- **I-161 · Zones are looked up per host.** *(new in dev/phase3a)* A stored zone is looked up on every read, and an id this host does not know reads as floating, silently. One row can therefore read differently on hosts with different zone data, and validation (I-006) checks the zone only on the host that writes it. *Systematic: the time model.*
  Rows: DE-15, DM-21. Where: `OccurrenceZones.cs:16-18`; `Declarative.cs:24-32`; `DeclarativeApiService.cs:511-520`.
- **I-162 · A default-calendar change reinterprets orbits with no reset.** Every unpinned declarative and timeframe orbit is re-read on the new calendar against a cursor built on the old one, and by design never reset. Cached `NextOccurrence` values stay stale until each row's next save or roll. *Systematic: a calendar-model decision; the engine could host the reaction once decided.*
  Rows: SH-05, SH-06, SH-07, SY-05. Where: `UserPreferenceService.cs:54-103`; `PlaintorchStatePolicyProcessor.cs:446-447`; `PlaintorchOrbitService.cs:42-52`, `358-361`.
- **I-163 · One-offs are detected two ways.** *(changed by dev/phase3a)* The API pins and keeps calendars on a `Z{` string prefix. SH-05 and DE-17 use the parser's lone-literal test. A `Z{…}` union is therefore pinned Gregorian as a one-off but never rewritten or anchored as one. *Systematic: orbit-notation interpretation.*
  Rows: SH-05, DE-04, DE-17. Where: `DeclarativeApiService.cs:558-559`, `684-687`; `OrbitDays.cs:203`; `PlaintorchStatePolicyProcessor.cs:498-499`.
- **I-164 · The due converter misreads and never validates.** "2026-07-20 14:30" (a space, not a "T") parses as an all-day due on 20 July with the zone "14:30" (defect, by reading). The zone is never checked, though dev/phase3a checks declarative zones (DE-15). An offset is converted to host-local time and a "Z" is kept as UTC, against the civil-time contract. A timed due at 00:00 is rewritten as all-day. *Systematic: markdown and time-model mechanics.*
  Rows: SM-19. Where: `Due.cs:67-87`, `98-103`.
- **I-165 · Pre-epoch dates throw.** The calendar cannot convert a date before 2023-03-21, so planning a cycle or titling a sprint for one fails with a raw `ArgumentOutOfRangeException`, while the orbit's Pleiadean calendar handles earlier years; `ToString` throws for year 1 (display only). *Systematic: calendar and time model.*
  Rows: ID-15. Where: `PleiadeanCalendar.cs:52-78`, `246-254`; `OrbitPleiadeanCalendar.cs:26-55`.
- **I-166 · Epoch window edge cases.** The default epoch has no unit and ends at its start; an unparseable duration ends an occurrence at its start; a manual month or year epoch without a duration ends a Gregorian month or year later, whatever its calendar; dues and slots test all-day by a midnight time, epochs by granularity. *Systematic: time model.*
  Rows: DM-02, DM-17. Where: `Occurrence.cs:32`; `Epoch.cs:52-62`; `OccurrenceDurations.cs:63-93`; `Due.cs:35`.
- **I-167 · Impossible ISO bounds are dropped silently.** *(changed by dev/phase3a: other tokens are now refused)* A bound with the ISO shape that names no date (`>2026-02-30`) parses, then fails `TryParseIso` and is skipped without an error; an ISO bound with an offset is shifted onto the civil timeline. *Systematic: orbit module.*
  Rows: DM-25. Where: `OrbitParser.cs:399-414`; `OrbitEngine.cs:1774-1784`; `OrbitCalendar.cs:159-173`.
- **I-168 · The 400-day horizon.** A schedule rarer than about 400 days caches no next occurrence and a longer period never matches a day; reflective selection restates the cover test. *Systematic: orbit module limits.*
  Rows: DM-28, DM-29. Where: `OrbitDays.cs:336-369`; `PlaintorchOrbitService.cs:205`, `236`; `ProximityMaterializationService.cs:86`.

## Domain model
- **I-169 · Manual eventives sit outside the dependency system.** *(new in dev/phase3a)* A manual eventive has no RECURRENCE-ID. No edge can name it, since the API requires a slot and matching is exact, and no slot lock can freeze it. *Systematic: the occurrence identity model.*
  Rows: LC-07, LC-11, DE-13, DP-02. Where: `DeclarativeApiService.cs:384-397`; `DependencyApiService.cs:387-391`; `DependencyEndpoints.cs:182-191`; `DependencyGateService.cs:176-181`.
- **I-170 · Kept work records lose their subject.** SH-01 keeps an ended cycle's executive but clears its incentive, and an executive has no title. The kept record says nothing about what was worked. *Systematic: the executive model.*
  Rows: SH-01. Where: `PlaintorchStatePolicyProcessor.cs:309-315`; `Executive.cs:10-90`.
- **I-171 · Deferring an executive moves the day's work out of its cycle.** Next-Polaris moves the row, carries its elapsed minutes in as the new envelope, and resets them. The source cycle keeps no record of the time worked. *Systematic: the model has one executive row per cycle.*
  Rows: PO-12. Where: `PolarisCycleApiService.cs:514-549`.
- **I-172 · A reschedule collapses granularity.** *(changed by dev/phase3a)* Setting or clearing an occurrence's time forces Minute or Day granularity. A week, month or year occurrence, manual ones included, loses its period. *Systematic: the API's epoch normalisation.*
  Rows: DE-11, DE-14. Where: `DeclarativeApiService.cs:426-430`, `474-478`.

## API surface and orchestration
- **I-173 · Operations commit in several steps.** Examples:
  - cycle begin commits the cycle, the reflectives, the note intent and the audit separately
  - creating today's cycle commits before activating it
  - a sprint commits before its milestone
  - planning a new objective commits it before the executive
  - next-Polaris commits the forecast before the move
  - a sprint delete takes two saves

  *Partly engine 3: API orchestration; one engine transaction per operation would collapse most of them.*
  Rows: ON-02, ON-08, PO-03, PO-04, PO-07, PO-12. Where: `PolarisCycleApiService.cs:85-91`, `155-163`, `355`, `362`, `412`, `540`, `569`; `ProximityMaterializationService.cs:122`; `OnrushSprintApiService.cs:41-42`, `74`, `145-146`, `163-164`, `216`, `224`.
- **I-174 · Adding to "current" starts a cycle before validating.** *(changed by dev/phase3a)* Planning an executive or adding a decree first auto-starts (or creates) today's cycle. Only then does it look up the objective or decree, check the decree is Active, and validate titles and uniqueness, so a refused request leaves a begun cycle behind. *Systematic: the API's orchestration order.*
  Rows: PO-05. Where: `PolarisCycleApiService.cs:342-392`, `708-717`, `860-869`.
- **I-175 · Status codes follow no convention.** Not-found is 404 on reads but 500 on writes. Argument, range and format exceptions and raw `DbUpdateException`s (FK, unique codename, a duplicate-day race) are all 500. Only three cases map to 400: preference validation, a blank lore PUCK, and an unknown directive kind. *Systematic: an API-surface convention.*
  Rows: EP-01. Where: `PreferenceModule.cs:41-44`; `LorePageModule.cs:46-82`; `DirectiveModule.cs:37-40`; `ObjectiveModule.cs:36-40`; `ObjectiveApiService.cs:97-98`.
- **I-176 · Refusal messages are hardcoded and uneven.** Service refusals are English literals, while the watcher's equivalents live on resx sheets. Some name no subject (directive delete, lore delete, milestone). The lunar endpoint message conflates not-found with wrong kind. *Systematic: an API-surface convention; the engine's typed violations would carry rule messages.*
  Rows: DI-05, DP-01, DP-11, LO-05. Where: `DirectiveApiService.cs:354`; `LorePageApiService.cs:166`; `DependencyApiService.cs:282`, `374`.
- **I-177 · Reactions are invisible to clients.** Purges, releases, bumps, settlements and dropped values appear only in the audit log. Affinities cleared by the database's SetNull are untracked and never announced. *Systematic: an API-surface concern; the engine's effect log could feed it.*
  Rows: SH-12, SH-18, SH-19, SH-20, DI-11. Where: `PlaintorchStatePolicyProcessor.cs:96-101`, `171-176`, `245-249`; `PlainfraContext.cs:455-469`.
- **I-178 · Requested ids are silently dropped.** Objective, stellar directive, fate, decree and checkpoint creates accept an `Id`. Their PUCK declarations are fully auto-minted, so the requested id is ignored without a signal. *Systematic: identity and transport.*
  Rows: ID-06. Where: `PuckCreationService.cs:43-46`, `81-102`; `ObjectiveModule.cs:44-46`; `DirectiveModule.cs:53-55`; `DeclarativeApiService.cs:76`, `217`; `DependencyApiService.cs:197`; `DependencyModule.cs:60`; `PlaintorchApiContracts.cs:428-436`, `460-467`.
- **I-179 · Standalone objective create drops the sprint.** The endpoint routes on `DirectiveId` alone, and the standalone path takes no sprint, so a requested `OnrushSprintId` is lost. *Systematic: endpoint routing.*
  Rows: OB-01. Where: `ObjectiveModule.cs:44-46`; `ObjectiveApiService.cs:64-67`; `PlaintorchApiTransportContracts.cs:76-80`.
- **I-180 · Request fields are ignored or one-way.** Examples:
  - the Polaris plan's `Body` and start-new's body are unused
  - a one-shot plan's `ExecutiveTitle`/`Title` is validated, then discarded, since executives have no title
  - `ExecutiveUpdate.ObjectiveId` reassigns to any incentive
  - an objective's sprint and a decree's `DefaultLength` can be set but not cleared

  *Systematic: the transport contracts.*
  Rows: OB-02, PO-01, PO-04, PO-07, PO-11. Where: `PolarisCycleModule.cs:56-63`; `PolarisCycleApiService.cs:33`, `64`, `348-351`, `438-442`; `ObjectiveApiService.cs:112-115`; `DeclarativeApiService.cs:265-268`.
- **I-181 · Intrinsic edges cannot be addressed.** They are served with Id 0, and `DELETE /api/dependencies/0` fails with "Dependency '0' was not found." (a 500). *Systematic: the API's representation of derived edges.*
  Rows: LC-06. Where: `DependencyApiService.cs:75-91`, `148-151`.
- **I-182 · Watcher-issue dismissal has quirks.** An unknown scope silently becomes Instance. The fatal guard is written in three layers, while restore has none. A File or Reason dismissal recorded while its status is inactive is stored but never applies. *Systematic: diagnostics.*
  Rows: SY-06, SY-07. Where: `SystemApiService.cs:259-304`; `OperationStatusRegistry.cs:139-143`, `198`, `238`.
- **I-183 · Preferences have no range rules.** *(changed by dev/phase3a)* Any value of the right kind is stored. A negative note-queue timeout means no bound, a negative daily reward grants nothing, and a negative base reflective count reads as 0. *Systematic: the preference catalog.*
  Rows: SH-16, SY-04. Where: `PreferenceApiService.cs:36-39`; `PreferenceDescriptor.cs:51-58`; `VaultWriteQueue.cs:163-167`; `PlaintorchStatePolicyProcessor.cs:1039`; `ProximityMaterializationService.cs:92`.
- **I-184 · Input normalisation is written per operation.** Inconsistencies:
  - a blank title is ignored in about twelve places
  - blank→null applies to codenames and order summaries on update only
  - timeframe colleges are dropped on create but re-applied after the clear on update

  So two directives created with an empty codename collide on the unique index with a raw error. *Systematic: API-surface convention; the engine could declare entity-level normalisation once.*
  Rows: OB-02, DI-02, DI-09, DI-10, ON-11. Where: `ObjectiveApiService.cs:101`; `DirectiveApiService.cs:134`, `139-142`, `235`, `240-243`, `485`, `545`, `678`, `707-721`, `765`; `OnrushSprintApiService.cs:336`, `419`, `460`, `465-468`; `PolarisCycleApiService.cs:176`; `DeclarativeApiService.cs:100`, `242`; `DependencyApiService.cs:225`; `LorePageApiService.cs:120`.
- **I-185 · The lunar/stellar refusal has six wordings.** *Systematic: API routing by family.*
  Rows: DI-01. Where: `DirectiveApiService.cs:128-131`, `229-232`, `280-283`, `336-339`, `503-506`, `530-533`.
- **I-192 · The SDK drops every refusal but state enforcement's.** *(found in Patch103.1)* Since Patch103.2 a state enforcement answer reaches the client's handler, which confirms and retries or shows the refusal. Any other response that is not OK is still skipped as though no transport had answered: the call resolves to `undefined` or `false`, and the body is discarded, so a client cannot show a service refusal's message (I-073). *Systematic: the SDK's transport.*
  Where: `sdk.ts/plaintorch/coreClient.ts:205-223`, `242-250`.

## Documentation and dead code
- **I-186 · Stale docs on the state rules.** *(changed by dev/phase3a: one remark now contradicts dev/phase3a's `Epoch`)* These say something the code no longer does:
  - The availability rule says the watcher cannot delete a directive whose note is gone; `.DISCUSSION.md:513-515` marks that resolved on 2026-09-26 (`PlaintorchStatePolicyProcessor.cs:362-365`).
  - The attentive reward doc says the key includes the cycle "when bound"; the code keys by decree and RECURRENCE-ID only (`PlaintorchStatePolicyProcessor.cs:776-781`, `823-826`).
  - The purge doc says an end entering or leaving Failed triggers the purge; any status change does (`PlaintorchStatePolicyProcessor.cs:106-109`).
  - `DependencyRules.cs:121-122` says an eventive slot may be omitted; the service refuses an eventive endpoint without one.
  - `.DISCUSSION.md:507-510` says a bare-context save gets the hooks; bare contexts carry only the connection interceptor.
  - The sprint-delete comment says SetNull foreign keys free the objectives; the key has no delete behaviour, I-121 (`OnrushSprintApiService.cs:187-193`).
  - Polaris comments say "a refused request leaves nothing behind"; that holds only for the affinity check, I-174 (`PolarisCycleApiService.cs:337-338`, `704`).
  - The eventive source's remarks call naive UTC comparison the codebase's convention, while `Epoch` says every comparison with "now" reads `LocalMoment` (`EventiveLifecyclePhaseSource.cs:8-13`).
  - `.DISCUSSION.md:309` still names the placeholder `Id == "0"`; it is `x0000`.
  - `LorePageSaveHook`'s remark says only a future reparent operation reaches its reassignment path and that `SetIndexAsync` passes no previous snapshot. `SetIndexAsync` now passes one, and the watcher's canonical rewrites can reach the path, I-012 (`LorePageSaveHook.cs:15-19`; `LorePageApiService.cs:287`).

  *Systematic: documentation.*
  Rows: SH-04, SH-14, SH-19, SH-21, SH-25, SH-26, LC-02, LC-06, ON-08, ON-10, PO-05, DP-02, DP-06, LO-07.
- **I-187 · Stale watcher and storage docs.** These say something the code no longer does:
  - `.DISCUSSION.md:373-375` and `Vault/.GENESIS.md:158-159` say unknown `LUNA` assertions are purged; they are left as `foreign-file`. The same `.DISCUSSION.md` passage says `lunarStatus` changes only through the API; the watcher applies a lunar `status:`.
  - `Vault/Watcher/.GENESIS.md:99` says an Enforced purge stands as an Error; it is graded Warning.
  - Incentive nesting, which is gone, and an executive's objective, which is exempt, are still named as delete blockers (`Vault/Watcher/.GENESIS.md:217`; `VaultEntityDeleteBlockedException.cs:8`; `VaultEntityGateway.cs:80-86`; `VaultEntityDeleteBlocker.cs:3-8`).
  - `OperationStatusDismissal.cs:12-14`, `29` say an Instance dismissal lapses when its status resolves and re-raises; it in fact matches by value until restored.
  - `ReadFilenameIdentity` carries two XML summaries (`VaultStoragePathComposer.cs:135-152`).

  *Systematic: documentation.*
  Rows: WA-14, WA-16, WA-27, WA-33, SM-07, SM-21, DB-17.
- **I-188 · Stale identity and domain docs.** *(changed by dev/phase3a: it fixed `Declarative.cs`'s calendar doc and added the zone and day docs)* These say something the code no longer does:
  - `Decree.Orbit` says a decree's orbit always resolves to day granularity; any granularity is allowed unless the decree reflects (`Decree.cs:34-35`).
  - The kind default calendar (decrees Pleiadean, fates Gregorian) is still described where the fallback is the vault default (`PlainfraContext.cs:306`; `OrbitDays.cs:46-55`; `OrbitPleiadeanCalendar.cs:10-11`; `OccurrenceHardeningService.cs:76`), and the projection still says "(Gregorian calendar)" (`AgendaProjectionService.cs:79`).
  - The placeholder is said to take a "final date-based identity" (`OnrushSprint.cs:21-22`) and to be "minted as `x0000`" (`OnrushPlanningPlaceholderRenameMigration.cs:16`).
  - `Puck/.GENESIS.md:22`, `34` recommend the `(x){I:4}` boundary syntax the parser now refuses and list `A{S:6}` as canonical, and `Puck/.GENESIS.md:26` says recorded ids enforce uniqueness.
  - `Declarative.TimeZone` and `Orchestration/.GENESIS.md:141-142` say a hand-added occurrence starts in the declarative's zone; a manual epoch stays floating unless it names one, as the contract says (`Declarative.cs:24-30`; `PlaintorchApiContracts.cs:508-512`).
  - `Orchestration/.GENESIS.md:118-121` says an eventive's begun and finished phases read `LocalMoment`; a floating one compares its civil moment as UTC, I-157.
  - The eventive flow in `Orchestration/.GENESIS.md:58` omits OptOut.
  - `PolarisCycle.Day` says a begun cycle's start day is its id's day, I-158 (`PolarisCycle.cs:44-49`).
  - `RewriteFixedLiteral` carries `FixedLiteralDate`'s summary beside its own, leaving that overload undocumented (`OrbitDays.cs:150-174`, `203`).
  - `Calendar/.GENESIS.md:9` names format tokens `Y`, `M`, `D`; the code reads `y`, `M`, `d`.

  *Systematic: documentation.*
  Rows: DP-02, ID-01, ID-09, ID-10, ID-15, DM-08, DM-19, DM-21, DM-23, DM-26.
- **I-189 · Cycle-day parsing is duplicated.** *(new in dev/phase3a)* `PolarisCycle.TryParseDay` and the processor's `TryParseCycleDate` are identical copies, and the forecast predicate is restated twice beside `IsForecast`. `PlaintorchDefaultTitleFactory.TryResolvePolarisDate` has no caller and a narrower catch, so a month-7 id would throw out of a "Try" method. *Systematic: duplication and dead code; the engine would drop the processor's copy.*
  Rows: SH-11, ID-09, DM-06. Where: `PolarisCycle.cs:42`, `58-74`; `PlaintorchStatePolicyProcessor.cs:700`, `1110-1124`; `PlaintorchDefaultTitleFactory.cs:39-60`; `PolarisCycleApiService.cs:237`.
- **I-190 · Dead code.** *(changed by dev/phase3a: the `FixedLiteralDate(string)` overload became test-only)* Nothing calls these:
  - Watcher: `VaultPathSyncModelCatalog.IsPartitionContainerPrimaryFile` (`VaultPathSyncModelCatalog.cs:410-433`). The incentive directive-mismatch check compares the path composer's parent with the same resolver's result, so it cannot fire, and its message says "Frontmatter…" for a field no frontmatter carries (`VaultMarkdownDiscoveryService.cs:851-854`, `874-888`; `VaultStoragePathComposer.cs:189-199`). The directive-parent mismatch is commented out.
  - Activation: the synchronous `PlaintorchEngine.InitializeVault`, `VaultBootstrapper.Initialize` and `PlainfraContextInitializer.Initialize` (`PlaintorchEngine.cs:32-40`; `VaultBootstrapper.cs:24-36`; `PlainfraContextInitializer.cs:19-23`).
  - Path readers: `ParsePuckIdentityFromPath`, `ApplyPuckIdentityFromPath` and `ApplyLoosePuckIdentityFromPath` (`MarkdownFileLocator.cs:160-175`, `187-217`).
  - Identity: `PuckCreationService.RequiresCallerInput` (`PuckCreationService.cs:104-107`).
  - Orbits: `OrbitEngine.Promote` and `IsAnchorStable`, which leaves the promoted-snapshot resume branch unreachable (`OrbitEngine.cs:252-258`, `269-284`, `317-338`); the Gregorian `FixedLiteralDate(string)` overload is test-only since dev/phase3a (`OrbitDays.cs:147-148`).
  - Lore: `ListActiveAsync`, on no interface and no route (`LorePageApiService.cs:51-57`), and the `today` that `LoadActiveLorePagesAsync` computes and never uses (`SystemApiService.cs:624`).
  - API: the aggregate `IPlaintorchApi` facade, registered and never resolved (`PlaintorchApiModule.cs:20`; `PlaintorchApiService.cs:1-36`).
  - The legacy `PlaintorchRepository`, of which only `GetCounts` is used. Its upserts save synchronously, which the state interceptor now covers, but they record no note intent and drop the fields they do not copy: icon, banner, availability, milestone, layout, due (`PlaintorchRepository.cs:27-226`; `PlaintorchEngine.cs:65`).

  *Systematic: dead code.*
  Rows: SH-25, SH-26, SH-28, LC-07, LO-08, WA-15, WA-18, PS-05, AC-06, AC-12, AC-13, ID-09, ID-13, DM-26, DM-27.
