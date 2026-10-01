import { Controller, ElementRef, type ReactiveElement } from '@a11d/lit'
import { fuzzyScore } from '../editing/fuzzy'
import type { IconName } from '../PleiadesIcon'

/** One value a {@link SearchFilter} offers. */
export interface SearchFilterOption<V = unknown> {
	/**
	 * The value as written after the colon: `objective` in `type:objective`. Typing is matched against it (and against
	 * the {@link label}), a value typed out in full is recognised by it, and a chip taken apart writes it back. One word,
	 * with no spaces, so a written clause always reads back as the value it was.
	 */
	readonly text: string
	/** What the option stands for, as the filter's {@link SearchFilter.matches} takes it. */
	readonly value: V
	/** How the option reads in the drawer and on its chip. */
	readonly label: string
	/** The option's glyph. */
	readonly icon?: IconName
	/** A note beside the label in the drawer, such as where a directive sits in its hierarchy. */
	readonly detail?: string
}

/**
 * A filter a search can be narrowed by. It is written as a `key:value` clause and, once committed, kept as a chip; its
 * chips narrow the items the search lets through.
 */
export interface SearchFilter<T, V = unknown> {
	/** The identifier written before the colon: `type` in `type:objective`. One lowercase word. */
	readonly key: string
	/** How the filter reads: the drawer's "Filter by …" and the prefix of its chips. */
	readonly label: string
	/** A line describing the filter in the drawer's list of filters. */
	readonly description?: string
	/** The filter's glyph. */
	readonly icon?: IconName
	/**
	 * Whether several of the filter's chips may stand at once. They then combine as a union: an item passes when it
	 * matches any of them. Without it, committing a new chip of the filter replaces the one standing.
	 */
	readonly multiple?: boolean
	/**
	 * The values the filter offers, given the chips standing, so that one filter can narrow another's values (statuses
	 * to the chosen types). Values already chipped are left out of the drawer by the controller.
	 */
	options(chips: readonly SearchChip[]): readonly SearchFilterOption<V>[]
	/** Whether an item passes the filter's chip for a value. */
	matches(item: T, value: V): boolean
	/**
	 * Whether an item passes while none of the filter's chips stands: the filter's default. Without it, everything
	 * passes. (The backlog's status filter hides archived items this way, until a status is chosen.)
	 */
	readonly unset?: (item: T) => boolean
}

/** A committed filter: the key of its filter, and the option it was committed with. */
export interface SearchChip<V = unknown> {
	readonly key: string
	readonly option: SearchFilterOption<V>
}

/** What a search asks of the items it lets through, as it stands. */
export interface SearchCriteria<T> {
	/** The free text: the search's words, each of which an item's text must hold. */
	readonly text: string
	/** The committed filters. */
	readonly chips: readonly SearchChip[]
	/** Whether the reader has asked anything, words or chips, beyond the filters' defaults. */
	readonly active: boolean
	/** Whether an item passes: it holds every word, and every filter passes it — any of the filter's chips, or its default. */
	matches(item: T): boolean
}

/** A row of the drawer: a filter whose clause to start writing, or one of a filter's values to commit. */
export type SearchDrawerRow<T> =
	| { readonly kind: 'filter', readonly filter: SearchFilter<T> }
	| { readonly kind: 'option', readonly filter: SearchFilter<T>, readonly option: SearchFilterOption }

/** The drawer as it stands: the filters on offer, or the values of the filter whose clause is being written. */
export interface SearchDrawer<T> {
	/** The filter whose values are on offer. Unset while the filters themselves are. */
	readonly filter?: SearchFilter<T>
	/** What has been typed after the filter's colon, which the values on offer are narrowed by. */
	readonly query: string
	readonly rows: readonly SearchDrawerRow<T>[]
	/** The index of the row Enter, or Tab, would take. */
	readonly highlighted: number
}

export interface SearchFilterControllerOptions<T> {
	/** The filters the search offers. Read whenever needed, so a getter can rebuild them as their sources change. */
	readonly filters: readonly SearchFilter<T, any>[]
	/** The text an item's free search is matched against, usually its title. */
	readonly textOf: (item: T) => string
	/** Called with the criteria whenever they change (a word typed, a chip committed or removed), and once on connecting. */
	readonly handleCriteriaChange?: (criteria: SearchCriteria<T>) => void
}

/** The clause being written: its filter, where it starts in the text, and what has been typed after its colon. */
interface WrittenClause<T> {
	readonly filter: SearchFilter<T>
	readonly start: number
	readonly query: string
}

