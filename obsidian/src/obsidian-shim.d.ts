declare module "obsidian" {
  export interface FrontMatterCache {
    [key: string]: unknown
  }

  export interface MarkdownPostProcessorContext {
    docId: string
    sourcePath: string
    frontmatter?: FrontMatterCache | null
    addChild(child: MarkdownRenderChild): void
    getSectionInfo(el: HTMLElement): unknown
  }

  export interface Command {
    id: string
    name: string
    callback: () => void
  }

  export interface EventRef {}

  export interface FileCache {
    frontmatter?: FrontMatterCache
  }

  export interface ViewState {
    type: string
    active?: boolean
  }

  export class TAbstractFile {
    public path: string
    public name: string
  }

  export class TFile extends TAbstractFile {
    public basename: string
    public extension: string
  }

  export class Vault {
    public getAbstractFileByPath(path: string): TAbstractFile | null
    public getFileByPath(path: string): TFile | null
    public getMarkdownFiles(): TFile[]
    public read(file: TFile): Promise<string>
    public modify(file: TFile, content: string): Promise<void>
    public getResourcePath(file: TFile): string
  }

  export class MetadataCache {
    public getFileCache(file: TFile): FileCache | null
  }

  export class WorkspaceLeaf {
    public view: View
    public setViewState(state: ViewState): Promise<void>
  }

  export class Workspace {
    public onLayoutReady(callback: () => void): void
    public on(name: "file-open", callback: (file: TFile | null) => void): EventRef
    public on(name: "active-leaf-change", callback: (leaf: WorkspaceLeaf | null) => void): EventRef
    public on(name: "layout-change", callback: () => void): EventRef
    public getLeavesOfType(type: string): WorkspaceLeaf[]
    public getRightLeaf(split: boolean): WorkspaceLeaf | null
    public getLeaf(newLeaf: boolean): WorkspaceLeaf
    public revealLeaf(leaf: WorkspaceLeaf): void
    public detachLeavesOfType(type: string): void
    public getActiveFile(): TFile | null
    public openLinkText(linktext: string, sourcePath: string, newLeaf?: boolean): Promise<void>
  }

  export class App {
    public workspace: Workspace
    public vault: Vault
    public metadataCache: MetadataCache
  }

  export class View {
    public app: App
    public leaf: WorkspaceLeaf

    public constructor(leaf: WorkspaceLeaf)
    public getViewType(): string
    public getDisplayText(): string
    public getIcon(): string
  }

  export class ItemView extends View {
    public containerEl: HTMLElement
    public contentEl: HTMLElement
    public onOpen(): Promise<void> | void
    public onClose(): Promise<void> | void
  }

  export class MarkdownView extends ItemView {
    public file: TFile | null
  }

  export class MarkdownRenderChild {
    public containerEl: HTMLElement
    public constructor(containerEl: HTMLElement)
  }

  export class Modal {
    public app: App
    public contentEl: HTMLElement
    public titleEl: HTMLElement

    public constructor(app: App)
    public open(): void
    public close(): void
    public onOpen(): void
    public onClose(): void
  }

  export class Plugin {
    public app: App

    public onload(): Promise<void> | void
    public onunload(): void
    public registerEvent(eventRef: EventRef): void
    public registerEditorExtension(extension: unknown): void
    public registerView(type: string, creator: (leaf: WorkspaceLeaf) => ItemView): void
    public addRibbonIcon(icon: string, title: string, callback: () => void): HTMLElement
    public addCommand(command: Command): void
    public registerMarkdownPostProcessor(
      callback: (element: HTMLElement, context: MarkdownPostProcessorContext) => void | Promise<void>
    ): void
  }
}

declare global {
  interface HTMLElement {
    empty(): void
    addClass(...classNames: string[]): void
    createDiv(attributes?: { cls?: string; text?: string }): HTMLDivElement
    createEl<K extends keyof HTMLElementTagNameMap>(
      tagName: K,
      attributes?: { cls?: string; text?: string }
    ): HTMLElementTagNameMap[K]
  }
}

export {}

declare module "*.svg" {
  const value: string
  export default value
}