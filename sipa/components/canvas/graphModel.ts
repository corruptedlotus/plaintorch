import { DependencyConstraint, DependencyEndpointKind, DependencyOrigin, DependencyTrigger, type Dependency, type EndpointRef } from '@pleiades/sdk'

/** The minimum a canvas asks of whatever entity a node stands for. */
export interface CanvasEntity {
	readonly id: string
	readonly title: string
}

/** One node of the graph: an entity, addressed the way a dependency endpoint addresses it. */
export interface CanvasNode {
	/** Stable across rebuilds, so Lit keeps the same element — and its subscription — for the same node. */
	readonly key: string
	readonly ref: EndpointRef
	readonly entity: CanvasEntity
	/**
	 * A node the context does not itself contain, pulled in only because it blocks something that is — an
	 * out-of-onrush prerequisite (PEP102). Drawn faintly, never removable, and gone on its own once the block
	 * it explains is resolved.
	 */
	readonly ghostly?: boolean
	/** The checkpoint that stands for the onrush's completion (PEP102). At most one, never removable. */
	readonly milestone?: boolean
}

/**
 * The runtime type names under which the store may track an entity of each endpoint kind, for resolution.
 *
 * A directive endpoint is a stellar directive (the search only offers those), and the core stamps the
 * *concrete* type name — `StellarDirective`, never the base `Directive` — so that is what the store keys on.
 * A lunar one is listed too, since an edge written elsewhere could name one. Looking `Directive` up was how a
 * pinned directive stayed unresolved and drew its PUCK for a title.
 */
export function endpointTypeNames(kind: DependencyEndpointKind): readonly string[] {
	switch (kind) {
		case DependencyEndpointKind.Directive: return ['StellarDirective', 'LunarDirective']
		case DependencyEndpointKind.Objective: return ['Objective']
		case DependencyEndpointKind.Fate: return ['Fate']
		case DependencyEndpointKind.Checkpoint: return ['Checkpoint']
		// An eventive names its owner under its own id but a different type, which cannot be told apart here.
		default: return []
	}
}

/** One dependency edge, resolved onto the nodes it joins. */
export interface CanvasEdge {
	/** Stable across rebuilds: a stored edge's own database identity, or an intrinsic edge's two ends. */
	readonly key: string
	readonly dependency: Dependency
	/** Key of the prerequisite node — the end that blocks. */
	readonly source: string
	/** Key of the dependant node — the end that is blocked. */
	readonly target: string
}

/** A resolved graph: what a context mode hands to the layout. */
export interface CanvasGraph {
	readonly nodes: readonly CanvasNode[]
	readonly edges: readonly CanvasEdge[]
}

/**
 * Identifies a node, and therefore an endpoint.
 *
 * Kind is part of the identity rather than decoration: the core addresses an occurrence of a fate as an eventive
 * carrying that fate's own id, so the id alone does not tell two endpoints apart.
 */
export function endpointKey(ref: EndpointRef): string {
	const kind = DependencyEndpointKind[ref.kind]
	// Nullish, not strictly undefined: the core writes an absent optional as `null` rather than omitting it,
	// so an edge endpoint arrives with `recurrenceId: null` where a node built in the client has the field
	// missing. Comparing against undefined alone would give the two the same endpoint two different keys, and
	// every edge would be dropped for connecting nodes that, by their keys, do not exist.
	const slot = ref.recurrenceId ?? undefined
	return slot === undefined ? `${kind}:${ref.id}` : `${kind}:${ref.id}@${slot}`
}

/**
 * Reads an edge's endpoints from its flattened columns rather than its {@link Dependency.source} getter, so a
 * payload that reached the surface without being constructed as a model still resolves.
 */
export function sourceRef(dependency: Dependency): EndpointRef {
	return {
		kind: dependency.sourceKind,
		id: dependency.sourceId,
		// null → undefined: the core writes an absent recurrence as null, and the rest of the module — keys,
		// cycle checks, create requests — reads it as optional, not nullable.
		recurrenceId: dependency.sourceRecurrenceId ?? undefined
	}
}