/**
 * The search-and-filter behaviour, without a face: a search field narrows items by free text and by filter clauses,
 * `key:value`, that turn into chips once committed. Any component can be the face; it renders the text input, the
 * chips and the drawer from the controller's state and hands their gestures back:
 *
 * ```ts
 * readonly search = new SearchFilterController<Item>(this, host => ({
 *   get filters() { return host.filters },
 *   textOf: item => item.title,
 *   handleCriteriaChange: criteria => host.criteria = criteria,
 * }))
 * ```
 * ```html
 * <input ${this.search.input.ref()}>
 * ```
 *
 * The controller owns the input's value: it listens to the typing, and writes the field itself whenever it rewrites the
 * text (a clause committed, a chip taken apart), so the face binds no value of its own.
 *
 * **Writing.** The text is free search until a filter's key and a colon are typed: from there to the end of the text is
 * the clause being written, and everything before it stays free search. While no clause is being written the drawer
 * offers the filters, narrowed to those whose key or name starts with the word being typed; choosing one writes its
 * key and colon in place of that word. While a clause is being written the drawer offers that filter's values,
 * narrowed by what has been typed after the colon.
 *
 * **Committing.** A value turns into a chip, and applies, when it is chosen from the drawer (Enter or a click), when Tab
 * is pressed with a value matched (the highlighted one, once something has been typed or the arrows have moved), when a
 * value is typed out in full and followed by a space, or when the field loses focus on a value typed out in full. A
 * filter that allows `multiple` chips keeps each, and they combine as a union; any other filter's new chip replaces its
 * old one.
 *
 * **Chips.** Backspace in an empty field removes the last chip. A chip taken apart for editing ({@link edit}) leaves the
 * chips and is written back into the field as its clause, ready to change; committed again, it returns to where it
 * stood, and Escape puts it back as it was.
 *
 * **Criteria.** An item passes when its text holds every word of the free search and every filter passes it: any of the
 * filter's chips, or, with none standing, the filter's default ({@link SearchFilter.unset}). The host receives the
 * criteria through `handleCriteriaChange` and applies them however its results are shown; a flat list can use
 * {@link results}.
 *
 * The input is a combobox over the drawer (`role`, `aria-expanded`, `aria-autocomplete`), stamped by the controller.
 */
export class SearchFilterController<T, THost extends ReactiveElement = ReactiveElement> extends Controller implements EventListenerObject {
	/** The search's text input: `<input ${controller.input.ref()}>`. The controller listens to it and keeps its value. */
	readonly input = new ElementRef<HTMLInputElement>({
		updated: element => this.attach(element),
		disconnected: element => this.detach(element)
	})

	protected readonly options: SearchFilterControllerOptions<T>

	private draft = ''
	private committed: readonly SearchChip[] = []
	private focused = false
	/** Escape closed the drawer; it stays closed until the reader types, or moves through it, again. */
	private dismissed = false
	private highlight = 0
	/** Whether the reader moved the highlight themselves, which lets Tab take it with nothing typed. */
	private navigated = false
	/** The chip taken apart for editing, and where it stood, so that it goes back there, or is restored on Escape. */
	private editing?: { readonly chip: SearchChip, readonly index: number }
	/** What the criteria were last announced as, so that only a real change is announced. */
	private announced?: string
	/** Whether the criteria are still to be announced since the host connected, once it has rendered. */
	private announcePending = false

	constructor(protected override readonly host: THost, options: SearchFilterControllerOptions<T> | ((host: THost) => SearchFilterControllerOptions<T>)) {
		super(host)
		this.options = typeof options === 'function' ? options(host) : options
	}

	/** The text in the field: free search, ending in the clause being written, if any. */
	get text() {
		return this.draft
	}

	/** The committed filters, in the order they stand. */
	get chips(): readonly SearchChip[] {
		return this.committed
	}

	/** The filter a key names, or `undefined` for a key no filter has. */
	filterOf(key: string): SearchFilter<T> | undefined {
		const wanted = key.toLowerCase()
		return this.options.filters.find(filter => filter.key.toLowerCase() === wanted)
	}

