---
status: available
assignee: Copilot 🤖
---
# Storage Mode: Quiet
Quiet as a storage policy is identical to Synced, with two caveats: PUCK storage and synchronisation boundary.
## PUCK Policy
In the Quiet storage mode, PUCK is stored in the frontmatter instead of filename composition, just as in Freeform storage mode.
## Synchronisation Boundary
Quiet entities do not initially sync to any file, but they can be prompted to create their file and begin syncing with it. This is a concept opposite of Freeform upstream initialisation.
# Model Changes
There should be no explicit changes to any model to be able to use the Quiet policy.
# Logic
PUCK-in-frontmatter logic already exists as a part of Freeform, and normal root-based file sync and enforcement exists as a part of Synced policy. Synchronisation boundary must be implemented through a new special type of audit log: `BoundaryBegin`.
## `BoundaryBegin`
Once a file for a Quiet entity begins existing, through any mean, a `BoundaryBegin` audit log entry will be recorded. Once this entry exists for a specific Quiet entity, deletions of that entity will be treated as authoritative and reflected upstream.