/** @see sourceRef */
export function targetRef(dependency: Dependency): EndpointRef {
	return {
		kind: dependency.targetKind,
		id: dependency.targetId,
		recurrenceId: dependency.targetRecurrenceId ?? undefined
	}
}

/**
 * Selects the edges that run between nodes the context holds.
 *
 * An edge with one end outside the context is dropped rather than drawn to nowhere — what it connects to is
 * not on screen, and inventing a node for it would put an entity into a context that deliberately excludes it.
 */
export function resolveEdges(dependencies: readonly Dependency[], nodeKeys: ReadonlySet<string>): CanvasEdge[] {
	const edges: CanvasEdge[] = []
	for (const dependency of dependencies) {
		const source = endpointKey(sourceRef(dependency))
		const target = endpointKey(targetRef(dependency))
		if (nodeKeys.has(source) && nodeKeys.has(target)) {
			// An intrinsic edge has no identity of its own; its two ends are what make it the same edge next time.
			const key = isIntrinsic(dependency) ? `intrinsic:${source}->${target}` : `dependency:${dependency.id}`
			edges.push({ key, dependency, source, target })
		}
	}

	return edges
}

/** Whether an edge is implied by the structure rather than drawn (PEP102 patch 9): dashed, and not up for editing. */
export function isIntrinsic(dependency: Dependency): boolean {
	return dependency.origin === DependencyOrigin.Intrinsic
}

/** A side in the ordering walk: 0 a work item's begin, 1 its finish, 2 a checkpoint's unlock (its single point). */
type Side = 0 | 1 | 2

function triggerSide(ref: EndpointRef, trigger: DependencyTrigger | undefined): Side {
	return ref.kind === DependencyEndpointKind.Checkpoint ? 2 : (trigger ?? DependencyTrigger.OnFinish) === DependencyTrigger.OnBegin ? 0 : 1
}

function constraintSide(ref: EndpointRef, constraint: DependencyConstraint | undefined): Side {
	return ref.kind === DependencyEndpointKind.Checkpoint ? 2 : (constraint ?? DependencyConstraint.ToBegin) === DependencyConstraint.ToFinish ? 1 : 0
}

/**
 * Determines whether a prospective edge would contradict the begin/finish ordering the edges already impose.
 *
 * Mirrors `DependencyOrdering.FindContradiction` in the core, which is the authority — this only spares the
 * round-trip, and refuses the gesture where it happens rather than after a write comes back rejected. The new edge
 * asserts the source's triggering side comes before the target's constrained side; it is impossible when, walking
 * forward from the target's constrained side — along each edge from its source's triggering side to its target's
 * constrained side, and from any node's begin to its own finish — the source's triggering side is already reachable.
 * A checkpoint is a single point, its unlock, which edges both enter and leave, so a loop through it is refused like
 * one through a work item (I-034). Strictly finer than node-level acyclicity: a loop whose begins and
 * finishes order out (a directive beginning before its own child begins, say) is allowed, and with the intrinsic
 * edges on the canvas that distinction is the everyday case.
 */
export function wouldContradict(
	edges: readonly CanvasEdge[],
	source: EndpointRef,
	target: EndpointRef,
	trigger: DependencyTrigger | undefined = undefined,
	constraint: DependencyConstraint | undefined = undefined
): boolean {
	const originSide = triggerSide(source, trigger)
	const seedSide = constraintSide(target, constraint)

	const stateKey = (node: string, side: Side) => `${node}#${side}`
	const origin = stateKey(endpointKey(source), originSide)
	const outgoing = new Map<string, { node: string, side: Side }[]>()
	for (const edge of edges) {
		const from = triggerSide(sourceRef(edge.dependency), edge.dependency.trigger ?? undefined)
		const to = constraintSide(targetRef(edge.dependency), edge.dependency.constraint ?? undefined)
		const key = stateKey(edge.source, from)
		const known = outgoing.get(key)
		const next = { node: edge.target, side: to }
		if (known) {
			known.push(next)
		}
		else {
			outgoing.set(key, [next])
		}
	}

	const seed = { node: endpointKey(target), side: seedSide }
	const visited = new Set<string>([stateKey(seed.node, seed.side)])
	const pending = [seed]
	while (pending.length > 0) {
		const current = pending.pop()!
		const key = stateKey(current.node, current.side)
		if (key === origin) {
			return true
		}

		for (const next of outgoing.get(key) ?? []) {
			if (visited.add(stateKey(next.node, next.side))) {
				pending.push(next)
			}
		}

		if (current.side === 0 && visited.add(stateKey(current.node, 1))) {
			pending.push({ node: current.node, side: 1 })
		}
	}

	return false
}

