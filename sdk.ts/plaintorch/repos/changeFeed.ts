import type { PlaintorchCoreClient } from "../coreClient"
import { ChangeFeed, type ChangeFeedOptions, type ChangeFeedSink } from "@corruptedlotus/local-repo"

export interface PlaintorchChangeFeedOptions extends ChangeFeedOptions {
	/** Path of the feed endpoint. */
	path?: string
}

const defaultPath = "/api/system/changes"

/**
 * The core's change feed: its `text/event-stream` of entity changes, opened over whichever of the client's
 * transports can hold a response open.
 *
 * This is how writes made outside the frontend — a markdown edit the watcher picks up, the CLI, the scheduler —
 * reach the screen. Everything it does with the stream is {@link ChangeFeed}'s; what is declared here is only
 * where PLAINTORCH's stream comes from.
 */
export class PlaintorchChangeFeed extends ChangeFeed {
	public constructor(client: PlaintorchCoreClient, sink: ChangeFeedSink, options: PlaintorchChangeFeedOptions = {}) {
		const { path = defaultPath, ...feedOptions } = options
		super({ open: async (signal) => await client.openStream(path, signal) }, sink, feedOptions)
	}
}
