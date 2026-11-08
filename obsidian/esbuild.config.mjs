import esbuild from "esbuild";
import { copyFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { localRepoFromSource } from "../sipa/bundling.mjs";

const production = process.argv.includes("production");
const watch = !production;
const outputDirectory = !production ? "_dist" : "../bin/obsidian";

console.log("---- FOR THE GLORY OF THE TRILUNE ----")
console.log("Pleiades Affairs Intelligence & Tristate Orchestrator (PLAINTORCH) Obsidian Plugin")
console.log("--------------------------------------")
console.log(`Building for ${production ? "production" : "development"}...`)

const copyManifestPlugin = {
  name: "copy-manifest",
  setup(build) {
    build.onEnd(async () => {
      await mkdir(outputDirectory, { recursive: true });
      await copyFile("manifest.json", path.join(outputDirectory, "manifest.json"));
      await copyFile("styles.css", path.join(outputDirectory, "styles.css"));
      // await copyFile(path.join(outputDirectory, "main.js"), "main.js");
    });
  }
};

const context = await esbuild.context({
  entryPoints: ["src/main.ts"],
  bundle: true,
  loader: {
	".svg": "dataurl",
	".png": "dataurl",
	".webp": "dataurl",
  },
  format: "cjs",
  target: "es2020",
  // SIPA is bundled from its TypeScript, and so is the repository package (`@corruptedlotus/local-repo`), through the
  // plugin that selects only that package's `source` entries (see sipa/bundling.mjs). The package's Lit bindings import
  // `@a11d/lit`; that must be the one copy SIPA itself uses — a second Lit runtime would register its own element bases
  // and directives — so it is aliased here.
  alias: { "@a11d/lit": path.resolve("../sipa/node_modules/@a11d/lit") },
  logLevel: "info",
  define: {
    // Bake the dev-profile flag directly into the bundle as a literal (compile-time substitution, not a runtime
    // env read). Dev/watch builds route the bundled node core client at the persistent dev sub-profile socket
    // (~/.pleiades/plaintorch-dev) that a manual `serve` binds; production builds use the real per-user profile.
    __PLAINTORCH_DEV_PROFILE__: production ? "false" : "true"
  },
  sourcemap: production ? false : "inline",
  outfile: `${outputDirectory}/main.js`,
  plugins: [localRepoFromSource(), copyManifestPlugin],
  external: [
    "obsidian",
    "electron",
    "http",
    "os",
    "path",
    "node:http",
    "node:os",
    "node:path",
    "@codemirror/state",
    "@codemirror/view",
    "@codemirror/language"
  ]
});

if (watch) {
	console.warn("Initiating watcher...");
  await context.watch();
} else {
  await context.rebuild();
  await context.dispose();
}
