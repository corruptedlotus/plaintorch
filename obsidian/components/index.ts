/**
 * The core client. `core.repos` gives cached, observable reads — use those for anything a surface displays
 * and must keep current. The domain SDKs on `core` stay the way to run a one-shot query (a picker's search)
 * or an imperative vault command.
 */
export { plaintorchNodeCoreClient as core } from '@pleiades/sdk/plaintorch/node'

export * from './data'
export * from './media'
export * from './PleiadesIcon'
export * from './design'
export * from './editing'
export * from './system'
export * from './banners'
export * from './entities'
export * from './grid'
export * from './canvas'
export * from './briefing'
export * from './NoteBanner'