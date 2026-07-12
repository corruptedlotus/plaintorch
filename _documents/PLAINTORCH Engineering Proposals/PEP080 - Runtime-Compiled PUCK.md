---
status: implemented
assignee: Copilot 🤖
---
# Abstract
This proposal simply requires PUCKs to be fully "compiled" when a vault starts, preloading its processed properties to be used during the rest of the operation.
# Problem
PUCKs are composite keys that have different facets like pattern matching, tokenisation, composition, and generation. As PUCKs are primary entity identifiers of the vault system, they will be processed in high frequency and should be as efficient as possible. If the patterns, tokens, and properties of each entity's PUCK is processed every time a PUCK operation is executed, we're going to experience a massive processing overhead.
# Solution
To prevent the processing overhead, when a vault starts being served by a PLAINTORCH instance, it must pre-process every parameter, pattern, token and logic required to handle every entity's PUCK, and store it in the memory (as a singleton service) to be reused during operation. This compiled model should be purged once the vault is no longer being served (given that the service is still running).
