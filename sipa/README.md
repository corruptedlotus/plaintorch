# @pleiades/sipa — Sunnyside Interface for Pleiades Affairs

PLAINTORCH's platform-neutral UI: the lit web components (briefing, entity and occurrence items, cards, editables, the
dependency canvas, dialogs), the orbit humaniser (`orbits/`) and the icon/design assets (`assets/`). It is bundled
from source by its hosts — the Obsidian plugin (`../obsidian`) today and the standalone shell (`../standalone`) once
it mounts the UI — the same way both consume `@pleiades/sdk`.

The plan that governs this package is PEP110, *Sunnyside Mk1 — SIPA*: the UI becomes SIPA-first and Obsidian becomes
one adapter among two.

## Layout
- `index.ts` — the package entry; importing it registers every custom element (`export * from "./components"`).
- `components/` — the UI. Imports inside the package are relative; hosts import from `@pleiades/sipa` only.
- `orbits/` — the orbit notation humaniser (`@pleiades/sipa/orbits`); `orbit-humanize.tsx` is its dev REPL.
- `assets/` — SVG/PNG assets, indexed into `assets/*/index.ts` by `npm run index-assets`.

## Transitional coupling
Until the `PlatformHost` inversion completes, parts of `components/` still import `obsidian` (dialogs, `Notice`,
icons, navigation, `window.app`) and the barrel still exports the node `core` client. That is why `obsidian` and
`@types/node` are dev dependencies here; both go when the host and core-provider inversions land (P2–P7). Hosts
mark `obsidian` external. The components also read Obsidian's theme CSS variables (`--text-normal`,
`--interactive-accent`, `--background-primary`, …) by name, and are meant to keep doing so: a non-Obsidian host
defines those variables in its own stylesheet.

## Build and typecheck
There is no build step of its own — hosts bundle the sources with esbuild, which applies this package's
`tsconfig.json` to its files. That config pins `experimentalDecorators: true` and `useDefineForClassFields: false`:
`@a11d/lit` installs its reactive accessors through legacy decorators, and define-semantics class fields would shadow
them (a view renders once and never updates).

`npm run typecheck` checks the package on its own. The Obsidian plugin's `tsc` covers the same files through its
import graph.

The package owns the UI dependencies (`@a11d/lit`, `@3mo/*`, dagre, …) in its own `node_modules`. Hosts must not
import lit themselves from a second copy: esbuild follows the `file:` link to this directory, so every import of lit
from package code resolves here, and a host-side copy would register a second lit runtime.
