import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { EntityStore } from "./entityStore.js"
import { RepositorySet } from "./repositorySet.js"
import { ChangeFeed } from "./changeFeed.js"
import { entityKey } from "./identity.js"
import { unresolved } from "./outcome.js"
import type { EntityPolicy } from "./policy.js"

// The acceptance test of the separation: a consumer whose domain shares nothing with PLAINTORCH's runs the
// whole cycle — read, write, feed, removal, eviction — through the generic pieces alone. Its entities carry
// no `title` and no `@type`, each type names its identifier differently, its models are plain objects, and
// its backend answers writes with nothing at all. Everything domain-specific below is the consumer's own.

// --- The bookshop ---------------------------------------------------------------------------------------------

interface Book { kind: "book", isbn: string, name: string, price: number, authors: Author[] }
interface Author { kind: "author", slug: string, name: string, books: Book[] }

/** The one piece of the policy every consumer must write: what an entity is, and under which identity. */
const policy: EntityPolicy = {
	identify: (value) => {
		const record = value as { kind?: unknown, isbn?: unknown, slug?: unknown } | null
		if (record?.kind === "book" && typeof record.isbn === "string") {
			return entityKey("Book", record.isbn)
		}

		return record?.kind === "author" && typeof record.slug === "string" ? entityKey("Author", record.slug) : undefined
	},
	// The shop serializes a navigation it did not load as an empty collection, so an empty collection on a
	// *nested* entity says "not loaded", not "none" — and must not merge over one a direct read populated.
	mergeableKeys: (payload, isRoot) => Object.keys(payload).filter((field) => isRoot || !(Array.isArray(payload[field]) && payload[field].length === 0))
}

/** A tiny backend: an in-memory table behind HTTP-shaped answers, and a feed it announces changes on. */
class Bookshop {
	public down = false
	public readonly requests: string[] = []
	private readonly books = new Map<string, Omit<Book, "authors"> & { authors: string[] }>()
	private readonly authors = new Map<string, Omit<Author, "books">>()
	private readonly feed: string[] = []
	private wake?: () => void

	public constructor() {
		this.authors.set("le-guin", { kind: "author", slug: "le-guin", name: "Ursula K. Le Guin" })
		this.books.set("978-0", { kind: "book", isbn: "978-0", name: "The Dispossessed", price: 12, authors: ["le-guin"] })
		this.books.set("978-1", { kind: "book", isbn: "978-1", name: "The Lathe of Heaven", price: 9, authors: ["le-guin"] })
	}

	/** Answers a GET: the serialized graph, 404 for an unknown address, 503 while the shop is down. */
	public get(path: string): { status: number, body?: string } {
		this.requests.push(`GET ${path}`)
		if (this.down) {
			return { status: 503 }
		}

		const [, collection, id] = path.split("/")
		if (collection === "books" && id === undefined) {
			return { status: 200, body: JSON.stringify([...this.books.keys()].map((isbn) => this.book(isbn))) }
		}

		if (collection === "books" && this.books.has(id!)) {
			return { status: 200, body: JSON.stringify(this.book(id!)) }
		}

		if (collection === "authors" && this.authors.has(id!)) {
			return { status: 200, body: JSON.stringify(this.author(id!)) }
		}

		return { status: 404 }
	}

	/** Answers a PATCH with nothing — a 204 — and announces the change. */
	public patch(isbn: string, change: Partial<Book>): { status: number } {
		this.requests.push(`PATCH /books/${isbn}`)
		const book = this.books.get(isbn)
		if (this.down || !book) {
			return { status: this.down ? 503 : 404 }
		}

		Object.assign(book, change)
		this.announce({ type: "Book", id: isbn, operation: "Modified" })
		return { status: 204 }
	}

	public delete(isbn: string): void {
		this.books.delete(isbn)
		this.announce({ type: "Book", id: isbn, operation: "Deleted", critical: true })
	}

	/** The feed, as the lines of a server-sent event stream. */
	public async *stream(signal: AbortSignal): AsyncIterable<string> {
		while (!signal.aborted) {
			const line = this.feed.shift()
			if (line !== undefined) {
				yield line
				continue
			}

			await new Promise<void>((resolve) => { this.wake = resolve })
		}
	}

