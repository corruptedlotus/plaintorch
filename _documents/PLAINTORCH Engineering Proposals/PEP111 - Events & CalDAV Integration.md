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
- **`Occurrence`** (non-hierarchical base of Attentive/Eventive). Fields:
  - **`Epoch`** owned type `{ Moment: DateTime (civil/wall), Granularity: OrbitUnit, Duration?: <nominal, Orbit
    duration notation>, TimeZone?: string }`. Duration null ⇒ one granularity unit (the window). `TimeZone` null
    ⇒ wall/floating.
  - **`RecurrenceId`** = the original `Epoch.Moment` (immutable identity; iCalendar RECURRENCE-ID). *(Collapse of
    the old `RecurrenceDate`/`RecurrenceTime` pair; shared with dependency endpoint refs, so done with the
    dependency sub-chunk.)*
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
1. Orbit engine — `Z`/`z` literal, span granularity, floating support. **Done (Phase 1).**
2a. `Declarative` base + calendar-as-data. **Done.**
2b. Occurrence spine — `Epoch` owned type, occurrence reshape, `EventiveResolution.OptOut`,
   `OrbitOccurrenceInstance` rename, three-service materialiser rewrite. **In progress.**
2c. Declaratives — Fate → Orbit-only, generation modes + seek-to-now (+ the three tests above), frontmatter
   migration.
2d. Executive — `IncentiveId` + allocation ownership; cycle-bound attentives → executives.
2e. Due + Checkpoint owned moments; Checkpoint toll-before-Due.
2f. Dependency endpoint refs → single `RecurrenceId`.
2g. Contracts/SDK sweep; the `IncentiveItem`/`IncentiveItemExecutive` UI restructure is last.
