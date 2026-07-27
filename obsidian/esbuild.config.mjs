import esbuild from "esbuild";
import { copyFile, mkdir } from "node:fs/promises";
import path from "node:path";

const production = process.argv.includes("production");
const watch = !production;
const outputDirectory = "_dist";

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
  },
  format: "cjs",
  target: "es2020",
  logLevel: "info",
  define: {
    // Bake the dev-profile flag directly into the bundle as a literal (compile-time substitution, not a runtime
    // env read). Dev/watch builds route the bundled node core client at the persistent dev sub-profile socket
    // (~/.pleiades/plaintorch-dev) that a manual `serve` binds; production builds use the real per-user profile.
    __PLAINTORCH_DEV_PROFILE__: production ? "false" : "true"
  },
  sourcemap: production ? false : "inline",
  outfile: `${outputDirectory}/main.js`,
  plugins: [copyManifestPlugin],
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
	console.log("Watching PLAINTORCH Obsidian plugin...");
  await context.watch();
} else {
  await context.rebuild();
  await context.dispose();
}
