---
status: implemented
patches:
  - Patch102.1 - Dependency Graph Editor
  - Patch102.2 - Dependency Graph Usability
  - Patch102.3 - Milestone Binding and Graph
  - Patch102.4 - Graph Deep Editability
  - Patch102.5 - Onrush Management UI
  - Patch102.6 - Global Planning Mode
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

## Patch102.4 - Graph Deep Editability
The graph could draw entities and connect them, but not *edit them in place*. This patch makes the entity behind a node editable through its own banner, gives the checkpoint the endpoint its inline edits needed, and adds the state marks on edges and checkpoints that inline editing made worth reading — along with the frictions that inline editing surfaced once the fields were live.

### Editing an entity where it sits
A node now opens the entity's **own banner in a modal** (`EntityDetailModal`) — the same banner every other surface shows, so an edit made here reaches all of them at once and edits made elsewhere reach it, the banner resolving and observing the canonical instance by its PUCK id. It opens two ways: a **double-click** on the node, or the node's context menu, which grew a **Details** item at its head. The menu also gained **Delete** (routed per kind through `deleteEntity`) and, for an onrush member, **Remove from Onrush**, so the two senses of "take this off the canvas" — unmake it, or unmember it — are both reachable and kept apart. On the canvas the title no longer opens the note, since a title click is now the first half of a double-click; opening the note is the menu's job instead.

### The checkpoint, made editable
The checkpoint was reachable but frozen — no way to rename it or change its toll or condition once created. This patch closes that:
- **`PUT /api/checkpoints/{id}`** (`CheckpointUpdate`, `UpdateCheckpointAsync`), the endpoint the checkpoint lacked. The toll and the condition are optional, so each follows the house set-or-clear convention: a value sets it, a paired `Clear…` flag removes it, both unset leaves it. A negative toll is refused, and clearing a toll clears its paid flag with it — a toll that no longer exists cannot stand paid. This is the checkpoint's answer to the dependency edge's still-open "no `PUT`" note (102.1); a checkpoint, unlike an edge, is worth editing in place rather than deleting and remaking.
- A **`p7t-checkpoint-banner`**, the checkpoint's face wherever banners appear. Its name and its toll are inline edits; its condition is a tri-state control — **Require condition** when there is none, then **Mark met / Mark unmet** and **Remove condition** — and the standing **Pay toll** action remains. Every edit goes through the checkpoints repository, so it syncs to the node and every other surface at once.

### Reading an edge at a glance
The edge marks were rebuilt around the midpoint, where a mark sits on the line rather than crowding the arrowhead:
- The **to-finish** constraint's `Finish` text is now a **`state-raced` glyph** at the edge's midpoint, coloured to the connector — the same "raced, expected to win" reading the node badge carries, said on the edge itself.
- A **pending** connector is now **opaque**, a firm line rather than a faint one. A **satisfied** connector recedes to **half strength** and carries a **`state-done` glyph** at its midpoint — met, and stepping back. One pair of colour variables (`--p7t-edge-line`, `--p7t-edge-line-satisfied`) keeps the line, its arrowhead and its midpoint badge in agreement. The badge rides the HTML surface rather than the SVG, so it is the same themed icon every other surface draws — a `foreignObject` would strand the custom element in the SVG namespace and never upgrade it.

### A checkpoint's own state, in colour
A checkpoint aggregates rather than acts, but it can still be held back, and now shows it in the same two colours an entity node wears. An unmet incoming dependency is the severe **blocked**; once those are all met, an unpaid toll or an unmet external condition is the softer **raced**; nothing owed is plain. Having no card to tint, it colours its glyph and its label. The toll and condition are read through a nullish guard, so a checkpoint the core sends with a null (absent) toll or condition reads as owing nothing.

### Frictions inline editing surfaced
Live fields exposed three things that were latent while nothing was typed into them:
- **Editable fields clobbered their own caret.** Each `p7t-editable-*` rewrote its text on every render — including the render that focusing it triggers — which collapsed the selection and dropped what was being typed. It was why a banner's fields felt un-typeable in the modal. The text is now synced only while the field is idle, never mid-edit.
- **A toll could not be blanked.** The starfire editor settled on `NaN` for empty or non-numeric input; it now yields `undefined`, so clearing the field clears the toll.
- **A condition-less checkpoint read as one with an unmet condition.** The core serialises an absent `bool?`/`int?` as `null`, not a missing field, so the banner's `=== undefined` checks misfired — showing "condition required, not met" and the wrong buttons for a checkpoint that had no condition at all. Read through the nullish guard now. Checked against the core while there: nothing seeds a condition — `AttachMilestone` and `CreateCheckpointAsync` both leave it null, and the reconciler only reads it (`ExternalCondition != false`). The phantom condition was the client's null/undefined mismatch, not a stray write.

### Open edges
- **Double-click is detected by timing, not the native event.** The browser's `dblclick` is unreliable here: the first click is intercepted to bring the node forward, which changes what the second lands on, and that reliably suppresses the native event for a mouse (a touch double-tap survived it). A same-node second click inside a short window opens the details instead. The cost is that a quick second click on an *already active* node's own control can be read as a double-click; the window is short, and the modal is the deeper editor either way.
- The modal edits through the banner as-is; a checkpoint with no lifecycle shows less than an objective's banner does, which is correct but leaves the checkpoint banner sparse.

## Patch102.5 - Onrush Management UI
The graph could plan and run an onrush but not *manage* one: there was no way to start, create, activate, conclude or delete a sprint from the canvas, and no way to reach the sprint itself. This patch adds the two surfaces that close that — an offer where a graph is empty, and a management tray where a sprint is present — and the one endpoint the delete needed.

