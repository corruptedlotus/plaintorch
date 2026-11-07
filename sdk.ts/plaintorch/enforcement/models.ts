import type { LifecycleState } from "../schedules/models"

/** The request header carrying a client's state enforcement choice (PEP103). */
export const stateEnforcementHeader = "X-Plaintorch-Enforcement"

/** The one value {@link stateEnforcementHeader} takes: go ahead with the compensations the core proposed. */
export const forceCompensationValue = "auto"

/** The `type` of every answer the core's state enforcement gives. */
export const stateEnforcementProblemType = "state-enforcement"

/**
 * How firmly the core holds the backlog to its lifecycle rules when a change would break one (PEP103), the
 * `backlog.state-enforcement` preference. Numeric to match the wire form.
 */
export enum BacklogStateEnforcementLevel {
	/** The change goes through; only systemic violations are refused. */
	Ignore = 0,
	/** The core moves the other items the change needs. */
	Auto = 1,
	/** The client is asked first: cancel, or go ahead with the moves. The default. */
	Warn = 2,
	/** The change is refused. */
	Block = 3
}

/** What became of a change the core's state enforcement judged (PEP103). Numeric to match the wire form. */
export enum StateDecisionOutcome {
	Proceed = 0,
	Compensate = 1,
	/** Held back: the client confirms by repeating the request with {@link stateEnforcementHeader}, or cancels. */
	Confirm = 2,
	Refuse = 3
}

/** Whether moving other items could resolve a conflict. Numeric to match the wire form. */
export enum StateConflictKind {
	/** A lifecycle rule; the enforcement level decides. */
	Lifecycle = 0,
	/** A violation no state change resolves; refused at every level. */
	Systemic = 1
}

/** The item a conflict or a compensation is about: its kind (the core's type name), its id and its title. */
export interface StateSubject {
	kind: string
	id: string
	title?: string | null
}

/** One move the core would make to another item so a change can go ahead. */
export interface StateCompensation {
	subject: StateSubject
	from: LifecycleState
	to: LifecycleState
	/** The status the item would take, by name, when its kind has one for the state. */
	status?: string | null
	/** Why the move is needed. */
	reason: string
}

/** One rule a change would break. Its message says what blocks the change, not which items do. */
export interface StateConflict {
	kind: StateConflictKind
	/** A stable name for the rule, such as `dependency.start-gate` or `directive.open-children`. */
	rule: string
	subject: StateSubject
	message: string
	compensations: StateCompensation[]
}

/**
 * The body of the 409 the core answers a change its state enforcement stopped with (PEP103): held back for confirmation,
 * or refused. {@link compensations} lists the moves confirming would make.
 */
export interface StateEnforcementProblem {
	type: typeof stateEnforcementProblemType
	outcome: StateDecisionOutcome
	level: BacklogStateEnforcementLevel
	message: string
	conflicts: StateConflict[]
	compensations: StateCompensation[]
}

/**
 * Answers the core's state enforcement on the client's behalf (PEP103), installed on the core client
 * ({@link PlaintorchCoreClient.stateEnforcement}). Without one, a held-back change is cancelled and a refusal goes unseen.
 */
export interface StateEnforcementHandler {
	/**
	 * A change waits for confirmation. Resolve `true` to repeat it forcing the compensations, `false` to cancel it — the
	 * call then fails as any other write does.
	 */
	confirm(problem: StateEnforcementProblem): Promise<boolean>
	/** A change was refused; nothing was written. */
	refused?(problem: StateEnforcementProblem): void
}

/**
 * What a {@link PlaintorchCoreClient.withStateEnforcement} scope does to the writes sent inside it (PEP103): forces them,
 * or hands the core's answer to the caller in place of the client-wide {@link StateEnforcementHandler}.
 */
export interface StateEnforcementScope {
	/** Sends the writes forcing the compensations the core proposed: the user's confirmation of a held-back change. */
	readonly force?: boolean
	/**
	 * Takes the core's answer when it holds a write back or refuses it, so the caller shows it where the write was asked
	 * for. The client-wide handler is not asked, and the write fails as any write the core turned down does.
	 */
	readonly answer?: (problem: StateEnforcementProblem) => void
}

/** Whether a parsed response body is a state enforcement answer. */
export function isStateEnforcementProblem(value: unknown): value is StateEnforcementProblem {
	return typeof value === "object"
		&& value !== null
		&& (value as { type?: unknown }).type === stateEnforcementProblemType
}
