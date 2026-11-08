import { describe, it, expect, vi, afterEach } from "vitest"
import { PlaintorchCoreClient } from "../coreClient"
import { FetchPlaintorchCoreTransport } from "../internal/transport"
import { Objective } from "../objectives/models"
import { objective, scriptedClient } from "./test-utils"

// What PLAINTORCH's repositories do with each thing the core can say: an answer, "there is nothing here", and
// no answer at all. Before this, the last two were the same emptiness — so an outage blanked what was on screen,
// and a deleted entity went on being shown.

afterEach(() => {
	vi.useRealTimers()
	vi.restoreAllMocks()
	vi.unstubAllGlobals()
})

describe("getJsonAnswer — whether the core answered", () => {
	it("a success is an answer, an empty one included", async () => {
		const { client } = scriptedClient({ "/a": { body: { value: 1 } }, "/empty": { body: undefined }, "/list": { body: [] } })

		expect(await client.getJsonAnswer("/a")).toEqual({ answered: true, value: { value: 1 } })
		expect(await client.getJsonAnswer("/empty")).toEqual({ answered: true, value: undefined })
		expect(await client.getJsonAnswer("/list")).toEqual({ answered: true, value: [] })
	})

	it("a not-found is an answer that carries nothing", async () => {
		const { client } = scriptedClient({ "/gone": { status: 410 } })

		expect(await client.getJsonAnswer("/missing")).toEqual({ answered: true, value: undefined })
		expect(await client.getJsonAnswer("/gone")).toEqual({ answered: true, value: undefined })
	})

	it("silence, a core failure and any other refusal are not an answer", async () => {
		const { client } = scriptedClient({ "/silent": "silence", "/failed": { status: 500 }, "/idle": { status: 503 }, "/refused": { status: 409 } })

		for (const path of ["/silent", "/failed", "/idle", "/refused"]) {
			expect(await client.getJsonAnswer(path), path).toEqual({ answered: false, value: undefined })
		}
	})

	it("getJson keeps its contract: nothing either way", async () => {
		const { client } = scriptedClient({ "/silent": "silence", "/failed": { status: 500 } })

		expect(await client.getJson("/silent")).toBeUndefined()
		expect(await client.getJson("/failed")).toBeUndefined()
		expect(await client.getJson("/missing")).toBeUndefined()
	})
})

describe("PlaintorchRepositories — a listing", () => {
	it("keeps what it holds when the core does not answer, and asks again next time", async () => {
		const { client, routes, paths } = scriptedClient({ "/api/objectives": { body: [objective("O1"), objective("O2")] } })
		let notified = 0
		client.repos.objectiveList.subscribe("", () => notified++)
		const listed = await client.repos.objectiveList.get()

		for (const outage of ["silence", { status: 500 }] as const) {
			routes["/api/objectives"] = outage
			expect(await client.repos.objectiveList.refresh()).toBe(listed)
		}

		expect(client.repos.objectiveList.peek("")).toHaveLength(2)
		expect(notified).toBe(1)

		routes["/api/objectives"] = { body: [objective("O1")] }
		expect(await client.repos.objectiveList.get()).toHaveLength(1)
		expect(paths().filter((path) => path === "/api/objectives")).toHaveLength(4)
	})

	it("empties when the core answers with an empty list", async () => {
		const { client, routes } = scriptedClient({ "/api/objectives": { body: [objective("O1")] } })
		await client.repos.objectiveList.get()

		routes["/api/objectives"] = { body: [] }
		expect(await client.repos.objectiveList.refresh()).toEqual([])
	})

	it("is not loaded, rather than empty, when the core never answered", async () => {
		const { client } = scriptedClient({ "/api/objectives": "silence" })

		expect(await client.repos.objectiveList.get()).toBeUndefined()
		expect(client.repos.objectiveList.isResolved("")).toBe(false)
	})

	it("a record with no sprint to name resolves to nothing rather than staying unloaded", async () => {
		const { client } = scriptedClient()

		expect(await client.repos.onrushCurrent.get()).toBeUndefined()
		expect(client.repos.onrushCurrent.isResolved("")).toBe(true)
	})

	it("a note resolution keeps its last answer through an outage", async () => {
		const { client, routes, paths } = scriptedClient()
		// The first read finds nothing, which is an answer; it also tells the test which path a resolution asks.
		expect(await client.repos.noteResolution.get("Notes/A.md")).toBeUndefined()
		expect(client.repos.noteResolution.isResolved("Notes/A.md")).toBe(true)
		const path = paths()[0]!

		routes[path] = { body: { exists: true, entityKind: "objective" } }
		const resolved = await client.repos.noteResolution.refresh("Notes/A.md")
		expect(resolved).toEqual({ exists: true, entityKind: "objective" })

		routes[path] = "silence"
		expect(await client.repos.noteResolution.refresh("Notes/A.md")).toBe(resolved)
	})
})

