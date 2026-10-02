# PLAINTORCH desktop shell

The per-user desktop package of PLAINTORCH: a tray application built with Electron that either **hosts** the core
(standalone flavour) or **attaches** to one that is already running (client flavour). Both flavours are one codebase
and one build; the flavour is baked in at build time.

## What it does

- **Standalone flavour** spawns the published core beside the app (`resources/core`; a development run uses the core
  project's own build, see [Which core the shell spawns](#which-core-the-shell-spawns)) in spawn mode
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
- A **tray icon** whose menu is kept short: the phase and the active vault (as read-only lines), **Check Status**,
  **Open Briefing**, **Restart core** (standalone flavour only), the update entry (check, download, or install and
  restart, following the updater's state), and **Quit PLAINTORCH**. Clicking the icon opens the briefing.
- The **status window** holds everything else the shell does: activating and deactivating a vault, restarting the
  core (standalone flavour), opening the logs folder and the briefing, start-at-login, updates, and quit. The briefing
  also offers **Activate vault…** while the core is idle.
- **Activating a vault** picks a folder and writes it as `ActiveVaultPath`. A folder that is no vault yet (no
  `.plaintorch` settings file) is offered for initialization first; once the user agrees, the shell runs the core's
  own `init` against it (`plaintorch init --vault <folder> --profile <root>`, see `src/main/vault-initialization.ts`)
  and only then writes the path, so the core never sees it uninitialized. The client flavour ships no core to
  initialize with, and says to run `plaintorch core init` instead.
- **Planning windows**: a planning graph file (`.p7tpx`, a saved global planning context) opens in a window of its own
  with the file-backed dependency canvas, as the Obsidian plugin opens it in a tab, and the briefing's canvas pops its
  context — an onrush, or an unsaved global context — out into one the same way; see [Planning files](#planning-files).
- A **tray icon** with the phase, the active vault, the briefing and the status window, activate/deactivate,
  start-at-login, the logs folder, restart, updates, and quit.
- A **status window** with the core, the shell settings, and the watcher (`p7t-watcher-status-card`: its health, what
  it means, and the issues the plugin's status-bar drawer lists), drawn with the SIPA cards, chips and buttons.
- **Custom window frames**: the status, briefing and planning windows are frameless and draw their own title bar
  (`p7t-window-frame`, below).
- **Start at login** through the OS login-item API on Windows and macOS, and an XDG autostart entry on Linux.
- **Updates** through electron-updater in the "download the new installer and run it" flow; see `../installer`.

## Profiles

A development build targets `~/.pleiades/plaintorch-dev`, exactly like a manual `serve` and a dev Obsidian plugin, so
it never collides with an installed shell on `~/.pleiades/plaintorch`. `--profile <dir>` overrides both, and the
core inherits it. The shared `config.json` is read and written directly (atomically, via a temp file and rename);
the core owns `ActiveVaultPath` and reconciles on change, the shell owns the `shell` section.

The shell keeps its Electron `userData` in the profile too, at `<profile>/shell`: the Chromium caches, the windows' web
storage, electron-updater's `.updaterId`, and the single-instance lock. Electron keys that lock on the `userData`
folder, so it applies per profile: a second launch on the same profile quits and opens the running shell's briefing,
while shells on different profiles run side by side. That covers a dev build beside an installed one, and two shells of
one build with different `--profile` folders (`electron . --profile <scratch dir>` next to a running dev shell).
Downloaded updates are cached elsewhere, in the OS cache folder (`%LOCALAPPDATA%` on Windows).

Earlier builds used Electron's default `userData`, named after `package.json`'s `name` (the installer's `productName`
never reaches it): `%APPDATA%\@pleiades\plaintorch-standalone` for installed and dev shells alike, and the platform
equivalent elsewhere. That also put an installed shell and a dev shell on one lock. Nothing in that folder needs to
carry over: the caches rebuild, the web storage holds nothing the shell set, and the updater mints a new id. The first
launch after the change starts with an empty `shell` folder, and the old folder can be deleted.

## Development

```bash
npm install
npm run dev            # standalone flavour, watch mode
npm run dev:client     # client flavour, watch mode
npm start              # run the built shell with Electron
```

Build the core first (`dotnet build core`); the dev shell runs that build (see below). `npm run typecheck` runs `tsc`
(the package's own pre-existing errors show up there too, since the renderer bundles it from source).

### Which core the shell spawns

The standalone flavour resolves the core afresh on every start (`src/main/core-location.ts`); the first match wins:

1. `PLAINTORCH_CORE_PATH`, whenever it is set, packaged or not.
2. A packaged app: the core it ships, `resources/core`, and nothing else.
3. A development (unpackaged) run: the core project's own build, `../core/bin/Debug/net10.0` and then
   `../core/bin/Release/net10.0`, so a fresh `dotnet build` is what runs.
4. Only when there is no build at all: `core-dist`, the installer's publish (`installer/build.mjs`). It is
   git-ignored, outlives the installer build, and stays pinned to that build's commit, so against a vault a newer core
   has migrated it fails with errors like `SQLite Error 1: 'no such column: …'`. Choosing it always warns.

The shell logs its pick on the console: the origin, the path and the version (`<version>+<commit>`, the assembly's
`ProductVersion`), as `[core] Using the Debug build: … (1.0.0+33ac9b7…)`. A `[core] WARNING: …` line follows when it
fell back to `core-dist`, or when a candidate it passed over has a newer `plaintorch.dll` than the one it chose;
rebuild the chosen one, or point `PLAINTORCH_CORE_PATH` at the newer one.

## Planning files

A planning graph file (`.p7tpx`) is a saved global planning context (PEP102): the pinned entities and where they were
arranged, as JSON. The Obsidian plugin opens one in a tab; the shell opens one in a **planning window**: the same
`p7t-dependency-canvas`, locked to the file's one global context (no onrush mode switcher), under the window's own
title bar, which is named after the file.

- **Opening**: the installer associates the extension with the app (`installer/electron-builder.yml`,
  `fileAssociations`), so opening a file from the file manager launches the shell with the file's path. A launch
  naming planning files opens them instead of the briefing, when the splash hands over (at once without a splash);
  files asked for while the splash is still up wait for it. A second launch hands its
  command line to the running shell (`second-instance`), which opens the files it names and brings the briefing
  forward when it names none; macOS delivers files as `open-file` instead. Arguments are resolved against the
  launching process's working directory, and only existing files count. Each file gets one window: opening a file
  already open brings its window forward (the path is compared without case on Windows and macOS).
- **The file belongs to the main process** (`src/main/planning-files.ts`), since the sandboxed page has no file access.
  The page reads it and writes it over `window.plaintorch.planning`, and only the file of its own window. Writes are
  coalesced for a moment, skipped when nothing changed, and the last one lands when the window closes or the shell
  quits.
- **Changes from elsewhere** (Obsidian showing the same file, another editor, a sync) are followed. The file's folder
  is watched, so a save that renames a new file over the old one is followed too. A changed text replaces the canvas's
  pins, as the Obsidian view does; the canvas keeps its own layout once it has adopted the file's. The shell's own
  writes never echo back. A change from elsewhere wins over a write still waiting.
- **A file moved or deleted elsewhere** is announced in a toast and never written back into being: edits are kept in
  the window but not saved until a file is at the path again, which is announced too.
- **Saving a new one**: the briefing's scratch *Global Planning* context has **Save to file**, which asks where through
  a native save dialog (`window.plaintorch.planning.saveAs`, modal to the window that asked). The dialog starts in the
  served vault (the documents folder without one) under a name not taken there yet ("Global planning.p7tpx", then
  numbered), may save anywhere, and confirms replacing a file. A name typed without the extension gets it, and a file
  that already has the resulting name is refused rather than replaced unconfirmed. The saved file opens in a planning
  window, and the toast names it vault-relative, or by its full path outside the vault. Saving over a file open in a
  planning window replaces it through that window, which shows the new pins at once.
- **The vault**: like the briefing, the canvas waits for a core serving an active vault whose startup sweep is done.
  The pins are the active vault's entities, so a file from another vault shows its pins by their saved titles, but
  they do not resolve.

### Popping a canvas out

The briefing's canvas has a pop-out button (*Open in a new window*, beside the readout) that opens the context on
screen in a planning window, as if it were a file (`host.canvasPopOut` → `window.plaintorch.planning.popOut`; the
Obsidian plugin pops out into tabs the same way). A
planning window shows one context alone (`singleContext`: no mode switcher, no pop-out), and the main process keeps
what each window shows: a file, an unsaved global context, or an onrush.

- **An onrush** (active or planning) has one window each, brought forward when popped out again. The vault holds the
  onrush, so the briefing keeps showing it too and both stay current. A move to the other onrush from inside its
  window — *Go to planning* on an empty active onrush, or activating the planning one — opens that onrush's window
  rather than switching this one.
- **A scratch global context** moves into a new window — every pop-out a window of its own — as the text its file
  would hold, and the briefing's canvas starts a fresh scratch. The window is marked **Unsaved** in its title bar (and
  "(unsaved)" in its taskbar title). The main process keeps the context as it is edited, so a reload keeps it, but
  closing the window discards it, as closing a scratch tab does.
- **Save to file** in an unsaved window turns that window into the saved file's: titled after it, no longer marked,
  its edits written to the file from then on. Saved over a file already open in another window, that window takes the
  saved context and the unsaved one closes.

In development nothing is registered with the OS. Open a file by handing it to a running dev shell (a second launch on
the same profile), e.g. `npx electron . C:\path\to\Roadmap.p7tpx` next to `npm run dev`, or start a shell with it:
`npx electron . --profile <dir> C:\path\to\Roadmap.p7tpx`.

## Window frames

The status, briefing and planning windows draw their own title bar: `p7t-window-frame`
(`src/renderer/WindowFrame.ts`) wraps each page — the PLAINTORCH mark, the window's title, a `title-bar` slot beside
the title for a page's own controls (a planning window's *Unsaved* mark), and the minimize,
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

## Fonts

The shell embeds its type rather than relying on what the machine has installed: Outfit (the interface), Space
Grotesk (text) and Cascadia Code (monospace), all variable and under the SIL Open Font License, from the
`@fontsource-variable/*` packages. `src/renderer/fonts.css` imports them; esbuild bundles it into `dist/fonts.css` and
copies each face to `dist/assets/fonts/`, and every page links the stylesheet. The faces are split by script, and a
page fetches only those whose `unicode-range` it draws.

The families keep fontsource's names, `"Outfit Variable"` and so on, which the SIPA host's `--font-*` tokens and the
splash name first, with the installed names behind them. So no installed font of the same name can shadow the embedded
one. That was the cause of accented letters drawn in a fallback face: a stale variable Outfit without them, installed
per user, won over the good one in newer Chromium.

## Layout

- `src/main`: the Electron main process. `shell.ts` is the single source of truth; `core-process.ts` supervises the
  spawned core and `core-location.ts` picks which core that is; `core-transport.ts` is HTTP over the pipe/socket (the SDK's socket transport); `core-streams.ts`
  relays the change feed to the windows; `media-protocol.ts` serves vault files; `request-guard.ts` refuses hosted
  `file:` requests (UNC/SMB); `window-frame.ts` makes the custom frames work; `planning-files.ts` finds planning
  files in a launch's arguments, reads, writes and watches each planning window's file, saves new ones through the
  save dialog, and answers the planning windows' pages; `windows.ts`, `tray.ts`,
  `autostart.ts`, `updater.ts`, `config.ts`, `profile.ts` do what their names say.
- `src/preload`: the only bridge into the sandboxed renderer (`window.plaintorch`).
- `src/renderer`: `splash.ts` (the splash, kept small), `status.ts` (the status window), `briefing.ts` (the SIPA
  briefing) and `planning.ts` (a planning window), each its own bundle and page. The briefing and the planning windows
  mount through `vaultWindow.ts` (the SIPA host, the core client and its change feed, the wait for the vault); every
  SIPA window shares `bridgeTransport.ts` and `WindowFrame.ts`. All resolve `@a11d/lit` to the package's copy, so
  there is one lit.
- `src/shared/contracts.ts`: the shapes shared by all three, mirroring `core/Plaintorch/Hosting`.
