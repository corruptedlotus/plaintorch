import { Component, css, eventListener, html, HTMLTemplateResult, nothing, repeat, type PropertyValues } from '@a11d/lit'
import { DataGridColumn, DataGridController, type DataGridColumnModification, type DataRecord } from '@3mo/data-grid/controller'
import type { EntitySubscription, EntityTypeName } from '@pleiades/sdk'
import { ContextMenuController, core, ExpandingAction, type ContextMenuEntry, type ContextMenuSpec } from '..'
import { gridTitleColumn, type GridNode, type GridRow } from './entityTree'

/**
 * The shared shell every grid variant renders into.
 *
 * The grid's structure is 3MO's `DataGridController`, a headless data grid this shell renders its own way: it owns
 * the columns (their definitions and whatever the user did to them — widths, order, visibility), flattens the
 * variant's tree into records that know their level and siblings, keeps which records are open, and stamps the
 * treegrid semantics — roles, levels, set sizes, expansion — onto the rows. What stays here is the rendering: the
 * sticky header whose headings resize and move their columns, the column tracks derived from the columns, the
 * variant's own row elements (keyed by entity so each keeps its subscriptions as the tree opens and closes around it),
 * and the sticky creation FAB.
 *
 * The shell rebuilds when the store changes, since a tree's shape comes from the entities' own references and can
 * change without any listing changing. A variant supplies its tree ({@link buildTree}), its {@link columns}, its
 * {@link loading} signal, the element it draws a row with ({@link renderRow}) and what the FAB offers
 * ({@link fabActions}); every row element lands its cells on the tracks this shell lays out, which is what keeps the
 * columns aligned down the whole list.
 */
export abstract class GridBase extends Component {
	/**
	 * The key of the column holding each row's lead — the indent lanes, the kind icon and the title. It is always
	 * first: every variant's rows draw their lead into it, and its column is pinned so no other can move before it.
	 */
	static readonly titleColumn = gridTitleColumn

	/** How narrow the title column may get, by resizing or by a narrow pane; below it the grid scrolls sideways. */
	private static readonly titleFloor = '7em'

	/** Where the user's column arrangement of each grid is remembered, suffixed by the grid's {@link layoutKey}. */
	private static readonly layoutStoragePrefix = 'plaintorch.grid-layout.'

	/**
	 * A column of a grid. Columns are identified by key rather than by a path into the data — a row element draws
	 * each cell itself — and none sorts, since a grid's sibling order is part of its structure (a directive's
	 * subdirectives before its incentives, the world heading last) rather than a presentation choice.
	 */
	protected static column(key: string, heading: string, init?: Partial<DataGridColumn<GridNode>>) {
		return new DataGridColumn<GridNode>({
			...init,
			heading,
			dataSelector: key as KeyPath.Of<GridNode>,
			sortable: false
		})
	}

	/** The key a column is known by — to the row elements, which draw a cell per key, and to the saved layout. */
	protected static keyOf(column: DataGridColumn<GridNode>): string {
		return String(column.dataSelector)
	}

	/** Whether the column is the title column, which holds each row's lead. */
	protected static isTitle(column: DataGridColumn<GridNode>) {
		return GridBase.keyOf(column) === GridBase.titleColumn
	}

	/** The title column: flexible by default, and pinned first so the lead always opens the row. */
	protected static titleColumnDefinition(heading = 'Title') {
		return GridBase.column(GridBase.titleColumn, heading, {
			width: `minmax(${GridBase.titleFloor}, 1fr)`,
			// Pinned: the controller keeps a sticky column where it is when columns are dragged around, so no other
			// column can move before it. It is not drawn sticky — the rows draw their own cells, and the grid only
			// scrolls sideways once the pane is narrower than every column's floor.
			sticky: 'start'
		})
	}

	/** The tree the rows are built from, rebuilt once per update so every read within an update sees the same one. */
	private tree: readonly GridNode[] = []

	/** The expandable rows seen so far, so a row that gains children opens by itself only the first time. */
	private readonly seenExpandable = new Set<string>()

	/** The rows the reader had open when a search began, to return to once it ends; unset while no search is on. */
	private openBeforeSearch?: ReadonlySet<string>

	/** The rows a search has already opened, so one the reader closes during the search stays closed. */
	private readonly openedBySearch = new Set<string>()

	private layoutRestored = false

	private storeSubscription?: EntitySubscription

