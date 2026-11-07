> Plan for the error system deferred by [[Plan - Local Repo Separation]]: how a refusal, a validation failure or an outage travels from the core to the surface that asked, and what `@corruptedlotus/local-repo` 0.2 carries of it. Drafted 2026-10-12 against the code on `claude/local-repo-readiness-df5060` and the enforcement contract of [[PEP103 - State Enforcement Engine]] on `claude/state-system-polish-5a88d8`. Nothing here is implemented yet.

# Goal
One failure model, end to end. The core answers every refusal with a typed problem. The client turns it into one failure value and never drops it. The library carries that value, opaque, to every place a surface reads an outcome: a reference's `error`, a write's result, a draft's and a binder's field errors, the rejection hook. Surfaces render it through one registry of problem types instead of a string per call site.

**Done means:** the bookshop fixture runs against a backend that refuses with structured problems, a field-level validation failure lands on the draft and on the editable that sent it, an outage is told apart from a refusal and from "nothing there", and PLAINTORCH's interim reachability adapter (`sdk.ts/plaintorch/repos/answered.ts`) is deleted. The package ships as 0.2.0.

# Where things stand
- **a11d.** `@a11d/api` has an abstract `HttpError` (holds the `Response`, parses lazily in `throw()`) and `@a11d/api-dotnet` one subclass that reads ASP.NET ProblemDetails (`title`, `status`, `type`, `traceId`, `errors` per field). Both are bound to a11d's static `Api`, which PLAINTORCH does not use; the SDK takes only the `model` registry from it. The wire contract is worth adopting; the classes are not.
- **Core.** No ProblemDetails. Endpoints answer `Results.Ok` or, in a handful of places, `Results.BadRequest("string")`; services throw `InvalidOperationException` (117 sites) and five typed exceptions (`PreferenceValidationException`, `VaultNotActiveException`, `VaultDatabaseAheadOfCoreException`, `VaultEntityDeleteBlockedException`, `MarkdownDeserializationException`), which surface as a bodiless 500. [[PEP103 - State Enforcement Engine]] adds `StateEnforcementResponseMiddleware`: a 409 whose body is a `StateEnforcementProblem` with `type: "state-enforcement"`, `outcome`, `level`, `message`, `conflicts`, `compensations`, confirmed by repeating the request with `X-Plaintorch-Enforcement: auto`. That is the one typed problem that exists, and the shape this plan generalises.
- **SDK.** `coreClient.exchange` drains a refused response and keeps only its status (`refusedWith`); the body is lost. Domain SDK methods answer `T | undefined`, so a rejection, an outage and a missing entity are the same `undefined`. `getJsonAnswer` / `answered.ts` is the interim patch that tells "no answer" from "nothing", for the repositories only. The state-system branch adds `stateEnforcement` handling in `send`: the one place a refused body is read today.
- **Library.** A fetcher answers a value, `undefined` or `unresolved`; `WriteSuccess` judges a write by what its persist call returned; `EntityRef.error` holds a thrown value; `rejected(entity)` says a write was rolled back and nothing else; `commit` and `draft.commit` return a boolean. Every one of these is a place a failure passes through without being looked at, which is what the separation plan reserved.
- **SIPA.** 82 `toast(...)` sites, about half of them failure wording written per call; 34 `mutate` / `commit` sites judge a boolean; `EditableTextPart` has its own syntax-check error slot that server-side errors never reach; `p7t-entity-absence` knows "removed" and "unavailable", not "refused".

# Settled by earlier rulings
| Decision | Ruling |
|---|---|
| The library stays opaque | It carries a `failure` value and never inspects it. Codes, registry, validation detail and wording live in the consumer |
| Shapes may change once | `commit`, `mutate` and `EntityDraft.commit` change their return shape in 0.2, with this plan, and not again |
| Data states stay as they are | not-loaded / loaded / removed say nothing about *why*; the failure sits beside them |
| The interim adapter is deleted | `answered.ts` is replaced, not evolved |

# Decisions to take
Each changes the shape of the work; rule on them before stage A.

