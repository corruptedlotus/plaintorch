---
assignee: Copilot 🤖
status: implemented
phase: 2d
---
# Problem
We allow every character (except linebreak) inside entity names, but since entity names are synced to filenames in the watcher, they ultimately tend to break.
Some characters don't break at the filesystem, but disable Obsidian indexing.
# Solution
Give the watcher's markdown serialiser a filter that replaces forbidden characters with unicode lookalikes when writing and vice versa when scanning or reading.

| Character | Replacement | Restriction |
| --------- | ----------- | ----------- |
| /         | ∕           | Filesystem  |
| \         | ∖           | Filesysten  |
| :         | ː           | Filesystem  |
| *         | ⁕           | Filesystem  |
| ?         | ？           | Filesystem  |
| "         | ″           | Filesystem  |
| <         | ˂           | Filesystem  |
| >         | ˃           | Filesystem  |
| \|        | ⏐           | Filesystem  |
| [         | ⦋           | Obsidian    |
| ]         | ⦌           | Obsidian    |
| ^         | ˆ           | Obsidian    |
| #         | ♯           | Obsidian    |

## Implementation (2026-09-18)
A reversible filename character filter, `PuckFileNameCodec` (`core/Puck`), sits at the markdown title↔filename chokepoints:

- **Write (encode):** `PuckNamedIdentity.FormatFileName` / `FormatTitleOnlyFileName` — the only methods that compose a filename segment from a title — now encode via the codec instead of the old lossy `_`-replacement sanitiser. Every write path (the per-shape storage strategies through `VaultStoragePathComposer.GetBaseName`, the lore folder name, the auto-generated-PUCK consistency pass) converges here.
- **Read (decode):** the filename readers decode symmetrically — `PuckNamedIdentity.ParsePath`/`ParseLoosePath` (and the new `DecodeFileName` helper), `VaultStoragePathComposer.ReadFilenameIdentity`, and the two direct directory-name readers (`MarkdownFileLocator.TryGetContainingOnrushSprintId`, `LorePage` self-named segment collection). The pure value-parsers (`Parse`/`ParseLoose`/`TryParse`) stay untouched, preserving the display `Format`/`Parse` pair.

The entity title in the database and in frontmatter always carries the real characters; only the on-disk filename carries look-alikes. Encode and decode are exact inverses, so a title survives the round-trip. Out of scope: `VaultMediaService` upload-name sanitising (arbitrary user binaries, a validation/whitelist boundary, not an entity name). Accepted limitation: a look-alike a user types by hand decodes to its plain counterpart, and characters outside the 13-entry set (including control characters) are left as-is rather than made irreversible. Covered by `FilenameCodecTests` (the pure filter) and `FilenameConstraintTests` (write, read, and live scan/reconcile round-trips).
