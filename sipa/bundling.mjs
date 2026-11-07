import { existsSync, readFileSync, realpathSync } from "node:fs"
import path from "node:path"

const localRepoImport = /^@corruptedlotus\/local-repo(\/.*)?$/

/**
 * An esbuild plugin that bundles `@corruptedlotus/local-repo` from its TypeScript, the entry its `source` export
 * condition names, so the sibling checkout PLAINTORCH links needs no build step.
 *
 * Only that package. A global `conditions: ["source"]` also selects the `source` entries other packages declare
 * without publishing the files behind them (3MO's `@3mo/data-grid` and `@3mo/date-time-fields` ship `dist` only), and
 * esbuild does not fall back to the next condition when the file is missing. Once the package is consumed from npm the
 * plugin can go, and its `dist` is bundled like any other dependency.
 *
 * @returns {import("esbuild").Plugin}
 */
export function localRepoFromSource() {
	return {
		name: "local-repo-from-source",
		setup(build) {
			build.onResolve({ filter: localRepoImport }, (args) => {
				const manifestPath = findManifest(args.resolveDir)
				if (!manifestPath) {
					return { errors: [{ text: `@corruptedlotus/local-repo is not installed above ${args.resolveDir}` }] }
				}

				const subpath = `.${args.path.slice("@corruptedlotus/local-repo".length)}`
				const manifest = JSON.parse(readFileSync(manifestPath, "utf8"))
				const source = manifest.exports?.[subpath]?.source
				if (typeof source !== "string") {
					return { errors: [{ text: `@corruptedlotus/local-repo exports no source entry for "${subpath}"` }] }
				}

				// The real path, so the package reached through several links (SIPA's and the SDK's) is bundled once.
				return { path: realpathSync(path.resolve(path.dirname(manifestPath), source)) }
			})
		}
	}
}

/** The package's manifest in the nearest `node_modules` above `directory`, as Node would find the package itself. */
function findManifest(directory) {
	for (let current = directory; ; current = path.dirname(current)) {
		const candidate = path.join(current, "node_modules", "@corruptedlotus", "local-repo", "package.json")
		if (existsSync(candidate)) {
			return candidate
		}

		if (path.dirname(current) === current) {
			return undefined
		}
	}
}
