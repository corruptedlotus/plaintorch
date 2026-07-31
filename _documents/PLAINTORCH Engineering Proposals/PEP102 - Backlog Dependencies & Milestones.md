---
status: implemented
patches:
  - Patch102.1 - Dependency Graph Editor
  - Patch102.2 - Dependency Graph Usability
  - Patch102.3 - Milestone Binding and Graph
assignee: Soraya 🧙‍♀️
---
> [!idea]
> let's add a lock milestones or unlockable milestones whatever it is that instead of having their dependencies met of course they can have their dependencies met as well but their whole gimmick is that they are unlocked by paying a certain price it could be Celestron or something

![[PEP102 - Backlog Dependencies & Milestones 20260724003322]]

# Patches
## Patch102.1 - Dependency Graph Editor
The dependency system was complete in the core — edges, checkpoints, cycle validation, the satisfaction reconciler, the API and the SDK — with no surface at all. The only way to see or make a dependency was a raw API call. This patch adds the canvas that dependencies are *drawn* on: dragging from one entity to another creates the edge, clicking an edge changes or removes it. It renders like a mermaid flowchart but is interactive, which mermaid — a text-to-SVG renderer with no editing affordance and no stable node placement — cannot be.

### Context is the feature
The canvas is not one graph with a filter over it. What it shows, and what *adding or removing a node means*, is decided by the context mode. Adding a node is never only a visual act.
- **Onrush context (implemented).** The active or the planning onrush. Every objective the sprint holds is a node, **including the ones no edge touches** — the canvas is showing the sprint, and an objective with no dependencies is still part of it. Only objectives appear, because only objectives are what a sprint holds. Adding a node adds the objective to the onrush; removing it takes the objective out. The sprint is planned by arranging it.
- **Directive focus (deferred).** Everything under a directive, recursively, drawing dependency *and* parent-child edges, where adding or removing a node modifies the hierarchy and parent relationships can be edited directly. **Blocked on [[PEP103 - State Enforcement Engine|PEP103]]**: the reparenting endpoints already exist (`ObjectiveUpdate.ParentIncentiveId` with `ClearParentIncentive`, `DirectiveUpdate.ParentDirectiveId`, validated by `IncentiveParenting`), but PEP103 owns the gating and automation logic for entity states, and parent-child relationships fall under it. Writing parents from a canvas before then would mutate state the engine is meant to govern. The read-only half — rendering the subtree with its parent-child edges — carries no such dependency.
- **Freeform (deferred).** A curated set: any node can be added, optionally pulling in its dependencies with it; removing one hides it, optionally along with its connected graph. Here — and only here — add and remove are **show and hide**: a dependency that was defined stays defined after its node is hidden, and reappears when the node comes back. Needs somewhere to persist the curated set, which the plugin has today in neither `saveData()` nor per-leaf view state.

### Rendering
- Two layers share one transform: an SVG beneath for the edges, absolutely positioned elements above for the nodes. Keeping nodes as elements is what lets each one be a real entity item rather than something redrawn by hand inside the SVG — `p7t-canvas-node` derives from `EntityItem`, inheriting the entity watch, the theming and the notch/title/info structure every other surface uses, and adds only what a graph needs: the two connection handles, and the states a node can be in while the graph is edited around it.
- Placement comes from dagre (`@dagrejs/dagre`), left to right, prerequisites before dependants. Only the placement is taken from it, not its edge routing: a node the reader has dragged invalidates every polyline routed around where it used to be, so edges are drawn as curves between the two boxes, which stays honest wherever they end up. The engine is confined to `graphLayout.ts` — replacing it, or hand-rolling one, is a change to that file alone.
- The same component appears twice: the briefing's `planning` tab, and a workspace leaf of its own (`plaintorch-dependency-canvas`) opened by command, for when a graph deserves the whole pane or wants to sit beside the note being planned. The leaf is revealed rather than rebuilt when it already exists, so its pan, zoom and arrangement survive.