	/**
	 * The headless data grid. Its options are read whenever needed, so the getters keep them live: the tree it
	 * flattens through each node's children, the variant's columns, and which nodes have anything to open.
	 */
	protected readonly grid = new DataGridController<GridNode, GridBase>(this, host => ({
		get data() { return host.tree },
		get columns() { return host.columns },
		subDataGridDataSelector: 'children' as KeyPath.Of<GridNode>,
		// The rows are the variant's own elements, which open into their children rather than into a details pane.
		hasDefaultRowElements: false,
		hasDataDetail: node => (node.children?.length ?? 0) > 0 && node.collapsible !== false,
		multipleDetails: true,
		handleColumnsChange: () => host.persistLayout()
	}))

	/** The header's menu: which columns show, their widths, and opening or closing every row at once. */
	protected readonly headerMenu = new ContextMenuController(this, event =>
		this.grid.header.value && event.composedPath().includes(this.grid.header.value) ? this.headerMenuSpec : undefined)

	static override get styles() {
		return css`
			:host {
				position: relative;
				display: grid;
				/*
				 * The shared tracks, derived from the columns (see columnTracks): one per visible column, then a
				 * filler, then the two action columns. The title column keeps a floor rather than taking whatever is
				 * left, since the others size to their content and an unbounded title track is the one that collapses
				 * in a narrow pane; below the floor the grid scrolls sideways instead. The filler takes the room a
				 * resized title no longer does, so the actions stay at the far edge.
				 */
				grid-template-columns: var(--p7t-grid-columns, minmax(7em, 1fr) auto max-content max-content);
				align-content: start;
				max-height: max-content;
				padding-block: 0 .4em;
			}

			/* A column is being dragged to a new place: nothing under the pointer should be selected meanwhile. */
			:host([data-reordering]) {
				user-select: none;
			}

			/*
			 * The heading row sticks to the top of whatever scrolls the grid, so the columns stay named however far
			 * down the list runs — beneath the briefing's sticky tab bar (--p7t-page-sticky-top), or wherever a page
			 * sets --p7t-grid-header-top to, below chrome of its own. It needs a surface of its own for the rows to pass
			 * under.
			 */
			.header {
				display: grid;
				grid-template-columns: subgrid;
				grid-column: 1 / -1;
				position: sticky;
				top: var(--p7t-grid-header-top, var(--p7t-page-sticky-top, 0px));
				z-index: 4;
				margin-block-end: .3em;
				background: var(--p7t-grid-header-background, color-mix(in srgb, black 20%, var(--background-primary)));
				border-block-end: 1px solid color-mix(in srgb, var(--text-normal) 12%, transparent);
				border-start-start-radius: 12px;
				border-start-end-radius: 12px;
				font-family: var(--font-interface);
				font-size: .72em;
				font-weight: 500;
				letter-spacing: .06em;
				text-transform: uppercase;
				color: var(--text-muted);
			}

			.column-header {
				position: relative;
				display: flex;
				align-items: center;
				gap: .3em;
				min-width: 0;
				min-height: 2.4em;
				padding-inline: .6em;
				user-select: none;
			}

			.column-header[data-alignment='end'] {
				justify-content: flex-end;
			}

			.column-header[data-alignment='center'] {
				justify-content: center;
			}

			/* A heading that is not pinned can be dragged to move its column. */
			.column-header[role='columnheader']:not([data-sticky]) {
				cursor: grab;
			}

			.column-header .heading {
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
			}

			.column-header[data-reorderability='dragging'] {
				opacity: .45;
			}

			.column-header[data-reorderability='drop-before'] {
				box-shadow: inset 2px 0 0 var(--interactive-accent);
			}

			.column-header[data-reorderability='drop-after'] {
				box-shadow: inset -2px 0 0 var(--interactive-accent);
			}

			.column-header p7t-button {
				font-size: 1.2em;
				margin-inline-start: -.3em;
				text-transform: none;
				letter-spacing: normal;
			}

			/*
			 * The resize handle straddles the heading's trailing edge. It draws a faint divider that lights up under the
			 * pointer, and while it is dragged, a guide down the viewport where the column's edge will land.
			 */
			.resizer {
				position: absolute;
				inset-block: .45em;
				inset-inline-end: -4px;
				width: 9px;
				z-index: 1;
				cursor: col-resize;
				touch-action: none;
			}

			.resizer::before {
				content: '';
				position: absolute;
				inset-block: 0;
				inset-inline-start: 4px;
				border-inline-start: 1px solid color-mix(in srgb, var(--text-normal) 16%, transparent);
				transition: border-color .2s ease;
			}

			.resizer:hover::before,
			.resizer[data-resizing]::before {
				border-inline-start-color: var(--interactive-accent);
			}

			.resizer[data-resizing]::after {
				content: '';
				position: fixed;
				inset-block: 0;
				inset-inline-start: var(--mo-data-grid-column-resizer-pointer);
				border-inline-start: 1px dashed var(--interactive-accent);
				pointer-events: none;
			}

			.notice {
				grid-column: 1 / -1;
				padding: 1em;
				opacity: .5;
				font-family: var(--font-interface);
				font-weight: 300;
			}

			/*
			 * Sticks to the corner of the scrolling area rather than to the document, so it stays reachable however
			 * far down the list runs. It sits in the last row's track so it never overlaps a row.
			 */
			.fab {
				position: sticky;
				grid-column: 1 / -1;
				justify-self: end;
				bottom: 1em;
				margin: .5em 1em 0 0;
				z-index: 5;
			}
		`
	}

