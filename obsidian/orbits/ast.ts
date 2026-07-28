// Vendored from @pleiades/orbits (orbit-scheduler, packages/node/src/ast.ts).
// Kept verbatim so it stays in sync with upstream; edit upstream and re-vendor
// rather than diverging here. See orbits/index.ts for the plugin-facing helpers.

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

export type ASTNode = TimeUnitNode | SetOperationNode
