# PLAINTORCH Obsidian Plugin

This is the initial scaffold for the PLAINTORCH Obsidian plugin.

## Current scaffold goals
- load a bundled PLAINTORCH webcomponent library
- detect PLAINTORCH markdown entities in reading view through the PLAINTORCH core API
- render the matching custom element at the top of the page
- expose a ribbon button that opens a briefing page with state actions

## Current interaction surface
- the briefing page can start or conclude onrush and Polaris according to the current API-reported state
- onrush objectives and executive-linked objectives are navigable links back into vault notes
- objective note decoration includes quick actions for adding the objective to onrush or Polaris
- objective note decoration also includes a directive picker that edits the note frontmatter so watcher-driven sync can reconcile the directive change back into core state

## Authority lookup
The plugin now asks the local PLAINTORCH core for the authoritative entity kind of a vault note by calling `/api/system/resolve-note`.

It prefers the local loopback HTTP transport at `http://127.0.0.1:43118`, with the per-user PLAINTORCH socket kept as a secondary fallback path.

If the core is unavailable, entity detection and PLAINTORCH rendering do not run.

## Development
1. Install dependencies with `npm install`.
2. Build once with `npm run build` or run watch mode with `npm run dev`.
3. Copy or symlink this folder into your Obsidian vault plugins directory.

## Bundled webcomponent library placeholder
The plugin currently imports a local placeholder bundle from `src/webcomponents/bundle/plaintorch-elements.ts`.

That is the handoff point for the future real bundle. When the actual PLAINTORCH element library exists, replace the placeholder implementation or redirect `ensurePlaintorchElementsRegistered()` to the final bundle entry.

A realistic future import shape would look like one of these:
- `await import("./vendor/plaintorch-elements.js")`
- `await import("@pleiades/plaintorch-elements/register")`

The current placeholder bundle keeps the plugin scaffold functional until that library exists.