1. **Thrown or returned, in the domain SDKs.** `core.objectives.update(...)` today returns `undefined` on any failure. *Recommended:* throw a `PlaintorchError` carrying the problem, as a11d's convention does, so call sites that care use `try`/`catch` and reaction hooks receive the value; the library's write cycle treats a thrown value as a rejection with that failure. The alternative, returning an outcome object from every SDK method, touches every signature and reads worse at the 34 write sites.
2. **The `type` scheme.** ProblemDetails `type` is the registry key. *Recommended:* short stable slugs, as PEP103 already does (`state-enforcement`), namespaced by area where helpful (`preference.invalid-value`, `vault.not-active`, `entity.not-found`), never a URL. The core owns the list; the SDK mirrors it; a contract test keeps the two equal.
3. **Blanket mapping of `InvalidOperationException`.** 117 sites cannot be retyped at once. *Recommended:* an interim mapping to 409 with `type: "invalid-operation"` and the message as `detail`, counted as a refusal (not an outage) by the client, and a backlog of the hot paths to retype into named problems. Without this the client cannot tell a refusal from a crash until every site is retyped.
4. **Where wording lives.** The core's `detail` is a developer sentence; the user-facing line comes from the type. *Recommended:* a message sheet in SIPA keyed by `type` (the way the watcher's issue prose lives in sheets, never classified on message text), with `detail` shown as the secondary line.
5. **Retry.** A transport failure on a read leaves the reference not loaded with `refresh()` as the retry. *Recommended:* keep that in 0.2 and leave automatic retry (backoff on reads, never on writes) for a later minor; the feed already reconnects with backoff.

# Stage A — The core answers with problems
*Size: medium. Lands after `claude/state-system-polish-5a88d8` is merged, so that its 409 is reshaped once rather than written twice.*

- [ ] **ProblemDetails everywhere.** `AddProblemDetails()`; one `ProblemResponseMiddleware` replacing `StateEnforcementResponseMiddleware`, mapping exceptions to problems: `StateEnforcementException` → 409 `state-enforcement` (the same members as today as extensions, so `isStateEnforcementProblem` keeps working), `PreferenceValidationException` → 422 with `errors` per field, `VaultNotActiveException` and `VaultDatabaseAheadOfCoreException` → 503, `VaultEntityDeleteBlockedException` → 409, `InvalidOperationException` → 409 `invalid-operation` (decision 3), anything else → 500 `unexpected` with `traceId`. `Results.BadRequest("string")` sites become `Results.Problem` / `ValidationProblem` with a type.
- [ ] **A registry in the core.** `ProblemTypes`: one constant per type the core can emit, with its status and whether it is a refusal, a validation failure, an outage or a fault. The middleware and the endpoints draw from it; nothing emits a type that is not in it.
- [ ] **Validation detail.** Field errors follow ASP.NET's `errors` shape (`{ field: [messages] }`), keyed by the request's property names as the client sent them, so the editable that sent the field can find its own error.
- [ ] **Tests.** Middleware: each mapped exception, the unknown one, a response already started. Endpoint samples: a refusal, a validation failure. Contract test: every `ProblemTypes` member appears in the SDK's registry (stage C) and vice versa, mirroring `EntityTypeNameContractTests`.

# Stage B — The library carries a failure (0.2)
*Size: medium. Independent of A; can start first. Behaviour-neutral for a consumer that ignores the new fields.*

- [ ] **One failure type parameter, opaque.** `unknown` everywhere the library hands a failure on; no base class, no shape.
- [ ] **Write outcomes.** `WriteSuccess` becomes `judge(result) → accepted | rejected(failure)`, with the thrown value of a persist call judged the same way (a throw is a rejection whose failure is the thrown value). `EntityRepository.mutate` / `commit`, `EntityDraft.commit` and `EntityRef.commit` return a `WriteOutcome`: `{ accepted: true, result }` or `{ accepted: false, failure }`. The boolean the consumers test today is the `accepted` member; the SIPA sites change mechanically.
- [ ] **Field errors.** A consumer-declared `fieldErrorsOf(failure) → Record<field, message[]> | undefined` on the policy (or the set). A draft keeps `errors` from its last rejected commit, cleared on the next edit of that field; `EntityRef` keeps the same per field for its binder, read as `ref.fieldError(key)`. The library never parses the failure itself.
- [ ] **Hooks carry the failure.** `rejected(entity, failure, keyPath?)` on `EntityRef`; `ReactiveBinder` gains a `rejected` hook with the key path, so an editable can mark itself.
- [ ] **Read failures.** `EntityRef.error` / `DerivedRef.error` hold the thrown value as today; a fetcher may also *return* `unresolved` with a reason: `unresolved(failure)` keeps the sentinel semantics and lets the reference show why the backend could not be asked. `notLoaded` is unchanged.
- [ ] **Tests.** Bookshop fixture extended with a backend that refuses with structured problems: a rejected write's failure reaches the hook and the outcome; field errors land on the draft and on the binder's input; an unreachable shop leaves `unresolved(reason)` on the reference. The package suite drives it through a native `<input>`.
- [ ] **Publish 0.2.0.** README's write and binder sections updated; `.GENESIS.md` records the shape change.

