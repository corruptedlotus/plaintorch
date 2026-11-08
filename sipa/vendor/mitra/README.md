# Mitra embed (vendored)

`embed.js` is the embeddable calendar bundle of [Mitra](https://github.com/a11delavar/mitra) — its `dist/embed.js`, a
self-contained IIFE that defines the `<mitra-embed>` element: a Mitra calendar view fed by the host page instead of
Mitra's backend, with per-day slots the host fills with its own elements. PLAINTORCH's Forecast tab is one such host:
a horizontally scrolling strip of days, each slotted with that day's Polaris column (`p7t-polaris-column`).

`VERSION` names the Mitra commit the bundle was built from.

## Why vendored

Mitra is not published as a package, and a `file:` link would tie every checkout of this repository to a sibling
Mitra checkout at a fixed path. The built bundle is what an embedding page consumes anyway, so it is checked in as it
is and refreshed deliberately:

```bash
npm run vendor-mitra -- <path to the Mitra checkout>
```

The script builds Mitra there, copies `dist/embed.js` here and updates `VERSION`.

## How SIPA loads it

`components/forecast/mitraEmbed.ts` imports the bundle lazily, the first time a surface needs `<mitra-embed>` — the
bundle is large (the editor and provider code of Mitra come with it) and installs globals of its own (`t`/`Localizer`
from `@3mo/localization`, a Temporal polyfill where the runtime's is incomplete), so nothing else pays for it until
the Forecast tab opens. The hosts bundle it from source like the rest of SIPA; esbuild inlines the dynamic import.

The bundle carries its own copy of lit. That is deliberate and safe: its elements are all `mitra-*` (plus
`lit-application-*` / `lit-page-*` from `@a11d/lit-application`, which SIPA does not define), so the two runtimes
never register the same element. SIPA elements slotted into a Mitra view are SIPA's own, rendered by SIPA's lit.

`mitraEmbed.ts` also types the slice of the element SIPA drives (`view`, `date`, `dayCount`, `dayFill`, `range`,
`refreshDayContent`, the `range-change` event).
