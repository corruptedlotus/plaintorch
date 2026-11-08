import { existsSync, readFileSync, realpathSync } from "node:fs"
import path from "node:path"

/** An import of `@corruptedlotus/local-repo` or one of its subpaths. */
export const localRepoImport = /^@corruptedlotus\/local-repo(\/.*)?$/

/**
 * An esbuild plugin that bundles `@corruptedlotus/local-repo` from its TypeScript, the entry its `source` export
 * condition names, so the sibling checkout PLAINTORCH links needs no build step.
 *
 * Only that package. A global `conditions: ["source"]` also selects the `source` entries other packages declare
 * without publishing the files behind them (3MO's `@3mo/data-grid` and `@3mo/date-time-fields` ship `dist` only), and
 * esbuild does not fall back to the next condition when the file is missing. Once the package is consumed from npm the
 * plugin can go, and its `dist` is bundled like any other dependency. SIPA's Vitest config resolves the package the
 * same way, through {@link resolveLocalRepoSource}.
 *
 * @returns {import("esbuild").Plugin}
 */
export function localRepoFromSource() {
	return {
		name: "local-repo-from-source",
		setup(build) {
			build.onResolve({ filter: localRepoImport }, (args) => {
				const resolved = resolveLocalRepoSource(args.path, args.resolveDir)
				return "error" in resolved ? { errors: [{ text: resolved.error }] } : { path: resolved.path }
			})
		}
	}
}

/**
 * The TypeScript file an import of `@corruptedlotus/local-repo` (or a subpath) names under the package's `source`
 * export condition, read from the package Node would load from `resolveDir`. It is the real path, so the package
 * reached through several links (SIPA's and the SDK's) is loaded once. When the package is missing, or exports no
 * source entry for the subpath, the answer is an error message instead.
 *
 * @param {string} specifier The import, matching {@link localRepoImport}.
 * @param {string} resolveDir The directory of the importing file.
 * @returns {{ path: string } | { error: string }}
 */
export function resolveLocalRepoSource(specifier, resolveDir) {
	const manifestPath = findManifest(resolveDir)
	if (!manifestPath) {
		return { error: `@corruptedlotus/local-repo is not installed above ${resolveDir}` }
	}

	const subpath = `.${specifier.slice("@corruptedlotus/local-repo".length)}`
	const manifest = JSON.parse(readFileSync(manifestPath, "utf8"))
	const source = manifest.exports?.[subpath]?.source
	if (typeof source !== "string") {
		return { error: `@corruptedlotus/local-repo exports no source entry for "${subpath}"` }
	}

	return { path: realpathSync(path.resolve(path.dirname(manifestPath), source)) }
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
