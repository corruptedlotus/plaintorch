> This blueprint is based on changes up until PEP090.

# Abstract
The watcher is the main core service responsible for watching over the files inside a PLAINTORCH vault, and syncing them with the database depending on policy and content. The watcher will, depending on policy, create files based on new entities in the database, new entities based on files, update properties to and from frontmatter, watching over folder structure for entities that depend on it, and ultimately enforcing a valid vault by reverting invalid frontmatter, purging violating files and moving files to their correct places.
# Serialisation
> [!Note]
> Frontmatter processing itself is not a part of watcher's domain, but will be discussed here as the watcher is closely dependent on it.
## PUCK, Title, Frontmatter
Every entity file has 3 important parameters vital to the watcher, besides the essential relative path. These parameters are PUCK, Title, and frontmatter data.
Entity files are named in the format `{PUCK} - {Title}`, leave for certain storage modes. This storage in the filename is two-ways, based on storage mode, and the user can modify the title of the entity by changing that part of the filename. For manual PUCKs, this means the user would have to specify them in the same fashion when creating an entity file.
PUCK is additionally stored in the frontmatter of entity files, but for policies that do not use a frontmatter PUCK, it is considered completely non-authoritative and is only enforced downstream for posterity.
Frontmatter stores properties of the model that are marked by `MarkdownFieldAttribute`, depending on storage policy, they can also be synced both ways.
## Update vs Initial Frontmatter Serialisation
When an entity's file is serialised for the first time (triggered by either core or watcher), it will serialise every marked property, even if null. However, beyond the first sync, null fields are optional; the watcher should keep them as is.
## Non-model Frontmatter
In all storage modes and regardless of storage policy, non-model frontmatter must be respected and kept as-is. Touching custom frontmatter can interrupt the experience for certain Obsidian features and plugins and limit the user in how they are allowed to store extra data about entities.
# Policy System
## PUCK-bound Models
Models marked by a PUCK notation, a storage policy (`VaultStorageAttribute`), and existent in the data model are synced by the watcher.
The watcher however should be completely decoupled from any model-specific logic, and be able to function purely based on parameters given and models discovered from the assembly by the PUCK registry.
There are a few parameters to storage policy that these models may define.
## Shape
### File (Default)
Stores entity files as a single markdown file.
### Folder: Same-name Index
Stores entity files as a directory, with the main data being stored in a markdown file with the same name.
The watcher must apply any change to the file name onto both the folder and the file, and if applicable by other policies, respect user changes to either.
Be careful that calling a resolution API call on both the folder path and file path should yield the entity.
### Folder: Keyword Index
> This shape is not part of the current specification, but it exists in a future PEP, therefore it's wise to have it in mind.

