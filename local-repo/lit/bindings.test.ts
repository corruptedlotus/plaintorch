/** @vitest-environment happy-dom */
import { describe, it, expect, beforeEach } from "vitest"
import { bindingDefaultPropertyKey, Component, component, html, property } from "@a11d/lit"
import { EntityStore } from "../entityStore.js"
import { RepositorySet } from "../repositorySet.js"
import { identifyByShape } from "../identity.js"
import { unresolved } from "../outcome.js"
import { EntityRef, EntityWatch, QueryRef } from "./EntityRef.js"
import { DerivedRef } from "./DerivedRef.js"

// The Lit bindings, mounted as real elements over a store whose backend is a table the test edits. What is
// pinned here is the lifecycle each binding owns — resolve, observe, re-render, release — the states it carries,
// and the edit cycle a two-way binder runs through a real `<input>`, so no host component wires any of it by hand.

// --- A bookshop with a table, a feed-less backend, and a store over it ---------------------------------------

interface Book { kind: "Book", id: string, name: string, price: number }

class Bookshop {
	public down = false
	public rejectWrites = false
	public readonly requests: string[] = []
	public readonly store = new EntityStore({ identify: identifyByShape({ typeKey: "kind" }) })
	public readonly repos = new RepositorySet(this.store, { succeeded: (result) => result !== false })
	public readonly books = this.repos.entity<Book>("Book", async (id) => await this.read<Book>(`/books/${id}`, () => this.table.get(id)))
	public readonly catalogue = this.repos.derived<Book[]>(async () => await this.read<Book[]>("/books", () => [...this.table.values()]))
	private readonly table = new Map<string, Book>([
		["b1", { kind: "Book", id: "b1", name: "The Dispossessed", price: 12 }],
		["b2", { kind: "Book", id: "b2", name: "The Lathe of Heaven", price: 9 }]
	])

	/** Writes one field of a book; `false` — the consumer's own rejection signal — when the shop refuses. */
	public async patch(id: string, field: string, value: unknown): Promise<unknown> {
		this.requests.push(`PATCH /books/${id} ${field}=${JSON.stringify(value)}`)
		if (this.rejectWrites) {
			return false
		}

		Object.assign(this.table.get(id)!, { [field]: value })
		return undefined
	}

	public delete(id: string): void {
		this.table.delete(id)
	}

	/** A GET as the consumer's client makes it: absorbed under a read; nothing is "nothing"; down is no answer. */
	private async read<T>(path: string, row: () => unknown) {
		this.requests.push(`GET ${path}`)
		return await this.store.read(async (parse) => {
			if (this.down) {
				return unresolved
			}

			const value = row()
			return value === undefined ? undefined : parse<T>(JSON.stringify(value))
		})
	}
}

let shop = new Bookshop()
let rejections = 0

// Under a test DOM, Node resolves Lit to its server build, which reports itself as a server; `@a11d/lit` then skips
// installing the default binding property of native form elements. A browser bundle gets it for free; do it here.
;(HTMLInputElement as unknown as Record<symbol, string>)[bindingDefaultPropertyKey] = "value"

beforeEach(() => {
	shop = new Bookshop()
	rejections = 0
	document.body.innerHTML = ""
})

/** Lets every pending fetch, absorption and Lit update settle. */
async function settle(): Promise<void> {
	for (let turn = 0; turn < 6; turn++) {
		await new Promise((resolve) => setTimeout(resolve, 0))
	}
}

async function mount<T extends HTMLElement>(tag: string, setUp?: (element: T) => void): Promise<T> {
	const element = document.createElement(tag) as T
	setUp?.(element)
	document.body.appendChild(element)
	await settle()
	return element
}

// --- The hosts ----------------------------------------------------------------------------------------------

@component("test-book-host")
class BookHost extends Component {
	@property() bookId = ""
	public renders = 0
	public readonly ref = new EntityRef<Book>(this, () => shop.books, () => this.bookId || undefined, { rejected: () => rejections++ })

	protected override get template() {
		this.renders++
		return html`<span>${this.ref.value?.name ?? ""}</span>`
	}
}

@component("test-book-editor")
class BookEditor extends Component {
	@property() bookId = ""
	public readonly ref = new EntityRef<Book>(this, () => shop.books, () => this.bookId || undefined, { rejected: () => rejections++ })
	public readonly reactions: string[] = []
	protected readonly binder = this.ref.binder("entity", {
		"*": async (book, key) => await shop.patch(book.id, key, (book as unknown as Record<string, unknown>)[key])
	}, (key, _, saved) => { this.reactions.push(`${key}:${saved}`) })