	protected override connected() {
		this.restoreLayout()
		// The tree's shape comes from parent references on the entities themselves, so it can change without any
		// listing changing: reparenting leaves every membership intact. No per-entity subscription would report that,
		// so this observes the kinds the variant is built from — scoped to those types rather than the whole store,
		// so a change to an unrelated kind never re-runs the grid.
		this.storeSubscription = core.store.subscribeTypes(this.observedKinds, () => this.requestUpdate())
	}

	protected override disconnected() {
		this.storeSubscription?.()
		this.storeSubscription = undefined
	}

	/**
	 * Lets the controller tell a change of the grid's own state from one it stamped onto the rows itself (an
	 * expansion), which re-renders only the rows it changed. The controller is absent while the element constructs.
	 */
	override requestUpdate(...parameters: Parameters<Component['requestUpdate']>) {
		this.grid?.handleUpdateRequest(...parameters)
		super.requestUpdate(...parameters)
	}

	protected override willUpdate(changedProperties: PropertyValues<this>) {
		super.willUpdate(changedProperties)
		this.tree = this.buildTree()
		this.openNewlyExpandableRows()
		this.revealSearchResults()
	}

	protected override updated(changedProperties: PropertyValues<this>) {
		super.updated(changedProperties)
		this.style.setProperty('--p7t-grid-columns', this.columnTracks)
	}

	@eventListener('requestRowToggle')
	protected onRequestRowToggle(e: CustomEvent<string>) {
		e.stopPropagation()
		this.toggleRow(e.detail)
	}

	@eventListener('requestRowExpand')
	protected onRequestRowExpand(e: CustomEvent<string>) {
		e.stopPropagation()
		const record = this.recordOf(e.detail)
		if (record) {
			void this.grid.details.open(record)
		}
	}

	/** Expands or collapses one row, identified by its key. */
	public toggleRow(key: string) {
		const record = this.recordOf(key)
		if (record) {
			void this.grid.details.toggle(record)
		}
	}

	/** Opens every row that has anything to open. */
	public expandAll() {
		this.grid.details.openAll()
	}

	/** Closes every open row. */
	public collapseAll() {
		this.grid.details.closeAll()
	}

	/** Forgets whatever the user did to the columns, returning each to its definition. */
	public resetColumns() {
		this.grid.columns.columns.modifications.set([])
	}

	private recordOf(key: string) {
		return this.grid.records.records.find(record => record.data.id === key)
	}

	/**
	 * The records on show: every root, and the children of every open record. A record that cannot be collapsed at
	 * all (the world heading) keeps its children in sight.
	 */
	private get visibleRecords() {
		const { records, details } = this.grid
		return records.records.filter(record => record.node?.ancestors.every(ancestor => {
			const parent = records.recordOf(ancestor)
			return !details.hasDetail(parent) || details.isOpen(parent)
		}) ?? true)
	}

	/**
	 * A variant whose rows start open ({@link expandsByDefault}) opens each row the first time it has anything to
	 * open, so a newly nested page shows its children without the user's earlier collapses being undone.
	 */
	private openNewlyExpandableRows() {
		if (!this.expandsByDefault) {
			return
		}

		const opened = new Array<DataRecord<GridNode>>()
		for (const record of this.grid.records.records.filter(record => this.grid.details.hasDetail(record))) {
			if (!this.seenExpandable.has(record.data.id)) {
				this.seenExpandable.add(record.data.id)
				opened.push(record)
			}
		}

		if (opened.length > 0) {
			this.grid.details.expanded = [...this.grid.details.expanded, ...opened]
		}
	}

