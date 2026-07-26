// @ts-nocheck
// Vendored from @pleiades/orbits (orbit-scheduler, packages/node/src/parser.ts).
// Kept verbatim so it stays in sync with upstream; edit upstream and re-vendor
// rather than diverging here. @ts-nocheck keeps the plugin's strict tsconfig off
// upstream code (mirrors assets/icons/index.ts).

import { ASTNode, TimeUnitNode, IndexSpec, TimeUnit, DurationPart } from './ast'

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
			return this.parseZShorthandModifiers()
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

	private parseZShorthandModifiers(): TimeUnitNode {
		if (!this.match('{')) throw new Error(`Expected '{' after 'z' shorthand`)

		const hIndex = this.parseNumber()
		if (!this.match(':')) throw new Error(`Expected ':' inside 'z' shorthand`)
		const mIndex = this.parseNumber()

		let sIndex: number | undefined
		if (this.match(':')) {
			sIndex = this.parseNumber()
		}
		if (!this.match('}')) throw new Error(`Expected '}' closing 'z' shorthand`)

		// Build nested z structure: h{A}[m{B}[s{C}]]
		const rootNode: TimeUnitNode = {
			kind: 'TimeUnitNode',
			unit: 'h',
			indices: { type: 'list', values: [hIndex] },
			limits: []
		}

		const mNode: TimeUnitNode = {
			kind: 'TimeUnitNode',
			unit: 'm',
			indices: { type: 'list', values: [mIndex] },
			limits: []
		}
		rootNode.child = mNode

		if (sIndex !== undefined) {
			mNode.child = {
				kind: 'TimeUnitNode',
				unit: 's',
				indices: { type: 'list', values: [sIndex] },
				limits: []
			}
		}

		// Shorthand gets modifiers applied to the root 'h' node (e.g. z{12:00}%2 -> interval 2 on 'h')
		this.parseModifiers(rootNode)

		return rootNode
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

	private parseModifiers(node: TimeUnitNode) {
		while (this.pos < this.input.length) {
			this.skipWhitespace()
			if (this.match('%')) {
				node.interval = this.parseNumber()
			} else if (this.match('*')) {
				node.limits.push({ type: 'iterations', count: this.parseNumber() })
			} else if (this.match('@')) {
				node.limits.push({ type: 'instances', count: this.parseNumber() })
			} else if (this.match('<')) {
				node.limits.push({ type: 'before', timestamp: this.parseTimestamp() })
			} else if (this.match('>')) {
				node.limits.push({ type: 'after', timestamp: this.parseTimestamp() })
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

	private parseTimestamp(): string {
		let str = ''
		// Timestamps read until they hit an operator or structural character.
		// NOTE: '-' is intentionally NOT a stop char so ISO dates like 2026-07-15
		// (and 2026-07-15T12:30:00Z) parse. A '-' exclusion directly after a
		// timestamp must therefore be whitespace-separated (e.g. "d>2026-01-01 - d{1}").
		const stopChars = new Set(['%', '*', '@', '<', '>', '=', '+', '&', '^', '[', ']', '(', ')', '{', '}'])
		while (
			this.pos < this.input.length &&
			!stopChars.has(this.peek()) &&
			!/\s/.test(this.peek())
		) {
			str += this.consume()
		}
		if (str === '') throw new Error(`Expected timestamp at position ${this.pos}`)
		return str
	}

	private skipWhitespace() {
		while (this.pos < this.input.length && /\s/.test(this.peek())) {
			this.pos++
		}
	}
}
