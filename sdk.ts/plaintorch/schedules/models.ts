import { model } from "@a11d/api-dotnet"

/** Where a backlog schedule comes from (PEP101). Numeric to match the wire form; members are only ever appended. */
export enum BacklogScheduleType {
	/** The user's plan: when the item is meant to begin or finish. */
	Plan = 0,
	/** The reality: when the item began or finished, standing while it stays so. Written and withdrawn by the core. */
	Record = 1
}

/** A lifecycle phase (PEP101): the item's begin or its finish. Numeric to match the wire form. */
export enum LifecyclePhase {
	Begin = 0,
	Finish = 1
}

/**
 * One point in a backlog item's schedule (PEP101): when an incentive, a directive or a checkpoint begins or finishes
 * ({@link state}), as the user plans it or as it actually turned out ({@link type}). An owner holds at most one schedule
 * per type and state; adding one to an occupied slot replaces it. Records are the core's: one stands exactly while its
 * phase does, and is withdrawn when the item's status is pulled back out of it. A checkpoint is one-shot — it cannot be
 * started, only finished, by unlocking — so it never has a begin.
 */
@model('BacklogSchedule')
export class BacklogSchedule {
	id!: number
	type!: BacklogScheduleType
	state!: LifecyclePhase
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
 * Payload to add a schedule to an incentive, a directive or a checkpoint (PEP101). Only plans are accepted, and only of a
 * phase the owner passes through: a checkpoint has no begin to plan.
 */
export interface AddBacklogScheduleRequest {
	/** The owner's id: an incentive, a directive or a checkpoint. */
	entityId: string
	type: BacklogScheduleType
	state: LifecyclePhase
	/** A civil ISO datetime (`2026-10-05T00:00:00`); a time with an offset is read as the core's local wall clock. */
	time: string
}
