import { describe, it, expect } from "vitest"
import { readdirSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { EntityStore } from "./entityStore"
import { EntityRepository } from "./entityRepository"
import { createAbsorbingReviver } from "./absorption"
import { collectEntityKeys, identifyByShape } from "./identity"
import { everyKey, plainModels, resolvePolicy, type EntityPolicy, type ModelConstructor } from "./policy"

// The store is told about its domain rather than knowing one. These suites run it over a domain that shares
// nothing with PLAINTORCH's — a different type field, no title, its own model classes and its own idea of
// which fields a response speaks for — to prove each of those is the policy's to decide.

function parse<T>(store: EntityStore, value: unknown): T {
	return JSON.parse(JSON.stringify(value), createAbsorbingReviver(store)) as T
}

describe("identifyByShape", () => {
	const identify = identifyByShape({ typeKey: "kind" })

	it("recognises a type name and a string identifier under the configured fields", () => {
		expect(identify({ kind: "User", id: "u1", name: "Ada" })).toBe("User:u1")
		expect(identifyByShape({ typeKey: "kind", idKey: "uuid" })({ kind: "User", uuid: "u1" })).toBe("User:u1")
	})

	it("rejects a missing or empty type name, a missing, empty or non-string identifier, and non-objects", () => {
		expect(identify({ id: "u1" })).toBeUndefined()
		expect(identify({ kind: "", id: "u1" })).toBeUndefined()
		expect(identify({ kind: 3, id: "u1" })).toBeUndefined()
		expect(identify({ kind: "User" })).toBeUndefined()
		expect(identify({ kind: "User", id: "" })).toBeUndefined()
		expect(identify({ kind: "User", id: 7 })).toBeUndefined()
		expect(identify(null)).toBeUndefined()
		expect(identify(undefined)).toBeUndefined()
		expect(identify("User:u1")).toBeUndefined()
	})

	it("applies a further requirement when one is given", () => {
		const named = identifyByShape({ typeKey: "kind", requires: (value) => typeof value.name === "string" })
		expect(named({ kind: "User", id: "u1", name: "Ada" })).toBe("User:u1")
		expect(named({ kind: "User", id: "u1" })).toBeUndefined()
	})
})

describe("resolvePolicy", () => {
	it("fills in plain models and every-key merging, keeping what was given", () => {
		const identify = identifyByShape({ typeKey: "kind" })
		const resolved = resolvePolicy({ identify })
		expect(resolved.identify).toBe(identify)
		expect(resolved.models).toBe(plainModels)
		expect(resolved.mergeableKeys).toBe(everyKey)

		const models: ModelConstructor = { ...plainModels }
		expect(resolvePolicy({ identify, models }).models).toBe(models)
	})

	it("the plain models construct nothing and route every type", () => {
		const value = { kind: "User", id: "u1" }
		expect(plainModels.shallConstruct(value)).toBe(false)
		expect(plainModels.construct(value)).toBe(value)
		expect(plainModels.isRegistered("Anything")).toBe(true)
	})
})

describe("EntityStore — identity is the policy's", () => {
	const policy: EntityPolicy = { identify: identifyByShape({ typeKey: "kind" }) }

	it("tracks an entity of a foreign shape, and gives two responses one instance", () => {
		const store = new EntityStore(policy)
		const first = parse<object>(store, { kind: "User", id: "u1", name: "Ada" })
		const second = parse<Record<string, unknown>>(store, { kind: "User", id: "u1", name: "Ada Lovelace" })

		expect(second).toBe(first)
		expect(second.name).toBe("Ada Lovelace")
		expect(store.peek("User:u1")).toBe(first)
		expect(store.recordCount).toBe(1)
	})

	it("does not track what the policy does not recognise, whatever another policy would say", () => {
		const store = new EntityStore(policy)
		const value = { "@type": "Objective", id: "O1", title: "an entity elsewhere" }
		expect(store.absorb(value)).toBe(value)
		expect(parse(store, value)).not.toBe(parse(store, value))
		expect(store.recordCount).toBe(0)
	})

	it("exposes the identifier as a plain function, and compares and walks with it", () => {
		const store = new EntityStore(policy)
		const { identify } = store
		expect(identify({ kind: "User", id: "u1" })).toBe("User:u1")

		// The same identity is equivalent whatever its fields; a different one is not.
		expect(store.isEquivalent({ kind: "User", id: "u1", name: "a" }, { kind: "User", id: "u1", name: "b" })).toBe(true)
		expect(store.isEquivalent({ kind: "User", id: "u1" }, { kind: "User", id: "u2" })).toBe(false)

		const into = new Set<string>()
		collectEntityKeys({ members: [{ kind: "User", id: "u1" }, { kind: "Team", id: "t1" }] }, into, identify)
		expect([...into].sort()).toEqual(["Team:t1", "User:u1"])
	})

	it("sweeps by the policy's identity — an entity nested in an observed one is retained", () => {
		const store = new EntityStore(policy)
		parse(store, { kind: "Team", id: "t1", lead: { kind: "User", id: "u1" } })
		parse(store, { kind: "User", id: "u2" })
		store.subscribe("Team:t1", () => { })

		expect(store.sweep()).toBe(1)
		expect(store.has("User:u1")).toBe(true)
		expect(store.has("User:u2")).toBe(false)
	})
})

describe("EntityStore — construction is the policy's", () => {
	class User {
		public kind = "User"
		public id = ""
		public name = ""
		public get initial(): string {
			return this.name.charAt(0)
		}
	}

	const classes = new Map<string, new () => object>([["User", User]])
	const models: ModelConstructor = {
		shallConstruct: (value) => !!value && typeof value === "object" && "kind" in value,
		construct: (value) => {
			const Model = classes.get((value as { kind: string }).kind)
			return Model ? Object.assign(new Model(), value) : value
		},
		isRegistered: (typeName) => classes.has(typeName)
	}
	const policy: EntityPolicy = { identify: identifyByShape({ typeKey: "kind" }), models }

	it("absorbs a payload as an instance of the class the policy constructs", () => {
		const store = new EntityStore(policy)
		const user = parse<User>(store, { kind: "User", id: "u1", name: "Ada" })
		expect(user).toBeInstanceOf(User)
		expect(user.initial).toBe("A")
	})

	it("a draft of a constructed type is an isolated instance of the same class", async () => {
		const store = new EntityStore(policy)
		const repository = new EntityRepository<User>(store, "User", async () => undefined)
		const user = parse<User>(store, { kind: "User", id: "u1", name: "Ada" })

		const draft = repository.fork("u1")!
		draft.value.name = "Grace"
		expect(draft.value).toBeInstanceOf(User)
		expect(draft.value).not.toBe(user)
		expect(draft.value.initial).toBe("G")
		expect(user.name).toBe("Ada")
	})

	it("a draft of a type with no model class is still an isolated copy, prototype kept", () => {
		const store = new EntityStore({ identify: policy.identify })
		const repository = new EntityRepository<User>(store, "User", async () => undefined)
		const user = store.absorb(Object.assign(new User(), { id: "u1", name: "Ada" }))

		const draft = repository.fork("u1")!
		draft.value.name = "Grace"
		expect(draft.value).not.toBe(user)
		expect(draft.value).toBeInstanceOf(User)
		expect(user.name).toBe("Ada")
	})
})

describe("EntityStore — which fields merge is the policy's", () => {
	const identify = identifyByShape({ typeKey: "kind" })

	it("by default trusts every field a payload carries, an empty nested collection included", () => {
		const store = new EntityStore({ identify })
		parse(store, { kind: "Team", id: "t1", members: [{ kind: "User", id: "u1" }] })
		parse(store, { kind: "User", id: "u1", team: { kind: "Team", id: "t1", members: [] } })

		expect(store.peek<{ members: unknown[] }>("Team:t1")?.members).toEqual([])
	})

	it("withholds what the policy says a payload does not speak for, and is told which value is the root", () => {
		const seen: { id: unknown, isRoot: boolean }[] = []
		const store = new EntityStore({
			identify,
			mergeableKeys: (payload, isRoot) => {
				seen.push({ id: payload.id, isRoot })
				return Object.keys(payload).filter((field) => isRoot || field !== "members")
			}
		})
		parse(store, { kind: "Team", id: "t1", members: [{ kind: "User", id: "u1" }] })
		seen.length = 0
		parse(store, { kind: "User", id: "u1", team: { kind: "Team", id: "t1", members: [] } })

		expect(store.peek<{ members: unknown[] }>("Team:t1")?.members).toHaveLength(1)
		expect(seen).toEqual([{ id: "t1", isRoot: false }, { id: "u1", isRoot: true }])
	})
})

describe("the generic repository files", () => {
	it("import nothing from outside this directory — the domain reaches them through policy and interfaces", () => {
		const directory = dirname(fileURLToPath(import.meta.url))
		// A sibling file, or what a suite runs on. Anything else — a parent directory, a package — is a
		// dependency the library must not have.
		const allowed = (specifier: string) =>
			(specifier.startsWith("./") && !specifier.includes("..")) || specifier === "vitest" || specifier.startsWith("node:")

		const offenders: string[] = []
		for (const file of readdirSync(directory).filter((name) => name.endsWith(".ts"))) {
			const source = readFileSync(join(directory, file), "utf8")
			// A side-effect import, the closing line of a multi-line one, or a single-line import / re-export.
			for (const match of source.matchAll(/^(?:\s*import\s+|\s*\}\s*from\s+|\s*(?:import|export)\s[^"'\n]*\sfrom\s+)["']([^"'\n]+)["']/gm)) {
				const specifier = match[1]!
				if (!allowed(specifier) || (!file.endsWith(".test.ts") && !specifier.startsWith("./"))) {
					offenders.push(`${file} → ${specifier}`)
				}
			}
		}

		expect(offenders).toEqual([])
	})
})
