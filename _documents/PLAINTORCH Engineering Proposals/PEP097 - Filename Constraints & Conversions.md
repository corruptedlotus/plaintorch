---
assignee: Copilot 🤖
status: accepted
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
