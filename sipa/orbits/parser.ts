// @ts-nocheck
// Vendored from @pleiades/orbits (orbit-scheduler, packages/node/src/parser.ts).
// Kept verbatim so it stays in sync with upstream; edit upstream and re-vendor
// rather than diverging here. @ts-nocheck keeps the plugin's strict tsconfig off
// upstream code (mirrors assets/icons/index.ts).

// parser.ts
import { ASTNode, TimeUnitNode, DateTimeLiteralNode, IndexSpec, TimeUnit, DurationPart, CalendarMoment } from './ast'

const TIME_UNITS = ['y', 'M', 'w', 'd', 'h', 'm', 's']

export class OrbitParser {
	private pos = 0
	private input = ''

	constructor(input: string) {
		this.input = input
	}

	public parse(): ASTNode {
		this.pos = 0
		const result = this.parseExpression()
		this.skipWhitespace()
		if (this.pos < this.input.length) {
			throw new Error(`Unexpected character at position ${this.pos}: '${this.input[this.pos]}'`)
		}
		return result
	}

	// Parses + and - (Union, Exclusion)
	private parseExpression(): ASTNode {
		let node = this.parseTerm()
		this.skipWhitespace()

		while (this.pos < this.input.length) {
			const char = this.peek()
			if (char === '+' || char === '-') {
				this.consume()
				const right = this.parseTerm()
				node = { kind: 'SetOperationNode', operator: char, left: node, right }
				this.skipWhitespace()
			} else {
				break
			}
		}
		return node
	}

	// Parses & and ^ (Intersection, Difference - higher precedence)
	private parseTerm(): ASTNode {
		let node = this.parseFactor()
		this.skipWhitespace()

		while (this.pos < this.input.length) {
			const char = this.peek()
			if (char === '&' || char === '^') {
				this.consume()
				const right = this.parseFactor()
				node = { kind: 'SetOperationNode', operator: char, left: node, right }
				this.skipWhitespace()
			} else {
				break
			}
		}
		return node
	}

	// Parses parentheses or a base Time Unit Node
	private parseFactor(): ASTNode {
		this.skipWhitespace()
		if (this.match('(')) {
			const node = this.parseExpression()
			if (!this.match(')')) throw new Error(`Expected ')' at position ${this.pos}`)
			return node
		}
		return this.parseNode()
	}

	// Core parsing logic for a TimeUnit node and its modifiers
	private parseNode(): ASTNode {
		this.skipWhitespace()
		const char = this.peek()

		// Handle Shorthand
		if (char === 'z') {
			this.consume()
			return this.parseTimeLiteral()
		}

		// Fixed-datetime shorthand: Z{y/M/d[Th:m[:s]]}
		if (char === 'Z') {
			this.consume()
			return this.parseDateTimeLiteral()
		}

		// Standard Time Unit
		if (!['y', 'M', 'w', 'd', 'h', 'm', 's'].includes(char)) {
			throw new Error(`Expected time unit at position ${this.pos}, got '${char}'`)
		}

		const unit = this.consume() as TimeUnit
		let node: TimeUnitNode = { kind: 'TimeUnitNode', unit, limits: [] }

		// 1. Indexing
		this.skipWhitespace()
		if (this.match('{')) {
			node.indices = this.parseIndexSpec()
		}

		// 2. Child Nodes
		this.skipWhitespace()
		if (this.match('[')) {
			node.child = this.parseExpression() // allows robust nesting like d[h{1}+h{3}]
			if (!this.match(']')) throw new Error(`Expected ']' at position ${this.pos}`)
		}

		// 3. Modifiers (Intervals and Limits)
		this.parseModifiers(node)

		return node
	}

	// z{h:m[:s]} -> a time-of-day literal (no date part; recurs every day at that time).
	private parseTimeLiteral(): DateTimeLiteralNode {
		if (!this.match('{')) throw new Error(`Expected '{' after 'z' shorthand`)

		const hour = this.parseNumber()
		if (!this.match(':')) throw new Error(`Expected ':' inside 'z' shorthand`)
		const minute = this.parseNumber()

		let second: number | undefined
		if (this.match(':')) {
			second = this.parseNumber()
		}
		if (!this.match('}')) throw new Error(`Expected '}' closing 'z' shorthand`)

		const node: DateTimeLiteralNode = { kind: 'DateTimeLiteralNode', hour, minute, second, limits: [] }
		// The literal carries the modifiers the shorthand used to hang on its root 'h'
		// (e.g. z{12:00}%2 -> interval 2, z{12:00}=2h -> a 2h span from 12:00).
		this.parseModifiers(node)
		return node
	}

