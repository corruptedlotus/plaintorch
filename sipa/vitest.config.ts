import path from "node:path"
import { defineConfig } from "vitest/config"
import { transformWithEsbuild } from "vite"
import { localRepoImport, resolveLocalRepoSource } from "./bundling.mjs"

// The same arrangement as the SDK's: Vite's default transform does not apply the legacy decorators `@a11d/lit`
// components are written with (`@component`, `@property`), so the project's own TypeScript — and the SDK's,
// which is linked in as source — goes through esbuild first, with the settings the real bundles use.
export default defineConfig({
	plugins: [
		{
			name: "sipa-legacy-decorators",
			enforce: "pre",
			async transform(code, id) {
				const file = id.split("?")[0]
				if (!file.endsWith(".ts") || file.includes("/node_modules/")) {
					return null
				}

				const result = await transformWithEsbuild(code, id, {
					loader: "ts",
					tsconfigRaw: { compilerOptions: { experimentalDecorators: true, useDefineForClassFields: false } }
				})
				return { code: result.code, map: result.map }
			}
		},
		{
			// The repository package is linked from the sibling repository (`../../local-repo`) and resolved to its
			// TypeScript, as the bundles do (see `bundling.mjs`). Only that package: a global `source` condition would also
			// pick the entries 3MO's packages declare but do not publish, and break every suite that reaches them.
			name: "local-repo-from-source",
			enforce: "pre",
			resolveId(source, importer) {
				if (!importer || !localRepoImport.test(source)) {
					return null
				}

				const resolved = resolveLocalRepoSource(source, path.dirname(importer.split("?")[0]))
				if ("error" in resolved) {
					this.error(resolved.error)
				}

				return resolved.path
			}
		}
	],
	resolve: {
		// The repository package's Lit bindings import `@a11d/lit`, which must be the one copy SIPA itself uses: a second
		// Lit runtime would register its own base classes and directives, so the package is deduplicated onto this
		// project's copy.
		dedupe: ["@a11d/lit", "lit"]
	},
	test: {
		include: ["components/**/*.test.ts"],
		// The bindings are Lit controllers: they need a document to mount hosts into.
		environment: "happy-dom",
		server: {
			deps: {
				// `@3mo/localization` (under `@3mo/popover`, so under nearly every component) default-imports the CommonJS
				// `intl-format-cache`. Loaded by Node as is, that import is the module object rather than its function.
				// Run through Vitest instead, the import gets the interop the bundles' esbuild gives it. The whole family
				// is inlined, because a package Node loads takes everything beneath it out of Vitest's hands.
				inline: [/[\\/]@3mo[\\/]/]
			}
		}
	}
})
