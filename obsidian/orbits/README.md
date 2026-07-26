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

`index.ts` is the only plugin-authored file. It re-exports the vendored modules and adds:

- `humanizeOrbit(orbit)` — parse + humanize a raw notation into a `{ text, invalid }` phrase,
  falling back to the raw string when it cannot be parsed.
- `isValidOrbit(orbit)` — whether a raw notation parses.

Only `packages/node/src`'s parser/humanizer/calendar are vendored; the resolution engine
(`engine.ts`, `spans.ts`) is intentionally left out — the banners surface materialized
eventives from the core rather than resolving occurrences client-side.
