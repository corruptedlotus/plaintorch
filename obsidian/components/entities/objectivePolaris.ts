import { Notice } from 'obsidian'
import type { Objective, PolarisCycle } from '@pleiades/sdk'
import { core } from '..'

/**
 * Whether a cycle already holds an executive for an objective. A Polaris cycle holds at most one instance of an
 * objective — the core refuses a second — so every surface offering "add to Polaris" asks this first, and every
 * surface showing membership (a checked button, a filtered picker, a refused drop) reads the same answer.
 */
export function isObjectiveInCycle(objectiveId: string, cycle: PolarisCycle | undefined | null): boolean {
	return !!cycle?.executives.some(executive => (executive.objectiveId ?? executive.objective?.id) === objectiveId)
}

/** The active Polaris cycle as the briefing record knows it — cached, and kept current by the repositories. */
export async function currentPolarisCycle(): Promise<PolarisCycle | undefined> {
	return (await core.repos.briefing.get())?.currentPolaris ?? undefined
}

/**
 * Plans an objective into the active Polaris cycle as an executive — the one add-to-polaris path, shared by the
 * banner button, the grid row action, the Polaris card's picker and its drop target.
 *
 * Refuses locally when the objective is already in the cycle (the core would refuse too, but this way the
 * reason is said rather than swallowed as a failed write). The objective and briefing ride on the mutate; only
 * the owning cycle needs a nudge afterwards. Resolves to whether the objective was added.
 */
export async function addObjectiveToPolaris(objective: Pick<Objective, 'id' | 'title'>): Promise<boolean> {
	if (isObjectiveInCycle(objective.id, await currentPolarisCycle())) {
		new Notice(`${objective.title} is already in the active Polaris cycle.`)
		return false
	}

	const added = await core.repos.objectives.mutate(objective.id, async () =>
		await core.polaris.addObjectiveToCurrent(objective.id))
	if (added) {
		await core.repos.polaris.revalidateObserved()
		new Notice(`Added ${objective.title} to the active Polaris cycle.`)
	}
	else {
		new Notice(`PLAINTORCH could not add ${objective.title} to Polaris.`)
	}

	return added
}
