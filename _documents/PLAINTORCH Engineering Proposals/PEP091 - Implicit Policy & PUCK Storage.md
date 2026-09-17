---
status: implemented
assignee: Copilot 🤖
phase: 2a
---
# Storage Policy: PUCK Storage
This PEP introduces a new vault storage policy parameter: PUCK Storage.
PUCK can be stored in two forms:
## PUCK Storage Forms
### Quiet
In this mode, entity ID will be stored as a PUCK frontmatter. This behaviour is already introduced as a part of "Freeform" policy before. From this PEP onwards, this will be the default PUCK storage form.
### Index
This mode stores the entity ID as a part of the filename. This behaviour was already the default form of PUCK serialisation for non-freeform policies prior to this PEP.
## Logic Decoupling
Given this new policy parameter, freeform will no longer explicitly set PUCK storage mode, and all PUCK storage logic is decoupled from the storage modes altogether.
## Recalibration
Following this decoupling of logic, these models must be explicitly set to use "Index" as their PUCK storage form:
- `PolarisCycle`
- `OnrushSprint`
- `LorePage`
Be careful to specify the index policy from now for any new model that requires it.
# New Storage Mode: Implicit
The implicit storage mode is identical to the freeform policy, but with the important caveat being its synchronisation boundary.
## Synchronisation Boundary
Implicit entities do not initially sync to any file, but they can be prompted to create their file and begin syncing with it. This is a concept opposite of Freeform upstream initialisation.
# Model Changes
There should be no explicit changes required from any model to be able to use the Implicit policy.
# Logic
Synchronisation boundary must be implemented through a new special type of audit log: `BoundaryBegin`. This means the audit log type column should now be indexed to ensure minimum performance cost on rapid file boundary checks.
## `BoundaryBegin`
Once a file for a Implicit entity begins existing, through any mean, a `BoundaryBegin` audit log entry will be recorded. Once this entry exists for a specific Implicit entity, deletions of that entity will be treated as authoritative and reflected upstream.