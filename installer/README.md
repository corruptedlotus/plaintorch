# PLAINTORCH installers

Builds the desktop installers for the two shell flavours with electron-builder.

```bash
node installer/build.mjs --flavor standalone            # publishes the core for this OS, bundles, packs
node installer/build.mjs --flavor standalone --rid linux-x64
node installer/build.mjs --flavor client                # no core inside
```

Outputs land in `bin/installer/<flavor>/`. The standalone flavour first runs `dotnet publish` (self-contained, for the
target runtime identifier) into `standalone/core-dist`, which electron-builder packs as `resources/core`.

## Updates

`electron-builder.yml` configures electron-updater with a generic provider pointing at the update web repository.
Nothing downloads on its own: the shell checks, offers the download, and on install stops its core (so the core
executable is not locked) and quits into the new installer. Publishing a release means uploading the installer and
the `latest*.yml` manifest electron-builder emits beside it. winget later reuses the same installer artefacts.

## Before shipping

- Sign Windows builds; SmartScreen warns on unsigned installers and electron-updater refuses an update whose signature
  differs from the running app's.
- Sign and notarize macOS builds; the updater does not work without it.
- The icons are the PLAINTORCH marks: `branding/plaintorch.ico` and the NSIS art are generated from
  `standalone/assets` by `npm run make-icons` (in `standalone/`); re-run it after changing a mark.
- Point `publish.url` at the real update host.
