---
status: implemented
assignee: Copilot 🤖
phase: 2a
---
# Core Unit Tests
The core has no automated tests. Every proposal so far has been validated by hand — running `serve` and poking the API — against the developer's real per-user environment. This PEP establishes an automated test suite for the core, and the isolated dev environment that makes running it (and future sandbox development) safe and parallelisable.

## Motivation
The correctness of the vault/watcher system rests on invariants that nothing but discipline currently pins: markdown body preservation, implicit synchronisation-boundary authority, path classification, idempotent canonical rewrites, and vault-migration round-trips. There is a long runway of proposals ahead that all pass through the most fragile part of the system. A test safety net is the highest-value investment before that work, and a prerequisite for refactoring the core toward the eventual abstracted platform.

Manual verification today is also invasive: it uses the real `~/.pleiades/plaintorch` socket, config, and the fixed loopback port, so it cannot run in parallel and pollutes the developer's environment. The suite needs an isolated environment first.

## Dev-Latch Environment
`serve` selects its per-user environment automatically:
- **Under the service runner** (the installed Windows service / systemd unit the end user runs) — the normal per-user environment, unchanged.
- **`--ephemeral`** — a randomised, user-independent temporary profile (its own socket, config, and settings under the OS temp directory), cleaned up afterwards. This is what the test suite and throwaway sandbox runs use, and it is inherently parallel-safe.
- **Interactive manual `serve`** (dev/testing) — relaunches itself to run as a static predefined OS user, `PLAINTORCHDEV`, and **rejects** if that relaunch fails. Manual serve is only ever used in dev/testing; end users use the service runner.

The loopback HTTP port is now **opt-in** (`--loopback`); `serve` is **socket-only by default**. Clients are expected to use the socket file.

The `PLAINTORCHDEV` account is created once, out of band, by static setup scripts (`Setup-DevUser.ps1`, `setup-devuser.sh`) that the developer runs with elevation. The core never provisions OS accounts itself.

