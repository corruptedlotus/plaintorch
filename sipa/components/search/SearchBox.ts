import { Component, component, css, event, html, nothing, property, ref, type HTMLTemplateResult } from '@a11d/lit'
import { popover, PopoverAlignment, PopoverPlacement } from '@3mo/popover'
import type { SelectList, SelectListRow } from '../editing/SelectList'
import '../editing/SelectList'
import { SearchFilterController, type SearchCriteria, type SearchDrawer, type SearchFilter } from './SearchFilterController'
import './FilterChip'
import './SearchDrawerItem'

/**
 * A search field that narrows items by free text and by filters, the face of a {@link SearchFilterController}.
 *
 * The field holds the committed filters as chips, then the text input. While it is focused a drawer hangs beneath it:
 * the filters on offer, or the values of the filter whose `key:` clause is being written. The controller does all of
 * the behaviour (what the typing means, what the drawer offers, how a chip is committed, taken apart or removed, and
 * which items pass); the box draws its state and hands its gestures back. A host gives the box its {@link filters} and
 * how an item reads ({@link textOf}), and takes the criteria from `criteriachange` to narrow whatever it shows.
 *
 * The drawer is a {@link SelectList}, the select components' floating list, hosted through @3mo's `popover` directive
 * in the application's top layer, so no ancestor's overflow clips it.
 */
@component('p7t-search-box')
export class SearchBox<T = unknown> extends Component {
	/** The filters the search offers. A new array is fine on every render; the controller reads it as it needs it. */
	@property({ attribute: false }) filters: readonly SearchFilter<T, any>[] = []
	/** How an item reads to the free search: its title, by default. */
	@property({ attribute: false }) textOf: (item: T) => string = item => String((item as { title?: unknown }).title ?? '')
	@property() placeholder = 'Search…'

	/** Raised with the criteria whenever they change, and once with the defaults the search starts with. */
	@event() criteriachange!: EventDispatcher<SearchCriteria<T>>

	readonly search = new SearchFilterController<T, SearchBox<T>>(this, host => ({
		get filters() { return host.filters },
		textOf: item => host.textOf(item),
		handleCriteriaChange: criteria => host.criteriachange.dispatch(criteria)
	}))

	/** The drawer's list once the popover directive has made it; its live state is pushed into it on every update. */
	private listElement?: SelectList

	static override get styles() {
		return css`
			:host {
				display: block;
				font-family: var(--font-interface);
			}

			.field {
				display: flex;
				flex-wrap: wrap;
				align-items: center;
				gap: .35em;
				min-height: 2.6em;
				box-sizing: border-box;
				padding: .3em .4em .3em .8em;
				border-radius: 1.3em;
				border: 1px solid color-mix(in srgb, var(--text-normal) 14%, transparent);
				background-color: var(--background-primary-alt);
				/* The focus ring: an outline held 3px off the field, there all along but clear until the field is focused. */
				outline: 3px solid transparent;
				outline-offset: 3px;
				cursor: text;
				transition: border-color .2s ease, outline-color .2s ease;
			}

			.field:focus-within {
				border-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 55%, transparent);
				outline-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, transparent);
			}

			.glyph {
				flex: none;
				width: 1.15em;
				height: 1.15em;
				margin-inline-end: .15em;
				color: var(--text-muted);
			}

			.input {
				all: unset;
				flex: 1;
				min-width: 10ch;
				padding-block: .25em;
				font: inherit;
				color: var(--text-normal);
			}

			.input::placeholder {
				color: var(--text-faint, var(--text-muted));
			}

			.clear {
				flex: none;
				color: var(--text-muted);
			}
		`
	}

