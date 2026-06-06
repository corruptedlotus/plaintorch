---
status: implemented
assignee: Copilot 🤖
---
# Abstraction
This proposal is about entities that have a parent/nested storage system, and how parent entities link to relocation.
# Importance of Distinction
It's important that the watcher distinguishes between deletion, creation and relocation. The watcher must first ensure that a relocation has not occurred, before falling to deletion and creation cases.
# File Relocation
When an entity file/folder is relocated to a new folder, its parent should be re-detected and updated to the database (even if null).
# Parent Reassignment
When an entity's parent is changed on the database, and if the entity's storage policy is not sync-agnostic, it should relocate the file/folder to the storage location designated by the new parent. This relocation should move everything that is relevant to the entity, including its children.