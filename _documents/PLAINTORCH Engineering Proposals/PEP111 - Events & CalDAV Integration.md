---
status: idea
assignee: Copilot 🤖
phase: 3b
---

# PEP111 — Events & CalDAV Integration

The eventual goal is two-way CalDAV/iCalendar (RFC 5545) integration. Before the sync layer can exist, the
declarative/occurrence model has to be unified and solidified so it maps cleanly to iCalendar's occurrence
shape. That unification is the **Preparation** below; the sync protocol itself is deferred to the main body.

## Preparation — Declarative & Occurrence Unification

### Why
Occurrences were a mess: an occurrence carried `Date` + `Time` + `RecurrenceDate` + `RecurrenceTime` +
`PeriodEndDate`, eventives added `StartTime`/`EndTime`, dated-vs-orbit fates took two materialization paths, and
allocation lived on the occurrence. The unified model makes an occurrence the literal shape the Orbit engine
emits — an `Epoch` (a moment plus how much of it is real) — so it maps 1:1 onto an iCalendar instance.

### Locked model
- **`Declarative : Incentive`** (non-hierarchical base of Fate/Decree). Carries `Orbit` (sole temporal source),
  `Calendar?` (resolution-only; null → kind default, Pleiadean once user prefs land — see PEP116), and a
  denormalized `NextOccurrence?`. Fate loses `Date`/`StartTime`/`EndTime`/`EventDuration` — everything is Orbit
  (one-shots use the `Z{…}` literal). *(Declarative base + Calendar landed in Phase 2a.)*
- **`Occurrence`** (non-hierarchical base of Attentive/Eventive: an abstract CLR base outside the EF model, so
  each kind keeps its own table). Fields:
  - **`Epoch`** owned type `{ Moment: DateTime (civil/wall), Granularity: OrbitUnit, Duration?: <nominal, Orbit
    duration notation>, TimeZone?: string }`. Duration null ⇒ one granularity unit (the window). `TimeZone` null
    ⇒ wall/floating. A super-day (week/month/year) occurrence stores its whole period as the `Duration`, in days
    resolved on its declarative's calendar — the one-unit fallback adds a *nominal* (Gregorian) unit, which would
    misplace a 60/61-day Pleiadean month's end.
  - **`RecurrenceId`**: a single `DateTime` — the original `Epoch.Moment`, pinned at materialization (immutable
    identity; iCalendar RECURRENCE-ID). An all-day slot sits at midnight. The old `RecurrenceDate`/`RecurrenceTime`
    pair is gone, and dependency endpoints carry the same single moment per side.
  - Resolution/state, owner FK. **No** Estimation/Min/Max — allocation lives on the `Executive` (done with the
    Executive sub-chunk).
- **`EventiveResolution`** gains **`OptOut`** → {Pending, Missed, Cancelled, OptOut}.
- **`OrbitOccurrenceInstance`** → `(Epoch, Granularity, Duration)` (the engine already carries `Granularity` on
  both resolution and span entries as of Phase 1).
- **`Executive`**: `ObjectiveId → IncentiveId` (nav `Incentive`), optional `IsDecree`, `DefaultLength` fallback;
  cycle-bound attentives become Executives.
- **Due** (Objective/StellarDirective/Checkpoint): a lighter owned moment `{ Moment, TimeZone? }`, no granularity.
  Checkpoint gains `Due`; toll is suppressed (effective 0) before Due.
- **Dependency** endpoint refs collapse to a single `RecurrenceId` per side.

### Floating windows (implicit)
An occurrence "floats" when its **granularity window is larger than its duration** (e.g. `Granularity=Day`,
`Duration=2h` → "2h somewhere that day"). It is *derived*, not a stored flag: `Duration` null (fills the window)
or `Duration ≥ window` (anchored at `Epoch`) never floats.

### Generation modes (Fate status → occurrence behavior)
- **Active** — resolve + harden normally (spawns `Pending`).
- **OptOut** (fate only) — resolution *continues*; occurrences project/spawn with `Resolution = OptOut` (hidden
  from the agenda). **Not** auto-hardened; the time-passage pass leaves them alone by default (a user preference,
  PEP116, can opt into time-hardening). Per-occurrence opt-in = set one back to `Pending`. The cursor advances
  through *now* during OptOut, so resume needs no fast-forward.
- **Cancelled** (fate) / **Abandoned** (decree) — resolution *pauses* (no projection, no hardening). On resume →
  Active, **seek the cursor to now** so the paused gap is not back-filled (reuses `SeekOccurrencesThroughInstant`,
  discarding the occurrences and persisting only the advanced state).