/**
 * The transitive unmet prerequisites of each root, walked to a fixpoint.
 *
 * Only the edges that still block are followed — a `satisfied` edge is left out of the adjacency, so the walk
 * stops at a met dependency: a prerequisite already satisfied holds nothing back, and whatever blocked *it* is
 * no longer part of what blocks the root. The adjacency runs dependant → prerequisite, against the forward walk of
 * {@link wouldContradict}, so from a member the walk reaches the blocker, then the blocker's own blocker, and
 * on down the unmet chain.
 *
 * Returned per root, so a surface can attribute each prerequisite to the member it ultimately holds back (the
 * briefing card groups by it); a graph that only wants the whole set unions the values. Deduplicated within a
 * root by endpoint key, and guarded by a per-root visited set against a cycle the core is meant to forbid.
 */
export function unresolvedPrerequisites(
	dependencies: readonly Dependency[],
	roots: ReadonlySet<string>
): Map<string, EndpointRef[]> {
	const blockers = new Map<string, EndpointRef[]>()
	for (const dependency of dependencies) {
		if (dependency.satisfied) {
			continue
		}

		const target = endpointKey(targetRef(dependency))
		const known = blockers.get(target)
		if (known) {
			known.push(sourceRef(dependency))
		}
		else {
			blockers.set(target, [sourceRef(dependency)])
		}
	}

	const reachable = new Map<string, EndpointRef[]>()
	for (const root of roots) {
		const collected = new Map<string, EndpointRef>()
		const visited = new Set<string>([root])
		const pending = [root]
		while (pending.length > 0) {
			const current = pending.pop()!
			for (const source of blockers.get(current) ?? []) {
				const key = endpointKey(source)
				if (!collected.has(key)) {
					collected.set(key, source)
				}

				// Descend into each prerequisite once, so its own unmet prerequisites join the chain. (`Set.add` returns the
				// set, not whether it grew, so membership is tested first — else a loop of prerequisites never ends.)
				if (!visited.has(key)) {
					visited.add(key)
					pending.push(key)
				}
			}
		}

		if (collected.size > 0) {
			reachable.set(root, [...collected.values()])
		}
	}

	return reachable
}

/**
 * The keys of each root's transitive unmet prerequisites that hold it back *now*: those reached from the root through
 * edges that gate their target's next lifecycle transition alone (the core's {@link Dependency.gatesNextTransition}).
 * The rest of {@link unresolvedPrerequisites} is still in the way, but of a later transition, or of one a link in the
 * chain has already made — which a list draws faint.
 */
export function immediatePrerequisites(dependencies: readonly Dependency[], roots: ReadonlySet<string>): Map<string, Set<string>> {
	const next = dependencies.filter(dependency => dependency.gatesNextTransition)
	const immediate = new Map<string, Set<string>>()
	for (const [root, refs] of unresolvedPrerequisites(next, roots)) {
		immediate.set(root, new Set(refs.map(endpointKey)))
	}

	return immediate
}