	protected override get template() {
		const { search } = this
		const empty = search.text === '' && search.chips.length === 0
		return html`
			<div class='field' ${popover(() => this.drawerTemplate)} @click=${(e: MouseEvent) => this.onFieldClick(e)}>
				<p7t-icon class='glyph' icon='lucide:search'></p7t-icon>
				${search.chips.map(chip => {
					const filter = search.filterOf(chip.key)
					return html`
						<p7t-filter-chip
							heading=${filter?.label ?? chip.key}
							label=${chip.option.label}
							.icon=${chip.option.icon ?? filter?.icon}
							@chipedit=${() => search.edit(chip)}
							@chipremove=${() => search.remove(chip)}>
						</p7t-filter-chip>
					`
				})}
				<input
					class='input'
					type='text'
					spellcheck='false'
					autocomplete='off'
					aria-label=${this.placeholder}
					placeholder=${search.chips.length > 0 ? '' : this.placeholder}
					${search.input.ref()}>
				${empty ? nothing : html`
					<p7t-button ghost class='clear' icon='lucide:x' label='Clear the search' @click=${() => search.clear()}></p7t-button>
				`}
			</div>
		`
	}

	/** A click on the field itself, around its chips and input, puts the caret in the input. */
	private onFieldClick(e: MouseEvent) {
		if (e.target === e.currentTarget) {
			this.search.input.value?.focus()
		}
	}

	/**
	 * The drawer, as the popover directive renders it. Its state is bound here as well as pushed by {@link syncDrawer}:
	 * the directive makes the list at browser idle time, and a list made after the box's last update must still open
	 * with what the box shows.
	 */
	private get drawerTemplate(): HTMLTemplateResult {
		const state = this.drawerState
		return html`
			<p7t-select-list
				${ref(element => this.adoptList(element as SelectList | undefined))}
				mode='manual'
				.placement=${PopoverPlacement.BlockEnd}
				.alignment=${PopoverAlignment.Start}
				.offset=${6}
				?open=${state.open}
				.heading=${state.heading}
				.emptyNote=${state.emptyNote}
				.rows=${state.rows}
				.highlighted=${state.highlighted}
				@rowhighlight=${(e: CustomEvent<number>) => this.search.highlightRow(e.detail)}
				@rowchoose=${(e: CustomEvent<number>) => this.chooseAt(e.detail)}>
			</p7t-select-list>
		`
	}

	protected override updated() {
		this.syncDrawer()
	}

	/** Takes the list the popover directive made, and brings it up to date at once. */
	private adoptList(list: SelectList | undefined) {
		this.listElement = list
		this.syncDrawer()
	}

	/** What the drawer's list shows as the search stands. */
	private get drawerState() {
		const drawer = this.search.drawer
		const query = drawer?.query.trim()
		return {
			open: !!drawer,
			heading: drawer ? (drawer.filter ? `Filter by ${drawer.filter.label}` : 'Filters') : undefined,
			emptyNote: !drawer?.filter ? '' : query
				? `No ${drawer.filter.label.toLowerCase()} matches “${query}”`
				: `Every ${drawer.filter.label.toLowerCase()} is chosen already`,
			rows: drawer ? this.rowsOf(drawer) : [],
			highlighted: drawer?.highlighted ?? 0
		}
	}

	/**
	 * Pushes the drawer's live state into its list at once. The popover directive renders the list only at browser idle
	 * time, far too loose for a highlight that follows an arrow key or a list that closes on a choice (as SelectBase).
	 */
	private syncDrawer() {
		const list = this.listElement
		if (!list) {
			return
		}

		const state = this.drawerState
		list.heading = state.heading
		list.emptyNote = state.emptyNote
		list.rows = state.rows
		list.highlighted = state.highlighted
		list.style.minWidth = `${Math.round(Math.min(this.offsetWidth, 448))}px`
		list.open = state.open
	}

	private rowsOf(drawer: SearchDrawer<T>): SelectListRow[] {
		return drawer.rows.map(row => row.kind === 'filter'
			? {
				key: `filter:${row.filter.key}`,
				template: html`
					<p7t-search-drawer-item .icon=${row.filter.icon} label=${row.filter.label} .detail=${row.filter.description} hint=${`${row.filter.key}:`}></p7t-search-drawer-item>
				`
			}
			: {
				key: `option:${row.filter.key}:${row.option.text}`,
				template: html`
					<p7t-search-drawer-item .icon=${row.option.icon} label=${row.option.label} .detail=${row.option.detail}></p7t-search-drawer-item>
				`
			})
	}

	private chooseAt(index: number) {
		const row = this.search.drawer?.rows[index]
		if (row) {
			this.search.choose(row)
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-search-box': SearchBox
	}
}
