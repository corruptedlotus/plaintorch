# Search and filter

A search field that narrows items by free text and by **filter clauses**, `key:value`, which turn into chips once
committed. The behaviour is a headless controller, `SearchFilterController`; any component can be its face. The one
face so far is `p7t-search-box` (`SearchBox`), which the backlog page uses to narrow the backlog grid.

---

## Who owns what

| Concern | Owner |
|---|---|
| The filters, their values, and what each value lets through | the host, as `SearchFilter` definitions (the backlog's are in `../backlog/backlogFilters.ts`) |
| What the typing means, what the drawer offers, committing, chips, the keyboard, the criteria | `SearchFilterController` |
| The input's value | the controller, through its `input` reference (see below) |
| Drawing the field, the chips and the drawer, and handing their gestures back | the face (`SearchBox`) |
| Narrowing what is shown by the criteria | the host (`EntityGrid` draws each result inside its hierarchy) |

## Defining filters

A filter is a plain object:

```ts
const statusFilter: SearchFilter<Item, Status> = {
	key: 'status',                  // written before the colon
	label: 'Status',                // "Filter by Status", and the chips' prefix
	multiple: true,                 // several chips combine as a union; otherwise a new chip replaces the old one
	options: chips => [...],        // the values on offer, given the chips standing (one filter may narrow another's values)
	matches: (item, status) => ..., // whether an item passes a chip of this value
	unset: item => ...              // optional: whether an item passes while no chip of this filter stands
}
```

An option has a `text`, the one-word value written after the colon (`objective`, `opt-out`, `launch-the-atlas`), plus a
`value` handed to `matches`, a `label`, and an optional `icon` and `detail`. Typing is matched against the text and the
label. A value counts as typed out in full when either matches, ignoring case and treating spaces and hyphens alike.
A chip taken apart writes its text back.

`unset` is a filter's **default**. The backlog's status filter hides archived states until a status is chosen, and
`status:all` is simply an option that lets everything through.

## Writing states

The text is free search until a filter's key and a colon are typed. From the last such `key:` to the end of the text
is the **clause being written**, and everything before it stays free search.

- **No clause being written.** While focused, the drawer offers the filters, narrowed to those whose key or label starts
  with the word being typed. All of them show when no word is being typed, and the drawer closes when none matches,
  since the reader is searching titles. Choosing a filter writes its `key:` in place of that word.
- **A clause being written.** The drawer offers that filter's values (`Filter by Status`), narrowed and ranked by what
  follows the colon: an exact match first, then fuzzy matches (the select components' `fuzzyScore`). Values already
  chipped are left out. With nothing matching, it says so.

## Committing a chip

A value becomes a chip, and applies, when:

- it is chosen from the drawer (Enter, or a click);
- **Tab** is pressed while a value is highlighted *and* something has been typed after the colon, or the arrows chose
  the row. With nothing typed and nothing chosen, Tab moves focus as usual;
- it is typed out in full and followed by a space (`type:objective `);
- the field loses focus on a value typed out in full.

## Chips

- Each chip is split, like a split button: its body says `Status: Active`, its far side (×) removes it.
- **Backspace** in an empty field removes the last chip.
- Clicking a chip's body **takes it apart**: it leaves the chips and is written back as its clause, ready to edit.
  Committed again, it returns to where it stood. **Escape** restores it as it was. Leaving the field commits it as typed
  if that still names a value, and otherwise restores it.

## Keys

| Key | Does |
|---|---|
| `ArrowDown` `ArrowUp` | Moves the drawer's highlight (and reopens a drawer Escape closed). |
| `Enter` | Takes the highlighted row: a filter starts its clause, a value commits. |
| `Tab` | Takes the highlighted row when the typing narrowed to it or the arrows chose it; otherwise moves focus. |
| `Backspace` | In an empty field, removes the last chip. |
| `Escape` | Restores a chip being edited; otherwise closes the drawer; then clears the text. |

## Criteria

`SearchCriteria` holds the free `text`, the `chips`, `active` (anything asked beyond the defaults), and `matches(item)`.
An item passes when its text (`textOf`, usually its title) holds every word of the free search, and every filter passes
it: any of that filter's chips, or its `unset` default when it has none. The controller announces the criteria through
`handleCriteriaChange` whenever they change, and once after its host first renders. `results(items)` filters a flat list.

## The input

The face puts the controller's reference on its input (`<input ${search.input.ref()}>`) and binds no value. The
controller listens to the input and writes it whenever it rewrites the draft. A value binding would compare against
what it last wrote rather than what the reader has typed since, so a clause committed into a chip would stay on screen.
The input is also stamped as a combobox: `role`, `aria-expanded`, `aria-autocomplete`.

## The face

`SearchBox` draws the glyph, the chips (`p7t-filter-chip`), the input and a clear button. Its drawer is the select
components' floating list (`p7t-select-list`, with its `heading` and `emptyNote`), hosted through @3mo's `popover`
directive, with one self-styled row element per row (`p7t-search-drawer-item`). Like `SelectBase`, it binds the drawer's
state in the popover's template and also pushes it on every update. The directive creates the list at browser idle
time, too late to follow a keystroke, and a list created after the last update must still open with the current state.
