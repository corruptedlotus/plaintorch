import { DependencyConstraint, DependencyEndpointKind, DependencyTrigger, type Dependency, type EndpointRef } from '@pleiades/sdk'

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
}

/** One dependency edge, resolved onto the nodes it joins. */
export interface CanvasEdge {
	/** Stable across rebuilds, from the edge's own database identity. */
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
 * Kind is part of the identity rather than decoration: the core addresses an occurrence of an objective as an
 * eventive carrying that objective's own id, so the id alone does not tell two endpoints apart.
 */
export function endpointKey(ref: EndpointRef): string {
	const kind = DependencyEndpointKind[ref.kind]
	if (ref.recurrenceDate === undefined && ref.recurrenceTime === undefined) {
		return `${kind}:${ref.id}`
	}

	return `${kind}:${ref.id}@${ref.recurrenceDate ?? ''}${ref.recurrenceTime === undefined ? '' : `T${ref.recurrenceTime}`}`
}

/**
 * Reads an edge's endpoints from its flattened columns rather than its {@link Dependency.source} getter, so a
 * payload that reached the surface without being constructed as a model still resolves.
 */
export function sourceRef(dependency: Dependency): EndpointRef {
	return {
		kind: dependency.sourceKind,
		id: dependency.sourceId,
		recurrenceDate: dependency.sourceRecurrenceDate,
		recurrenceTime: dependency.sourceRecurrenceTime
	}
}

/** @see sourceRef */
export function targetRef(dependency: Dependency): EndpointRef {
	return {
		kind: dependency.targetKind,
		id: dependency.targetId,
		recurrenceDate: dependency.targetRecurrenceDate,
		recurrenceTime: dependency.targetRecurrenceTime
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
			edges.push({ key: `dependency:${dependency.id}`, dependency, source, target })
		}
	}

	return edges
}

/**
 * Determines whether joining two nodes would close a loop.
 *
 * Mirrors `DependencyRules.EnsureNoCycle` in the core, which is the authority — this only spares the
 * round-trip, and refuses the gesture where it happens rather than after a write comes back rejected. The
 * adjacency runs dependant → prerequisite, so the new edge closes a loop exactly when the prospective target
 * is already a prerequisite of the prospective source.
 */
export function wouldCycle(edges: readonly CanvasEdge[], source: string, target: string): boolean {
	if (source === target) {
		return true
	}

	const prerequisites = new Map<string, string[]>()
	for (const edge of edges) {
		const known = prerequisites.get(edge.target)
		if (known) {
			known.push(edge.source)
		}
		else {
			prerequisites.set(edge.target, [edge.source])
		}
	}

	const visited = new Set<string>()
	const pending = [source]
	while (pending.length > 0) {
		const current = pending.pop()
		if (current === undefined || !visited.add(current)) {
			continue
		}

		if (current === target) {
			return true
		}

		pending.push(...prerequisites.get(current) ?? [])
	}

	return false
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
	const trigger = effectiveTrigger(dependency) === DependencyTrigger.OnBegin ? 'begins' : 'finishes'
	const constraint = effectiveConstraint(dependency) === DependencyConstraint.ToFinish ? 'finish' : 'begin'
	return `blocks the ${constraint} until it ${trigger}`
}