	private announce(event: object): void {
		this.feed.push(`data: ${JSON.stringify(event)}`, "")
		this.wake?.()
	}

	private book(isbn: string): Book {
		const book = this.books.get(isbn)!
		return { ...book, authors: book.authors.map((slug) => ({ ...this.authors.get(slug)!, books: [] })) }
	}

	private author(slug: string): Author {
		const author = this.authors.get(slug)!
		const books = [...this.books.values()].filter((book) => book.authors.includes(slug))
		return { ...author, books: books.map((book) => ({ ...book, authors: [] })) }
	}
}

/** The consumer's wiring: a store with its policy, reads through `store.read`, and a repository set. */
function bookshopClient(shop: Bookshop) {
	const store = new EntityStore(policy)

	// A GET as the consumer's client makes it: absorbed under a read, 404 is "nothing", anything else unreached.
	const read = async <T>(path: string) => await store.read(async (parse) => {
		const { status, body } = shop.get(path)
		if (status === 200) {
			return parse<T>(body!)
		}

		return status === 404 ? undefined : unresolved
	})

	const repos = new RepositorySet(store, {
		// The shop answers writes with a 204, so nothing coming back is success here; only an explicit `false`
		// — the consumer's own way of reporting a refusal — is a rejection.
		succeeded: (result) => result !== false,
		// A book's author shows that book, so a change to a book makes its authors stale.
		dependents: { Book: (book) => (book.authors as Author[]).map((author) => entityKey("Author", author.slug)) }
	})
	const books = repos.entity<Book>("Book", (isbn) => read<Book>(`/books/${isbn}`))
	const authors = repos.entity<Author>("Author", (slug) => read<Author>(`/authors/${slug}`))
	const catalogue = repos.derived<Book[]>(() => read<Book[]>("/books"))
	const feed = new ChangeFeed({ open: async (signal) => shop.stream(signal) }, repos, { initialRetryDelayMs: 10 })

	return { store, repos, books, authors, catalogue, feed }
}

const settle = async () => await vi.advanceTimersByTimeAsync(0)

beforeEach(() => {
	vi.useFakeTimers()
})

afterEach(() => {
	vi.useRealTimers()
})

