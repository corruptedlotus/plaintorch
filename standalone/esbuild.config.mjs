import esbuild from "esbuild"
import { cpSync, mkdirSync, rmSync } from "node:fs"
import path from "node:path"

// Usage: node esbuild.config.mjs --flavor standalone|client [--production]
// Development builds watch, target the dev sub-profile, and bake the flavour in exactly like the Obsidian plugin bakes
// its profile flag: as a compile-time literal, so a shipped bundle cannot be talked into another flavour at runtime.
const args = process.argv.slice(2)
const production = args.includes("--production")
const flavorIndex = args.indexOf("--flavor")
const flavor = flavorIndex >= 0 ? args[flavorIndex + 1] : "standalone"
if (flavor !== "standalone" && flavor !== "client") {
	console.error(`Unknown flavor '${flavor}'. Use --flavor standalone or --flavor client.`)
	process.exit(1)
}

const outputDirectory = "dist"
// A production build starts from an empty output: the installer packs dist/ whole, and a file an earlier build left
// there (an old icon, a bundle since renamed) would otherwise ship with it.
if (production) {
	rmSync(outputDirectory, { recursive: true, force: true })
}
console.log("---- FOR THE GLORY OF THE TRILUNE ----")
console.log(`PLAINTORCH desktop shell: ${flavor} flavour, ${production ? "production" : "development"} build`)
console.log("--------------------------------------")

const define = {
	__PLAINTORCH_FLAVOR__: JSON.stringify(flavor),
	__PLAINTORCH_DEV_PROFILE__: production ? "false" : "true"
}

const copyStaticPlugin = {
	name: "copy-static",
	setup(build) {
		build.onEnd(() => {
			mkdirSync(outputDirectory, { recursive: true })
			cpSync("src/renderer/index.html", path.join(outputDirectory, "index.html"))
			cpSync("src/renderer/briefing.html", path.join(outputDirectory, "briefing.html"))
			cpSync("assets", path.join(outputDirectory, "assets"), { recursive: true })
		})
	}
}

const shared = {
	bundle: true,
	sourcemap: production ? false : "inline",
	minify: production,
	logLevel: "info",
	define,
	// @a11d/lit installs its reactive accessors through legacy (experimental) decorators; class fields must NOT use
	// define semantics or they shadow those accessors and reactivity silently dies (the view renders once, then never
	// updates). Set here too so a build never depends on tsconfig auto-discovery.
	tsconfigRaw: { compilerOptions: { experimentalDecorators: true, useDefineForClassFields: false } },
	loader: { ".png": "dataurl", ".svg": "dataurl" }
}

// The renderers bundle the SIPA UI from source. Its lit runtime lives in the package's own node_modules, and the
// renderers' own lit imports must resolve to that same copy — two lit runtimes would each register their own elements.
const renderer = {
	...shared,
	platform: "browser",
	format: "iife",
	target: "es2022",
	nodePaths: [path.resolve("../sipa/node_modules")]
}

const contexts = await Promise.all([
	esbuild.context({
		...shared,
		entryPoints: ["src/main/main.ts"],
		platform: "node",
		format: "cjs",
		target: "node22",
		outfile: `${outputDirectory}/main.cjs`,
		// Electron itself, and electron-updater which reads its own package files at runtime, stay external.
		external: ["electron", "electron-updater"],
		plugins: [copyStaticPlugin]
	}),
	esbuild.context({
		...shared,
		entryPoints: ["src/preload/preload.ts"],
		platform: "node",
		format: "cjs",
		target: "node22",
		outfile: `${outputDirectory}/preload.cjs`,
		external: ["electron"]
	}),
	esbuild.context({
		...renderer,
		entryPoints: ["src/renderer/renderer.ts"],
		outfile: `${outputDirectory}/renderer.js`
	}),
	esbuild.context({
		...renderer,
		entryPoints: ["src/renderer/briefing.ts"],
		outfile: `${outputDirectory}/briefing.js`
	})
])

if (production) {
	await Promise.all(contexts.map(context => context.rebuild()))
	await Promise.all(contexts.map(context => context.dispose()))
}
else {
	console.warn("Initiating watcher...")
	await Promise.all(contexts.map(context => context.watch()))
}
