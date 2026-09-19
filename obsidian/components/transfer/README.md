# Transfer Controller

Drag-and-drop exchange of items between components, in the component-controller model the plugin already
uses (`ContextMenuController`, `EntityRef`, `EntityWatch`). A host declares one `TransferController` as a field,
marks the rows it offers with a directive, and every other host holding a controller that *expects* that kind
of item becomes a drop target.

It has no dependency on the repository system — plain lit and DOM — but is shaped to sit beside it (see below).

---

## Vocabulary

| Term | Meaning |
|---|---|
| **kind** | The channel an item travels on. By convention the core's runtime type name (`'Objective'`), so any two hosts showing the same entity type interoperate with no further agreement. |
| **bag** | A host's own collection: what it offers, where the default receive appends, and what a `move` removes from. |
| **outbound** | What happens to an item on its *source* once a receiver has fully accepted it: `clone` (keep it, the default) or `move` (remove it). |
| **translation** | A receiver's `accept` hook, replacing the default "append to the bag" with whatever taking the item means for that host. |
| **transaction** | One item in flight: kind, item, outbound policy, source, target, state. Handed to every hook. |

---

## Usage

### A source that keeps what it hands out (clone)

```ts
protected readonly transfer = new TransferController<Objective>(this, {
	kind: 'Objective',
	bag: () => this.data?.objectives,
	outbound: 'clone',
	accepts: [],                                  // source-only: takes nothing back
})

// template
html`<p7t-objective-item .entity=${objective} ${this.transfer.draggable(objective)}></p7t-objective-item>`
```

### A receiver that translates the drop

```ts
protected readonly transfer = new TransferController<Objective>(this, {
	accepts: ['Objective'],
	canAccept: objective => !!this.data && !this.data.executives.some(e => e.objectiveId === objective.id),
	accept: objective => this.addObjectiveActivity(objective),   // resolves to whether it was taken
})
```

### A plain local list (defaults all the way)

```ts
protected readonly transfer = new TransferController<Tag>(this, { kind: 'Tag', bag: () => this.tags, outbound: 'move' })
```

Dropping a tag from another such list appends it here and, once appended, splices it out of the source — the
default receive and the default `move` removal, both by identity (`id` when present, else the instance).

### Rows that are both source and receiver (the backlog grid)

Every grid row holds its own controller (`GridItemBase.transfer`), so a row is a source for its own entity and,
where its variant allows, a receiver. A variant opts in through four small hooks rather than touching the
controller: `transferKind` (what the row's entity travels as — its runtime type name), `acceptedTransferKinds`,
`canTakeTransfer` and `takeTransfer`. The backlog row (`GridItem`) uses them to **reparent**: dropping any row on
a directive row moves it under that directive, through `reparentEntity`. `canTakeTransfer` asks `reparentRefusal`
up front — not onto itself or a descendant, lunar and stellar do not mix, not where it already is — so a row that
would refuse never lights up as a target. A collapsed row under a lingering drag opens, like a folder.

The world heading ("World Quests & Events") is the way back out: a drop there **de-parents** — `reparentEntity`
with no directive, which sends an explicit `null` parent — lifting a directive to the top of the tree and an
incentive into the world. The heading is inert outside a drag, and is emitted even when the world is empty
(`GridRow.vacant`), hidden until a drag that could use it starts; an entity already at the top level never
lights it.

The handle is the row's kind icon, not the row: a row is full of editables, and a draggable ancestor would take
text selection away from them. The second argument of `draggable` names what the pointer is seen carrying:

```ts
html`<p7t-media class='kind' ${this.transfer.draggable(row.entity, () => this)}></p7t-media>`
```

Because rows travel under their type names, an objective dragged out of the grid is also welcome wherever else
objectives are taken — the Polaris card adds it to the cycle.

---

## Lifecycle of a drop

1. **dragstart** on a marked row: the source opens a transaction under its kind. Every connected controller
   is asked once whether it is a **candidate** — it accepts the kind, is not the source, and its `canAccept`
   (default: the item is not already in its bag) passes. Candidates reflect `transfer-target` on their drop
   target; the browser cursor shows copy or move from the outbound policy.
2. **dragenter / dragover / dragleave** on a candidate's drop target reflect `transfer-over` while the pointer
   is over it. Enter/leave are depth-counted so crossing child elements never flickers.
3. **drop** on a candidate: the drag ends for everyone; the receiver runs `accept` (or the default append).
4. If accepted, the **source's outbound policy** applies — `clone` does nothing, `move` runs `remove` (or the
   default splice) — and both hosts re-render. `transferaccepted` fires on the receiver, `transfersettled` on
   the source (also on rejection). A rejection or a throw leaves both sides exactly as they were.
5. A drag that lands nowhere ends **cancelled** on `dragend`; nothing fires.

Styling hooks, on the drop target (the host by default, or `dropTarget()`):

```css
:host([transfer-target]) { outline: 2px dashed …; }   /* a compatible item is in the air */
:host([transfer-over])   { outline-style: solid; }    /* …and over this host */
```

---

## Alongside the repository system

- **By reference, not by copy.** `clone` is about the *source keeping its item*, not about duplicating the
  object — the receiver is handed the same instance. A canonical repository instance therefore stays canonical
  on both sides; nothing needs re-absorbing.
- **Identity agrees.** The default identity reads `id`, which is what repositories key on. A host may
  substitute `identify` from the SDK when it wants the full `{type}:{id}` key.
- **Repository-owned collections are not the controller's to mutate.** A host whose bag is an array the
  repository handed it (`sprint.objectives`, `cycle.executives`) must **translate**: `accept` through
  `repo.mutate` and let invalidation redraw the collection; `remove` through the matching API call for a
  `move`; and, if it never wants the default receive, say `accepts: []` (source-only) — which is what the Onrush
  card does. The defaults are for collections a component owns outright.
- **The receive resolves *after* the core answers.** Because `accept` is awaited before the outbound policy
  runs, a `move` never removes an item the core then refused.