Stores entity files as directory, with the main data being stored in a markdown file with a specific name. By default this policy uses "index" as its default file name.
The watcher will sync data related to the entity file name with the folder name, and frontmatter data with the index file.
Be careful that calling a resolution API call on both the folder path and file path should yield the entity.
## Mode
The mode policy indicates how a model will be stored as files, and how the user can interact with its entities through its files.
### Synced (Default)
Entities can be created by both files and the core, property changes between them will be synced, removals will also be synced from both sides.  
If any errors occur in parsing the properties from files, those values will be overridden by the core.
### Optional
Entities will be created by the core with a default file for them, property changes between them will be synced, but removals will not be synced by either side.  
If any errors occur in parsing the properties from files, those values will be overridden by the core.
### File-first
Entities will be created by files, the core will only keep track of them and their properties, including removals.  
If any errors occur in parsing the properties from files, those values will be overridden by the core. Initialisation errors will be recorded as issues and not cause a purge, unless it's a PUCK reuse violation.
### Enforced
Entities will be created by the core with a default file for them, properties and the existence of the file will be enforced by the core. Files that are not in the database will be purged.
### Freeform
Entities can be stored anywhere in the vault while keeping canonical core-authored defaults. PUCK identity is expected from frontmatter instead of filename composition. Initialisation happens explicitly, not by the watcher. (Refer to [[PEP090 - Freeform Storage using Stealth PUCK]])
If any errors occur in parsing the properties from files, those values will be overridden by the core.
## Parent System
Entities that have or can have a parent (either same type or different), can reflect that in their directory structure, by being placed inside of their parent's folders, given that the entity type of their parent has a folder-based shape.
This placement can be partitioned into a subfolder of their parent's folder. (Refer to [[PEP083 - Parent Folder Partitioning]])
Changing this parent should accordingly cascade, both upstream and downstream, if not blocked by other policies. (Refer to [[PEP084 - Parent Folder Syncing]])
# Modus Operandi
## Roots & Exclusions
A PLAINTORCH vault has a base root. Each puck-bound model can have its own root directory. Models with a Freeform storage mode can exist anywhere in the vault, so existence of one in the model registry means the watcher should keep an eye out even outside model roots.
### Attachments & Special Folders
Obsidian vaults can have a special attachments folder per-directory defined in `.obsidian/app.json: "attachmentFolderPath"`, those folders should be excluded from watcher scans. Directories that start with an underscore (`_`) or dot (`.`) should also be excluded by default.
### Partition Folders
If a parental folder is subject to direct scan for a specific entity, partition folders that exist alongside them should be taken into account not to be mistaken as uninitialized folder-based entities.
### Freeform Entity Folders
If an entity has a freeform storage mode and a folder-based shape, its residing folder should be considered as its entity folder. However its freeform children can be within any depth of said folder, as long as there's no other eligible parent for them within that hierarchy.
### Untitled Files & Folders
The watcher should ignore files and folders named "Untitled" or "Untitled {number}" while looking to auto-initialise entities from files.
### Unknown File Types
Non-markdown files are none of the watcher's business up until PEP090.
## Idle State
The idle state of the watcher is when no vault is active. During this state the watcher will not do anything, until a vault becomes active.
## Preference of Inaction
The watcher should by default prefer to take no action. This means that if every parameter for a file matches authority, has its default file created and has done the initial serialisation, if its path, ID or existence is not violating any policy or entity, then the watcher should not do anything further about the file. Upstream changes from a file should also not trigger any update by nature, but if the change causes a chain reaction or side effect, that should be independently handled. Invalid upstream changes should also naturally cause a downstream re-sync, based on policy.
## Start-up Sweep
When the service starts serving a vault, either by vault activation while the core is online or after startup with a default active vault, the watcher will wake and perform an initial sweep on the vault.
The sweep should first and foremost try and find existing entities, make sure they're up to date, then based on policies proceed to first try upstream additions, then upstream deletions, then downstream and everything else.
## Continuous Watch
During normal operation of the watcher, it will watch all roots (and the entire vault in case of freeform entities), and based on policy, detect and sync upstream changes from files and apply downstream changes from the database.
# Transactions
## Creation
Upstream creation of entities via files happens in multiple forms, but before anything, when the user creates a file, it starts as an "uninitialised" file. The watcher will decide whether to initialise a file, however some policies like Freeform require manual initialisation only. Initialisation means creating the entity based off the file, or purging it if violating. For manual initialisation and certain policies, violation in initialisation simply throws an error, this error will be reflected  
For entities with an auto-generated PUCK, this means the uninitialised file should be PUCK-less, which would mean file name must be just the raw title.
For entities with manual PUCK, this means the file name should contain a full PUCK-title combo.
Downstream creation if handled by the watcher must happen in default locations. If child of a freeform entity, it follows the defaults, based in its folder.
## Deletion
Both upstream and downstream deletion will be synced as long as policy allows it.
If the index/default file for a folder-based entity is deleted,
- Freeform mode: The entity is deleted and its references are removed, the folder structure and content will not change.
- Non-freeform: If the policy allows it, the entity is deleted and its references are removed, and every immediate child entity will move to its parent position. Afterwards, the original folder without its child entities will be moved to a `_archive` folder in the root. 
## Syncing
Based on policy, the watcher will continuously sync filename and frontmatter -encoded properties, between a known entity and its known file. If applicable, this syncing could mean downstream enforcement, especially when upstream parsing or validation errors occur. 
## Enforcement & Error Handling
The watcher should under no circumstance shut down. Errors that occur in upstream or downstream syncing should be checked against enforcement criteria and violations and acted against accordingly; if not the case, should log the error and continue working. Fatal operational errors however are possible, as long as they're actual system-wide impediments, these will put the vault into standby mode, until the problem is resolved. If the watcher irrecoverably crashes and shuts down, the core must continue working and serving the API, allowing the user to attempt a restore via API or CLI.
The watcher should report two factors to the core, and the core should reflect them in the system status API:
### Issue Registry
Certain issues in watching and syncing should be uniquely recorded in an issue registry and periodically tested for resolution (if applicable). These issues can be file-specific, in which case should be identifiable/groupable by their common error type, or system-wide. This registry should be instance-based and not persisted between runs.
These issues can be critical or not, depending on whether or not they can cause data corruption. For example failing to initialise a new file-first entity will be considered non-critical, but failing to sync an entity or enforce a purge due to violation is critical.
If the watcher encounters an issue again while it is already registered, it should keep it there and not record duplicates.
If during operation the watcher realises that a certain issue no longer persists, it should remove it from the registry. This would require the watcher to define certain criteria of action for these errors, all of which can fail or succeed, setting or removing the issue. These criteria must support entity/file specification if relevant.
### Watcher/Core Status
The core should report whether the watcher is working normally, has pent up issues that can possibly cause data corruption, is unable to work and is standby, or has completely crashed and is no longer online.