import esbuild from "esbuild"
import { cpSync, mkdirSync } from "node:fs"
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
		...shared,
		entryPoints: ["src/renderer/renderer.ts"],
		platform: "browser",
		format: "iife",
		target: "es2022",
		outfile: `${outputDirectory}/renderer.js`
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