### The empty offer
An empty graph now says what can be done about it rather than only that it is empty. The **active** graph, with no active onrush, offers **Start now** (a `state-onrush` glyph — the core starts and auto-titles a fresh active sprint) and **Go to planning** (which turns the same canvas to the planning graph). The **planning** graph, with none in planning, offers **Create new** (a lucide star — a plan under a title the reader gives it). A sprint that merely holds nothing keeps its old nudge to add a member. The buttons live inside the mid-screen notice, which waives pointer events, so they sit in a layer that takes them back.

### The management tray
While a sprint is on screen, a tray sits bottom-left, mirroring the add-FAB across the viewport. Above it, three tallies read left to right — **objectives, checkpoints (the milestone among them), executive orders** — off the sprint's own collections. The tray itself is two controls:
- **Details** (a pen) opens the sprint's own window: its `p7t-onrush-banner` — the same banner every surface edits its name through — above a grid of its **executive orders**, in the spirit of the directive tab's grid but for one sprint's orders alone. Each order edits its name and its effective window in place and can be removed; a new one is added blank and named inline (`p7t-onrush-orders`, its list fetched and re-read around each write since orders are not a tracked repository).
- A vertical **three-dot** menu whose actions differ by mode. **Planning** offers **Activate** — refused when an onrush is already active, since only one runs at a time; on success the canvas turns to the active graph the plan has become — and **Delete**. **Active** offers **Conclude**, which sets the sprint's end date.

### Deleting a sprint
Delete is the one action with no endpoint behind it, so this patch adds **`DELETE /api/onrush/{id}`** (`DeleteAsync`). A sprint owns its milestone and its executive orders, so they go with it — the milestone is the very deletion the checkpoint service refuses on its own terms, done here because the sprint is going. Its objectives and any other checkpoints it merely tracked are **freed, not deleted**: they are independent entities, so their onrush membership is dropped (the objectives' notes re-saved to match) and they live on. The sprint and its milestone point at each other through two foreign keys, so the cycle is broken and the milestone removed in one save before the sprint itself is deleted in the next — deleting both ends of the cycle at once leaves EF unable to order the commands.

### Open edges
- Delete is offered only for a planning sprint, which is always the placeholder identity, so that is the path exercised. Deleting an *active* sprint through the API alone hits an ordering wrinkle in the same two-save flow and is left for when a surface actually calls for it.
- Activation reads the active sprint once to refuse a second; between that read and the begin, a sprint started elsewhere would still be caught by the core, which is the authority.

## Patch102.6 - Global Planning Mode
The freeform phase (PEP102 phase 3). The canvas had two *sprint-scoped* contexts — active and planning onrush — where the node set is a sprint's membership and adding or removing a node changes that membership. **Global Planning Mode** is a third context that is not sprint-scoped: a **user-curated set of arbitrary entities**, drawn with the dependency edges between them, where add and remove are **show and hide only**. A hidden node's edge is never touched, so it persists and reappears when the node is pinned again. It is independent of the deferred phase 2 (directive focus) and needs no PEP103, since it mutates no entity state.

### What a node is here
A global node is only *shown*, so the mix of kinds must read apart at a glance: every node wears its **type icon**, not the objective status the onrush contexts show (a `typed` flag on `p7t-canvas-node`; checkpoints already carry a type glyph). The set is built by `globalContext(pinned, dependencies, resolve)`, which draws exactly the pins plus every dependency running between two of them. Drawing, reshaping and deleting edges work as in any context — only node membership is show/hide.

### Finding a node to add
A global context draws from anywhere in the backlog, so adding one needs a cross-kind search rather than a single listing. **`GET /api/dependencies/endpoints?q=&take=`** returns a kind-tagged `EndpointHit { kind, id, title }` over the entity kinds that may be a dependency endpoint — **stellar directives, objectives, and fates**. Lunar directives, decrees, and Polaris-level records are never returned, because they cannot take part in a dependency. An eventive is an occurrence of a fate, so the search returns the *fate*; pinning a specific occurrence follows the dependency system's fate-per-instance recurrence and is a follow-up. The picker (`SelectEndpointModal`) shows each hit under its type icon. Adding may optionally pull in the new node's one-hop dependency neighbours; removing may optionally take the whole connected group of pins with it.

### Where a global context lives
Unlike an onrush, a global context has **no database home** — an onrush stores its arrangement in a `GraphLayout` column, but a global context is either scratch or a file:
- a **scratch tab** — a third *Global Planning* button beside the onrush tabs — held in the canvas's own state and lost on close unless saved; and
- a **`.p7tpx` file** in the vault: JSON of `{ version, pinned: EndpointHit[], layout }`, opened in its own leaf.

The canvas emits a `contextChanged` snapshot (pins + positions) on every change. In the scratch tab nobody listens; a `.p7tpx` file is hosted by `PlaintorchGlobalFileView` (a `TextFileView` bound to the extension via `registerExtensions`), which persists the snapshot back to the file. **Save to file…** in the scratch tab's toolbar graduates it into a `.p7tpx` and opens it; the **New global planning** command opens a fresh scratch context in a new leaf; opening any `.p7tpx` (file explorer, link, anywhere) routes to its view automatically.

### Open edges
- **Eventive occurrence nodes** are deferred — the search returns whole fates; pinning a specific `RECURRENCE-ID` occurrence is a follow-up over the same recurrence mechanism dependency edges already use.
- The endpoint search is a bounded three-table title match, not a real global search index; a proper search service is later work, and a better picker UI can sit on top of it.
- A ghostly-blocker overlay (as the onrush contexts draw) is not applied here — a global context is an explicit set, so nothing is pulled in uninvited beyond the optional add-with-dependencies.
