---
status: implemented
patches:
  - Patch090.1 - Add Obsidian Command
---
# Abstract: Operational Goals
- Allow directives to be created anywhere in the vault,
- Without having their PUCK attached to their name,
- To be auto-discovered by the watcher,
- While still keeping their default location scheme for core-first creation.
# Solution
## "Freeform" Storage Mode
To address our operational goals we will create a new storage mode called "Freeform". This mode indicates this specific ruleset:
- PUCK will no longer be serialised in filename, instead it will be recorded in frontmatter.
- If the storage shape is directory mode, its parent directory will be considered parent, regardless of name.
- The entity type still has a default storage directory, and will use that to store data authored from the core. This includes parent folder storage policies.
- The entity's storage is no longer bound to a single folder, and will be detected by the watcher mass-scanning the vault, based on frontmatter PUCK data and using the PUCK reverse resolution logic.
- Directory-based parent detection is still authoritative, but instead of having a fixed directory to detect from, the deepest (closest) parent folder that is of parent type is chosen.
- Watcher no longer automatically creates entities based on files existing in their designated folder.
- The watcher will still validate and sync data from and to files marked with a correct PUCK, and purge unknown PUCKs from frontmatter data. The watcher will delete entities when their vault file is deleted, and sync relocation as per policy.
- If a freeform entity with a directory shape violates the boundaries of another entity of the same type (is moved into its main directory) or tries to own a key directory (this includes partitions, root and entity root folders), the watcher rejects that assertion and moves the entity file to the default location for that entity, inside a folder of the same name (with failsafe numbering).
## Entity Initialisation
Now that the watcher no longer auto-initialises a freeform entity, we should implement an initialisation tooling for manually doing it. The same logic that the watcher uses to create an entity based on a file should now be accessible to APIs, so that entities that require it can implement an `/init` endpoint.
Be warned that this service call should follow all the aforementioned policies and reject the initialisation if violating.
# Enable: Directive
As of PEP090, Directives will switch to Freeform storage mode, and implement the `init` API accordingly.
A command will be added to the Obsidian plugin to facilitate this:
- **Initialise as Directive**: which can be used in a file to initialise as a directive, if successful refreshes the page so that banners can load, if failed, shows a notice. 
# Patches
## Patch090.1 - Add Obsidian Command