describe("PlaintorchRepositories — an entity", () => {
	it("is kept when the core does not answer, and is not counted as freshly resolved", async () => {
		vi.useFakeTimers()
		const { client, routes, paths } = scriptedClient({ "/api/objectives/O1": { body: objective("O1") } })
		const held = await client.repos.objectives.get("O1")
		vi.advanceTimersByTime(60_000)

		routes["/api/objectives/O1"] = "silence"
		expect(await client.repos.objectives.get("O1")).toBe(held)
		expect(client.repos.objectives.stateOf("O1")).toBe("loaded")

		await client.repos.objectives.get("O1")
		expect(paths()).toHaveLength(3)
	})

	it("is removed when the core answers that it does not exist", async () => {
		const { client, routes } = scriptedClient({ "/api/objectives/O1": { body: objective("O1") } })
		await client.repos.objectives.get("O1")
		let notified = 0
		client.repos.objectives.subscribe("O1", () => notified++)

		delete routes["/api/objectives/O1"]
		expect(await client.repos.objectives.refresh("O1")).toBeUndefined()

		expect(client.repos.objectives.stateOf("O1")).toBe("removed")
		expect(notified).toBe(1)
	})

	it("is not removed by a core that is failing rather than answering", async () => {
		const { client, routes } = scriptedClient({ "/api/objectives/O1": { body: objective("O1") } })
		const held = await client.repos.objectives.get("O1")

		for (const failure of [{ status: 500 }, { status: 503 }, { status: 409 }]) {
			routes["/api/objectives/O1"] = failure
			expect(await client.repos.objectives.refresh("O1")).toBe(held)
		}
	})

	it("an observed PUCK the core has never had reads as removed, not as forever loading", async () => {
		const { client } = scriptedClient()
		client.repos.objectives.subscribe("NOPE", () => { })

		await client.repos.objectives.get("NOPE")

		expect(client.repos.objectives.stateOf("NOPE")).toBe("removed")
	})
})

describe("PlaintorchRepositories — a deletion announced on the change feed", () => {
	it("removes the entity at once and refreshes the listing that held it", async () => {
		vi.useFakeTimers()
		const { client, routes } = scriptedClient({
			"/api/objectives": { body: [objective("O1"), objective("O2")] },
			"/api/objectives/O1": { body: objective("O1") }
		})
		client.repos.objectiveList.subscribe("", () => { })
		await client.repos.objectiveList.get()
		await client.repos.objectives.get("O1")
		const states: string[] = []
		client.repos.objectives.subscribe("O1", () => states.push(client.repos.objectives.stateOf("O1")))

		// The core deletes O1, then says so.
		delete routes["/api/objectives/O1"]
		routes["/api/objectives"] = { body: [objective("O2")] }
		async function* events(): AsyncIterable<string> {
			yield `data: ${JSON.stringify({ type: "Objective", id: "O1", operation: "Deleted", critical: true })}`
			yield ""
			await new Promise(() => { })
		}
		client.openStream = async () => events()

		client.repos.changeFeed.start()
		await vi.advanceTimersByTimeAsync(0)
		await client.repos.invalidation.settled()
		client.repos.changeFeed.stop()

		expect(states[0]).toBe("removed")
		expect(client.repos.objectives.stateOf("O1")).toBe("removed")
		expect(client.repos.objectiveList.peek("")?.map((member) => member.id)).toEqual(["O2"])
	})
})

