// Refreshes the vendored Mitra embed bundle from a Mitra checkout: builds it there, copies `dist/embed.js` here and
// records which commit it came from in VERSION.
//
//   npm run vendor-mitra -- <path to the Mitra checkout>      (or set MITRA_REPO)
import { execFileSync } from "node:child_process"
import { copyFileSync, existsSync, writeFileSync } from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"

const here = path.dirname(fileURLToPath(import.meta.url))
const repository = process.argv[2] ?? process.env.MITRA_REPO
if (!repository || !existsSync(path.join(repository, "package.json"))) {
	console.error("Usage: npm run vendor-mitra -- <path to the Mitra checkout> (or set MITRA_REPO)")
	process.exit(1)
}

const run = (command, args) => execFileSync(command, args, { cwd: repository, stdio: ["ignore", "pipe", "inherit"], shell: process.platform === "win32" }).toString().trim()

console.log(`Building Mitra in ${repository}…`)
run("npm", ["run", "build"])
copyFileSync(path.join(repository, "dist", "embed.js"), path.join(here, "embed.js"))

const commit = run("git", ["rev-parse", "HEAD"])
const dirty = run("git", ["status", "--porcelain", "--", "src"]).length > 0
writeFileSync(path.join(here, "VERSION"), `${commit}${dirty ? " (with uncommitted changes)" : ""}\n`)
console.log(`Vendored Mitra's embed from ${commit}${dirty ? " with uncommitted changes" : ""}.`)
