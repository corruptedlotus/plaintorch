import { model } from "@a11d/api-dotnet"

/** Which source lifecycle event satisfies a dependency (PEP101). */
export enum DependencyTrigger {
	OnBegin = 0,
	OnFinish = 1
}

/** Which target transition a dependency gates while unsatisfied (PEP101). */
export enum DependencyConstraint {
	ToBegin = 0,
	ToFinish = 1
}

/** The kind of entity at one end of a dependency edge (PEP101). Lunar directives are excluded. */
export enum DependencyEndpointKind {
	Directive = 0,
	Objective = 1,
	Fate = 2,
	Eventive = 3,
	Checkpoint = 4
}

/**
 * A reference to one end of a dependency: a kind plus an id (the PUCK id, or an eventive owner id acting as the
 * iCalendar `UID`), optionally qualified by an occurrence slot (`RECURRENCE-ID`) for an eventive endpoint.
 */
export interface EndpointRef {
	kind: DependencyEndpointKind
	id: string
	/** Original occurrence slot date (RECURRENCE-ID), for an eventive endpoint. */
	recurrenceDate?: string | undefined
	/** Original occurrence slot time, for a timed eventive endpoint. */
	recurrenceTime?: string | undefined
}

/** A directed blocking edge: the source (prerequisite) blocks the target (dependant) (PEP101). */
@model('Dependency')
export class Dependency {
	id!: number
	sourceKind!: DependencyEndpointKind
	sourceId!: string
	sourceRecurrenceDate: string | undefined
	sourceRecurrenceTime: string | undefined
	targetKind!: DependencyEndpointKind
	targetId!: string
	targetRecurrenceDate: string | undefined
	targetRecurrenceTime: string | undefined
	/** Source-side trigger; undefined resolves to OnFinish (empty for checkpoint sources). */
	trigger: DependencyTrigger | undefined
	/** Target-side constraint; undefined resolves to ToBegin (empty for checkpoint targets). */
	constraint: DependencyConstraint | undefined
	satisfied: boolean = false

	get source(): EndpointRef {
		return { kind: this.sourceKind, id: this.sourceId, recurrenceDate: this.sourceRecurrenceDate, recurrenceTime: this.sourceRecurrenceTime }
	}

	get target(): EndpointRef {
		return { kind: this.targetKind, id: this.targetId, recurrenceDate: this.targetRecurrenceDate, recurrenceTime: this.targetRecurrenceTime }
	}
}

/**
 * A simple point in the journey that only aggregates dependencies (PEP101). Unlocks once all incoming
 * dependencies are met, unless it still owes a Celestron toll or an external condition.
 */
@model('Checkpoint')
export class Checkpoint {
	id!: string
	title!: string
	/** Optional Celestron toll that must be paid before unlock; undefined means no toll. */
	celestronToll: number | undefined
	tollPaid: boolean = false
	/** External condition switch: undefined = none required, false = required-unmet, true = met. */
	externalCondition: boolean | undefined
	/** The emitted unlock signal, maintained by the core. */
	unlocked: boolean = false
	/** The onrush sprint that tracks this checkpoint, if any (PEP102). */
	onrushSprintId: string | undefined
}

/** The emitted dependency lock for an entity, separate from its status (PEP101). */
export interface DependencyLockView {
	entityId: string
	blockedBegin: boolean
	blockedFinish: boolean
	unsatisfied: Dependency[]
}

/**
 * A candidate endpoint for a dependency, kind-tagged so a picker can tell one from another (PEP102). Returned
 * by the endpoint search over the kinds that may take part in a dependency: stellar directives, objectives,
 * and fates. Lunar directives, decrees, and Polaris-level records never appear.
 */
export interface EndpointHit {
	kind: DependencyEndpointKind
	id: string
	title: string
}

/** One endpoint of a dependency in a create request (PEP101). */
export interface DependencyEndpointRequest {
	kind: DependencyEndpointKind
	id: string
	recurrenceDate?: string | undefined
	recurrenceTime?: string | undefined
}

/** Payload to create a dependency edge (source blocks target) (PEP101). */
export interface CreateDependencyRequest {
	source: DependencyEndpointRequest
	target: DependencyEndpointRequest
	trigger?: DependencyTrigger | undefined
	constraint?: DependencyConstraint | undefined
}

/**
 * Payload to update a checkpoint's name, toll, or external condition (PEP102).
 *
 * The toll and the condition are optional on the checkpoint, so a value sets it while the paired `clear…`
 * flag removes it; leaving both unset leaves the field unchanged.
 */
export interface CheckpointUpdate {
	title?: string | undefined
	celestronToll?: number | undefined
	clearCelestronToll?: boolean | undefined
	externalCondition?: boolean | undefined
	clearExternalCondition?: boolean | undefined
}

/** Payload to create a checkpoint (PEP101). */
export interface CreateCheckpointRequest {
	title: string
	id?: string | undefined
	celestronToll?: number | undefined
	externalCondition?: boolean | undefined
	/** The onrush sprint that should track this checkpoint, if any (PEP102). */
	onrushSprintId?: string | undefined
}

/** Payload to set a checkpoint's external condition switch (PEP101). */
export interface SetCheckpointConditionRequest {
	met: boolean
}
