import { LifecycleState } from "../schedules/models"

/**
 * One status of a kind and the lifecycle state it stands for (PEP103), as the core declares it.
 */
export interface LifecycleStatus {
	/** The status's wire value. */
	value: number
	name: string
	state: LifecycleState
	/** Whether it is the status the core sets for its state of its own accord: the lowest-valued one. */
	isDefault: boolean
}

/**
 * How one kind's lifecycle reads (PEP103), served by the core (`GET /api/system/lifecycles`) so a client reads a status
 * the way the core does rather than keeping its own copy of the declarations.
 */
export interface LifecycleKind {
	/** The kind's runtime type name, as an entity's `@type` carries it: `Objective`, `StellarDirective`… */
	kind: string
	/** The lifecycle states the kind passes through, in order. */
	states: LifecycleState[]
	/** Its statuses in value order; empty for a kind whose state is not a status (a checkpoint). */
	statuses: LifecycleStatus[]
}

const progress = [LifecycleState.Started, LifecycleState.Finished] as const

/** The status of a kind with the given value, or undefined when the kind has none such. */
export function lifecycleStatus(kind: LifecycleKind, value: number): LifecycleStatus | undefined {
	return kind.statuses.find(status => status.value === value)
}

/**
 * The progress states — Started, Finished — a move from `from` to `to` enters, of those the kind passes through, as the
 * core reads a transition: a jump from Open to Finished enters both, a resume from Deferred counts as from before Open,
 * and a deferral or a move back enters nothing. These are the states a dependency can hold back.
 */
export function enteredStates(kind: LifecycleKind, from: LifecycleState, to: LifecycleState): LifecycleState[] {
	if (to === LifecycleState.Deferred) {
		return []
	}

	const floor = Math.max(from, LifecycleState.Open)
	return progress.filter(state => kind.states.includes(state) && state > floor && state <= to)
}