## Test Framework
The suite uses **xUnit**. Its per-test instance model maps directly onto the isolation requirement below, and its fixtures express shared read-only setup cleanly. (.NET's own first-party framework, MSTest, was considered; xUnit is the better fit for a filesystem-and-database integration harness.)

## Test Vault Isolation
Each test runs against a **fresh, isolated vault** — its own temporary directory and its own SQLite database — initialised through the **real dependency-injection graph** (the same composition the app uses, with the host built but never started, so the watcher and Kestrel stay dormant). A vault is disposed and deleted after each test. This maximises isolation, which is essential for a system whose behaviour *is* filesystem and database side effects; per-test cost is acceptable for a core suite.

Seeding is done either through the real application services (happy paths) or by placing raw files directly (legacy and edge cases, such as the migration scenario). Behaviour is driven deterministically through the discovery→decision→sync pipeline rather than through live filesystem-watcher timing.

## Coverage and the Criteria Document
This PEP builds the harness and a first tranche covering the highest-risk invariants; it does not attempt exhaustive coverage. Ongoing coverage is tracked in a living document, `_documents/Criteria - Validators, Checks & Unit Test.md`, which catalogues per subsystem what must be and is tested — happy cases, edge cases, and possible faults — with a status marker per item. It doubles as the test backlog for future proposals.

---

# Plan of Action

## Context
The core has zero automated tests; correctness rests on invisible invariants (body preservation, implicit boundary authority, path classification, idempotent rewrite, migration round-trip). Manual verification uses the real per-user socket/config/loopback, so it is invasive and non-parallel. This PEP delivers an isolated dev environment, an xUnit harness over the real DI graph, a high-risk test tranche, and a living Criteria document. Stack: net10.0, EF Core 10.0.7 + SQLite, A11d.Module DI (`Install<PLAINTORCH>()` auto-discovers modules); the core is a `Sdk.Web` exe a test project references and drives without starting the host.

## Part 0 — Dev-latch environment (prerequisite)
- **`core/Plaintorch/PlaintorchUserConfiguration.cs` — `PlaintorchUserLayout`:** add `const DevUserName = "PLAINTORCHDEV"` and a dev password source (`PLAINTORCHDEV_PASSWORD` env var, documented static default matching the setup scripts — dev-only, unprivileged). Add factories beside `CreateDefault()`: `CreateEphemeral()` (root under `Path.GetTempPath()/plaintorch-dev/{guid}`) and `CreateAt(root)`. Add `bool LoopbackEnabled` (default false) and `Cleanup()` (delete an ephemeral root). All paths stay derived from `RootPath`.
- **`core/Program.cs`:** environment selection for the **`serve`** command only (init/activate/bootstrap-service keep `CreateDefault`): `--ephemeral` → `CreateEphemeral()`; else running-as-service → `CreateDefault()`; else interactive → if `Environment.UserName` ≠ `PLAINTORCHDEV`, relaunch `Environment.ProcessPath` with the original args via `Process.Start` (`UseShellExecute=false`; Windows `UserName`+`PasswordInClearText`+`Domain="."`; Linux `sudo -u plaintorchdev`), wait, forward exit code, **reject (non-zero) on failure** with a setup-script hint. The relaunched child *is* `PLAINTORCHDEV` so the guard prevents a loop and `CreateDefault()` roots at its home. Register the chosen layout as a singleton **before** `Install<PLAINTORCH>()`; change `PlaintorchModule` to `TryAddSingleton(PlaintorchUserLayout.CreateDefault())` so the pre-registration wins. Kestrel always binds the socket; binds loopback only when `--loopback`/`LoopbackEnabled`. Print the socket path (and loopback URL if enabled) on start.
- **Setup scripts (static, checked in, never auto-run):** `core/DevUser/Setup-DevUser.ps1` (create local `PLAINTORCHDEV`, static dev password, grant batch-logon right) and `core/DevUser/setup-devuser.sh` (`useradd` system `plaintorchdev`); add both to the csproj `Content` copy.

## Part 1 — Test project + harness (`core.tests/plaintorch.core.tests.csproj`)
- `Microsoft.NET.Sdk`, `net10.0`, `IsPackable=false`; packages `Microsoft.NET.Test.Sdk`, `xunit.v3`, `xunit.runner.visualstudio`; `<ProjectReference ..\core\plaintorch.csproj>` (add explicit `FrameworkReference Microsoft.AspNetCore.App` only if `WebApplication` types don't resolve). Namespace `Pleiades.Tests`; run via `dotnet test core.tests`.
- **`PlaintorchTestHost`:** `WebApplication.CreateBuilder`; register `VaultOptions{VaultPath=tempDir}` **and** an ephemeral `PlaintorchUserLayout`; `Install<PLAINTORCH>()`; `.Build()`; never `RunAsync`.
- **`TestVault : IAsyncLifetime`** (fresh per test): create temp dir + host + one DI scope + run `PlaintorchEngine.InitializeVaultAsync()`. Expose `Get<T>()`, vault paths, and helpers `SeedObjectiveAsync(...)` (real `IObjectiveApi`), `WriteVaultFile/ReadVaultFile/VaultFileExists`, and pipeline drivers `ScanAsync()/InspectAsync(path)/ExecuteSyncAsync(candidate)`. Dispose: scope/host, `SqliteConnection.ClearAllPools()`, then delete temp dir (release DB handle **before** delete) and `Cleanup()` the ephemeral layout.

## Part 2 — High-risk test tranche
Serializer round-trip (quiet `puck` emission, field mapping, scalar formatting, unknown-key preservation, validation flags); body preservation (byte-identical across canonical rewrite / API update); implicit boundary (no-file-on-create, begin materialises title-only + `puck`, boundary-authoritative delete incl. title-only recovery, pre-begin delete ignored); path classification (objectives-root / partition / self-named; directive files not misclassified; ownership boundaries excluded); migration round-trip (legacy `{id} - Title.md` + DB row + `SchemaVersion=1` → quiet form, body preserved, graveyard snapshot, version→2, history row; re-run no-op); version store (round-trip, missing⇒baseline, fresh vault stamped current); file locator (path honours PuckStorage / partition / parent hierarchy).

## Part 3 — `_documents/Criteria - Validators, Checks & Unit Test.md`
A living coverage catalogue and backlog: one section per subsystem (PUCK, Markdown serialize/parse, FileLocator, Discovery/classification, Sync actions, Canonical storage, Implicit boundary, Vault migration, Version store, Dev environment), each with a table (*Behavior/Invariant* | *Kind* happy/edge/fault | *Status* ✅/⏳/🔲 | *Test ref*) plus prose on edge cases and known/possible faults. Seed the full surface; mark the tranche ✅, the rest ⏳.

## Verification
`dotnet build` (core + tests) clean; `dotnet test core.tests` green, run twice for isolation. Prove teeth: temporarily break the body-preservation invariant and confirm its test goes red. Dev env (in-sandbox, `PLAINTORCHDEV` absent): `serve --ephemeral --vault <tmp>` uses a random temp profile, socket-only, prints the socket path, touches no real `~/.pleiades`; `--loopback` re-enables the port; interactive `serve` without the account **rejects** with the setup-script hint.

## Notes
- The `PLAINTORCHDEV` relaunch is Windows-centric and cannot be exercised in the CI/sandbox (no account/credentials); the ephemeral and reject-on-failure paths are verified there, and true relaunch is verified by the developer post-setup.
- The static dev password in-tree is a deliberate, documented dev-only choice, overridable via `PLAINTORCHDEV_PASSWORD`.
- Tests are behaviour-focused (files + database + decisions) so they survive the planned dispatch-coupling refactors rather than obstructing them.
