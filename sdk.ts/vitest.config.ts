import { defineConfig } from "vitest/config"
import { transformWithEsbuild } from "vite"

// Vite 8's default transform is oxc, which does not apply the SDK's legacy `@model` registration decorators
// (it leaves the `@` in, and Node then fails to parse). So run the project's own TypeScript through esbuild
// first — which honours `experimentalDecorators` — and hand oxc plain JavaScript. Matches the SDK's real
// build (es2021, no define-for-fields).
export default defineConfig({
	plugins: [
		{
			name: "plaintorch-legacy-decorators",
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
	// `@corruptedlotus/local-repo` is linked from the sibling repository (`../../local-repo`); its `source` condition resolves it to
	// TypeScript, as the bundles do, so no build of the package is needed before the suites run. The suites run under Node,
	// where Vite resolves with its server-side conditions, so the condition goes there as well as on the client side.
	resolve: {
		conditions: ["source"]
	},
	ssr: {
		resolve: {
			conditions: ["source", "module", "node", "development|production"],
			externalConditions: ["source", "node"]
		}
	},
	test: {
		include: ["plaintorch/**/*.test.ts"],
		environment: "node"
	}
})
