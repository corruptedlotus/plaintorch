// @ts-nocheck
// Vendored from @pleiades/orbits (orbit-scheduler, packages/node/src/cycles.ts).
// Kept verbatim so it stays in sync with upstream; edit upstream and re-vendor
// rather than diverging here. @ts-nocheck keeps the plugin's strict tsconfig off
// upstream code (mirrors assets/icons/index.ts).

// cycles.ts
import type { ASTNode, TimeUnit } from './ast'

// The cycle each unit naturally recurs in. A year has none: it is the largest unit, and it never recurs.
export const IMPLICIT_CYCLE: Record<TimeUnit, TimeUnit | null> = { s: 'm', m: 'h', h: 'd', d: 'M', w: 'y', M: 'y', y: null }

// Binds every indexed node written at the top level (the root, or an operand of a top-level set operation) to its
// implicit cycle: `m{15}` is `h[m{15}]`, minute 15 of every hour; `h{11}` is `d[h{11}]`, every day at 11:00; `w{12}` is
// `y[w{12}]`, calendar week 12. A `z{h:m}` literal is an indexed hour, so it binds to the day; a `Z{…}` literal is
// rooted at its year and stays as written. It is a plain rewrite, so an indexed global reads in every way as if its
// cycle had been written around it: its limits count per cycle, and the schedule starts at the cycle's first complete
// period. A bare global (`d`, `w[d{1}]`) is not indexed and stays as written.
export function bindImplicitCycles(node: ASTNode): ASTNode {
	if (node.kind === 'SetOperationNode') {
		return { ...node, left: bindImplicitCycles(node.left), right: bindImplicitCycles(node.right) }
	}
	const unit: TimeUnit | undefined = node.kind === 'DateTimeLiteralNode'
		? (node.year !== undefined ? 'y' : 'h')
		: (node.indices ? node.unit : undefined)
	const cycle = unit === undefined ? null : IMPLICIT_CYCLE[unit]
	return cycle ? { kind: 'TimeUnitNode', unit: cycle, limits: [], child: node } : node
}
