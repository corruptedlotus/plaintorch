# Vendored Orbit scheduler

`ast.ts`, `calendar.ts`, `parser.ts` and `humanizer.ts` are copied **verbatim** from the
[`@pleiades/orbits`](../../../orbit-scheduler) package (`packages/node/src/*`). They power
the human-readable rendering of Orbit notation in the plugin — most visibly the
`p7t-editable-orbit` editable and the Fate/Decree banners.

They are vendored (rather than depended on) because `orbit-scheduler` is a sibling
repository outside `plaintorch`, is not published, and is not npm-linked into the plugin's
bundle. This mirrors the established pattern in `core/Orbit/` (a C# port of the same engine).

## Keeping in sync

To update, re-copy the upstream files and re-apply the header comments. Do **not** diverge
here: fix bugs upstream in `orbit-scheduler` first, then re-vendor. The vendored logic files
carry `// @ts-nocheck` so the plugin's strict `tsconfig` does not lint upstream code (same
convention as `assets/icons/index.ts`).

## Plugin surface

`index.ts` and `shortHumanizer.ts` are the plugin-authored files. `index.ts` re-exports the
vendored modules (and the short humanizer) and adds:

- `humanizeOrbit(orbit, short?, calendar?)` — parse + humanize a raw notation into a `{ text, invalid }`
  phrase. It runs the **model-based humaniser** (parse → normalize → realize; see below), falling
  back to the vendored humaniser for shapes the model does not cover yet — and, for the short
  register, to the legacy short humaniser first — and to the raw string when the notation cannot be
  parsed. With `short`, it returns the terse reading ("Mon @5&16") instead of the full one. `calendar`
  (`'gregorian'` | `'pleiadean'`, or a `CalendarSystem`) names months and weekdays; it defaults to Gregorian, so a
  surface must pass the calendar its entity resolves on — the SIPA components do, through `CalendarRef` (the
  entity's own calendar, else the vault's preferred one).
- `orbitCalendar(name)` — the shared `CalendarSystem` for a calendar name.
- `isValidOrbit(orbit)` — whether a raw notation parses.

Only `packages/node/src`'s parser/humanizer/calendar are vendored; the resolution engine
(`engine.ts`, `spans.ts`) is intentionally left out — the banners surface materialized
eventives from the core rather than resolving occurrences client-side.

### The short humanizer

`shortHumanizer.ts` (`OrbitShortHumanizer`) is **not** vendored — it is PLAINTORCH's own rendering
and follows the plugin's strict `tsconfig` (no `@ts-nocheck`). It predates the model humaniser and is
now the **fallback** for the short register: `humanizeOrbit(…, true)` tries the model's `realizeShort`
first and only reaches this for shapes the model does not cover. It exists for space-constrained
frontend chips (`p7t-schedule-item short`, the idle face of `p7t-editable-schedule`), where the full
phrase is too long to sit inline.

It mirrors the vendored humanizer's three shapes — the weekly-day shorthand, the clock shorthand,
and the regular unit assembly — but emits an abbreviated, **prefix-ordered** reading instead of
the long child-first one:

| notation | full (`humanizeOrbit`) | short (`humanizeOrbit(…, true)`) |
| --- | --- | --- |
| `w[d{3}[z{12:00}]]%3` | At 12:00 of 3rd day of every 3 weeks | Every 3 Wed @12:00 |
| `w[d{1,3,5}]` | 1st, 3rd, and 5th days of every week | Every Mon, Wed & Fri |
| `w{1}[d{3}]` | The 1st Wednesday | 1st Wed |
| `z{12:00}` | Every day at 12:00 | Daily @12:00 |

Conventions: weekday/month names clip to three letters; `at HH:MM` becomes `@HH:MM`; unit words
abbreviate (`yr mo wk day hr min sec`); lists join with `&`; set operators render as `+ & − ^`.
Phrases are lower-cased internally and only the first letter is capitalized by `humanizeOrbit`, so
a nested reading stays correct ("Day 2–4 of every wk"). Anything the compact serializer cannot
shorten cleanly it throws on, and `humanizeOrbit` falls back to the **full** phrase (never the raw
notation) — so exotic notation is only ever rendered long, not mangled. Because it re-derives the
shorthands rather than sharing them, keep it loosely in step with `humanizer.ts` when upstream
changes those.

## The model-based humaniser

The humaniser that now backs `humanizeOrbit` is restructured as a three-stage pipeline — a tiny compiler
whose target language is English:

```
parse (notation → AST)  →  normalize (AST → ScheduleModel)  →  realize (ScheduleModel → phrase)
```

The point is a **meaning layer** (`ScheduleModel`) between the parse tree and the words. It fixes the two
structural faults of the shape-matching humanisers: they pattern-match parse-tree *shapes* (so they silently
drop `limits` `*x @x <t >t` and `duration` `=<dur>`), and they are two hand-synced implementations that drift.

- `scheduleModel.ts` — the notation-*independent* meaning (`frames`, `time`, `span`, `bounds`, plus an
  `instant` for a fixed `Z{…}` datetime). `z{12:00}` and `h{12}[m{0}]` normalize to the same model. Limits
  and durations are first-class fields here. `NamedValue` carries an optional `shortName` for calendars whose
  short forms are not a plain clip.