	/** The criteria as they stand. */
	get criteria(): SearchCriteria<T> {
		const text = this.freeText.trim()
		const words = text.toLowerCase().split(/\s+/).filter(Boolean)
		const chips = this.committed
		const filters = () => this.options.filters
		const textOf = this.options.textOf
		return {
			text,
			chips,
			active: words.length > 0 || chips.length > 0,
			matches: item => {
				if (words.length > 0) {
					const haystack = textOf(item).toLowerCase()
					if (!words.every(word => haystack.includes(word))) {
						return false
					}
				}

				return filters().every(filter => {
					const own = chips.filter(chip => chip.key === filter.key)
					return own.length === 0
						? filter.unset?.(item) ?? true
						: own.some(chip => filter.matches(item, chip.option.value))
				})
			}
		}
	}

	/** The items of a flat list that pass the criteria, in their order. */
	results(items: readonly T[]): T[] {
		const criteria = this.criteria
		return items.filter(item => criteria.matches(item))
	}

	/**
	 * The drawer as it stands, or `undefined` while it is closed: the field is not focused, Escape closed it, or there is
	 * no filter to offer. While a clause is being written it stays open even with no value matching, to say so.
	 */
	get drawer(): SearchDrawer<T> | undefined {
		if (!this.focused || this.dismissed) {
			return undefined
		}

		const clause = this.clause
		const rows = clause ? this.optionRows(clause.filter, clause.query) : this.filterRows(this.word.text)
		if (!clause && rows.length === 0) {
			return undefined
		}

		return {
			filter: clause?.filter,
			query: clause?.query ?? '',
			rows,
			highlighted: Math.max(0, Math.min(this.highlight, rows.length - 1))
		}
	}

	override hostConnected() {
		this.announcePending = true
	}

	/** Stamps the input, and on the first render after connecting announces the criteria the search starts with. */
	override hostUpdated() {
		this.stampInput()
		if (this.announcePending) {
			this.announcePending = false
			this.announce(true)
		}
	}

	/**
	 * Moves the drawer's highlight to a row, as the pointer does by passing over it. A row reached so stays Enter's to
	 * take but not Tab's: only a row the arrows chose, or the typing narrowed to, is one Tab takes.
	 */
	highlightRow(index: number) {
		if (index !== this.highlight) {
			this.highlight = index
			this.host.requestUpdate()
		}
	}

	/** Takes a drawer row: a filter starts its clause, a value commits its chip. */
	choose(row: SearchDrawerRow<T>) {
		if (row.kind === 'filter') {
			this.startClause(row.filter)
		}
		else {
			this.commit(row.filter, row.option)
		}

		this.input.value?.focus()
	}

	/** Removes a chip. */
	remove(chip: SearchChip) {
		this.committed = this.committed.filter(standing => standing !== chip)
		this.changed()
	}

	/**
	 * Takes a chip apart for editing: it leaves the chips and is written back into the field as its clause (in place of
	 * any clause being written), with its value matched and ready to commit again. Committed again it returns to where
	 * it stood; Escape restores it as it was.
	 */
	edit(chip: SearchChip) {
		if (!this.committed.includes(chip)) {
			return
		}

		this.settleEditing()
		const before = this.freeText
		this.editing = { chip, index: this.committed.indexOf(chip) }
		this.committed = this.committed.filter(standing => standing !== chip)
		this.draft = `${before}${before && !/\s$/.test(before) ? ' ' : ''}${chip.key}:${chip.option.text}`
		this.resetHighlight()
		this.dismissed = false
		this.changed()
		this.input.value?.focus()
	}

	/** Empties the search: no words, no chips. */
	clear() {
		this.editing = undefined
		this.committed = []
		this.draft = ''
		this.resetHighlight()
		this.changed()
		this.input.value?.focus()
	}

	handleEvent(event: Event) {
		switch (event.type) {
			case 'input':
				this.type((event.target as HTMLInputElement).value)
				return
			case 'keydown':
				this.handleKey(event as KeyboardEvent)
				return
			case 'focus':
				this.focused = true
				this.dismissed = false
				this.host.requestUpdate()
				return
			case 'blur':
				this.focused = false
				// Leaving the field keeps a value typed out in full. A chip being edited goes back either way: as typed, if
				// that still names a value, or else as it was.
				if (this.editing) {
					this.settleEditing()
				}
				else {
					this.commitTypedValue()
				}

				this.host.requestUpdate()
				return
		}
	}

	/** The text before the clause being written: the free search. */
	private get freeText() {
		const clause = this.clause
		return clause ? this.draft.slice(0, clause.start) : this.draft
	}

