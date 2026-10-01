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
| A row's cells, bindings, entity watch, context menu, drag and drop | the row element (`GridItemBase` and its variants) |

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
  inside the text fields the cells are full of. Row-level keyboard navigation needs a design of its own. Cells carry
  `role='gridcell'` themselves.
- **Row reordering and selection.** Rows move by reparenting, through the row's own drag and drop (see
  `../transfer/README.md`). Nothing selects rows yet.

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
