import { defineConfig } from "vitest/config"
import { transformWithEsbuild } from "vite"

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
		}
	],
	resolve: {
		// The repository package is linked from `/local-repo` and resolved to its TypeScript, as the bundles do. Its Lit
		// bindings import `@a11d/lit`, which must be the one copy SIPA itself uses: a second Lit runtime would register its
		// own base classes and directives, so the package is deduplicated onto this project's copy.
		conditions: ["source"],
		dedupe: ["@a11d/lit", "lit"]
	},
	test: {
		include: ["components/**/*.test.ts"],
		// The bindings are Lit controllers: they need a document to mount hosts into.
		environment: "happy-dom"
	}
})
