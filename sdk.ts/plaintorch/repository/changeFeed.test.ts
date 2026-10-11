import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { ChangeFeed, type ChangeFeedOptions, type ChangeFeedSink } from "./changeFeed"

// The feed turns a line stream of server-sent events into invalidations, and reconnects for as long as it is
// started. Both ends are fakes: a source that hands out whatever stream the test scripts, and a sink that
// records what the feed asked of it, in order.

type Open = (signal: AbortSignal) => Promise<AsyncIterable<string> | undefined>

function harness(open: Open, options?: ChangeFeedOptions) {
	const calls: string[] = []
	const sink: ChangeFeedSink = {
		invalidateAll: () => { calls.push("invalidateAll") },
		revalidateObserved: async () => { calls.push("revalidateObserved") },
		acceptAuthority: (typeName, id) => { calls.push(`accept ${typeName}:${id}`) },
		invalidate: (typeName, id) => { calls.push(`invalidate ${typeName}:${id}`) },
		remove: (typeName, id) => { calls.push(`remove ${typeName}:${id}`) }
	}
	const feed = new ChangeFeed({ open }, sink, options)
	return { feed, calls }
}

/** A stream that yields the given lines and then ends, as a dropped connection does. */
async function* finite(...lines: string[]): AsyncIterable<string> {
	for (const line of lines) {
		yield line
	}
}

/** A stream the test feeds a line at a time and ends when it chooses. */
function live() {
	const queue: (string | undefined)[] = []
	let wake: (() => void) | undefined
	const push = (line: string | undefined) => {
		queue.push(line)
		wake?.()
	}

	async function* stream(): AsyncIterable<string> {
		for (;;) {
			while (queue.length === 0) {
				await new Promise<void>((resolve) => { wake = resolve })
			}

			const line = queue.shift()
			if (line === undefined) {
				return
			}

			yield line
		}
	}

	return { stream: stream(), push, end: () => push(undefined) }
}

const event = (change: Record<string, unknown>) => [`data: ${JSON.stringify(change)}`, ""]
const settle = async () => await vi.advanceTimersByTimeAsync(0)

beforeEach(() => {
	vi.useFakeTimers()
})

afterEach(() => {
	vi.useRealTimers()
})

describe("ChangeFeed — connecting", () => {
	it("marks everything stale and revalidates what is observed as soon as a connection is established", async () => {
		const connection = live()
		const { feed, calls } = harness(async () => connection.stream)

		feed.start()
		await settle()

		expect(calls).toEqual(["invalidateAll", "revalidateObserved"])
		expect(feed.connected).toBe(true)
		feed.stop()
	})

	it("reports disconnected once the stream ends, and before one is established", async () => {
		const connection = live()
		const { feed } = harness(async () => connection.stream)
		expect(feed.connected).toBe(false)

		feed.start()
		await settle()
		connection.end()
		await settle()

		expect(feed.connected).toBe(false)
		feed.stop()
	})

	it("a source with no stream to offer is not an error and resynchronises nothing", async () => {
		const { feed, calls } = harness(async () => undefined)

		feed.start()
		await settle()

		expect(calls).toEqual([])
		expect(feed.connected).toBe(false)
		feed.stop()
	})

	it("a source that throws is treated the same as one with no stream", async () => {
		const { feed, calls } = harness(async () => { throw new Error("unreachable") })

		feed.start()
		await settle()

		expect(calls).toEqual([])
		feed.stop()
	})

	it("start is idempotent — a second start opens no second connection", async () => {
		let opened = 0
		const connection = live()
		const { feed } = harness(async () => { opened++; return connection.stream })

		feed.start()
		feed.start()
		await settle()

		expect(opened).toBe(1)
		feed.stop()
	})
})

