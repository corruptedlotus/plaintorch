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

- `humanizeOrbit(orbit, short?)` — parse + humanize a raw notation into a `{ text, invalid }`
  phrase, falling back to the raw string when it cannot be parsed. With `short`, it returns the
  terse reading (see below) instead of the full one.
- `isValidOrbit(orbit)` — whether a raw notation parses.

Only `packages/node/src`'s parser/humanizer/calendar are vendored; the resolution engine
(`engine.ts`, `spans.ts`) is intentionally left out — the banners surface materialized
eventives from the core rather than resolving occurrences client-side.

### The short humanizer

`shortHumanizer.ts` (`OrbitShortHumanizer`) is **not** vendored — upstream has no short form, so
this is PLAINTORCH's own rendering and follows the plugin's strict `tsconfig` (no `@ts-nocheck`).
It exists for space-constrained frontend chips (`p7t-schedule-item short`, the idle face of
`p7t-editable-schedule`), where the full phrase is too long to sit inline.

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

## The model-based humaniser (in progress)

A newer humaniser is being built beside the two above, restructured as a three-stage pipeline — a tiny
compiler whose target language is English:

```
parse (notation → AST)  →  normalize (AST → ScheduleModel)  →  realize (ScheduleModel → phrase)
```

The point is a **meaning layer** (`ScheduleModel`) between the parse tree and the words. It fixes the legacy
humanisers' two structural faults: they pattern-match parse-tree *shapes* (so they silently drop `limits`
`*x @x <t >t` and `duration` `=<dur>`), and they are two hand-synced implementations that drift.

- `scheduleModel.ts` — the notation-*independent* meaning (`frames`, `time`, `span`, `bounds`). `z{12:00}` and
  `h{12}[m{0}]` normalize to the same model. Limits and durations are first-class fields here.
- `scheduleNormalizer.ts` — AST → ScheduleModel (a single-pass structural tree fold; resolves weekday/month
  names via the `CalendarSystem`). Throws `UnsupportedShapeError` for shapes not yet modelled (a set-op nested
  mid-chain, a non-clock time unit) so the caller can fall back.
- `scheduleRealizer.ts` — ScheduleModel → phrase, in **two registers over the same model**: `realizeLong`
  (full prose, e.g. "Every other Friday at 17:30") and `realizeShort` (compact, e.g. "Fri /2w @17:30",
  "Mon @5&16"). It does **not** import the calendar — names are already in the model; a register is just a
  different lexicon + ordering. Both share the same idiom recognizers (weekly-weekday, anchored date,
  day-of-month) and helpers.
- `scheduleDescribe.ts` — `describeOrbit(notation, calendar?)` runs the whole pipeline and returns the model
  plus both phrases; `resolveCalendar(name)` maps a name to a `CalendarSystem`.
- `pleiadeanNaming.ts` — a **naming-only** Pleiadean `CalendarSystem` (six months, universal 7-day week,
  Gregorian arithmetic delegated) so the preview tool can show a non-Gregorian reading. Not resolution-grade —
  a real Pleiadean calendar belongs upstream in `orbit-scheduler`.

**Not done yet:** it does not replace `humanizeOrbit`; set-op connective prose is rough; the short form's
Pleiadean month names are approximate 3-letter clips ("Tār", not the canonical "Tva"); the `Z{…}` exact-datetime
literal is not modelled. Once it covers enough shapes it will fold into `humanizeOrbit` and graduate upstream.

### Preview tool

`obsidian/orbit-humanize.tsx` is a dev CLI/REPL over `describeOrbit` for eyeballing both registers:

```
npx tsx obsidian/orbit-humanize.tsx "w[d{1,3,5}]"            # one-shot (one or more notations)
npx tsx obsidian/orbit-humanize.tsx --calendar pleiadean "y[M{6}[d{5}]]"
npx tsx obsidian/orbit-humanize.tsx                          # REPL
```

`--calendar` / `-c` picks the reference calendar for naming (`gregorian` | `pleiadean`). No `node_modules`
needed — the orbits files are self-contained.
