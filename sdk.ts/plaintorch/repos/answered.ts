import type { PlaintorchCoreClient } from "../coreClient"
import { unresolved, type Unresolved } from "@corruptedlotus/local-repo"
import { PlaintorchObjectivesSdk } from "../objectives/objectivesSdk"
import { PlaintorchDeclarativesSdk } from "../declaratives/declarativesSdk"
import { PlaintorchDirectivesSdk } from "../directives/directivesSdk"
import { PlaintorchOnrushSdk } from "../onrush/onrushSdk"
import { PlaintorchPolarisSdk } from "../polaris/polarisSdk"
import { PlaintorchLoreSdk } from "../lore/loreSdk"
import { PlaintorchDependenciesSdk } from "../dependencies/dependenciesSdk"
import { PlaintorchPreferencesSdk } from "../preferences/preferencesSdk"
import { PlaintorchSystemSdk } from "../system/systemSdk"

// INTERIM. The repositories need to tell "the core did not answer" from "the core says there is nothing", and
// the client's reads do not: a failed request and an empty one both come back as nothing, and the listing SDKs
// then turn nothing into an empty list. Until the SDK has an error model, this is the smallest thing that lets
// the repositories' fetchers see the difference without duplicating a single endpoint path. It is meant to be
// deleted, not built on.

/** Thrown out of a read, on its way to {@link answered}, when the core did not answer it. Never escapes this file. */
class Unanswered extends Error {
	public constructor(path: string) {
		super(`PLAINTORCH core did not answer ${path}`)
	}
}

/**
 * The domain SDKs, reading through a view of the client whose reads fail loudly when the core does not answer.
 *
 * The view is the client itself with one method replaced, so everything else — the store, the writes, the
 * transports — is the client's own. Only reads made through *these* SDK instances are affected; the ones on the
 * client keep their return-nothing contract.
 */
export function answeringSdks(client: PlaintorchCoreClient) {
	const view = Object.create(client) as PlaintorchCoreClient
	view.getJson = async function <T>(...[path, context]: Parameters<PlaintorchCoreClient["getJson"]>): Promise<T | undefined> {
		const answer = await client.getJsonAnswer<T>(path, context)
		if (!answer.answered) {
			throw new Unanswered(path)
		}

		return answer.value
	}

	return {
		objectives: new PlaintorchObjectivesSdk(view),
		declaratives: new PlaintorchDeclarativesSdk(view),
		directives: new PlaintorchDirectivesSdk(view),
		onrush: new PlaintorchOnrushSdk(view),
		polaris: new PlaintorchPolarisSdk(view),
		lore: new PlaintorchLoreSdk(view),
		dependencies: new PlaintorchDependenciesSdk(view),
		preferences: new PlaintorchPreferencesSdk(view),
		system: new PlaintorchSystemSdk(view)
	}
}

/**
 * Runs a read made through {@link answeringSdks}, reporting {@link unresolved} when the core did not answer it.
 * Whatever else the read throws is its own failure and is left to propagate.
 */
export async function answered<T>(read: () => Promise<T>): Promise<T | Unresolved> {
	try {
		return await read()
	}
	catch (error) {
		if (error instanceof Unanswered) {
			return unresolved
		}

		throw error
	}
}
