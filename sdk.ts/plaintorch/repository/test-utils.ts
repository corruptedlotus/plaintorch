import type { InvalidationTarget } from "./invalidation"

// Shared fakes for the repository test suite. Not a `.test.ts` file, so Vitest does not run it as a suite;
// it is imported by the suites that need it. Kept out of the package `exports`, so it never ships.

/** A plain entity-shaped payload: the `@type`/`id`/`title` shape `identify` recognises, plus extra fields. */
export function makeEntity(typeName: string, id: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
	return { "@type": typeName, id, title: id, ...extra }
}

/** Drains a few microtask turns — enough for the store's structural flush to run. */
export async function flushMicrotasks(turns = 4): Promise<void> {
	for (let index = 0; index < turns; index++) {
		await Promise.resolve()
	}
}

/** A fetcher that records how many times it was called, for in-flight-dedup and freshness assertions. */
export function countingFetcher<T>(resolve: (id: string) => T | undefined = () => undefined) {
	let calls = 0
	return {
		fetch: async (id: string): Promise<T | undefined> => {
			calls++
			return resolve(id)
		},
		get calls(): number {
			return calls
		}
	}
}

/** Records what an invalidation scheduler drives, so a test can assert entity keys and record passes. */
export function recordingInvalidationTarget() {
	const entities: string[] = []
	let recordPasses = 0
	const target: InvalidationTarget = {
		async revalidateEntityIfObserved(key) {
			entities.push(key)
		},
		async revalidateRecordsIfObserved() {
			recordPasses++
		}
	}
	return {
		target,
		entities,
		get recordPasses(): number {
			return recordPasses
		}
	}
}