	public get entity(): Book | undefined {
		return this.ref.value
	}

	public get input(): HTMLInputElement {
		return this.renderRoot.querySelector("input")!
	}

	protected override get template() {
		return html`<input ${this.binder.bind("name")}>`
	}
}

@component("test-watch-host")
class WatchHost extends Component {
	@property({ type: Object }) book?: Book
	public renders = 0
	public readonly watch = new EntityWatch(this, () => shop.store, () => this.book)

	protected override get template() {
		this.renders++
		return html`<span>${this.book?.name ?? ""}</span>`
	}
}

@component("test-query-host")
class QueryHost extends Component {
	public renders = 0
	public readonly query = new QueryRef<Book>(this, shop.books, (book) => book.price > 10, (a, b) => a.name.localeCompare(b.name))

	protected override get template() {
		this.renders++
		return html`${this.query.items.map((book) => html`<li>${book.name}</li>`)}`
	}
}

@component("test-catalogue-host")
class CatalogueHost extends Component {
	public renders = 0
	public readonly ref = new DerivedRef(this, shop.catalogue)

	protected override get template() {
		this.renders++
		return html`${(this.ref.value ?? []).map((book) => html`<li>${book.name}</li>`)}`
	}
}

// --- The suites ---------------------------------------------------------------------------------------------

describe("EntityRef", () => {
	it("resolves the entity its host names, re-renders when it changes, and follows a changed identifier", async () => {
		const host = await mount<BookHost>("test-book-host", (element) => { element.bookId = "b1" })
		expect(host.ref.value?.name).toBe("The Dispossessed")
		expect(host.renderRoot.textContent).toBe("The Dispossessed")
		expect(shop.requests).toEqual(["GET /books/b1"])

		// An edit made anywhere — here, straight on the canonical instance — reaches the host.
		const renders = host.renders
		host.ref.value!.name = "The Dispossessed (revised)"
		shop.store.touch("Book:b1")
		await settle()
		expect(host.renders).toBeGreaterThan(renders)
		expect(host.renderRoot.textContent).toBe("The Dispossessed (revised)")

		host.bookId = "b2"
		await settle()
		expect(host.renderRoot.textContent).toBe("The Lathe of Heaven")
		expect(shop.requests).toEqual(["GET /books/b1", "GET /books/b2"])
	})

	it("hands two hosts of one identity the same instance", async () => {
		const first = await mount<BookHost>("test-book-host", (element) => { element.bookId = "b1" })
		const second = await mount<BookHost>("test-book-host", (element) => { element.bookId = "b1" })
		expect(second.ref.value).toBe(first.ref.value)
		// Served from the store while fresh: one request for both.
		expect(shop.requests).toEqual(["GET /books/b1"])
	})

	it("says the entity is removed once the shop answers that there is no such book", async () => {
		const host = await mount<BookHost>("test-book-host", (element) => { element.bookId = "b1" })
		expect(host.ref.removed).toBe(false)

		shop.delete("b1")
		await host.ref.refresh()
		await settle()

		expect(host.ref.removed).toBe(true)
		expect(host.ref.value).toBeUndefined()
		expect(host.ref.notLoaded).toBe(false)
		expect(host.renderRoot.textContent).toBe("")
	})

	it("says the entity is not loaded — not removed — while the shop cannot be asked", async () => {
		shop.down = true
		const host = await mount<BookHost>("test-book-host", (element) => { element.bookId = "b9" })

		expect(host.ref.loading).toBe(false)
		expect(host.ref.notLoaded).toBe(true)
		expect(host.ref.removed).toBe(false)
		expect(host.ref.error).toBeUndefined()
	})

	it("stops observing when its host leaves the document", async () => {
		const host = await mount<BookHost>("test-book-host", (element) => { element.bookId = "b1" })
		expect(shop.store.hasSubscribers("Book:b1")).toBe(true)

		host.remove()
		await settle()
		expect(shop.store.hasSubscribers("Book:b1")).toBe(false)
	})
})

