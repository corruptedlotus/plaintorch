/**
 * `@pleiades/sipa` — the Sunnyside Interface for Pleiades Affairs: PLAINTORCH's platform-neutral UI. Importing the
 * package registers every custom element and exposes the component surface; hosts (the Obsidian plugin, the
 * standalone shell) bundle it from source and install their {@link PlatformHost} with `provideHost` at bootstrap.
 */
export * from "./host"
export * from "./components"