- `scheduleNormalizer.ts` — AST → ScheduleModel (a single-pass structural tree fold; resolves weekday/month
  names and short names via the `CalendarSystem`). Each node's own limits are read against it, as the engine resolves
  them: a `*x` on an inner bare unit becomes a `first` selection ("the first 3 days of every week"), on the outermost
  frame the schedule's run (`Frame.repeat`, "for 10 weeks"); on a `%`-stepped index it keeps each run's first x steps,
  spelled out as the values they land on (`d{5}%3*4` is the 5th, 8th, 11th and 14th); on any other index it means
  nothing and is dropped. An `@x` keeps a nested list's first x values (`d[h{9,12,15,18}@2]` is 09:00 and 12:00), else it
  becomes a `count` bound per period of the node's written parent ("up to 7 times a month"), or in all at the top level.
  A `%` on a weekday or month is spelled out too (`w[d{1}%2]` is Monday, Wednesday, Friday and Sunday), bounded by the
  values the calendar names. A set operation nested under the frames (`M[d{15}+d{4}%4]`) becomes the schedule's
  `branches`: each side is a schedule of its own, read relative to the frame above it. A dated `Z{…}` becomes an `instant`; a bare `z{h:m}` a
  frameless daily clock. Throws `UnsupportedShapeError` for shapes not yet modelled (a set-op nested mid-chain,
  a non-clock time unit, a clock shape it cannot express) so the caller can fall back. A `%N` on a clock unit
  becomes the clock time's `step`: `h{9}%2` runs from 09:00 to the end of the day, `h{9}[m{10}%15]` to the end
  of the hour, and a bare `h{9}[m%15]` streams within the 09:00 hour. Before the step it was dropped outright.
- `scheduleRealizer.ts` — ScheduleModel → phrase, in **two registers over the same model**. Idioms cover the
  weekday of a month's week ("the 1st Monday of every month", "the first 2 Mondays of every month") and the weekdays of
  each week of a month, which a month counts from its first complete week ("every Monday, Wednesday, Friday, and Sunday
  from each month's first Monday, up to 7 times a month"); a plain parent that adds nothing is left out ("every day",
  not "every day of every month"; "every June", not "June of every year"). A week within a year reads as a calendar
  week ("calendar week 12", "Monday of calendar week 1", "CW12"). A nested set reads each side under the shared frames
  and says a shared parent once ("the 15th and every 4 days from the 4th of every month", "the 1st and 15th, except
  the 15th, of every month"), and two times of day under the same frames read as one clock ("every day at 09:00 and
  17:30"); top-level sets share a parent the same way ("the 1st and the 15th of every month"). The registers: `realizeLong`
  (full prose, e.g. "Every other Friday at 17:30", "The 5th of June 2027 at 18:00") and `realizeShort`
  (compact, e.g. "Fri /2w @17:30", "Mon @5&16", "5/Jun 2027 @18"). It does **not** import the calendar —
  names are already in the model; a register is just a different lexicon + ordering. Set operations read as
  connective prose ("…, but only when it also falls on …", "…, or …, but never both"); the short register
  parenthesises a nested compound. Both registers share the idiom recognizers and helpers.
- `scheduleDescribe.ts` — `describeOrbit(notation, calendar?)` runs the whole pipeline and returns the model
  plus both phrases; `resolveCalendar(name)` maps a name to a `CalendarSystem`. **Plugin-only** (the preview
  tool's calendar switch); upstream ships its own calendar-agnostic `describeOrbit`.
- `pleiadeanNaming.ts` — a **naming-only** Pleiadean `CalendarSystem` (six months, **Saturday-first** week,
  Gregorian arithmetic delegated, curated `getUnitShortName` → Nil/Sol/Xun/Tar/Lua/Tva). Its `max` is Pleiadean
  (61-day months, up to 10 weeks a month, six months a year), since the normalizer asks how far a unit runs when it
  spells a stepped index out. `humanizeOrbit` reads an
  orbit on it whenever the orbit's entity resolves on the Pleiadean calendar, and the preview tool uses it too.
  **Plugin-only** and not resolution-grade — a real Pleiadean calendar belongs upstream in `orbit-scheduler`.

`scheduleModel.ts`, `scheduleNormalizer.ts` and `scheduleRealizer.ts` have **graduated upstream** to
`@pleiades/orbits` (`packages/node/src/`) as the canonical model humaniser; the copies here are kept in sync
with it. They are strict-clean, so — unlike the older vendored files — they carry no `@ts-nocheck`.

### Preview tool

`sipa/orbit-humanize.tsx` is a dev CLI/REPL over `describeOrbit` for eyeballing both registers:

```
npx tsx sipa/orbit-humanize.tsx "w[d{1,3,5}]"            # one-shot (one or more notations)
npx tsx sipa/orbit-humanize.tsx --calendar pleiadean "y[M{6}[d{5}]]"
npx tsx sipa/orbit-humanize.tsx                          # REPL
```

`--calendar` / `-c` picks the reference calendar for naming (`gregorian` | `pleiadean`). No `node_modules`
needed — the orbits files are self-contained.