describe("ChangeFeed — dispatch", () => {
	async function dispatched(...lines: string[]): Promise<string[]> {
		const { feed, calls } = harness(async () => finite(...lines))
		feed.start()
		await settle()
		feed.stop()
		return calls.filter((call) => call !== "invalidateAll" && call !== "revalidateObserved")
	}

	it("invalidates the entity an event names, at the blank line that ends the event", async () => {
		expect(await dispatched(...event({ type: "Objective", id: "O1", operation: "Modified" }))).toEqual(["invalidate Objective:O1"])
		// No terminating blank line: the event never completed, so nothing is dispatched.
		expect(await dispatched(`data: ${JSON.stringify({ type: "Objective", id: "O1" })}`)).toEqual([])
	})

	it("gives up the local claim before invalidating when the change is critical", async () => {
		expect(await dispatched(...event({ type: "Objective", id: "O1", operation: "Modified", critical: true }))).toEqual([
			"accept Objective:O1",
			"invalidate Objective:O1"
		])
	})

	it("removes a deleted entity at once — after queueing its invalidation, which reads its relations off the instance", async () => {
		expect(await dispatched(...event({ type: "Objective", id: "O1", operation: "Deleted", critical: true }))).toEqual([
			"accept Objective:O1",
			"invalidate Objective:O1",
			"remove Objective:O1"
		])
		expect(await dispatched(...event({ type: "Objective", id: "O1", operation: "Deleted" }))).toEqual([
			"invalidate Objective:O1",
			"remove Objective:O1"
		])
	})

	it("removes nothing for an addition, a modification, or an event that names no operation", async () => {
		expect(await dispatched(
			...event({ type: "Objective", id: "O1", operation: "Added" }),
			...event({ type: "Objective", id: "O2", operation: "Modified" }),
			...event({ type: "Objective", id: "O3" })
		)).toEqual(["invalidate Objective:O1", "invalidate Objective:O2", "invalidate Objective:O3"])
	})

	it("dispatches each event of a burst in order", async () => {
		expect(await dispatched(
			...event({ type: "Objective", id: "O1" }),
			...event({ type: "Fate", id: "F1" })
		)).toEqual(["invalidate Objective:O1", "invalidate Fate:F1"])
	})

	it("joins the data lines of one event", async () => {
		expect(await dispatched('data: {"type":"Objective",', 'data: "id":"O1"}', "")).toEqual(["invalidate Objective:O1"])
	})

	it("accepts a data line with no space after the colon", async () => {
		expect(await dispatched('data:{"type":"Objective","id":"O1"}', "")).toEqual(["invalidate Objective:O1"])
	})

	it("ignores comment lines, other fields, and a blank line with no data before it", async () => {
		expect(await dispatched(": keep-alive", "", "event: change", "id: 7", ...event({ type: "Objective", id: "O1" }))).toEqual([
			"invalidate Objective:O1"
		])
	})

	it("ignores malformed data and an event naming no type or no id, and carries on", async () => {
		expect(await dispatched(
			"data: {not json", "",
			...event({ id: "O1" }),
			...event({ type: "Objective" }),
			"data: null", "",
			...event({ type: "Fate", id: "F1" })
		)).toEqual(["invalidate Fate:F1"])
	})
})

describe("ChangeFeed — reconnecting", () => {
	const options = { initialRetryDelayMs: 100, maximumRetryDelayMs: 450 }

	it("backs off between failed attempts, doubling up to the ceiling", async () => {
		let opened = 0
		const { feed } = harness(async () => { opened++; return undefined }, options)

		feed.start()
		await settle()
		expect(opened).toBe(1)

		// Waits observed after each failure: 200, 400, then the 450 ceiling.
		await vi.advanceTimersByTimeAsync(199)
		expect(opened).toBe(1)
		await vi.advanceTimersByTimeAsync(1)
		expect(opened).toBe(2)

		await vi.advanceTimersByTimeAsync(399)
		expect(opened).toBe(2)
		await vi.advanceTimersByTimeAsync(1)
		expect(opened).toBe(3)

		await vi.advanceTimersByTimeAsync(449)
		expect(opened).toBe(3)
		await vi.advanceTimersByTimeAsync(1)
		expect(opened).toBe(4)

		await vi.advanceTimersByTimeAsync(450)
		expect(opened).toBe(5)
		feed.stop()
	})

	it("a connection that worked resets the delay, and its reconnect resynchronises again", async () => {
		let opened = 0
		const { feed, calls } = harness(async () => {
			opened++
			// Fails twice, connects and drops, then connects and drops again.
			return opened <= 2 ? undefined : finite()
		}, options)

		feed.start()
		await settle()
		await vi.advanceTimersByTimeAsync(200)
		await vi.advanceTimersByTimeAsync(400)
		expect(opened).toBe(3)
		expect(calls).toEqual(["invalidateAll", "revalidateObserved"])

		// The delay is back at its initial value rather than continuing from 400.
		await vi.advanceTimersByTimeAsync(99)
		expect(opened).toBe(3)
		await vi.advanceTimersByTimeAsync(1)
		expect(opened).toBe(4)
		expect(calls).toEqual(["invalidateAll", "revalidateObserved", "invalidateAll", "revalidateObserved"])
		feed.stop()
	})

	it("stop aborts the open connection and ends the reconnecting", async () => {
		let opened = 0
		let signal: AbortSignal | undefined
		const connection = live()
		const { feed } = harness(async (given) => { opened++; signal = given; return connection.stream })

		feed.start()
		await settle()
		feed.stop()

		expect(signal?.aborted).toBe(true)
		expect(feed.connected).toBe(false)

		connection.end()
		await vi.advanceTimersByTimeAsync(10_000)
		expect(opened).toBe(1)
	})
})