### Required tests (generation modes)
1. An OptOut fate spawns its eventives with `Resolution = OptOut` (generation continues, hidden).
2. OptOut spawns are not auto-hardened, and the time-passage pass does not harden them under the default pref
   (pref on → they do harden; pref off → they don't).
3. Cancelled fate / Abandoned decree pauses generation, and resume → Active fast-forwards the cursor to now with
   no back-fill of the gap.

### Frontmatter migration
Fold each existing fate's `date`/`startTime`/`endTime`/`eventDuration` into a `Z{y/M/d[Th:m]}=<dur>` orbit, and
**pin every pre-existing fate to `Calendar = Gregorian`** so its meaning is preserved when the kind default flips
to Pleiadean. Coordinate with the frontmatter-authority refactor (separate branch) rather than adding an ongoing
codec — `Z{…}` is itself the legible stored form, so no sugar codec is needed.

### CalDAV mapping (for the deferred sync layer)
Two-way at the occurrence/instance level is lossless **except** two export cases where PLAINTORCH is strictly
more expressive: (1) floating intraday windows have no iCalendar form (export must pin or all-day them); (2)
nominal month/year `Duration` has no iCalendar `DURATION` unit (export as a computed DTEND, losing nominalness).
Everything else round-trips: Epoch→DTSTART (VALUE=DATE/DATE-TIME by granularity), TimeZone→floating/TZID,
RecurrenceId→RECURRENCE-ID, OptOut/Cancelled→STATUS/EXDATE, Due→VTODO DUE. Calendar is resolution-only, so
Pleiadean resolution costs nothing on export (occurrences store concrete civil instants). The native record stays
authoritative so a round-trip through an external client can't clobber a floating/nominal original.

### Sub-chunk sequence & status

The refactor is **complete**: the core (C#) side is green and the client (SDK + Obsidian plugin) sweep (2g) has
landed. 2h brought the occurrence spine in line with the locked model, which 2b/2f had not fully honoured.

1. Orbit engine — `Z`/`z` literal, span granularity, floating support. **Done (Phase 1, `5ffdf5b`).**
2a. `Declarative` base + calendar-as-data. **Done (`a1d91d4`).**
2b. Occurrence spine — `Epoch` owned type, occurrence reshape, `EventiveResolution.OptOut`,
   `OrbitOccurrenceInstance` rename, three-service materialiser rewrite. **Done (`998c2f5`).**
2c. Declaratives — Fate → Orbit-only, generation modes + seek-to-now (+ the three tests above), `NextOccurrence`
   denormalization, DB + vault (markdown) migrations. **Done (`6c84584`, `274cb45`, `6ed4016`; migration e2e test `98a9243`).**
2d. Executive — `IncentiveId` (objective OR decree) + allocation ownership; cycle-bound attentives → decree-executives;
   occurrences drop `Estimation/Min/Max`. **Done (`d912ddf`, `13425cc`, `f625b89`).**
2e. Due + Checkpoint owned `Due` moments; Checkpoint toll-before-Due; markdown-scalar serializer hook. **Done (`cb388b4`, `66742fc`).**
2f. Dependency endpoint refs → single `RecurrenceId`. **Done (`24fdc51`).**
   *(PEP116 user preferences merged in at `e007e73`; the temporal branch's OptOut toggle was reconciled onto
   `AgendaPreferences.AutoMaterialiseOptOut`.)*

2g. **Contracts/SDK sweep + UI restructure — Done (SDK `42a4f86`, plugin `19f70c1`).** The TS SDK (`sdk.ts`)
   and the Obsidian plugin now mirror the unified C# contracts. Highlights: occurrences carry an owned `Epoch`
   (with server-computed `date`/`isAllDay`/`timeOfDay`/`endMoment`) and no allocation; `Attentive` is unbound-only;
   `Fate` is orbit-only with `calendar`/`nextOccurrence`; `Executive.incentive` is an objective **or** decree
   (`incentiveKind`/`isDecreeIncentive`/`isObjectiveIncentive` narrow it); `PolarisDecreeAdd`/`addDecreeExecutive`
   replace the attentive-add path; owned `Due` on Objective + Checkpoint. In the plugin, `ObjectiveItemExecutive`
   + `DecreeItemAttentive` collapsed into one `IncentiveItemExecutive`, `polarisActivity` reduced to `Executive`,
   the fate schedule controls fold a fixed date/window into a `Z{…}` literal (`fateScheduleToOrbit`, validated
   against the vendored orbit parser), and the attentive editor dropped allocation. The C# wire shapes it mirrors
   live in `core/Plaintorch/Api/Transport/PlaintorchApiTransportContracts.cs` and
   `.../Api/Contracts/PlaintorchApiContracts.cs`. Sweep inventory (as delivered):
   - **Fate**: no more `date`/`startTime`/`endTime`/`eventDuration`; orbit-only. A one-off is a `Z{y/M/d[Th:m]}`
     literal (with a `=<dur>` span for a window). `FatePlan` keeps the one-off fields as *create sugar* (folded to
     `Z{…}` server-side); `FateUpdate` is orbit-only (reschedule = new orbit).
   - **Declarative**: new `calendar` (`Gregorian`/`Pleiadean`, nullable → kind default) and read-only denormalized
     `nextOccurrence` (a moment, or null).
   - **Occurrences (`Eventive`/`Attentive`)**: date/time replaced by an owned `epoch` `{ moment, granularity,
     duration?, timeZone? }`. `Attentive` is unbound-only — no `polarisCycleId`/`isBound`. Occurrences no longer
     carry `estimation`/`minimum`/`maximum`.
   - **`Executive`**: `objectiveId` → `incentiveId` (an objective **or** a decree); allocation (`estimation/min/max`)
     lives here now; `ExecutiveUpdate` gained `moveToPolarisCycleId` (relocate to another cycle).
   - **Polaris cycle API**: `AddDecreeAttentiveAsync`/`PolarisAttentiveAdd` → `AddDecreeExecutiveAsync`/`PolarisDecreeAdd`
     (returns an `Executive`; routes `POST /polaris/cycles/current|{id}/decrees`). `RemoveAttentiveAsync` and the
     `DELETE /api/polaris/attentives/{id}` route are gone — remove a decree from a cycle via `RemoveExecutiveAsync`.
     `AttentiveOccurrenceRef` lost `id`/`addressesById`; `AttentiveUpdate` lost `moveToPolarisCycleId` and allocation;
     `EventiveUpdate` lost allocation.
   - **Due**: owned `{ moment, timeZone? }`. `ObjectiveUpdate.due` and `CheckpointUpdate.due` carry it; frontmatter is
     one compact field (`due: 2026-07-20`, `…T14:30`, or `…T14:30 America/New_York`). Checkpoint gained a due whose
     presence suppresses its Celestron toll until the due passes.
   - **Dependency**: *(superseded by 2h)* the occurrence slot is now one `recurrenceId` datetime end to end —
     entity columns, `EndpointRef`, `DependencyEndpointRequest`, and the SDK.
   - **Preferences**: already merged — the PEP116 SDK module + settings surface are present and consistent; nothing to
     redo there.
   - **UI restructure (last):** `IncentiveItem`/`IncentiveItemExecutive` — executives now back both objectives and
     decrees; occurrence items read `epoch` and carry no allocation; the due is a moment. Align the components
     accordingly.
   - **Verify:** plugin builds via `node esbuild.config.mjs production` (no `tsc` gate) + `tsc --noEmit --ignoreDeprecations 6.0`;
     SDK Vitest (`npm test`); browser smoke where controllers changed.

2h. **Occurrence spine, as locked — Done.** 2b and 2f had drifted from the locked model: there was no
   `Occurrence` base (each kind duplicated its members behind an `IOccurrenceInstance` interface), `RecurrenceId`
   stayed flat `RecurrenceDate` + `RecurrenceTime` columns behind a computed struct, and `Attentive.PeriodEndDate`
   duplicated what the epoch duration expresses. Now:
   - **Core**: `Occurrence` is an abstract, non-hierarchical base (`Id`, `Epoch`, `RecurrenceId`, abstract
     `RecurrenceOwnerUid`) that EF never maps — no table, no discriminator. `RecurrenceId` is one `DateTime` pinned
     to the materializing occurrence's moment; `Dependency` carries `SourceRecurrenceId`/`TargetRecurrenceId`, and
     `EndpointRef`, the occurrence refs, and `DependencyEndpointRequest` carry one `RecurrenceId` each. The
     `*Materialization` slot-carrier DTOs are gone — the hardening service takes the recurrence-id directly.
     `Epoch.For(occurrence)` builds an occurrence's epoch, folding a super-day period into `Duration` (whole days).
   - **Migration** `OccurrenceRecurrenceId`: hand-written add → backfill → drop (the scaffold guessed renames that
     would have dropped every time of day and moved dependency target slots into the source column). Backfilled
     moments use EF's own text form, so equality lookups match. `Up` and `Down` are exercised against rows written in
     the pre-migration shape by `OccurrenceRecurrenceIdMigrationTests`.
   - **SDK**: `Occurrence` abstract base; `Eventive`/`Attentive` are `@model` classes, so the transport revives them
     from the runtime `@type` the core stamps; `recurrenceId` replaces the date/time pair on occurrences, occurrence
     refs, and dependency endpoints; `periodEndDate` is gone.