	/**
	 * While the variant is {@link searching}, the rows open so that what matched is in sight: every row with anything to
	 * open opens the first time the search shows it, and one the reader closes meanwhile stays closed. When the search
	 * ends, the rows the reader had open before it are open again, and only those.
	 */
	private revealSearchResults() {
		const { records, details } = this.grid
		if (!this.searching) {
			if (this.openBeforeSearch) {
				const before = this.openBeforeSearch
				this.openBeforeSearch = undefined
				this.openedBySearch.clear()
				details.expanded = records.records.filter(record => before.has(record.data.id))
			}

			return
		}

		this.openBeforeSearch ??= new Set(details.expanded.map(record => record.data.id))
		const opened = records.records.filter(record => details.hasDetail(record) && !this.openedBySearch.has(record.data.id))
		if (opened.length > 0) {
			opened.forEach(record => this.openedBySearch.add(record.data.id))
			details.expanded = [...details.expanded, ...opened]
		}
	}

	/**
	 * The column tracks: one per visible column at its width — the title column never below its floor — then the
	 * filler, then the two action columns.
	 */
	private get columnTracks() {
		return [
			...this.grid.columns.columns.visible.map(column => {
				const width = String(column.width)
				return !GridBase.isTitle(column) || width.startsWith('minmax(')
					? width
					: `minmax(${GridBase.titleFloor}, ${width})`
			}),
			'auto',
			'max-content',
			'max-content'
		].join(' ')
	}

	private restoreLayout() {
		if (this.layoutRestored) {
			return
		}

		this.layoutRestored = true
		const modifications = this.readLayout()
		if (modifications.length > 0) {
			this.grid.columns.columns.modifications.set(modifications)
		}
	}

	private readLayout(): DataGridColumnModification<GridNode>[] {
		if (!this.layoutKey) {
			return []
		}

		try {
			const stored = JSON.parse(localStorage.getItem(GridBase.layoutStoragePrefix + this.layoutKey) ?? '[]')
			return !Array.isArray(stored) ? [] : stored
				.filter(entry => typeof entry?.dataSelector === 'string')
				.map(entry => ({
					dataSelector: entry.dataSelector,
					width: typeof entry.width === 'string' ? entry.width : undefined,
					hidden: typeof entry.hidden === 'boolean' && entry.dataSelector !== GridBase.titleColumn ? entry.hidden : undefined
				}))
		} catch {
			return []
		}
	}

	/** Remembers the user's column arrangement — widths, order and visibility — for the next time the grid opens. */
	private persistLayout() {
		if (!this.layoutKey || !this.layoutRestored) {
			return
		}

		try {
			const key = GridBase.layoutStoragePrefix + this.layoutKey
			const modifications = [...this.grid.columns.columns.modifications]
			if (modifications.length === 0) {
				localStorage.removeItem(key)
			} else {
				localStorage.setItem(key, JSON.stringify(modifications))
			}
		} catch {
			// Storage may be unavailable (a private window, a locked-down host); the arrangement then lasts the session.
		}
	}

	private get headerMenuSpec(): ContextMenuSpec {
		const { columns, details } = this.grid
		const toggles = [...columns.columns]
			.filter(column => !GridBase.isTitle(column))
			.map((column): ContextMenuEntry => ({
				label: column.heading,
				icon: column.hidden ? 'lucide:square' : 'lucide:square-check',
				run: () => column.modify({ hidden: !column.hidden })
			}))

		return {
			title: 'Columns',
			entries: [
				...toggles,
				...(toggles.length === 0 ? [] : [{ separator: true } as const]),
				{
					label: 'Fit columns to content',
					icon: 'lucide:move-horizontal',
					run: () => columns.columns.visible.forEach(column => column.modify({ width: 'max-content' }))
				},
				{
					label: 'Reset columns',
					icon: 'lucide:rotate-ccw',
					disabled: columns.columns.modifications.length === 0,
					run: () => this.resetColumns()
				},
				...(!details.hasDetails ? [] : [
					{ separator: true } as const,
					{ label: 'Expand all', icon: 'lucide:chevrons-up-down', disabled: details.areAllOpen, run: () => this.expandAll() },
					{ label: 'Collapse all', icon: 'lucide:chevrons-down-up', disabled: details.expanded.length === 0, run: () => this.collapseAll() }
				] satisfies ContextMenuEntry[])
			]
		}
	}