describe("PlaintorchRepositories — what held a removed entity", () => {
	it("a listing loses the row the moment the entity is removed, without being refetched", async () => {
		const { client, routes, paths } = scriptedClient({
			"/api/objectives": { body: [objective("O1"), objective("O2")] },
			"/api/objectives/O1": { body: objective("O1") }
		})
		let notified = 0
		client.repos.objectiveList.subscribe("", () => notified++)
		await client.repos.objectiveList.get()
		await client.repos.objectives.get("O1")

		delete routes["/api/objectives/O1"]
		await client.repos.objectives.refresh("O1")

		expect(client.repos.objectiveList.peek("")?.map((member) => member.id)).toEqual(["O2"])
		expect(notified).toBe(2)
		expect(paths().filter((path) => path === "/api/objectives")).toHaveLength(1)
	})

	it("a sprint loses the objective from its own collection, and its observers are told", async () => {
		const { client, routes } = scriptedClient({
			"/api/onrush/N1": { body: { "@type": "OnrushSprint", id: "N1", title: "sprint", objectives: [objective("O1"), objective("O2")] } },
			"/api/objectives/O1": { body: objective("O1") }
		})
		const sprint = (await client.repos.onrush.get("N1"))! as unknown as { objectives: { id: string }[] }
		await client.repos.objectives.get("O1")
		let notified = 0
		client.repos.onrush.subscribe("N1", () => notified++)

		delete routes["/api/objectives/O1"]
		await client.repos.objectives.refresh("O1")

		expect(sprint.objectives.map((member) => member.id)).toEqual(["O2"])
		expect(notified).toBe(1)
	})

	it("the record naming the current sprint resolves to nothing when that sprint is removed", async () => {
		const { client, routes } = scriptedClient({
			"/api/onrush/current": { body: { "@type": "OnrushSprint", id: "N1", title: "sprint" } },
			"/api/onrush/N1": { body: { "@type": "OnrushSprint", id: "N1", title: "sprint" } }
		})
		await client.repos.onrushCurrent.get()
		await client.repos.onrush.get("N1")

		delete routes["/api/onrush/N1"]
		await client.repos.onrush.refresh("N1")

		expect(client.repos.onrushCurrent.peek("")).toBeUndefined()
	})
})

describe("PlaintorchRepositories — a collection the core truncated", () => {
	it("an entity first seen as a back-reference is adopted without the hole", async () => {
		const { client } = scriptedClient({
			"/api/objectives/O1": {
				body: objective("O1", { onrushSprintId: "N1", onrushSprint: { "@type": "OnrushSprint", id: "N1", title: "sprint", objectives: [null] } })
			}
		})

		await client.repos.objectives.get("O1")

		const sprint = client.store.peek<{ objectives: unknown[] }>("OnrushSprint:N1")!
		expect(sprint.objectives).toEqual([])
	})

	it("a partly truncated collection keeps the members that did arrive", async () => {
		const { client } = scriptedClient({
			"/api/objectives/O1": {
				body: objective("O1", {
					onrushSprint: { "@type": "OnrushSprint", id: "N1", title: "sprint", objectives: [objective("O2"), null] }
				})
			}
		})

		await client.repos.objectives.get("O1")

		const sprint = client.store.peek<{ objectives: Objective[] }>("OnrushSprint:N1")!
		expect(sprint.objectives.map((member) => member.id)).toEqual(["O2"])
		expect(sprint.objectives[0]).toBe(client.store.peek("Objective:O2"))
	})

	it("a truncated collection still never replaces one a fuller response loaded", async () => {
		const { client } = scriptedClient({
			"/api/onrush/N1": { body: { "@type": "OnrushSprint", id: "N1", title: "sprint", objectives: [objective("O1"), objective("O2")] } },
			"/api/objectives/O1": {
				body: objective("O1", { onrushSprint: { "@type": "OnrushSprint", id: "N1", title: "sprint", objectives: [null] } })
			}
		})
		await client.repos.onrush.get("N1")

		await client.repos.objectives.refresh("O1")

		expect(client.store.peek<{ objectives: unknown[] }>("OnrushSprint:N1")!.objectives).toHaveLength(2)
	})
})

describe("PlaintorchCoreClient over the fetch transport", () => {
	it("reads a response body exactly once — a GET resolves instead of failing on a second read", async () => {
		vi.spyOn(console, "debug").mockImplementation(() => { })
		vi.stubGlobal("fetch", async () => new Response(JSON.stringify(objective("O1")), { status: 200 }))
		const client = new PlaintorchCoreClient({ transports: [new FetchPlaintorchCoreTransport({ baseUrl: "http://core.test" })] })

		const read = await client.getJson<Objective>("/api/objectives/O1")

		expect(read).toBeInstanceOf(Objective)
		expect(read).toBe(client.store.peek("Objective:O1"))
	})

	it("drains a refusal and a boolean write without failing", async () => {
		vi.spyOn(console, "debug").mockImplementation(() => { })
		let status = 404
		vi.stubGlobal("fetch", async () => new Response("{}", { status }))
		const client = new PlaintorchCoreClient({ transports: [new FetchPlaintorchCoreTransport({ baseUrl: "http://core.test" })] })

		expect(await client.getJson("/api/objectives/NOPE")).toBeUndefined()
		status = 200
		expect(await client.delete("/api/objectives/O1")).toBe(true)
	})
})
