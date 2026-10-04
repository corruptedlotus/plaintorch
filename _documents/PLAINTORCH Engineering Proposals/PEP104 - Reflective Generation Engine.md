---
status: implemented
assignee: Copilot 🤖
phase: 3a
---
# Reflective Generation Engine

Reflectives are the reflection prompts of a Polaris cycle. They come from **Lunar Reflection**, introduced in [[PEP100 - Moonlight Directives, The Declarative Ecosystem & Timeframes]]: decrees that opt in with `reflect: true`. PEP100 confined it to decrees inside a Moonlight hierarchy; that restriction is lifted (see *Any hierarchy* below). This PEP gathers what PEP100 built and adds three rules:
- when generation pauses;
- how a cycle chooses its reflectives;
- what finishing them is worth.

![[PEP104 - Reflective Generation Engine 20260807012402]]

*The original sketch: the cycle's reflectives sit in their own collapsible "Daily Reflectives" group below the executives, with a done/total count.*

# Lunar Reflection (from PEP100)
## Reflect-decrees
- A decree carries a `reflect` flag (markdown field `reflect`). Any decree may set it, whichever hierarchy it sits in: a **Moonlight hierarchy** (a lunar directive is the decree's own directive or one of its ancestors), a stellar one, or none at all for a top-level decree.
- With `reflect` on, the decree's orbit must have strict **day granularity**. Nothing finer than a day and no span format is allowed. This is checked on create and on every update that touches the orbit or the flag.
- Reflect-decrees never produce attentives. The rolling harden-on-time pass and the agenda projection both skip them, so a reflect-decree's orbit only ever feeds reflectives.

## Any hierarchy
PEP100 limited Lunar Reflection to Moonlight hierarchies, and the flag did nothing under a purely stellar lineage. That restriction is **lifted**: a stellar-hierarchy or top-level decree with `reflect` on is a reflect-decree like a lunar one. It is selected the same way, rewards the same way, and stops producing attentives. Only pausing still depends on the hierarchy, since only lunar directives can pause reflection (see *Pausing*), so a decree with no lunar directive on its lineage is never paused by one.

A decree that already carried `reflect: true` outside a Moonlight hierarchy, set by hand in its note or kept when it moved out of one, is a reflect-decree from the next cycle begin on. Attentives it hardened before then are kept.

## Reflectives
- A reflective is **cycle-bound**. It belongs to one Polaris cycle and has these fields:
  - a description: the decree's title;
  - its originating decree;
  - an optional time of day;
  - an executed flag.
- Reflectives are generated once, when a cycle begins. Every activation path (begin, start-new, and the auto-start when adding to the current cycle) goes through that single point, `ProximityMaterializationService.MaterializeForCycleAsync`.
- A new reflective takes the same auto-inclusion affinity an executive gets: the nearest directive **availability**, else the decree's **college** (PEP100 Patch100.2). This happens at creation only.
- Deleting a decree keeps its reflectives in ended cycles, with the decree reference cleared, because they are work records. Its reflectives in the active cycle or a planned cycle are removed with it.
- Cycle begin is the **only** way a reflective comes into existence, because reflectives belong to Polaris cycles alone. The manual draw (`POST /api/polaris/{id|current}/reflectives/draw`) is **retired**. It dated from the genesis scaffold of the "Draw Reflectives" action and minted decree-less placeholder prompts ("Reflective prompt N for …"). Every reflective therefore starts out with a decree. The reference is cleared only when that decree is deleted after the cycle has ended.

## Surfaces
- **Decree banner:** every decree banner has the *Lunar Reflection* toggle (`p7t-editable-toggle`) that edits `reflect`. The pre-heading still names the hierarchy: "Moonlight Decree" when the decree's directive is lunar, "Pleiades Decree" otherwise.
- **Moonlight page:** the *Lunar Reflection* grid lists every live reflect-decree, stellar and top-level ones beside the lunar ones. Its directive column's glyph tells the hierarchies apart.
- **Briefing:** the Polaris card shows the cycle's reflectives as the "Daily Reflectives" group from the sketch. Each row can be checked off, and the count updates as reflectives are done.

# Pausing
A reflect-decree is **live**, and so can generate, only when:
- the decree itself is **Active**. An Abandoned decree never generates.
- every **lunar directive** on its lineage is **Active**. That means the decree's own lunar directive and every lunar ancestor above it, so an **On Hold** or **Stale** lunar directive pauses its whole subtree. Stellar directives' own states are not consulted, so a decree with no lunar directive on its lineage, stellar or top-level, is paused only by being Abandoned.

A paused reflect-decree creates no reflectives and does not fall back to attentives. Its orbit cursor stands still, and the days it missed are never back-filled. Generation resumes at the first cycle that begins after the hierarchy is active again.

> Lunar directives are created **On Hold**. A new Moonlight hierarchy must be set Active before its reflect-decrees generate.

# Selection
When a Polaris cycle begins, its reflectives are chosen in two steps:
1. **Orbit matches.** Every live reflect-decree whose orbit resolves onto the cycle's day is taken. The cycle's day is its local start day. The orbit resolves on the decree's own calendar, else the vault default. A match means an occurrence's period contains that day. A super-day occurrence (week, month) therefore matches the first cycle that begins inside its period, because seeking moves the cursor past it.
2. **The draw.** Among the live reflect-decrees that have **no orbit**, the cycle draws *base number of daily reflectives* (a preference, default **3**). The draw is uniformly at random, without replacement. A pool smaller than the base number is taken whole.

Some consequences of these two steps:
- **The steps add up.** The base number never counts the orbit matches, so a cycle holds every orbit match plus up to the base number of drawn reflectives.
- **One reflective per decree per cycle.** Running the generation again for a cycle it already ran on adds nothing. Pool decrees already reflected in the cycle count toward the draw, so raising the base number tops the draw up only by the difference.
- **The pool spans hierarchies.** Every live orbit-less reflect-decree joins it, lunar, stellar or top-level, and the draw does not weigh them apart.

# Rewards
Two rewards apply, and both follow execution:
- **Daily reward.** When every reflective of a cycle is done, the collection grants the *daily reflection reward* (a preference, default **4** Celestron) once. The ledger source is the cycle. Un-executing any reflective of the cycle revokes it. The amount is read when the reward is granted, so changing the preference never rewrites a reward already on the ledger. A value of 0 or less grants nothing.
- **Per-run reward.** A reflective generated from a decree also grants that decree's per-run reward (`ActiveCelestron`) when executed, and un-executing revokes it. The ledger key is decree + cycle. The key uses its own description prefix, so a decree that is both reflected and added to the same cycle as an executive rewards each run separately. This **supersedes PEP100's rule** that reflectives ignore their decree's reward.

Both rules are state-policy rules on the save hook (`PlaintorchStatePolicyProcessor`), so every write pathway gets them.

# Preferences
Both preferences are vault-bound (PEP116) and appear in the settings panel under **Lunar Reflection**.

| Preference | Key | Type | Default | Meaning |
|---|---|---|---|---|
| `ReflectionPreferences.BaseDailyReflectives` | `reflection.base-daily-reflectives` | integer | **3** | How many orbit-less reflect-decrees each cycle draws. A negative value reads as 0. |
| `ReflectionPreferences.DailyReward` | `reflection.daily-reward` | integer | **4** | The Celestron granted once every reflective of a cycle is done. 0 or less grants nothing. |

A changed base number applies from the next cycle begin. A changed daily reward applies to the next grant.

# Implementation
- `core/Plaintorch/Materialization/ProximityMaterializationService.cs`: `MaterializeForCycleAsync` handles liveness, orbit matches, the pool draw, and the one-per-decree guard.
- `core/Plaintorch/PlaintorchOrbitService.cs`: `IsReflectionPausedAsync` answers whether a lunar directive on the lineage pauses reflection. A lineage with no lunar directive on it is never paused.
- The attentive paths (the rolling pass in `ProximityMaterializationService.MaterializeForNowAsync` and `AgendaProjectionService`) leave out every reflect-decree in their query, with no lineage walk.
- SIPA mirrors the selection's liveness in `buildLunarReflection` (`sipa/components/moonlight/moonlightTree.ts`), which feeds the Moonlight page's Lunar Reflection grid.
- `core/Plaintorch/State/PlaintorchStatePolicyProcessor.cs`: `ApplyReflectiveDecreeRewardRulesAsync` grants the per-run reward, and `ApplyReflectiveCollectionRewardRulesAsync` grants the daily reward. The former placeholder constant (10) is gone.
- `core/Plaintorch/Preferences/ReflectionPreferences.cs` holds the preferences, with their `PreferenceKeys` constants, `PreferenceCatalog` entries and Options binding.
- Retired: both `reflectives/draw` endpoints, `IPolarisCycleApi.DrawReflectivesAsync`, the `ReflectiveDrawRequest` contract, and the SDK's `drawReflectives`/`drawReflectivesForCycle`.

# Verification
`core.tests/Core/LunarReflectionTests.cs` covers:
- pausing under an On Hold or Stale lunar directive, and under an inactive lunar ancestor above an active lunar child;
- stellar levels being ignored;
- stellar-hierarchy and top-level reflect-decrees generating like lunar ones, and projecting no attentives;
- resuming without back-fill;
- abandoned decrees generating nothing;
- orbit matches plus the pool draw, with a stellar decree in the pool;
- the base number preference (0, 1, negative, larger than the pool);
- repeated generation adding nothing, and topping up after the base number is raised;
- both rewards, together and revoked;
- the daily reward preference (a custom amount, and 0).

The generation, affinity, rolling-pass, deletion and preference-catalog suites cover the rest.

# Open Questions
- **Weighting and cooldowns.** The draw is uniformly random. Nothing keeps a decree from being drawn on consecutive days, or from going undrawn for a long run. The source pools, weighting, cooldowns and prompt synthesis once pictured for this engine are not built.
- **Prompt text.** A reflective reads as its decree's title. There is no per-day prompt.
- **Mid-cycle changes** take effect only at the next cycle begin. This covers turning `reflect` on, activating a lunar directive, and raising the base number. Nothing re-runs generation for a cycle that has already begun.
- **Negative preference values** are not rejected at write time, because the catalog only knows "integer". They are clamped where the value is used.
