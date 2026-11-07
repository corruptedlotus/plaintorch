# Grids

The backlog (`p7t-entity-grid`), the lore hierarchy (`p7t-lore-grid`) and the Moonlight page's Lunar Reflection
(`p7t-lunar-reflection-grid`, in `../moonlight`) are one grid system: a shell, `GridBase`, that owns the structure,
and row elements, `GridItemBase` variants, that draw each line. The shell's structure is 3MO's
**`DataGridController`** (`@3mo/data-grid/controller`), a headless data grid the shell renders its own way. Nothing
of 3MO's visual `mo-data-grid` is used or bundled.

A variant need not nest. Lunar Reflection's tree is flat, one leaf node per decree, so it takes the header, the
column arrangement and the row lead without any expansion. Its nodes carry an extra field (the owning directive), which
reaches the row unchanged, since a `GridRow` is the node spread with the grid's placement.

---

## Who owns what

| Concern | Owner |
|---|---|
| The tree's **shape**: which node sits under which, sibling order, cycles, the world heading | the variant's builder (`buildEntityTree`, `buildLoreTree`) |
| **Flattening** the tree into records with level, position and set size | the controller (`records`, via `@3mo/hierarchy`) |
| Which rows are **open** | the controller (`details`, an `@3mo/expandability` controller keyed by `GridNode.id`); `GridBase` opens them while a search is on |
| Which entities a tree shows (search results with their ancestors) | the variant's builder, given the variant's criteria (`buildEntityTree(source, include)`) |
| The **columns**: definitions, and what the user did to them (width, order, visibility) | the controller (`columns`) |
| **Treegrid semantics** on the grid, the header and the rows (`role`, `aria-level`, `aria-setsize`, `aria-posinset`, `aria-expanded`, `columnheader`) | the controller, stamped onto the parts it is handed |
| Rendering: the sticky header, the column tracks, the row elements, the FAB | `GridBase` |
| The rows' tab stop, and moving the focus between rows | `GridBase` |
| A row's cells, bindings, entity watch, context menu, drag and drop, double-click, its own keys | the row element (`GridItemBase` and its variants) |

The builders emit a **tree of `GridNode`s**. A node's `children` hold its sub-nodes, and its `guides` name the
lining of each indent lane, so the row can draw the lanes itself. The controller walks `children`. Its expansion
keys come from a field named `id`, so `GridNode.id` is load-bearing: it is the row key (`identify(entity)`), stable
across rebuilds.

`GridBase` rebuilds the tree once per update, in `willUpdate`, so every read within an update sees the same tree.
It renders the **visible** records: every root, and the children of every open record. A node with
`collapsible: false` never opens or closes, so its children always show; the world heading is one. Each record
reaches its row element as a `GridRow`: the node, plus whether it is `expandable`/`expanded`, plus the keys of the
visible `columns` in display order. The row element also carries the record's registration,
`${this.grid.row(record)}`, which is how the controller stamps its place in the tree onto it.

## Columns and the header

A variant declares its columns once, as a field. Keep the same array for the grid's lifetime, since a new array
re-syncs the columns:

```ts
protected override readonly columns = [
	GridBase.titleColumnDefinition(),
	GridBase.column(EntityGridColumn.Measure, 'Measure'),
	GridBase.column(EntityGridColumn.Status, 'Status')
]
```

- A column is known by a **key**, not by a path into the data, because the row element draws each cell itself:
  `cellContent(key)`. The title column (`gridTitleColumn`) is drawn by `GridItemBase` as the lead cell: indent
  lanes, notch, kind icon, leading label and title.
- The **title column** is flexible (`minmax(7em, 1fr)`) and pinned first. It is `sticky: 'start'`, which the
  controller treats as "cannot be moved or dropped before". It is not drawn sticky. Once resized, it keeps its
  7em floor.
