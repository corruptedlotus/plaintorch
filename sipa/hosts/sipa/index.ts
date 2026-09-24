/**
 * `@pleiades/sipa/hosts/sipa` — the SIPA platform host: what the standalone shell's renderer installs with
 * `provideHost`. Kept out of the package's main entry so the Obsidian plugin never bundles it (nor `lucide`).
 */
export * from './SipaHost'
export * from './Modal'
export * from './SuggestModal'
export * from './Toast'
export * from './styles'
export { lucideIcons } from './icons'