	/**
	 * The clause being written: the last `key:` in the text whose key names a filter, at the start of the text or after a
	 * space, and everything after its colon.
	 */
	private get clause(): WrittenClause<T> | undefined {
		let found: WrittenClause<T> | undefined
		for (const match of this.draft.matchAll(/(^|\s)([^\s:]+):/g)) {
			const filter = this.filterOf(match[2]!)
			if (filter) {
				const start = match.index! + match[1]!.length
				found = { filter, start, query: this.draft.slice(match.index! + match[0].length) }
			}
		}

		return found
	}

	/** The word being typed at the end of the text, and where it starts. Empty after a space. */
	private get word() {
		const match = /(\S*)$/.exec(this.draft)!
		return { text: match[1]!, start: match.index }
	}

	/** The filters on offer: those whose key or name starts with the word being typed, or all of them for no word. */
	private filterRows(word: string): SearchDrawerRow<T>[] {
		const prefix = word.toLowerCase()
		return this.options.filters
			.filter(filter => filter.options(this.committed).length > 0)
			.filter(filter => !prefix || filter.key.toLowerCase().startsWith(prefix) || filter.label.toLowerCase().startsWith(prefix))
			.map(filter => ({ kind: 'filter', filter }))
	}

	/** A filter's values on offer: the ones not already chipped, best match first for what has been typed. */
	private optionRows(filter: SearchFilter<T>, query: string): SearchDrawerRow<T>[] {
		const taken = new Set(this.committed.filter(chip => chip.key === filter.key).map(chip => normalise(chip.option.text)))
		const offered = filter.options(this.committed).filter(option => !filter.multiple || !taken.has(normalise(option.text)))
		const typed = query.trim()
		if (!typed) {
			return offered.map(option => ({ kind: 'option', filter, option }))
		}

		const exact = normalise(typed)
		return offered
			.map((option, index) => {
				const fuzzy = Math.max(fuzzyScore(typed, option.text) ?? -Infinity, fuzzyScore(typed, option.label) ?? -Infinity)
				const whole = normalise(option.text) === exact || normalise(option.label) === exact
				return { option, index, score: whole ? Infinity : fuzzy }
			})
			.filter(entry => entry.score > -Infinity)
			.sort((a, b) => b.score - a.score || a.index - b.index)
			.map(({ option }) => ({ kind: 'option', filter, option }))
	}

	/** The option a typed value names in full, by its text or its label. */
	private typedOption(filter: SearchFilter<T>, value: string): SearchFilterOption | undefined {
		const wanted = normalise(value)
		return wanted ? filter.options(this.committed).find(option => normalise(option.text) === wanted || normalise(option.label) === wanted) : undefined
	}

	private type(text: string) {
		this.draft = text
		this.dismissed = false
		this.resetHighlight()

		// A value typed out in full and followed by a space is taken as it stands, as Tab would take it.
		const clause = this.clause
		const match = clause && /^(\S+)\s$/.exec(clause.query)
		const option = match && clause && this.typedOption(clause.filter, match[1]!)
		if (clause && option) {
			this.commit(clause.filter, option)
			return
		}

		this.changed()
	}

	private handleKey(event: KeyboardEvent) {
		const drawer = this.drawer
		const row = drawer?.rows[drawer.highlighted]
		switch (event.key) {
			case 'ArrowDown':
			case 'ArrowUp': {
				if (this.dismissed) {
					this.dismissed = false
					this.host.requestUpdate()
					event.preventDefault()
					return
				}

				const count = drawer?.rows.length ?? 0
				if (count > 0) {
					event.preventDefault()
					this.navigated = true
					this.highlightRow((drawer!.highlighted + (event.key === 'ArrowDown' ? 1 : -1) + count) % count)
				}

				return
			}
			case 'Enter':
				if (row && !event.ctrlKey && !event.metaKey && !event.altKey) {
					event.preventDefault()
					this.choose(row)
				}

				return
			case 'Tab':
				// Tab takes a match: a value (or a filter's key) the typing has narrowed to, or a row the arrows chose. With
				// nothing typed and nothing chosen it moves focus on, as Tab does.
				if (row && !event.shiftKey && (this.navigated || (drawer!.filter ? drawer!.query.trim() : this.word.text) !== '')) {
					event.preventDefault()
					this.choose(row)
				}

				return
			case 'Backspace': {
				const input = event.target as HTMLInputElement
				if (this.draft === '' && input.selectionStart === 0 && input.selectionEnd === 0 && this.committed.length > 0) {
					event.preventDefault()
					this.remove(this.committed[this.committed.length - 1]!)
				}

				return
			}
			case 'Escape':
				if (this.editing) {
					this.restoreEditing()
				}
				else if (drawer) {
					this.dismissed = true
					this.host.requestUpdate()
				}
				else if (this.draft) {
					this.type('')
				}
				else {
					return
				}

				event.preventDefault()
				event.stopPropagation()
				return
		}
	}