- The tracks are `[one per visible column] auto max-content max-content`. The `auto` filler takes the room a
  resized title gives up, so the open-note and add columns stay at the far edge. The rows draw a `.filler` span
  for it. The add column's content is the row's `trailingAction`: the add button by default, or a variant's own
  icon button where it has nothing to add (Lunar Reflection's "Edit decree").
- **The header** sticks to the top of whatever scrolls the grid, at `--p7t-grid-header-top`. That defaults to
  `--p7t-page-sticky-top`, the height of the briefing's sticky tab bar, which the briefing measures and hands down
  (`0` outside the briefing). A page with sticky chrome of its own sets `--p7t-grid-header-top` below that (the
  backlog adds its search bar). Its surface is `--p7t-grid-header-background`.
  - Dragging a heading moves its column.
  - Dragging a heading's edge resizes the column. A dashed guide follows the pointer, and the new width applies
    on release.
  - Double-clicking the edge fits the column to its content.
  - The chevron button in the title heading opens or closes every row.
  - Right-clicking the header opens a menu: show or hide columns, fit all columns, reset, expand or collapse all.
- The **arrangement** (widths, order, hidden columns) is kept in `localStorage` under
  `plaintorch.grid-layout.<layoutKey>` (`backlog`, `lore`, `lunar-reflection`). "Reset columns" clears it.

## What is deliberately not used

The controller also offers sorting, selection, row reordering, cell navigation, inline editing, virtualization and
CSV export. They are inert here:

- **Sorting.** Each column is `sortable: false`. Sibling order is structure, not presentation. A directive's
  subdirectives come before its incentives, and the world heading comes last. The controller sorts every sibling set
  as one list, which would interleave the two groups and move the heading. Status values are also per-kind enums,
  so they do not compare across kinds.
- **Cell registration (`grid.cell`)**, and with it arrow-key cell navigation. The controller's cell keyboard
  grammar acts on any key event inside a registered cell. It turns Enter into a click on the cell, which swallows a
  focused button's activation. It also moves between cells on arrow keys and copies the whole cell on Ctrl+C, even
  inside the text fields the cells are full of. The grid moves between rows instead (see "Keyboard"). Cells carry
  `role='gridcell'` themselves.
- **Row reordering and selection.** Rows move by reparenting, through the row's own drag and drop (see
  `../transfer/README.md`) or the "Move to…" row of the entity's context menu, which opens the directive picker
  (`SelectDirectiveModal`) over the directives the row may go under — the same rules the drop applies
  (`reparentRefusal`), the same write (`reparentEntity`). An entity's banner edits its place the same way, through
  `p7t-editable-hierarchy` (`../entities/EditableHierarchy.ts`). Nothing selects rows yet.

## Lore starts open

`LoreGrid` sets `expandsByDefault`. A row opens by itself the first time it has anything to open. The user's
collapses are kept across rebuilds, and a row that newly gains children still opens. This is the old
"toggled set tracks collapsed rows" behaviour, expressed through the controller's expansion state.

## Searching the backlog

`EntityGrid` takes `criteria` (`GridCriteria`: `matches(entity)` and `active`), which the backlog page
(`../backlog/BacklogView.ts`) gets from its search box (`../search/README.md`).

- **Results stay in their hierarchy.** `buildEntityTree(source, include)` keeps an entity that passes, and keeps a
  directive that does not pass while anything beneath it does. A result is never shown without its ancestors. The world
  heading keeps the incentives without a directive that pass. With none, it stays as the vacant drop target.
- **The defaults are criteria too.** With no `status:` chosen, the backlog's status filter hides the archived states,
  so an archived directive appears only as the ancestor of something that is not archived.
- **Rows open to reveal results.** While a variant's `searching` getter is true (`criteria.active`: words or chips,
  not defaults alone), `GridBase` opens every row with anything to open, the first time the search shows it. A row the
  reader closes during the search stays closed. When the search ends, exactly the rows that were open before it are
  open again.

## Rows done with

A row whose entity is in a terminal state is drawn dimmed: done, fulfilled, failed, cancelled, archived, over, opted
out, abandoned, stale (`isInactiveStatus`, set on the status table in `../entities/StatusItem.ts`). It dims its kind
icon, title and cells, but keeps its indent lanes and action columns at full strength. A variant says so through
`GridItemBase.inactive`; `GridItem` reads it off the entity with `isInactiveEntity`.

