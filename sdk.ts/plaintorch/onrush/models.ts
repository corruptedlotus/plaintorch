import type { Objective } from "../objectives/models"
export interface OnrushSprint {
	id: string
	title: string
	startDate: string | undefined
	endDate: string | undefined
	objectives: Objective[]
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
