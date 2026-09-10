import { describe, it, expect } from "vitest"
import { ModelValueConstructor } from "@a11d/api-dotnet"
import { EntityStore } from "./entityStore"
import { PlaintorchRepositories } from "./repositories"
import type { PlaintorchCoreClient } from "../coreClient"

// The routed entity types PlaintorchRepositories serves — the cross-repository contract the core's
// EntityTypeNameContractTests pins on its side. Adding a routed type means updating both this list and the
// registrations; that is the point of pinning it.
const routedTypes = [
	"Objective", "Fate", "Decree", "StellarDirective", "LunarDirective",
	"OnrushSprint", "ExecutiveOrder", "PolarisCycle", "LorePage", "Checkpoint"
] as const

describe("repository contract / drift guard", () => {
	it("constructs without throwing — every routed type is @model-registered (the register() guard passes)", () => {
		// Importing repositories loaded every model module for its @model side effect; construction then runs
		// register() for each routed type, which throws if one is not registered. This is what would have
		// caught both the StellarDirective drift and the five interface-only models.
		const client = { store: new EntityStore() } as unknown as PlaintorchCoreClient
		expect(() => new PlaintorchRepositories(client)).not.toThrow()
	})

	it("every routed type is registered and constructs to a real class instance, never a plain object", () => {
		const constructor = new ModelValueConstructor()
		for (const typeName of routedTypes) {
			expect(ModelValueConstructor.modelConstructorsByTypeName.has(typeName), `${typeName} is @model-registered`).toBe(true)
			const instance = constructor.construct({ "@type": typeName, id: "x", title: "y" }) as object
			// A plain object's constructor is Object; a registered model constructs to its class (directives to
			// the Directive class under both StellarDirective and LunarDirective).
			expect(instance.constructor, `${typeName} constructs to a class instance`).not.toBe(Object)
		}
	})

	it("routes each type name back to a repository through forTypeName", () => {
		const client = { store: new EntityStore() } as unknown as PlaintorchCoreClient
		const repositories = new PlaintorchRepositories(client)
		for (const typeName of routedTypes) {
			expect(repositories.forTypeName(typeName), `${typeName} routes`).toBeDefined()
		}
		expect(repositories.forTypeName("NotAThing")).toBeUndefined()
		expect(repositories.forTypeName(undefined)).toBeUndefined()
	})
})
