import { request as sendRequest } from "node:http"
import { NodeSocketPlaintorchCoreTransport } from "@pleiades/sdk/plaintorch/node-transport"
import type { BridgeRequest, BridgeResponse, CoreHealth } from "../shared/contracts"
import type { UserProfile } from "./profile"

const probeTimeoutMs = 1_500

/** The response headers carried across the bridge — the ones the SDK reads. */
const forwardedHeaders = ["x-note-ready"]

/**
 * HTTP over the profile's named pipe or unix socket, the same transport the Obsidian plugin uses: the renderer's SDK
 * client sends through it via the preload bridge, so the renderer never needs Node access.
 *
 * Requests and streams go through the SDK's own socket transport — bounded, drained, pooled, a timed-out read retried
 * once, a long-lived stream on a connection of its own — so a burst of refetches after the change feed reconnects
 * cannot exhaust the core's pipe instances. The health probe stays a single short, unretried request.
 */
export class CoreTransport {
	private readonly socket: NodeSocketPlaintorchCoreTransport

	public constructor(private readonly profile: UserProfile) {
		this.socket = new NodeSocketPlaintorchCoreTransport(profile.transportPath)
	}

	/** Sends one request; resolves `undefined` when the core is unreachable. */
	public async send(request: BridgeRequest): Promise<BridgeResponse | undefined> {
		const response = await this.socket.send(request)
		if (!response) {
			return undefined
		}

		const headers: Record<string, string> = {}
		for (const name of forwardedHeaders) {
			const value = response.header(name)
			if (value !== undefined) {
				headers[name] = value
			}
		}

		return { ok: response.ok, status: response.status, text: await response.text(), headers }
	}

	/** Opens a long-lived GET, yielding its body a line at a time; `undefined` when the core would not open it. */
	public stream(path: string, signal: AbortSignal): Promise<AsyncIterable<string> | undefined> {
		return this.socket.stream({ method: "GET", path }, signal)
	}

	/** Asks the core for its health; resolves `undefined` when nothing answers in time. */
	public async probeHealth(): Promise<CoreHealth | undefined> {
		const text = await this.probe("/healthz")
		if (text === undefined) {
			return undefined
		}

		try {
			return JSON.parse(text) as CoreHealth
		}
		catch {
			return undefined
		}
	}

	private probe(path: string): Promise<string | undefined> {
		return new Promise(resolve => {
			const outgoing = sendRequest({
				socketPath: this.profile.transportPath,
				path,
				method: "GET",
				headers: { Accept: "application/json" },
				timeout: probeTimeoutMs
			}, incoming => {
				const chunks: Buffer[] = []
				const ok = (incoming.statusCode ?? 0) >= 200 && (incoming.statusCode ?? 0) < 300
				incoming.on("data", chunk => chunks.push(chunk))
				incoming.on("end", () => resolve(ok ? Buffer.concat(chunks).toString("utf8") : undefined))
				incoming.on("error", () => resolve(undefined))
			})
			outgoing.on("timeout", () => outgoing.destroy())
			outgoing.on("error", () => resolve(undefined))
			outgoing.end()
		})
	}
}
