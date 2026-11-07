import { model } from "@a11d/api-dotnet"

/** Where a backlog schedule comes from (PEP101). Numeric to match the wire form; members are only ever appended. */
export enum BacklogScheduleType {
	/** The user's plan: when the item is meant to start or finish. */
	Plan = 0,
	/** The reality: when the item started, finished or was set aside. Written and withdrawn by the core. */
	Record = 1
}

/**
 * The lifecycle state an item is in (PEP103), ordered along the main line Open → Started → Finished. Deferred sits below
 * Open: the side exit for an item set aside (failed, abandoned, opted out, cancelled, skipped, overdue), which has neither
 * started nor finished. Numeric to match the wire form.
 */
export enum LifecycleState {
	Deferred = -1,
	Open = 0,
	Started = 1,
	Finished = 2
}

/**
 * One point in a backlog item's schedule (PEP101, PEP103): when an incentive, a directive or a checkpoint reaches a
 * lifecycle state ({@link state}), as the user plans it or as it actually turned out ({@link type}). An owner holds at
 * most one schedule per type and state; adding one to an occupied slot replaces it. Plans place a start or a finish.
 * Records are the core's and track every state but Open: reaching Started or Finished records it, pulling the item back
 * along the main line withdraws the records it leaves, and setting it aside keeps them and adds a Deferred record. Only
 * the states an owner's kind reaches are scheduled: a checkpoint never starts, and a fate, a decree or a lunar directive
 * never finishes.
 */
@model('BacklogSchedule')
export class BacklogSchedule {
	id!: number
	type!: BacklogScheduleType
	state!: LifecycleState
	/** A civil (wall-clock) ISO datetime with no offset; a plan at midnight stands for the whole day. */
	time!: string
	/** The owning incentive (objective, fate or decree), when an incentive owns the schedule. */
	incentiveId: string | undefined
	/** The owning directive (stellar or lunar), when a directive owns the schedule. */
	directiveId: string | undefined
	/** The owning checkpoint, when a checkpoint owns the schedule. */
	checkpointId: string | undefined
	/** Server-computed: the owner's id, whichever kind of owner it is. Read-only. */
	ownerId!: string
}

/**
 * Payload to add a schedule to an incentive, a directive or a checkpoint (PEP101, PEP103). Only plans are accepted, of
 * {@link LifecycleState.Started} or {@link LifecycleState.Finished}, and only of a state the owner's kind reaches: a
 * checkpoint has no start to plan, and a fate, a decree or a lunar directive no finish.
 */
export interface AddBacklogScheduleRequest {
	/** The owner's id: an incentive, a directive or a checkpoint. */
	entityId: string
	type: BacklogScheduleType
	state: LifecycleState
	/** A civil ISO datetime (`2026-10-05T00:00:00`); a time with an offset is read as the core's local wall clock. */
	time: string
}
