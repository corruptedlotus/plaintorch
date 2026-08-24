import type { Objective } from "../objectives/models"
import type { Checkpoint } from "../dependencies/models"
export interface OnrushSprint {
	id: string
	title: string
	startDate: string | undefined
	endDate: string | undefined
	objectives: Objective[]
	executiveOrders: ExecutiveOrder[]
	/** The checkpoints this sprint tracks, its milestone among them (PEP102). */
	checkpoints: Checkpoint[]
	/** The id of this sprint's milestone checkpoint, created with the sprint (PEP102). */
	milestoneCheckpointId: string | undefined
	/** This sprint's milestone checkpoint, when loaded. */
	milestoneCheckpoint?: Checkpoint | undefined
	/** Persisted dependency-canvas layout: a JSON map of node key to position, or absent (PEP102). */
	graphLayout: string | undefined
}

export interface ExecutiveOrder {
	id: string
	title: string
	onrushSprintId: string
	onrushSprint?: OnrushSprint | undefined
	summary: string | undefined
	effectiveFrom: string | undefined
	effectiveUntil: string | undefined
	/** Whether the order is in effect today, resolving its window against the owning onrush (PEP102.5). */
	isActive?: boolean
	/** Whether the order has no explicit window and is bound to its parent onrush's active span. */
	isOnrushBound?: boolean
}

export interface ExecutiveOrderPlan {
	title: string
	summary?: string | undefined
	effectiveFrom?: string | undefined
	effectiveUntil?: string | undefined
}

export interface ExecutiveOrderUpdate {
	title?: string | undefined
	/** Nullable fields: omit to keep, a value to set, `null` to clear. */
	summary?: string | null | undefined
	effectiveFrom?: string | null | undefined
	effectiveUntil?: string | null | undefined
}

export interface OnrushSprintPlan {
	title: string
	startDate?: string | undefined
	endDate?: string | undefined
}

export interface OnrushSprintDateRequest {
	date?: string | undefined
}

/** Payload that persists a sprint's dependency-canvas layout; a null layout forgets it (PEP102). */
export interface SetGraphLayoutRequest {
	layout?: string | undefined
}

export interface OnrushSprintUpdate {
	title?: string | undefined
	/** Nullable dates: omit to keep, a value to set, `null` to clear. */
	startDate?: string | null | undefined
	endDate?: string | null | undefined
}