	protected override get template() {
		const records = this.visibleRecords
		const columns = this.grid.columns.columns.visible.map(GridBase.keyOf)
		return html`
			${records.length === 0 ? html`
				<div class='notice'>${this.loading ? 'Loading…' : this.emptyLabel}</div>
			` : this.headerTemplate}
			${/*
				Keyed by entity so a row keeps its element — and therefore its subscription — as the tree is expanded and
				collapsed around it.
			*/ repeat(records, record => record.data.id, record => this.renderRow(this.rowOf(record, columns), record))}
			${this.fabActions.length === 0 ? nothing : html`
				<p7t-expanding-actions
					class='fab'
					large
					actionLabel=${this.fabLabel}
					.actions=${this.fabActions}>
				</p7t-expanding-actions>
			`}
			${this.appendageTemplate}
		`
	}

	/**
	 * The heading row: one heading per visible column, each with a handle on its trailing edge that resizes the column
	 * when dragged and fits it to its content on a double click. Dragging a heading moves its column; right-clicking
	 * the row opens its menu.
	 */
	private get headerTemplate() {
		const { grid } = this
		return html`
			<div class='header' ${grid.header.ref()}>
				${grid.columns.columns.visible.map(column => html`
					<div class='column-header' data-alignment=${column.alignment} ${grid.columnHeader(column)}>
						${!GridBase.isTitle(column) ? nothing : this.expansionToggleTemplate}
						<span class='heading'>${column.heading}</span>
						<span class='resizer' ${grid.columns.resizer(column)}></span>
					</div>
				`)}
				<div class='column-header' aria-hidden='true' style='grid-column: span 3'></div>
			</div>
		`
	}

	/** Opens every row while any is closed, and closes them all once every one is open. */
	private get expansionToggleTemplate() {
		const { details } = this.grid
		if (!details.hasDetails) {
			return nothing
		}

		const allOpen = details.areAllOpen
		return html`
			<p7t-button ghost
				icon=${allOpen ? 'lucide:chevrons-down-up' : 'lucide:chevrons-up-down'}
				label=${allOpen ? 'Collapse all' : 'Expand all'}
				@click=${() => allOpen ? this.collapseAll() : this.expandAll()}>
			</p7t-button>
		`
	}

	/** The row a record renders as: its node, where the grid placed it, and the columns it draws cells for. */
	private rowOf(record: DataRecord<GridNode>, columns: readonly string[]): GridRow {
		const node = record.data
		return {
			...node,
			key: node.id,
			depth: node.guides.length,
			expandable: record.hasDetails,
			expanded: record.detailsOpen,
			columns
		}
	}

	protected get appendageTemplate(): HTMLTemplateResult {
		return html``
	}

	/** The entity kinds this grid is built from — the store subscription is scoped to exactly these. */
	protected abstract get observedKinds(): readonly EntityTypeName[]

	/**
	 * The grid's columns, the title column ({@link titleColumnDefinition}) first. Kept as one array for the grid's
	 * lifetime, as a new array re-syncs the columns.
	 */
	protected abstract readonly columns: ReadonlyArray<DataGridColumn<GridNode>>

	/** The tree to render, as the variant's listings currently shape it. */
	protected abstract buildTree(): GridNode[]

	/** Whether the listings the rows are built from have not arrived yet. */
	protected abstract get loading(): boolean

	/**
	 * Renders one row with the variant's own row element, which must carry the record's registration with the grid
	 * (`${this.grid.row(record)}`) for the grid to stamp its place in the tree onto it.
	 */
	protected abstract renderRow(row: GridRow, record: DataRecord<GridNode>): HTMLTemplateResult

	/** Whether rows start open. The user's collapses are kept either way. */
	protected get expandsByDefault(): boolean {
		return false
	}

	/**
	 * Whether the reader is searching the grid — narrowing it by text or filters, as opposed to defaults alone. The rows
	 * then open to reveal what matched (see {@link revealSearchResults}). False by default, for grids without a search.
	 */
	protected get searching(): boolean {
		return false
	}

	/** Names the grid's remembered column arrangement. `undefined` (the default) remembers none. */
	protected get layoutKey(): string | undefined {
		return undefined
	}

	/** What the creation FAB offers. Empty hides the FAB. */
	protected get fabActions(): ExpandingAction[] {
		return []
	}

	/** The FAB's trigger label. */
	protected get fabLabel(): string {
		return 'Add'
	}

	/** What the notice reads when there is nothing to show and nothing loading. */
	protected get emptyLabel(): string {
		return 'Nothing to show'
	}
}