### Refusals happen where the gesture happens
The core is the authority on what is a legal edge, but the node transport resolves a failed request to nothing rather than throwing, so a rejected write is indistinguishable from silence unless it is checked for. Two refusals are therefore also made client-side, before the round-trip: an edge that would close a loop (mirroring `DependencyRules.EnsureNoCycle`) and an edge that already exists. Everything that still comes back empty raises a notice.

### Supporting changes
- `dependencyList`, `checkpointList`, `onrushCurrent` and `onrushPlanning` repository records, and `Checkpoint` added to the tracked entity types — it is a `PuckNamedEntity` and was already announced by the feed, but nothing absorbed it.
- `PlaintorchChangeFeedInterceptor` now announces a dependency edge as a change to both of its endpoints. An edge is neither a PUCK-named entity nor reachable through a foreign key, so before this, creating or deleting one broadcast nothing at all.
- The absorbing reviver now repairs cycle-truncated collections instead of merging them. `ReferenceHandler.IgnoreCycles` writes `null` where an object would recur inside itself, so fetching one objective returned its sprint with `objectives: [null]` and emptied the canonical sprint the canvas was drawing. Latent until the edge announcement above started causing those refetches mid-session; see the frontend repository notes in `core/.DISCUSSION.md`.
- Serving the resync burst safely: SQLite gained WAL + a busy timeout (`SqlitePragmaConnectionInterceptor`) so the reads fired around an edge write no longer meet a whole-file lock with no timeout; the node transport moved to a bounded keep-alive pool instead of a fresh connection per request; and the canvas observes only the onrush sprint of the mode it is showing. Together these stop the `read EPIPE` bursts a write triggered — see `core/.DISCUSSION.md`.

### Open edges
- There is **no `PUT /api/dependencies/{id}`**. Changing an edge's trigger or constraint is a delete followed by a create, which is not one transaction; the original is restored if the recreate is refused. Worth a real endpoint if edge editing turns out to be frequent.
- An eventive endpoint carries its owner's id under a different kind, so an edge to one occurrence of a recurring objective is not an edge to the objective, and is left out until a context can draw occurrences.
- Node sizes are fixed rather than measured, so a very long title is clipped rather than laid out around.
- Blocked state is derived from the unsatisfied edges *inside the current context*; an entity blocked from outside it does not read as blocked. `GET /api/dependencies/lock/{entityId}` is the authority when that matters.
- Checkpoints — this proposal's own milestones — are modelled and reachable through the SDK but have no canvas affordance yet. They are the natural freeform-context companion, since a checkpoint belongs to no sprint or directive.

## Patch102.2 - Dependency Graph Usability
A pass over the graph editor for the frictions that showed once it was used, plus the structural rules the editor made it easy to violate.

### Interacting with a node
A node's body was inert — a press anywhere on it selected and moved the node, so the title and notch inside could never be clicked. A node now has an **active** state: the first click focuses it (a reflected `[active]` flag, reserved as the hook a later pass expands its shown data behind), and only then do its own controls take their clicks. The connection handles stay live throughout, so an edge can still be drawn from an unfocused node; dragging an active node means dropping focus (a backdrop press) first. This is implemented by letting the focused node's shadow content take pointer events while an unfocused node's falls through to the container that selects and drags it.

### Reading an edge and a lock
- All **begin-triggered** edges now wear the start-circle tail that the begin-to-begin form had — the trigger is what the tail marks, whatever the edge gates. A **to-finish** constraint gets a "Finish" label near its arrowhead, so the two constraint kinds read apart at a glance.
- A **finish-locked** entity now reads as **Raced** in a softer amber rather than **Blocked** in red: only its finish is gated, a race it is expected to win, not a begin it is barred from. The distinction is computed from every edge that targets the node, so a lock from outside the onrush counts (correcting the earlier context-only blocked state); a begin gate wins when both are present.
- An objective in the onrush that is held back by a prerequisite **outside** it now pulls that blocker onto the canvas as a **ghostly** node — drawn faintly, never removable, and gone on its own once the block resolves (it simply stops being pulled in). This is what makes a block visible without making the blocker a member.

