import { App } from 'obsidian'

export * from './ReactiveBinder'
export * from './EditableDataLink'
export * from './SelectCollegeModal'
export * from './SelectStatusModal'
export * from './EditableStarfire'
export * from './EditableTimeUnit'

export const getApp = () => (window as any).app as App