/** One side of an entity's {@link DependencyRoute}: its unmet edges that way, and the entities at their far ends. */
export interface RouteSide {
	/** The unmet dependency edges on this side, the intrinsic ones included. */
	readonly dependencies: readonly Dependency[]
	/** The endpoints at the far ends of the stored edges, once each, the ones gating a next transition first. */
	readonly endpoints: readonly EndpointRef[]
	/** Keys of the {@link endpoints} none of whose edges here gates a next transition — in the way, but not now. */
	readonly nonImmediate: ReadonlySet<string>
	/**
	 * How many of this side's unmet edges are intrinsic (PEP102 patch 9) — on a directive's blocked-by side, its open
	 * children. Counted rather than listed: the children are the directive's own contents, and a list of them would
	 * only repeat what its grid already shows.
	 */
	readonly intrinsic: number
}

/** An entity's place in the dependency graph, as its banner reads it: what blocks it, what it blocks. */
export interface DependencyRoute {
	/** The unmet dependencies blocking the entity in any way, by their sources. */
	readonly blockedBy: RouteSide
	/** The unmet dependencies the entity blocks, by their targets. */
	readonly blocks: RouteSide
	/** Whether anything blocks the entity's own next lifecycle transition. */
	readonly nextBlocked: boolean
}

/**
 * Reads an entity's route from the whole edge set: its unmet incoming dependencies (what blocks it, in any way) and its
 * unmet outgoing ones (what it blocks). Only the entity's own edges count — no walk down the chain — and an edge on one
 * of its occurrences addresses that occurrence, not the entity. Whether an edge holds its target back now is the core's
 * {@link Dependency.gatesNextTransition}, so it reads the same on both sides.
 */
export function dependencyRoute(dependencies: readonly Dependency[], endpoint: EndpointRef): DependencyRoute {
	const key = endpointKey(endpoint)
	const incoming = dependencies.filter(dependency => !dependency.satisfied && endpointKey(targetRef(dependency)) === key)
	const outgoing = dependencies.filter(dependency => !dependency.satisfied && endpointKey(sourceRef(dependency)) === key)
	return {
		blockedBy: routeSide(incoming, sourceRef),
		blocks: routeSide(outgoing, targetRef),
		nextBlocked: incoming.some(dependency => dependency.gatesNextTransition),
	}
}

function routeSide(dependencies: readonly Dependency[], farEnd: (dependency: Dependency) => EndpointRef): RouteSide {
	const endpoints = new Map<string, { ref: EndpointRef, immediate: boolean }>()
	let intrinsic = 0
	for (const dependency of dependencies) {
		if (isIntrinsic(dependency)) {
			intrinsic++
			continue
		}

		const ref = farEnd(dependency)
		const key = endpointKey(ref)
		const known = endpoints.get(key)
		endpoints.set(key, { ref, immediate: (known?.immediate ?? false) || dependency.gatesNextTransition })
	}

	const ordered = [...endpoints.entries()].sort(([, a], [, b]) => Number(b.immediate) - Number(a.immediate))
	return {
		dependencies,
		endpoints: ordered.map(([, entry]) => entry.ref),
		nonImmediate: new Set(ordered.filter(([, entry]) => !entry.immediate).map(([key]) => key)),
		intrinsic,
	}
}

/** The trigger an edge acts on, resolving the empty value the core leaves for a checkpoint source. */
export function effectiveTrigger(dependency: Dependency): DependencyTrigger {
	return dependency.trigger ?? DependencyTrigger.OnFinish
}

/** The constraint an edge gates, resolving the empty value the core leaves for a checkpoint target. */
export function effectiveConstraint(dependency: Dependency): DependencyConstraint {
	return dependency.constraint ?? DependencyConstraint.ToBegin
}

/** Names what an edge does, in the terms the dependency system uses. */
export function describeEdge(dependency: Dependency): string {
	if (isIntrinsic(dependency)) {
		return 'child blocks the finish until it finishes'
	}

	const trigger = effectiveTrigger(dependency) === DependencyTrigger.OnBegin ? 'begins' : 'finishes'
	const constraint = effectiveConstraint(dependency) === DependencyConstraint.ToFinish ? 'finish' : 'begin'
	return `blocks the ${constraint} until it ${trigger}`
}