describe("EntityRef.binder — the edit cycle through a real two-way binding", () => {
	it("writes an edit through to the canonical instance, broadcasts it, persists just that field, and reacts", async () => {
		const editor = await mount<BookEditor>("test-book-editor", (element) => { element.bookId = "b1" })
		const viewer = await mount<BookHost>("test-book-host", (element) => { element.bookId = "b1" })
		expect(editor.input.value).toBe("The Dispossessed")
		shop.requests.length = 0

		editor.input.value = "The Dispossessed, annotated"
		editor.input.dispatchEvent(new Event("change"))
		await settle()

		// On the canonical instance, hence on every other surface, before the shop was even asked.
		expect(editor.entity?.name).toBe("The Dispossessed, annotated")
		expect(viewer.renderRoot.textContent).toBe("The Dispossessed, annotated")
		// The '*' fallback was told which field changed, and sent only that; the write then revalidated the observed
		// entity, as every accepted write does.
		expect(shop.requests).toEqual(["PATCH /books/b1 name=\"The Dispossessed, annotated\"", "GET /books/b1"])
		expect(editor.reactions).toEqual(["name:true"])
		expect(rejections).toBe(0)
	})

	it("rolls a rejected edit back on every surface, and tells the host through its hook rather than a built-in notice", async () => {
		const editor = await mount<BookEditor>("test-book-editor", (element) => { element.bookId = "b1" })
		const viewer = await mount<BookHost>("test-book-host", (element) => { element.bookId = "b1" })
		shop.rejectWrites = true

		editor.input.value = "Not this"
		editor.input.dispatchEvent(new Event("change"))
		await settle()

		expect(editor.entity?.name).toBe("The Dispossessed")
		expect(viewer.renderRoot.textContent).toBe("The Dispossessed")
		expect(editor.input.value).toBe("The Dispossessed")
		expect(editor.reactions).toEqual(["name:false"])
		expect(rejections).toBe(1)
	})
})

describe("EntityWatch", () => {
	it("re-renders its host when the entity it was handed changes, without fetching anything", async () => {
		const book = (await shop.books.get("b1"))!
		const host = await mount<WatchHost>("test-watch-host", (element) => { element.book = book })
		expect(host.renderRoot.textContent).toBe("The Dispossessed")
		const renders = host.renders

		book.name = "Renamed elsewhere"
		host.watch.publish()
		await settle()

		expect(host.renders).toBeGreaterThan(renders)
		expect(host.renderRoot.textContent).toBe("Renamed elsewhere")
		expect(shop.requests).toEqual(["GET /books/b1"])
	})

	it("knows the instance it holds was removed, and says so on the host as an attribute", async () => {
		const book = (await shop.books.get("b1"))!
		const host = await mount<WatchHost>("test-watch-host", (element) => { element.book = book })
		expect(host.watch.removed).toBe(false)
		expect(host.hasAttribute("entity-removed")).toBe(false)

		shop.store.remove("Book:b1")
		await settle()

		expect(host.watch.removed).toBe(true)
		expect(host.hasAttribute("entity-removed")).toBe(true)
	})
})

describe("QueryRef", () => {
	it("is a live, ordered, filtered slice of what the store holds of one type", async () => {
		await shop.catalogue.get()
		const host = await mount<QueryHost>("test-query-host")
		expect(host.query.items.map((book) => book.name)).toEqual(["The Dispossessed"])
		const renders = host.renders

		// A member's field changes and it enters the predicate; the host learns through the type, not the member.
		const cheap = shop.store.peek<Book>("Book:b2")!
		cheap.price = 11
		shop.store.touch("Book:b2")
		await settle()

		expect(host.renders).toBeGreaterThan(renders)
		expect(host.query.items.map((book) => book.name)).toEqual(["The Dispossessed", "The Lathe of Heaven"])
	})
})

describe("DerivedRef", () => {
	it("resolves a record, re-renders when it is refetched, and says when it could not be answered for", async () => {
		const host = await mount<CatalogueHost>("test-catalogue-host")
		expect(host.ref.value).toHaveLength(2)
		expect(host.ref.notLoaded).toBe(false)
		expect(host.renderRoot.querySelectorAll("li")).toHaveLength(2)

		shop.delete("b2")
		await host.ref.refresh()
		await settle()
		expect(host.renderRoot.querySelectorAll("li")).toHaveLength(1)
	})

	it("is not loaded, not failed, while the shop cannot be asked", async () => {
		shop.down = true
		const host = await mount<CatalogueHost>("test-catalogue-host")

		expect(host.ref.loading).toBe(false)
		expect(host.ref.notLoaded).toBe(true)
		expect(host.ref.error).toBeUndefined()
		expect(host.ref.value).toBeUndefined()
	})
})