# Stage C — The client keeps the body
*Size: medium. Depends on A (the wire) and B (the outcome shapes).*

- [ ] **`PlaintorchProblem`.** `exchange` reads a refused body as ProblemDetails into `{ status, type, title, detail, errors, traceId, ...extensions }`; a body that is not one (an old core, a proxy) becomes `type: "unexpected"` with the raw text as `detail`. A transport that never reached the core yields `TransportFailure { kind: "unreachable" | "timeout" }`.
- [ ] **Thrown `PlaintorchError`** (decision 1) from every domain SDK method, carrying the problem or the transport failure. `sendForSuccess` and `sendForJson` throw instead of answering `undefined`; the listing SDKs stop turning nothing into `[]`.
- [ ] **The registry mirrored.** `sdk.ts/plaintorch/problems.ts`: the types as constants, type guards (`isValidationProblem`, `isStateEnforcementProblem` moves here), `fieldErrorsOf` for the library's policy, and the classification (refusal / validation / outage / fault) the surfaces switch on.
- [ ] **Fetchers.** `repos/repositories.ts` fetchers catch `PlaintorchError`: `entity.not-found` → `undefined` (removed), a transport failure or an outage class → `unresolved(failure)`, anything else rethrown into `ref.error`. `answered.ts` and `getJsonAnswer` deleted.
- [ ] **Enforcement.** The PEP103 handler keeps its place in `send`, now built on the parsed problem rather than its own reader.
- [ ] **Tests.** `coreClient.test.ts`: a refused body is kept and parsed; a non-problem body; a transport failure; a thrown error still closes the read bracket. `repos/outcomes.test.ts` rewritten for the three mappings. The contract test against the core's `ProblemTypes`.

# Stage D — Surfaces render by type
*Size: large, mechanical in the main. Depends on B and C.*

- [ ] **One describer.** `describeFailure(failure)` in SIPA: the message sheet keyed by `type` (decision 4), `detail` as the secondary line, a generic line for a transport failure. The toast on a rejected write (the `EntityRef` wrapper) and the 34 write sites use it; per-site wording goes where it says something the type does not.
- [ ] **Field errors on editables.** `EditableTextPart` and the other editables show the field error the binder hands them, in the slot their syntax check already uses, and clear it on the next edit. This is also the test that closes the editable harness row of [[Criteria - Client SDK & Repository System]].
- [ ] **Absence panel.** `p7t-entity-absence` gains a `refused` state for a read the core turned down (not an outage, not a removal), with the describer's line and no retry button.
- [ ] **Enforcement modal** unchanged in behaviour; it is one `type` the describer routes to a modal instead of a toast.
- [ ] **Tests.** SIPA's binding suite: a rejected write's toast comes from the sheet; a validation failure marks the editable.

# Stage E — Documents
- [ ] [[Clarification - Local Repo System]] gains a *Failures* section; [[Criteria - Client SDK & Repository System]] gains the stage A, C and D rows; `local-repo/README.md`, `local-repo/.GENESIS.md`, `sdk.ts/.GENESIS.md` updated; the *Reserved for the error system* section of [[Plan - Local Repo Separation]] marked as delivered.

# Sequencing
B first (it does not wait for the core), then A, then C, then D, then E. A waits for the state-system merge. Verification after every stage as in the separation plan: `npm test` in `local-repo`, `sdk.ts` and `sipa`, the SIPA typecheck against its baseline, both bundles, and for A the core's test project.