### Persisted layout
A sprint carries a `GraphLayout` column — a JSON map of node key to position, database-only UI state that never touches the vault, written after a drag through `PUT /api/onrush/{id}/graph-layout` (no markdown rewrite, no audit). Restored as the starting arrangement when a sprint opens. Keyed by node key, so a saved position applies to a node still present and is dropped for one that is gone, and a new node keeps its dagre placement: membership changes are absorbed by re-placing what is new, never by wiping the layout.

### Structural rules
Enforced where an edge is created:
- **No self-reference:** an endpoint cannot depend on itself, compared on kind and id so no occurrence slot slips it through.
- **Uniqueness:** at most one edge per source-target-trigger-constraint, compared on the *resolved* trigger and constraint so a defaulted (null) trigger and an explicit finish-trigger count as the same relation. A compound unique index on `Dependencies` supplements it, but is only a backstop — SQLite counts each null as distinct — so the application check is the authority.
- **Logical possibility:** the edge asserts `source.trigger` comes before `target.constraint`; it is rejected if the existing graph already orders those two the other way. This is a temporal-reachability walk over a two-sided (begin=0 / finish=1) state graph — a recursive SQL probe run against the database, since it is a question about the whole graph — and it **replaces** the old node-level acyclicity check rather than joining it. A temporal contradiction is always a node cycle, but a node cycle is not always a contradiction (A's begin gating B's begin gating A's *finish* orders cleanly as begin < begin < finish), so the coarse check forbade orderings this one correctly allows. A checkpoint has no begin or finish, so it is a dead end in the walk and its edges are always orderable — the intended handling, and the reason the walk's begin→finish step drops checkpoint states on its own (their side is null).

### Still needed
- Node sizes are still fixed, so a very long title clips. A ghostly blocker of a kind neither loaded nor an objective/checkpoint falls back to being labelled by its id.

## Patch102.3 - Milestone Binding and Graph
Every onrush now owns a checkpoint that stands for its completion, and checkpoints join objectives on the canvas.

### The milestone
An onrush is created with a **milestone checkpoint** (`OnrushSprint.MilestoneCheckpointId`, an optional 1:1) — attached whenever a sprint row is created, including the planning placeholder (`id 0`), and carried onto the real sprint when the placeholder is begun rather than replaced. Beside it, an onrush now **tracks checkpoints** as it tracks objectives (`Checkpoint.OnrushSprintId`, a 1:many, the milestone among them); a new checkpoint can be created already bound to a sprint (`onrushSprintId` on the create payload). Neither relation cascades — deleting a sprint detaches its checkpoints — and a checkpoint that is a milestone is refused deletion in the service; it goes only when its sprint does.

### On the canvas
Checkpoints, the milestone included, now appear in the onrush graph through a **separate node component** (`p7t-canvas-checkpoint`) with a **milestone** flag (a deliberately understated look, meant to be taken much further later). A checkpoint has no lifecycle, so:
- it shows no status and no lock badge;
- an edge to or from it leaves the corresponding side empty — a checkpoint source carries no trigger, a checkpoint target no constraint — enforced in the one create path both drawing and reshaping route through, and the edge menu withholds the trigger or constraint options for that side entirely;
- the milestone offers nothing to remove; a tracked checkpoint offers "Delete checkpoint"; a ghostly checkpoint is context, not a member.

### Schema
One migration (`DependencyUniquenessAndOnrushMilestones`) carries both patches: the `Dependencies` compound unique index and `OnrushSprint.GraphLayout` (102.2), and `OnrushSprint.MilestoneCheckpointId` + `Checkpoint.OnrushSprintId` with their `SetNull` foreign keys (102.3).
