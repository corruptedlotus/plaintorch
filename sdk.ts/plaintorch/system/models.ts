export interface SystemBrief {
	timestamp: string
	activeVaultPath: string
	activeOnrushSprintId: string | undefined
	activePolarisCycleId: string | undefined
	celestronBanked: number
}

export interface VaultNoteAuthorityResolution {
	vaultRelativePath: string
	isPlaintorchEntity: boolean
	entityKind: string | undefined
	entityName: string | undefined
	tagName: string | undefined
	puck: string | undefined
	title: string | undefined
}

export interface SystemBriefingObjective {
	id: string
	title: string
	status: string
	college: string
	celestronValue: number
	isEnduring: boolean
}

export interface SystemBriefingOnrushSprint {
	selectionMode: string
	id: string
	title: string
	startDate: string | undefined
	endDate: string | undefined
	objectives: SystemBriefingObjective[]
}

export interface SystemBriefingExecutive {
	id: number
	title: string | undefined
	executed: boolean
	objectiveId: string | undefined
	objectiveTitle: string | undefined
}

export interface SystemBriefingPolarisCycle {
	id: string
	title: string
	startTime: string | undefined
	endTime: string | undefined
	isForecast: boolean
	executives: SystemBriefingExecutive[]
}

export interface SystemBriefing {
	status: string
	timestamp: string
	activeVaultPath: string
	pleiadeanToday: string
	celestronBanked: number
	currentOnrush: SystemBriefingOnrushSprint | undefined
	currentPolaris: SystemBriefingPolarisCycle | undefined
}

export interface HealthStatus {
	status: string
}
