export interface PlaintorchCoreNoteResolution {
  vaultRelativePath: string;
  isPlaintorchEntity: boolean;
  entityKind?: string;
  entityName?: string;
  tagName?: string;
  puck?: string;
  title?: string;
}

export interface PlaintorchBriefingObjective {
  id: string;
  title: string;
  status: string;
  college: string;
  celestronValue: number;
  isEnduring: boolean;
}

export interface PlaintorchBriefingOnrushSprint {
  selectionMode: string;
  id: string;
  title: string;
  startDate?: string;
  endDate?: string;
  objectives: PlaintorchBriefingObjective[];
}

export interface PlaintorchBriefingExecutive {
  id: number;
  title?: string;
  executed: boolean;
  objectiveId?: string;
  objectiveTitle?: string;
}

export interface PlaintorchBriefingPolarisCycle {
  id: string;
  title: string;
  startTime?: string;
  endTime?: string;
  isForecast: boolean;
  executives: PlaintorchBriefingExecutive[];
}

export interface PlaintorchBriefing {
  status: string;
  timestamp: string;
  activeVaultPath: string;
  pleiadeanToday: string;
  celestronBanked: number;
  currentOnrush?: PlaintorchBriefingOnrushSprint;
  currentPolaris?: PlaintorchBriefingPolarisCycle;
}