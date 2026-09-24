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
- The **briefing** — the SIPA UI (`@pleiades/sipa`, the same components the Obsidian plugin shows) in a window of its
  own: the SIPA host (`@pleiades/sipa/hosts/sipa`), an SDK client over the preload bridge (requests, `x-note-ready`,
  and the change feed streamed line by line), and vault images through the `plaintorch-media://` scheme. It is the
  shell's main window: clicking the tray icon, relaunching the app, and a visible launch (once the core is up) open it.
  Notes have no in-app editor yet, so opening one says so.
- A **tray icon** with the phase, the active vault, the briefing and the status window, activate/deactivate,
  start-at-login, the logs folder, restart, updates, and quit.
- A **status window** with the core, the shell settings, and a summary of the active vault.
- **Custom window frames**: the status and briefing windows are frameless and draw their own title bar
  (`p7t-window-frame`, below).
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
`PLAINTORCH_CORE_PATH` points. `npm run typecheck` runs `tsc` (the package's own pre-existing errors show up there
too, since the renderer bundles it from source).

## Window frames

The status and briefing windows draw their own title bar: `p7t-window-frame` (`src/renderer/WindowFrame.ts`) wraps
each page — the PLAINTORCH mark, the window's title, a `title-bar` slot for a page's own controls, and the minimize,
maximize/restore and close buttons — over the page, which fills and scrolls in the rest of the window.

- **Main process** (`src/main/window-frame.ts`): `customFrame()` gives `frame: false` on Windows and Linux (the window
  keeps its resizable edges, shadow and snapping) and `titleBarStyle: "hidden"` on macOS, where the system keeps its
  traffic lights and the bar draws no buttons. `relayFrameState` pushes maximized/focused/full-screen to the page;
  `handleFrameControls` answers the buttons (`frame:*` IPC), each acting on the window that asked.
- **Dragging** is `-webkit-app-region: drag` on the bar; the buttons and anything slotted into it are `no-drag`.
  Double-clicking the bar maximizes, and right-clicking it opens the system menu, as on a system title bar. Windows
  11's snap-layout flyout on the maximize button belongs to the native caption button and is not available. Only
  controls in the `title-bar` slot are `no-drag`: page content that scrolls beneath the bar must not cut its drag
  region.
- **Under a modal**: a SIPA modal leaves the page — the bar included — inert beneath its backdrop. It offers a click on
  the backdrop as a cancelable `backdrop-click` event first (`isBackdropDismissal`), and the frame works the window
  button at that point and keeps the modal open.
- **Full screen** (F11, the default menu's accelerator) hides the bar; the state is read once the change has settled,
  since on Windows Electron announces it before the window takes it.
- **Styling**: parts `title-bar`, `icon`, `title`, `controls`, `control` (+ `minimize`, `maximize`, `close`) and
  `content`; custom properties `--p7t-title-bar-height`, `-background`, `-color`, `-border`, `-control-hover` and
  `-close-hover`; the host reflects `platform`, `maximized`, `inactive` and `fullscreen`.
- A page whose chrome covers its top edge declares it as `--p7t-safe-top` (the briefing: 32px), which the SIPA toasts
  stay below.

## Icons

The PLAINTORCH marks live in `assets/`: `plaintorch-full.png` (the full-colour mark — window, executable, installer),
`plaintorch-mono-{dark,light}{,@2x}.png` (the tray, following the taskbar's theme) and `splash-loading.png`.
`npm run make-icons` derives the installer branding from them into `../branding` — `plaintorch.ico` (16–256 px, for
the shell's, the core's and the CLI's executables, the installer, the shortcuts and Startup apps) and the NSIS
`installer-sidebar.bmp` / `installer-header.bmp` — under Electron, so no image library is needed. The generated files
are committed; re-run it after changing a mark.

> **Decorators:** `@a11d/lit` installs its reactive accessors through legacy (experimental) decorators, so
> `useDefineForClassFields` must stay `false` (see `tsconfig.json` and the esbuild `tsconfigRaw`). With it on, a view
> renders once and then never updates — the class field shadows the decorator's accessor and reactivity dies silently.

## Layout

- `src/main`: the Electron main process. `shell.ts` is the single source of truth; `core-process.ts` supervises the
  spawned core; `core-transport.ts` is HTTP over the pipe/socket (the SDK's socket transport); `core-streams.ts`
  relays the change feed to the windows; `media-protocol.ts` serves vault files; `request-guard.ts` refuses hosted
  `file:` requests (UNC/SMB); `window-frame.ts` makes the custom frames work; `windows.ts`, `tray.ts`,
  `autostart.ts`, `updater.ts`, `config.ts`, `profile.ts` do what their names say.
- `src/preload`: the only bridge into the sandboxed renderer (`window.plaintorch`).
- `src/renderer`: `renderer.ts` (the splash and the status window) and `briefing.ts` (the SIPA briefing), each its own
  bundle and page, sharing `bridgeTransport.ts` and `WindowFrame.ts`. Both resolve `@a11d/lit` to the package's copy, so there is one lit.
- `src/shared/contracts.ts`: the shapes shared by all three, mirroring `core/Plaintorch/Hosting`.
