import { defineConfig } from "vitest/config"
import { transformWithEsbuild } from "vite"

// The library itself uses no decorators, but the suites of the Lit bindings declare their test hosts with
// `@a11d/lit`'s legacy (experimental) decorators, which Vite's default transform does not apply. So the
// suites' TypeScript goes through esbuild first, with the settings a host application's bundle uses: legacy
// decorators on, and class fields without define semantics, or the decorated accessors are shadowed.
export default defineConfig({
	plugins: [
		{
			name: "local-repo-legacy-decorators",
			enforce: "pre",
			async transform(code, id) {
				const file = id.split("?")[0] ?? id
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
	test: {
		include: ["**/*.test.ts"],
		exclude: ["dist/**", "node_modules/**"],
		// The core runs without a document. The Lit suites under `lit/` ask for happy-dom themselves, with a
		// `@vitest-environment` docblock, since they mount hosts.
		environment: "node"
	}
})