	// Z{y/M/d[Th:m[:s]]} -> a fixed calendar datetime literal (one exact moment). The
	// time part is optional; the deepest present component fixes the granularity.
	private parseDateTimeLiteral(): DateTimeLiteralNode {
		if (!this.match('{')) throw new Error(`Expected '{' after 'Z' shorthand`)
		const { year, month, day, hour, minute, second } = this.parseMoment(`'Z' shorthand`)
		if (!this.match('}')) throw new Error(`Expected '}' closing 'Z' shorthand`)

		const node: DateTimeLiteralNode = { kind: 'DateTimeLiteralNode', year, month, day, hour, minute, second, limits: [] }
		this.parseModifiers(node)
		return node
	}

	private parseIndexSpec(): IndexSpec {
		this.skipWhitespace()
		if (this.match('#')) {
			const count = this.parseNumber()
			if (!this.match('}')) throw new Error("Expected '}' after random count")
			return { type: 'random', count }
		}

		const first = this.parseNumber()
		this.skipWhitespace()

		if (this.match('~')) {
			const end = this.parseNumber()
			this.skipWhitespace()
			if (!this.match('}')) throw new Error("Expected '}' after range")
			return { type: 'range', start: first, end }
		}

		const values = [first]
		while (this.match(',')) {
			this.skipWhitespace()
			values.push(this.parseNumber())
		}

		this.skipWhitespace()
		if (!this.match('}')) throw new Error("Expected '}' after index list")

		return { type: 'list', values }
	}

	private parseModifiers(node: TimeUnitNode | DateTimeLiteralNode) {
		while (this.pos < this.input.length) {
			this.skipWhitespace()
			if (this.match('%')) {
				node.interval = this.parseNumber()
			} else if (this.match('*')) {
				node.limits.push({ type: 'iterations', count: this.parseNumber() })
			} else if (this.match('@')) {
				node.limits.push({ type: 'instances', count: this.parseNumber() })
			} else if (this.match('<')) {
				node.limits.push({ type: 'before', ...this.parseBound() })
			} else if (this.match('>')) {
				node.limits.push({ type: 'after', ...this.parseBound() })
			} else if (this.match('=')) {
				node.duration = this.parseDuration()
			} else {
				break // No more recognized modifiers
			}
		}
	}

	// A span duration: one or more <count><unit> parts, e.g. `2h`, `90m`, `1d6h`.
	private parseDuration(): DurationPart[] {
		const parts: DurationPart[] = []
		do {
			const count = this.parseNumber()
			const unit = this.peek()
			if (!TIME_UNITS.includes(unit)) {
				throw new Error(`Expected time unit in duration at position ${this.pos}, got '${unit}'`)
			}
			this.consume()
			parts.push({ unit: unit as TimeUnit, count })
		} while (this.pos < this.input.length && /[0-9]/.test(this.peek()))
		return parts
	}

	// --- Utility Methods ---

	private peek() {
		return this.input[this.pos]!
	}

	private consume() {
		return this.input[this.pos++]!
	}

	private match(char: string): boolean {
		if (this.peek() === char) {
			this.pos++
			return true
		}
		return false
	}

	private parseNumber(): number {
		let str = ''
		while (this.pos < this.input.length && /[0-9]/.test(this.peek())) {
			str += this.consume()
		}
		if (str === '') throw new Error(`Expected number at position ${this.pos}`)
		return parseInt(str, 10)
	}

	// y/M/d[Th:m[:s]] -> a calendar date, as Z{…} writes one and a bound does.
	private parseMoment(context: string): CalendarMoment {
		const year = this.parseNumber()
		if (!this.match('/')) throw new Error(`Expected '/' after year in ${context}`)
		const month = this.parseNumber()
		if (!this.match('/')) throw new Error(`Expected '/' after month in ${context}`)
		const day = this.parseNumber()

		const moment: CalendarMoment = { year, month, day }
		if (this.match('T')) {
			moment.hour = this.parseNumber()
			if (!this.match(':')) throw new Error(`Expected ':' in the time part of ${context}`)
			moment.minute = this.parseNumber()
			if (this.match(':')) moment.second = this.parseNumber()
		}
		return moment
	}

	// <t / >t -> a bound, a calendar date written as Z{…} writes one (`>2026/7/15`, `<2026/7/15T12:30`). An ISO
	// timestamp (`>2026-07-15`, `>2026-07-15T12:30:00Z`), the older form, still reads as a UTC instant; it ends with its
	// last ISO field, so a '-' right after it is the set difference.
	private parseBound(): { timestamp: string; moment?: CalendarMoment } {
		const start = this.pos
		const iso = /^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?/.exec(this.input.slice(start))
		if (iso) {
			this.pos += iso[0].length
			return { timestamp: iso[0] }
		}
		const moment = this.parseMoment('a bound')
		return { timestamp: this.input.slice(start, this.pos), moment }
	}

	private skipWhitespace() {
		while (this.pos < this.input.length && /\s/.test(this.peek())) {
			this.pos++
		}
	}
}