describe("a bookshop over the generic repository system", () => {
	it("reads entities of its own shape, and one identity is one instance across responses", async () => {
		const shop = new Bookshop()
		const { books, authors, store } = bookshopClient(shop)

		const book = (await books.get("978-0"))!
		const author = (await authors.get("le-guin"))!

		expect(book.name).toBe("The Dispossessed")
		expect(store.stateOf("Book:978-0")).toBe("loaded")
		// The author's listing of books and the book fetched on its own are the same instance.
		expect(author.books.find((candidate) => candidate.isbn === "978-0")).toBe(book)
		// And the book's own author is the canonical author.
		expect(book.authors[0]).toBe(author)
	})

	it("serves a fresh entity from the store, and asks the shop again once it is stale", async () => {
		const shop = new Bookshop()
		const { books, repos } = bookshopClient(shop)

		await books.get("978-0")
		await books.get("978-0")
		expect(shop.requests.filter((request) => request === "GET /books/978-0")).toHaveLength(1)

		repos.invalidateAll()
		await books.get("978-0")
		expect(shop.requests.filter((request) => request === "GET /books/978-0")).toHaveLength(2)
	})

	it("writes optimistically, counts the shop's empty answer as success, and revalidates what depends on it", async () => {
		const shop = new Bookshop()
		const { books, authors, repos } = bookshopClient(shop)
		const book = (await books.get("978-0"))!
		await authors.get("le-guin")
		authors.subscribe("le-guin", () => { })
		shop.requests.length = 0

		const snapshot = books.snapshot("978-0")
		book.price = 15
		const accepted = await books.commit("978-0", async () => shop.patch("978-0", { price: 15 }).status === 204 ? undefined : false, snapshot)
		await repos.invalidation.settled()

		expect(accepted).toBe(true)
		expect(book.price).toBe(15)
		// The observed author was refetched; the unobserved book was only marked stale.
		expect(shop.requests).toEqual(["PATCH /books/978-0", "GET /authors/le-guin"])
	})

	it("rolls a rejected write back", async () => {
		const shop = new Bookshop()
		const { books } = bookshopClient(shop)
		const book = (await books.get("978-0"))!

		const snapshot = books.snapshot("978-0")
		book.price = 15
		const accepted = await books.commit("978-0", async () => false, snapshot)

		expect(accepted).toBe(false)
		expect(book.price).toBe(12)
	})

	it("keeps what it holds while the shop is down, and recovers when it is back", async () => {
		const shop = new Bookshop()
		const { books, catalogue, store } = bookshopClient(shop)
		const book = (await books.get("978-0"))!
		const listed = (await catalogue.get())!
		let notified = 0
		catalogue.subscribe("", () => notified++)

		shop.down = true
		expect(await books.refresh("978-0")).toBe(book)
		expect(await catalogue.refresh()).toBe(listed)
		expect(store.stateOf("Book:978-0")).toBe("loaded")
		expect(notified).toBe(0)

		shop.down = false
		shop.patch("978-0", { price: 20 })
		expect((await books.refresh("978-0"))?.price).toBe(20)
		expect(book.price).toBe(20)
	})

	it("hears a change on the feed and refetches what is observed", async () => {
		const shop = new Bookshop()
		const { books, feed, repos } = bookshopClient(shop)
		const book = (await books.get("978-0"))!
		books.subscribe("978-0", () => { })
		feed.start()
		await settle()

		shop.patch("978-0", { name: "The Dispossessed (revised)" })
		await settle()
		await repos.invalidation.settled()

		expect(book.name).toBe("The Dispossessed (revised)")
		feed.stop()
	})

	it("a deletion announced on the feed removes the book, and its authors and the catalogue let go of it", async () => {
		const shop = new Bookshop()
		const { books, authors, catalogue, feed, repos, store } = bookshopClient(shop)
		const book = (await books.get("978-1"))!
		const author = (await authors.get("le-guin"))!
		const listed = (await catalogue.get())!
		books.subscribe("978-1", () => { })
		expect(listed).toHaveLength(2)
		expect(author.books).toContain(book)
		feed.start()
		await settle()

		shop.delete("978-1")
		await settle()
		await repos.invalidation.settled()

		expect(store.stateOf("Book:978-1")).toBe("removed")
		expect(store.wasRemoved(book)).toBe(true)
		expect(author.books.map((member) => member.isbn)).toEqual(["978-0"])
		expect(catalogue.peek()?.map((member) => member.isbn)).toEqual(["978-0"])
		feed.stop()
	})

	it("does not let a slow read revert an edit made while it was in flight", async () => {
		const shop = new Bookshop()
		const { books, store } = bookshopClient(shop)
		const book = (await books.get("978-0"))!

		// A refetch goes out; before it lands the user edits the price.
		let release!: () => void
		const pending = store.read(async (parse) => {
			await new Promise<void>((resolve) => { release = resolve })
			return parse<Book>(shop.get("/books/978-0").body!)
		})
		book.price = 99
		store.touch("Book:978-0")
		release()
		await pending

		expect(book.price).toBe(99)
	})

	it("evicts what nothing holds any more, and keeps what a live view reaches", async () => {
		const shop = new Bookshop()
		const { books, catalogue, repos, store } = bookshopClient(shop)
		await books.get("978-0")
		await catalogue.get()
		const release = catalogue.subscribe("", () => { })
		const held = store.recordCount
		expect(held).toBeGreaterThan(0)

		// Everything is reachable from the observed catalogue: the books, and the author nested in each.
		expect(repos.sweep()).toBe(0)

		// Nothing observes the catalogue any more and it has gone stale, so it no longer pins anything.
		release()
		catalogue.invalidate()
		expect(repos.sweep()).toBe(held)
		expect(store.recordCount).toBe(0)
	})
})
