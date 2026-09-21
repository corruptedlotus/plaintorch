// @ts-nocheck
// Vendored from @pleiades/orbits (orbit-scheduler, packages/node/src/ast.ts).
// Kept verbatim so it stays in sync with upstream; edit upstream and re-vendor
// rather than diverging here. @ts-nocheck keeps the plugin's strict tsconfig off
// upstream code (mirrors assets/icons/index.ts).

// types.ts

export type TimeUnit = 'y' | 'M' | 'w' | 'd' | 'h' | 'm' | 's'
export type SetOperator = '+' | '&' | '^' | '-'

export type IndexSpec =
	| { type: 'list'; values: number[] }
	| { type: 'range'; start: number; end: number }
	| { type: 'random'; count: number }

export type LimitSpec =
	| { type: 'iterations'; count: number }  // *x
	| { type: 'instances'; count: number }   // @x
	| { type: 'before'; timestamp: string }  // <t
	| { type: 'after'; timestamp: string }  // >t

// One component of a span duration, e.g. `2h` -> { unit: 'h', count: 2 }.
export interface DurationPart {
	unit: TimeUnit;
	count: number;
}

export interface TimeUnitNode {
	kind: 'TimeUnitNode';
	unit: TimeUnit;
	indices?: IndexSpec;
	child?: ASTNode;
	interval?: number;
	limits: LimitSpec[]
	// `=<dur>` turns this occurrence into a span [start, start+dur). Its presence
	// makes the whole expression span-format (see OrbitEngine).
	duration?: DurationPart[]
}

export interface SetOperationNode {
	kind: 'SetOperationNode'
	operator: SetOperator;
	left: ASTNode;
	right: ASTNode;
}

// A literal moment written with the `z`/`Z` shorthand: `z{h:m[:s]}` is a daily
// time-of-day (no date part), `Z{y/M/d[Th:m[:s]]}` a fixed calendar datetime. It is
// kept as its own node so a humanizer can render it as a clock/date directly; the
// engine expands it into the equivalent nested TimeUnitNode chain (deepest present
// component sets the granularity) before resolving, so it needs no solver support.
// The modifier fields mirror TimeUnitNode: a literal accepts `%interval`, limits and
// `=<dur>` exactly as the `z` shorthand did.
export interface DateTimeLiteralNode {
	kind: 'DateTimeLiteralNode'
	year?: number;
	month?: number;
	day?: number;
	hour?: number;
	minute?: number;
	second?: number;
	interval?: number;
	limits: LimitSpec[]
	duration?: DurationPart[]
}

export type ASTNode = TimeUnitNode | SetOperationNode | DateTimeLiteralNode