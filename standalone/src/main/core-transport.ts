import { request as sendRequest } from "node:http"
import type { BridgeRequest, BridgeResponse, CoreHealth } from "../shared/contracts"
import type { UserProfile } from "./profile"

const requestTimeoutMs = 6_000
const probeTimeoutMs = 1_500

/**
 * One-shot HTTP over the profile's named pipe or unix socket, the same transport the Node SDK and the Obsidian
 * plugin use. The renderer's SDK client goes through this via the preload bridge, so it never needs Node access.
 */
export class CoreTransport {
	public constructor(private readonly profile: UserProfile) { }

	/** Sends one request; resolves `undefined` when the core is unreachable. */
	public send(request: BridgeRequest, timeoutMs = requestTimeoutMs): Promise<BridgeResponse | undefined> {
		return new Promise(resolve => {
			const body = request.body === undefined ? undefined : JSON.stringify(request.body)
			const headers: Record<string, string> = {
				Accept: "application/json",
				...request.headers
			}
			if (body !== undefined) {
				headers["Content-Type"] = "application/json"
				headers["Content-Length"] = String(Buffer.byteLength(body))
			}

			const outgoing = sendRequest({
				socketPath: this.profile.transportPath,
				path: request.path,
				method: request.method,
				headers,
				timeout: timeoutMs
			}, incoming => {
				const chunks: Buffer[] = []
				incoming.on("data", chunk => chunks.push(chunk))
				incoming.on("end", () => resolve({
					ok: (incoming.statusCode ?? 0) >= 200 && (incoming.statusCode ?? 0) < 300,
					status: incoming.statusCode ?? 0,
					text: Buffer.concat(chunks).toString("utf8")
				}))
				incoming.on("error", () => resolve(undefined))
			})
			outgoing.on("timeout", () => outgoing.destroy())
			outgoing.on("error", () => resolve(undefined))
			if (body !== undefined) {
				outgoing.write(body)
			}

			outgoing.end()
		})
	}

	/** Asks the core for its health; resolves `undefined` when nothing answers. */
	public async probeHealth(): Promise<CoreHealth | undefined> {
		const response = await this.send({ method: "GET", path: "/healthz" }, probeTimeoutMs)
		if (!response?.ok) {
			return undefined
		}

		try {
			return JSON.parse(response.text) as CoreHealth
		}
		catch {
			return undefined
		}
	}
}