	/** Writes a filter's key and colon in place of the word being typed, so its values come on offer. */
	private startClause(filter: SearchFilter<T>) {
		const { start } = this.word
		this.draft = `${this.draft.slice(0, start)}${filter.key}:`
		this.dismissed = false
		this.resetHighlight()
		this.changed()
	}

	/**
	 * Turns a filter's value into a chip: the clause being written leaves the text, the free search before it stays. A
	 * chip that was taken apart goes back where it stood; a filter without `multiple` gives up its old chip for the new.
	 */
	private commit(filter: SearchFilter<T>, option: SearchFilterOption) {
		const chip: SearchChip = { key: filter.key, option }
		const chips = [...this.committed]
		let index = this.editing?.index ?? chips.length
		if (!filter.multiple) {
			const replaced = chips.findIndex(standing => standing.key === filter.key)
			if (replaced >= 0) {
				chips.splice(replaced, 1)
				index = this.editing ? Math.min(index, chips.length) : replaced
			}
		}

		const duplicate = chips.some(standing => standing.key === filter.key && normalise(standing.option.text) === normalise(option.text))
		if (!duplicate) {
			chips.splice(Math.min(index, chips.length), 0, chip)
		}

		this.editing = undefined
		this.committed = chips
		this.draft = this.freeText
		this.resetHighlight()
		this.changed()
	}

	/** Commits the clause being written if its value is typed out in full. Returns whether it did. */
	private commitTypedValue(): boolean {
		const clause = this.clause
		const option = clause && this.typedOption(clause.filter, clause.query)
		if (clause && option) {
			this.commit(clause.filter, option)
			return true
		}

		return false
	}

	/** Before another chip is taken apart: the one being edited is committed as typed, or else put back as it was. */
	private settleEditing() {
		if (this.editing && !this.commitTypedValue()) {
			this.restoreEditing()
		}
	}

	/** Puts the chip being edited back where it stood, as it was, and drops its clause from the text. */
	private restoreEditing() {
		const editing = this.editing
		if (!editing) {
			return
		}

		const free = this.freeText
		const chips = [...this.committed]
		chips.splice(Math.min(editing.index, chips.length), 0, editing.chip)
		this.editing = undefined
		this.committed = chips
		this.draft = free
		this.resetHighlight()
		this.changed()
	}

	private resetHighlight() {
		this.highlight = 0
		this.navigated = false
	}

	/** Writes the draft into the field, re-renders the host, and announces the criteria if they changed. */
	private changed() {
		this.writeInput()
		this.host.requestUpdate()
		this.announce()
	}

	/**
	 * The field shows the draft. The controller owns the field's value rather than leaving it to a template binding: a
	 * binding compares with what it last wrote, not with what the reader typed since, so a clause committed into a chip
	 * would stay on screen while the draft behind it was already empty. Typing leaves the two equal, and the caret where
	 * it is; a draft the controller rewrote puts the caret at its end.
	 */
	private writeInput() {
		const input = this.input.value
		if (input && input.value !== this.draft) {
			input.value = this.draft
			input.setSelectionRange(this.draft.length, this.draft.length)
		}
	}

	private announce(force = false) {
		const criteria = this.criteria
		const signature = JSON.stringify([criteria.text.toLowerCase(), criteria.chips.map(chip => [chip.key, chip.option.text])])
		if (force || signature !== this.announced) {
			this.announced = signature
			this.options.handleCriteriaChange?.(criteria)
		}
	}

	private attach(element: HTMLInputElement) {
		for (const type of ['input', 'keydown', 'focus', 'blur']) {
			element.addEventListener(type, this)
		}

		this.writeInput()
		this.stampInput()
	}

	private detach(element: HTMLInputElement) {
		for (const type of ['input', 'keydown', 'focus', 'blur']) {
			element.removeEventListener(type, this)
		}
	}

	private stampInput() {
		const input = this.input.value
		if (!input) {
			return
		}

		input.setAttribute('role', 'combobox')
		input.setAttribute('aria-autocomplete', 'list')
		input.setAttribute('aria-expanded', String(!!this.drawer))
	}
}

/** A value's comparable form: lowercase, with spaces and hyphens counted alike. */
function normalise(text: string) {
	return text.trim().toLowerCase().replace(/[\s_]+/g, '-')
}