## Double-clicking a row

Double-clicking a row's open space opens its details: `GridItemBase.openDetails()`, which by default opens the entity's
full banner in a modal (`openEntityEditor`, the context menu's **Edit**). A variant overrides `openDetails` to open
something else or nothing. The world heading has no entity and opens nothing. The gesture itself is
`DoubleClickController` (`../design/DoubleClickController.ts`), which any component can declare. Enter on a focused row
opens the same details (see "Keyboard").

- **Open space** is anywhere no part of the row answers a press of its own: the indent lanes, a childless row's notch,
  the room around the title and in the cells, plain labels (lore's level), and the title itself while it is idle,
  since its edit button is the way into editing it. What answers a press is excluded: buttons and other form
  controls, anything with an interactive role (the expanding notch is `role=button`, the schedule chip too), anything
  focusable, the drag handle (the kind icon), an editable a press would start editing (`EditablePart.answersPress`),
  and anything whose cursor promises an interaction the row's own does not. Both presses must land on open space.
- **Never mid-edit.** A press made while anything in the row is being edited does not count: an editable holding its
  edit (`EditablePart.editing`), a focused field taking text, or a bubble raised from inside the row
  (`InteractionBubbleBase.isRaisedFrom`). That press commits or dismisses, as it always did. The state is read on the
  window's capturing `pointerdown`, before the bubble's light dismiss and the field's blur get to it. Edits elsewhere
  (the backlog's search box, another row) do not hold a row back.
- **No selection.** The second press of a double-click on open space has its default prevented, so the word under it
  is not selected. Inside a field being edited, a double-click still selects the word.
- **Mouse and pen** use the platform's `dblclick`, timed by the system's setting. The dependency canvas times its
  clicks itself because its first click changes what the second lands on; a press on a row's open space changes
  nothing, so that concern does not apply here.
- **Touch** has no reliable `dblclick`, so two taps on open space within 450ms and 24px count instead, and the
  `dblclick` a web view may add afterwards is ignored. Rows set `touch-action: manipulation` so a double tap is not
  taken for a zoom. A long press (the context menu) and a press-and-hold (lifting for drag) make no click, so neither
  collides with it.

## Keyboard

The rows are reached the way a tree's are: one row in the tab order, and the arrows moving the focus between rows.

- **One tab stop.** `GridBase` gives the row last focused or pressed `tabindex=0` and every other row `-1`, so Tab
  reaches the grid's rows once rather than once per row. Before any row was focused or pressed, the first row holds
  it. A press moves the tab stop without taking the focus, so the mouse behaves as before. Rows without an entity (the
  world heading) are passed over and carry no `tabindex`.
- **The row's own keys** (`GridItemBase.onKeyDown`): Enter opens its details (once, however long it is held), the right
  arrow opens a closed row, and the left arrow closes an open one.
- **The grid's keys** (`GridBase.onRowKeyDown`), for what the row leaves unanswered: Up and Down step to the row above
  or below, Home and End to the first and last, the right arrow from an open row to its first child, and the left arrow
  from a row that does not close to its parent. The left arrow never steps to the world heading. An arrow at either end
  is still claimed, so the page does not scroll out from under the focused row.
- **Only while a row itself has the focus.** A key pressed in a field or a control inside the row reaches that field
  alone: the arrows inside a title being edited move its caret, and Enter commits it. Keys with a modifier are left
  alone.
- **The ring.** A row reached by keyboard wears an accented outline (`:focus-visible`); a focused row is scrolled clear
  of the sticky heading row by its `scroll-margin-block-start`.
- Not done: typeahead, PageUp and PageDown, and a key that starts editing the title. On a focused row the platform's
  context-menu key (Shift+F10, the Menu key) is meant to raise the row's menu through `ContextMenuController`; that was
  not verified, because the browser pane's synthetic keys do not raise it.
