import type { Objective } from "../objectives/models"
export interface OnrushSprint {
	id: string
	title: string
	startDate: string | undefined
	endDate: string | undefined
	objectives: Objective[]
	executiveOrders: ExecutiveOrder[]
}

export interface ExecutiveOrder {
	id: string
	title: string
	onrushSprintId: string
	onrushSprint?: OnrushSprint | undefined
	summary: string | undefined
	effectiveFrom: string | undefined
	effectiveUntil: string | undefined
}

export interface ExecutiveOrderPlan {
	title: string
	summary?: string | undefined
	effectiveFrom?: string | undefined
	effectiveUntil?: string | undefined
}

export interface ExecutiveOrderUpdate {
	title?: string | undefined
	summary?: string | undefined
	effectiveFrom?: string | undefined
	effectiveUntil?: string | undefined
}

export interface OnrushSprintPlan {
	title: string
	startDate?: string | undefined
	endDate?: string | undefined
}

export interface OnrushSprintDateRequest {
	date?: string | undefined
}

export interface OnrushSprintUpdate {
	title?: string | undefined
	startDate?: string | undefined
	endDate?: string | undefined
}
