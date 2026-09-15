# PLAINTORCH desktop shell

The per-user desktop package of PLAINTORCH: a tray application built with Electron that either **hosts** the core
(standalone flavour) or **attaches** to one that is already running (client flavour). Both flavours are one codebase
and one build; the flavour is baked in at build time.

## What it does

- **Standalone flavour** spawns the published core beside the app (`resources/core`) in spawn mode
  (`plaintorch serve --spawn --profile <root>`), supervises it, restarts it after an unexpected exit, and stops it
  gracefully on quit by closing its stdin.
- **Client flavour** ships no core. It probes the profile's endpoint (the named pipe on Windows, the unix socket
  elsewhere) and follows whatever core answers: the standalone app, or the console daemon.
- A frameless **splash** mirrors the core's startup phases, driven by the JSON lines the core writes to stdout, so
  nothing is probed while the core comes up. A failure stays on screen until dismissed.
- A **tray icon** with the phase, the active vault, activate/deactivate, start-at-login, the logs folder, restart, updates, and quit.
- A **status window** with the same, plus a briefing of the active vault read through the SDK over the preload bridge.
- **Start at login** through the OS login-item API on Windows and macOS, and an XDG autostart entry on Linux.
- **Updates** through electron-updater in the "download the new installer and run it" flow; see `../installer`.

## Profiles

A development build targets `~/.pleiades/plaintorch-dev`, exactly like a manual `serve` and a dev Obsidian plugin, so
it never collides with an installed shell on `~/.pleiades/plaintorch`. `--profile <dir>` overrides both, and the
core inherits it. The shared `config.json` is read and written directly (atomically, via a temp file and rename);
the core owns `ActiveVaultPath` and reconciles on change, the shell owns the `shell` section.

## Development

```bash
npm install
npm run dev            # standalone flavour, watch mode
npm run dev:client     # client flavour, watch mode
npm start              # run the built shell with Electron
```

The dev shell finds the core at `../core/bin/Debug/net10.0` (build the core first) or wherever
`PLAINTORCH_CORE_PATH` points. `npm run typecheck` runs `tsc`; `npm run make-icons` rasterizes `assets/plaintorch.svg`
into the app icon (`icon.png`, the mark on transparent) and the tray icons (`tray*.png`, the mark on a dark disc so
it reads on light and dark taskbars) — it runs under Electron because `nativeImage` cannot load SVG. Edit
`plaintorch.svg` and re-run to refresh every size.

> **Decorators:** `@a11d/lit` installs its reactive accessors through legacy (experimental) decorators, so
> `useDefineForClassFields` must stay `false` (see `tsconfig.json` and the esbuild `tsconfigRaw`). With it on, a view
> renders once and then never updates — the class field shadows the decorator's accessor and reactivity dies silently.

## Layout

- `src/main`: the Electron main process. `shell.ts` is the single source of truth; `core-process.ts` supervises the
  spawned core; `core-transport.ts` is HTTP over the pipe/socket; `windows.ts`, `tray.ts`, `autostart.ts`,
  `updater.ts`, `config.ts`, `profile.ts` do what their names say.
- `src/preload`: the only bridge into the sandboxed renderer (`window.plaintorch`).
- `src/renderer`: lit components (`@a11d/lit`) for the splash and the status window, using the SDK client over the bridge.
- `src/shared/contracts.ts`: the shapes shared by all three, mirroring `core/Plaintorch/Hosting`.
