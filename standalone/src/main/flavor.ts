import type { ShellFlavor } from "../shared/contracts"

// Compile-time substitutions baked in by esbuild (see esbuild.config.mjs). They decide which package this build is,
// not what the environment says at runtime, so a shipped bundle cannot be talked into another flavour.
declare const __PLAINTORCH_FLAVOR__: ShellFlavor
declare const __PLAINTORCH_DEV_PROFILE__: boolean

/** Which package this shell was built as: the standalone one hosts the core, the client one only attaches. */
export const flavor: ShellFlavor = typeof __PLAINTORCH_FLAVOR__ === "undefined" ? "standalone" : __PLAINTORCH_FLAVOR__

/**
 * Whether this is a development build. A dev build targets the persistent dev sub-profile (`~/.pleiades/plaintorch-dev`)
 * the same way a manual `serve` and a dev Obsidian plugin do, so it never collides with a real installed shell.
 */
export const isDevelopmentBuild = typeof __PLAINTORCH_DEV_PROFILE__ !== "undefined" && __PLAINTORCH_DEV_PROFILE__

/** Whether this shell owns a core process. */
export const hostsCore = flavor === "standalone"
