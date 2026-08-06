import { Component, component, css, eventListener, html, nothing, property, query, repeat, state, svg } from '@a11d/lit'
import { DependencyConstraint, DependencyEndpointKind, DependencyTrigger, entityKey, type EntitySubscription } from '@pleiades/sdk'
import { Notice } from 'obsidian'
import { core, DerivedRef, getApp, navigateToEntity, type ExpandingAction, type IconName } from '..'
import { addCheckpointToOnrush, addObjectiveToOnrush, createDependency, deleteCheckpoint, deleteDependency, deleteEntity, removeObjectiveFromOnrush, reshapeDependency, saveGraphLayout } from './canvasActions'
import { EntityDetailModal } from './EntityDetailModal'
import { contextModeLabels, onrushContext, type CanvasContextMode, type EndpointResolver } from './graphContext'
import { edgeCurve, entryPoint, exitPoint, layoutGraph, parsePositions, serializePositions, type CanvasLayout, type NodeBox, type Point } from './graphLayout'
import { describeEdge, effectiveConstraint, effectiveTrigger, endpointKey, endpointTypeName, targetRef, wouldCycle, type CanvasEdge, type CanvasEntity, type CanvasGraph, type CanvasNode } from './graphModel'
import { SelectObjectiveModal } from './SelectObjectiveModal'
import type { CanvasNodePointer, NodeLock } from './CanvasNodeItem'

/** Which gesture a pointer is currently carrying out. */
type Gesture =
	| { readonly sort: 'pan', readonly pointerId: number, readonly originX: number, readonly originY: number, readonly fromX: number, readonly fromY: number }
	| { readonly sort: 'drag', readonly pointerId: number, readonly nodeKey: string, readonly offsetX: number, readonly offsetY: number, readonly originX: number, readonly originY: number }
	| { readonly sort: 'link', readonly pointerId: number, readonly nodeKey: string, readonly at: Point }

/** What the popover is showing, when it is showing anything. */
type MenuTarget =
	| { readonly sort: 'edge', readonly edge: CanvasEdge }
	| { readonly sort: 'node', readonly nodeKey: string }

const minimumScale = 0.3
const maximumScale = 2.5
/** How far a pointer must travel before it is a drag rather than an unsteady click. */
const dragThreshold = 3
/**
 * How close two clicks on the same node must be to count as a double-click that opens its details.
 *
 * Detected here rather than left to the browser's own `dblclick`: the first click is intercepted to bring the
 * node forward, which changes what the second lands on, and that reliably suppresses the native event for a
 * mouse (a touch double-tap survives it). Timing the pair ourselves opens the details for either input.
 */
const doubleClickWindow = 450

/**
 * The dependency canvas: the backlog as a graph you can draw on.
 *
 * Edges are read and written here rather than merely displayed — dragging from one node to another creates
 * the dependency, and the context mode decides what putting a node on the canvas at all means. In an onrush
 * context that is a membership of the sprint, so adding and removing nodes plans the sprint.
 *
 * Two layers share one transform: an SVG beneath for the edges, positioned elements above for the nodes.
 * Keeping the nodes as elements is what lets each one be an {@link CanvasNodeItem} — a real entity item, with
 * the same reactivity and the same theming as every other surface — rather than something redrawn by hand
 * inside the SVG.
 */
@component('p7t-dependency-canvas')
export class DependencyCanvas extends Component {
	@property() mode: CanvasContextMode = 'onrush-active'

	/** Named for what it is rather than `translate`, which is an element property of its own. */
	@state() private pan: Point = { x: 0, y: 0 }
	@state() private scale = 1
	/** Node positions the reader has moved, which win over the layout until it is reset. */
	@state() private overrides: ReadonlyMap<string, Point> = new Map()
	@state() private selected?: string
	/** The node clicked into, whose own contents take their clicks. At most one at a time. */
	@state() private activeKey?: string
	@state() private gesture?: Gesture
	@state() private menu?: MenuTarget

	@query('.viewport') private readonly viewportElement!: HTMLElement
	@query('.menu') private readonly menuElement!: HTMLElement

	private readonly dependencies = new DerivedRef(this, core.repos.dependencyList)
	// Observed so their entities are in the store for resolving a ghostly blocker's title — an objective or
	// checkpoint outside the sprint that no other surface here has loaded.
	private readonly objectiveList = new DerivedRef(this, core.repos.objectiveList)
	private readonly checkpointList = new DerivedRef(this, core.repos.checkpointList)
	// Each sprint is only observed while its mode is the one on screen: an undefined key makes the ref
	// release its subscription and fetch nothing, so the canvas never fetches or revalidates the sprint it
	// is not showing. Switching mode re-subscribes the other.
	private readonly activeSprint = new DerivedRef(this, core.repos.onrushCurrent, () => this.mode === 'onrush-active' ? '' : undefined)
	private readonly planningSprint = new DerivedRef(this, core.repos.onrushPlanning, () => this.mode === 'onrush-planning' ? '' : undefined)

	private storeSubscription?: EntitySubscription
	private layoutCache?: { readonly signature: string, readonly layout: CanvasLayout }
	private framed = false
	/** Whether the drag in progress has actually moved, which is what tells a drag from a click. */
	private dragged = false
	/** The last node clicked and when, so a quick second click on it is read as a double-click. */
	private lastClickKey?: string
	private lastClickAt = 0
	/** The sprint whose saved layout is currently loaded into {@link overrides}. */
	private layoutSprintId?: string
	private layoutSaveTimer?: number

	static override get styles() {
		return css`
			:host {
				display: grid;
				grid-template-rows: auto 1fr;
				height: 100%;
				min-height: 0;
				font-family: var(--font-interface);
				--p7t-canvas-node-width: 15em;
				/* The two edge colours, shared by the line, its arrow, and its midpoint badge so they match. */
				--p7t-edge-line: color-mix(in srgb, var(--text-normal) 70%, var(--background-primary, #1e1e1e));
				--p7t-edge-line-satisfied: color-mix(in srgb, var(--text-success, seagreen) 70%, var(--text-normal));
			}

			.toolbar {
				display: flex;
				align-items: center;
				gap: .4em;
				padding: .2em .6em .5em;
				flex-wrap: wrap;
			}

			.mode {
				padding: .25em .7em;
				border-radius: 8px;
				border: 1px solid color-mix(in srgb, var(--text-normal) 15%, transparent);
				background: transparent;
				color: inherit;
				font-family: inherit;
				font-size: .9em;
				cursor: pointer;
				transition: background-color .2s ease, border-color .2s ease;
			}

			.mode:hover {
				background-color: color-mix(in srgb, var(--text-normal) 8%, transparent);
			}

			.mode[aria-pressed='true'] {
				border-color: var(--p7t-flare-accent, var(--interactive-accent));
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 18%, transparent);
			}

			.spacer {
				flex: 1;
			}

			.readout {
				opacity: .5;
				font-size: .85em;
				font-variant-numeric: tabular-nums;
			}

			/*
			 * The pannable area. It clips rather than scrolls: the transform is the scroll position, and a
			 * scrollbar competing with it would fight every drag.
			 */
			.viewport {
				position: relative;
				overflow: hidden;
				min-height: 0;
				touch-action: none;
				cursor: grab;
				background-image: radial-gradient(color-mix(in srgb, var(--text-normal) 12%, transparent) 1px, transparent 1px);
				background-size: 22px 22px;
			}

			.viewport[data-panning] {
				cursor: grabbing;
			}

			.surface {
				position: absolute;
				inset-block-start: 0;
				inset-inline-start: 0;
				transform-origin: 0 0;
			}

			.edges {
				position: absolute;
				inset-block-start: 0;
				inset-inline-start: 0;
				/* Curves to a dragged node leave the laid-out bounds; clipping them would cut the graph. */
				overflow: visible;
				pointer-events: none;
			}

			.edge {
				fill: none;
				stroke-width: 2;
				/* Opaque, so a pending edge reads as a firm line rather than a faint one. */
				stroke: var(--p7t-edge-line);
			}

			/* A met dependency recedes: its line keeps the success colour but drops to half strength. */
			.edge.satisfied {
				stroke: var(--p7t-edge-line-satisfied);
				opacity: .5;
			}

			.edge.linking {
				stroke: var(--p7t-flare-accent, var(--interactive-accent));
				stroke-dasharray: 5 4;
			}

			/*
			 * A glyph at the middle of an edge: a done mark on a met dependency, a raced mark on one that gates
			 * a finish and is not yet met. It rides the surface as an HTML badge rather than living in the SVG,
			 * so it can be the same themed icon every other surface draws — a foreignObject would strand the
			 * custom element in the SVG namespace and never upgrade it. Its colour is the connector's own.
			 */
			.edge-badge {
				position: absolute;
				inset-block-start: 0;
				inset-inline-start: 0;
				display: flex;
				align-items: center;
				justify-content: center;
				box-sizing: border-box;
				border-radius: 50%;
				border: 1.5px solid var(--p7t-edge-line);
				background: var(--background-primary, #1e1e1e);
				color: var(--p7t-edge-line);
				pointer-events: none;

				&.satisfied {
					border-color: var(--p7t-edge-line-satisfied);
					color: var(--p7t-edge-line-satisfied);
					opacity: .7;
				}
			}

			/* An invisible fat stroke over each curve, because a 2px line is not a target anyone can hit. */
			.edge-hit {
				fill: none;
				stroke: transparent;
				stroke-width: 16;
				pointer-events: stroke;
				cursor: pointer;
			}

			.node {
				position: absolute;
				inset-block-start: 0;
				inset-inline-start: 0;
			}

			.notice {
				position: absolute;
				inset: 0;
				display: flex;
				align-items: center;
				justify-content: center;
				opacity: .5;
				font-weight: 300;
				pointer-events: none;
				text-align: center;
				padding: 1em;
			}

			.fab {
				position: absolute;
				inset-block-end: 1em;
				inset-inline-end: 1em;
				z-index: 5;
			}

			/*
			 * A popover, so the menu renders in the top layer and is not clipped by the viewport it was
			 * opened inside. Light dismissal comes with it.
			 */
			.menu {
				position: fixed;
				margin: 0;
				padding: .3em;
				border-radius: 10px;
				border: 1px solid var(--background-modifier-border, color-mix(in srgb, var(--text-normal) 20%, transparent));
				background-color: var(--background-secondary, #2b2b2b);
				color: var(--text-normal);
				box-shadow: 0 6px 24px rgb(0 0 0 / .28);
				font-family: var(--font-interface);
				min-width: 13em;
			}

			.menu-title {
				padding: .35em .6em;
				opacity: .55;
				font-size: .8em;
			}

			.menu-item {
				display: flex;
				align-items: center;
				gap: .5em;
				width: 100%;
				padding: .4em .6em;
				border: none;
				border-radius: 7px;
				background: transparent;
				color: inherit;
				font-family: inherit;
				font-size: .95em;
				text-align: start;
				cursor: pointer;
			}

			.menu-item:hover {
				background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
			}

			.menu-item[aria-pressed='true'] {
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.menu-separator {
				height: 1px;
				margin: .25em .3em;
				background-color: color-mix(in srgb, var(--text-normal) 12%, transparent);
			}

			.menu-note {
				padding: .4em .6em;
				opacity: .6;
				font-size: .85em;
				line-height: 1.25;
			}
		`
	}

	protected override connected() {
		// A node's own state — an objective moving to Done — changes nothing about the listings the graph is
		// built from, so no listing subscription would report it. The same reasoning as the entity grid.
		this.storeSubscription = core.store.subscribeAll(() => this.requestUpdate())
	}

	protected override disconnected() {
		this.storeSubscription?.()
		this.storeSubscription = undefined
		window.clearTimeout(this.layoutSaveTimer)
	}

	/** The sprint the current mode reads. */
	private get sprint() {
		return this.mode === 'onrush-planning' ? this.planningSprint.value : this.activeSprint.value
	}

	private get graph(): CanvasGraph {
		return onrushContext(this.sprint, this.dependencies.value ?? [], this.resolveEndpoint)
	}

	/**
	 * Resolves an out-of-context endpoint to the entity behind it, for a ghostly blocker's label.
	 *
	 * The store first, since it holds anything any surface has loaded; then the two listings this canvas keeps
	 * observed, which cover the kinds a blocker most often is even when nothing else has fetched them. A miss
	 * leaves the blocker to be labelled by its id, which is enough to say what is holding a member back.
	 */
	private readonly resolveEndpoint: EndpointResolver = (kind, id) => {
		const typeName = endpointTypeName(kind)
		if (typeName === undefined) {
			return undefined
		}

		const stored = core.store.peek<CanvasEntity>(entityKey(typeName, id))
		if (stored) {
			return stored
		}

		if (kind === DependencyEndpointKind.Objective) {
			return this.objectiveList.value?.find(objective => objective.id === id)
		}

		if (kind === DependencyEndpointKind.Checkpoint) {
			return this.checkpointList.value?.find(checkpoint => checkpoint.id === id)
		}

		return undefined
	}

	private get loading() {
		return this.dependencies.value === undefined && this.sprint === undefined
	}

	/**
	 * The placed graph, recomputed only when its shape changes.
	 *
	 * Panning, zooming and selecting all re-render, and running the layout engine for each of those would be
	 * work thrown away — the placement only depends on which nodes and edges exist.
	 */
	private layoutFor(graph: CanvasGraph): CanvasLayout {
		const signature = `${graph.nodes.map(node => node.key).join(',')}|${graph.edges.map(edge => `${edge.source}>${edge.target}`).join(',')}`
		if (this.layoutCache?.signature !== signature) {
			this.layoutCache = { signature, layout: layoutGraph(graph) }
		}

		return this.layoutCache.layout
	}

	/** Where a node actually is: what the reader dragged it to, else where it was laid out. */
	private boxesFor(layout: CanvasLayout): Map<string, NodeBox> {
		const boxes = new Map<string, NodeBox>()
		for (const [key, box] of layout.nodes) {
			const override = this.overrides.get(key)
			boxes.set(key, override ? { ...box, x: override.x, y: override.y } : box)
		}

		return boxes
	}

	/**
	 * How each node is locked, read from *every* edge that targets it, not only the ones inside this context.
	 *
	 * A dependant can be held back by a prerequisite the current onrush does not contain, and that lock is
	 * still true of it — so the whole dependency set is consulted. A begin gate is the severe `blocked`; a
	 * finish-only gate is the softer `raced`; begin wins when both are present.
	 */
	private get locks(): ReadonlyMap<string, NodeLock> {
		const begin = new Set<string>()
		const finish = new Set<string>()
		for (const dependency of this.dependencies.value ?? []) {
			if (dependency.satisfied) {
				continue
			}

			const target = endpointKey(targetRef(dependency))
			if (effectiveConstraint(dependency) === DependencyConstraint.ToFinish) {
				finish.add(target)
			}
			else {
				begin.add(target)
			}
		}

		const locks = new Map<string, NodeLock>()
		for (const key of finish) {
			locks.set(key, 'raced')
		}

		for (const key of begin) {
			locks.set(key, 'blocked')
		}

		return locks
	}

	protected override get template() {
		const graph = this.graph
		const layout = this.layoutFor(graph)
		const boxes = this.boxesFor(layout)
		const locks = this.locks

		return html`
			<div class='toolbar'>
				${(['onrush-active', 'onrush-planning'] as const).map(mode => html`
					<button
						class='mode'
						aria-pressed=${this.mode === mode}
						@click=${() => this.setMode(mode)}>
						${contextModeLabels[mode]}
					</button>
				`)}
				<button class='mode' @click=${() => this.reset(layout)}>Reset layout</button>
				<div class='spacer'></div>
				<span class='readout'>${graph.nodes.length} nodes · ${graph.edges.length} edges · ${Math.round(this.scale * 100)}%</span>
			</div>
			<div
				class='viewport'
				?data-panning=${this.gesture?.sort === 'pan'}
				@pointerdown=${(e: PointerEvent) => this.onPointerDown(e)}
				@pointermove=${(e: PointerEvent) => this.onPointerMove(e)}
				@pointerup=${(e: PointerEvent) => void this.onPointerUp(e)}
				@pointercancel=${() => this.endGesture()}
				@wheel=${(e: WheelEvent) => this.onWheel(e)}>
				<div class='surface' style='transform: translate(${this.pan.x}px, ${this.pan.y}px) scale(${this.scale})'>
					<svg class='edges' width=${Math.max(layout.width, 1)} height=${Math.max(layout.height, 1)}>
						<defs>
							<marker id='arrow-pending' viewBox='0 0 8 8' refX='7' refY='4' markerWidth='7' markerHeight='7' orient='auto-start-reverse'>
								<path d='M 0 0 L 8 4 L 0 8 z' fill='color-mix(in srgb, var(--text-normal) 70%, var(--background-primary, #1e1e1e))'></path>
							</marker>
							<marker id='arrow-satisfied' viewBox='0 0 8 8' refX='7' refY='4' markerWidth='7' markerHeight='7' orient='auto-start-reverse'>
								<path d='M 0 0 L 8 4 L 0 8 z' fill='color-mix(in srgb, var(--text-success, seagreen) 70%, var(--text-normal))'></path>
							</marker>
							<marker id='edge-begin' viewBox='0 0 10 10' refX='5' refY='5' markerWidth='7' markerHeight='7' orient='auto'>
								<circle cx='5' cy='5' r='3.4' fill='var(--background-primary, transparent)' stroke='context-stroke' stroke-width='1.6'></circle>
							</marker>
						</defs>
						${repeat(graph.edges, edge => edge.key, edge => this.edgeTemplate(edge, boxes))}
						${this.linkTemplate(boxes)}
					</svg>
					${repeat(graph.edges, edge => `badge:${edge.key}`, edge => this.edgeBadge(edge, boxes))}
					${repeat(graph.nodes, node => node.key, node => {
						const box = boxes.get(node.key)
						return !box ? nothing : html`
							<div
								class='node'
								data-key=${node.key}
								style='transform: translate(${box.x}px, ${box.y}px)'
								@pointerdown=${(e: PointerEvent) => this.onNodePointerDown(e, node.key, box)}
								@click=${{ handleEvent: (e: Event) => this.onNodeClick(e), capture: true }}
								@contextmenu=${(e: MouseEvent) => this.onNodeContextMenu(e, node.key)}>
								${this.nodeTemplate(node, locks.get(node.key) ?? 'none')}
							</div>
						`
					})}
				</div>
				${graph.nodes.length > 0 ? nothing : html`
					<div class='notice'>${this.loading ? 'Loading…' : this.emptyMessage}</div>
				`}
				<p7t-expanding-actions
					class='fab'
					large
					actionLabel='Add to the canvas'
					.actions=${this.additions}>
				</p7t-expanding-actions>
			</div>
			<div class='menu' popover='auto' @beforetoggle=${(e: Event) => this.onMenuToggle(e)}>
				${this.menuTemplate}
			</div>
		`
	}

	private get emptyMessage() {
		return this.sprint
			? 'Nothing in this Onrush yet — add an objective to begin.'
			: `There is no ${contextModeLabels[this.mode].toLowerCase()}.`
	}

	/**
	 * The right node element for what the node stands for.
	 *
	 * A checkpoint gets its own component — no lifecycle, its milestone flagged — while everything else is an
	 * entity node. Both share the pointer contract the container drives them by.
	 */
	private nodeTemplate(node: CanvasNode, lock: NodeLock) {
		const shared = {
			nodeKey: node.key,
			entity: node.entity,
			kind: node.ref.kind,
			selected: this.selected === node.key,
			active: this.activeKey === node.key,
			ghostly: node.ghostly === true,
			linking: this.gesture?.sort === 'link' && this.gesture.nodeKey !== node.key
		}

		if (node.ref.kind === DependencyEndpointKind.Checkpoint) {
			return html`
				<p7t-canvas-checkpoint
					interactive
					.nodeKey=${shared.nodeKey}
					.entity=${shared.entity}
					.kind=${shared.kind}
					.lock=${this.checkpointLock(node, lock)}
					?milestone=${node.milestone === true}
					?selected=${shared.selected}
					?active=${shared.active}
					?ghostly=${shared.ghostly}
					?linking=${shared.linking}>
				</p7t-canvas-checkpoint>
			`
		}

		return html`
			<p7t-canvas-node
				interactive
				.nodeKey=${shared.nodeKey}
				.entity=${shared.entity}
				.kind=${shared.kind}
				.lock=${lock}
				?selected=${shared.selected}
				?active=${shared.active}
				?ghostly=${shared.ghostly}
				?linking=${shared.linking}>
			</p7t-canvas-node>
		`
	}

	private edgeTemplate(edge: CanvasEdge, boxes: ReadonlyMap<string, NodeBox>) {
		const source = boxes.get(edge.source)
		const target = boxes.get(edge.target)
		if (!source || !target) {
			return nothing
		}

		const entry = entryPoint(target)
		const path = edgeCurve(exitPoint(source), entry)
		const satisfied = edge.dependency.satisfied
		// A begin-triggered edge fires on its source *starting*, not finishing, so it wears the same
		// start-circle tail whatever it gates — the begin-to-begin form generalised to every begin trigger.
		const beginTriggered = effectiveTrigger(edge.dependency) === DependencyTrigger.OnBegin
		return svg`
			<path
				class='edge ${satisfied ? 'satisfied' : 'pending'}'
				d=${path}
				marker-end='url(#${satisfied ? 'arrow-satisfied' : 'arrow-pending'})'
				marker-start=${beginTriggered ? 'url(#edge-begin)' : nothing}>
			</path>
			<path class='edge-hit' d=${path} @click=${(e: MouseEvent) => void this.openMenu(e.clientX, e.clientY, { sort: 'edge', edge })}></path>
		`
	}

	/**
	 * The glyph badge, if any, that rides the middle of an edge (PEP102).
	 *
	 * A met dependency carries a done mark; an unmet one that gates a finish carries a raced mark, the softer
	 * "expected to win" state. Everything else — an ordinary unmet begin-gate — has no badge, the plain line
	 * says all there is to say. The midpoint of the cubic is the mean of its ends, since both control points
	 * sit level with their own end.
	 */
	private edgeBadge(edge: CanvasEdge, boxes: ReadonlyMap<string, NodeBox>) {
		const source = boxes.get(edge.source)
		const target = boxes.get(edge.target)
		if (!source || !target) {
			return nothing
		}

		const satisfied = edge.dependency.satisfied
		const toFinish = effectiveConstraint(edge.dependency) === DependencyConstraint.ToFinish
		const icon: IconName | undefined = satisfied ? 'state-done' : toFinish ? 'state-raced' : undefined
		if (!icon) {
			return nothing
		}

		const exit = exitPoint(source)
		const entry = entryPoint(target)
		// The badge is 22px square; translating to the midpoint less half its size centres it on the line.
		const x = (exit.x + entry.x) / 2 - 11
		const y = (exit.y + entry.y) / 2 - 11
		return html`
			<div class='edge-badge ${satisfied ? 'satisfied' : 'pending'}' style='transform: translate(${x}px, ${y}px)'>
				<p7t-icon icon=${icon}></p7t-icon>
			</div>
		`
	}

	/**
	 * How a checkpoint is held back, in the same two states an entity node wears (PEP102).
	 *
	 * A checkpoint aggregates rather than acts, so its lock is read from what still stands between it and its
	 * unlock. An unmet incoming dependency is the severe `blocked`; once those are all met, an unpaid toll or an
	 * unmet external condition is the softer `raced`; nothing owed is `none`. The `??` guards read a toll or a
	 * condition the core sends as `null` — an absent optional — the same as one that is simply missing.
	 */
	private checkpointLock(node: CanvasNode, dependencyLock: NodeLock): NodeLock {
		if (dependencyLock !== 'none') {
			return 'blocked'
		}

		const checkpoint = node.entity as { celestronToll?: number | null, tollPaid?: boolean, externalCondition?: boolean | null }
		const owesToll = (checkpoint.celestronToll ?? 0) > 0 && checkpoint.tollPaid !== true
		const owesCondition = (checkpoint.externalCondition ?? undefined) === false
		return owesToll || owesCondition ? 'raced' : 'none'
	}

	private linkTemplate(boxes: ReadonlyMap<string, NodeBox>) {
		const gesture = this.gesture
		if (gesture?.sort !== 'link') {
			return nothing
		}

		const source = boxes.get(gesture.nodeKey)
		return !source ? nothing : svg`
			<path class='edge linking' d=${edgeCurve(exitPoint(source), gesture.at)}></path>
		`
	}

	private get menuTemplate() {
		const menu = this.menu
		if (!menu) {
			return nothing
		}

		return menu.sort === 'edge' ? this.edgeMenuTemplate(menu.edge) : this.nodeMenuTemplate(menu.nodeKey)
	}

	private edgeMenuTemplate(edge: CanvasEdge) {
		const trigger = effectiveTrigger(edge.dependency)
		const constraint = effectiveConstraint(edge.dependency)
		// A checkpoint has no begin or finish, so it offers no trigger on its source side and no constraint on
		// its target side — those are empty by rule, not a choice, so the menu withholds them entirely.
		const sourceIsCheckpoint = edge.dependency.sourceKind === DependencyEndpointKind.Checkpoint
		const targetIsCheckpoint = edge.dependency.targetKind === DependencyEndpointKind.Checkpoint
		return html`
			<div class='menu-title'>This ${describeEdge(edge.dependency)}</div>
			${sourceIsCheckpoint ? nothing : html`
				<button class='menu-item' aria-pressed=${trigger === DependencyTrigger.OnFinish}
					@click=${() => void this.reshape(edge, DependencyTrigger.OnFinish, constraint)}>
					Satisfied when it finishes
				</button>
				<button class='menu-item' aria-pressed=${trigger === DependencyTrigger.OnBegin}
					@click=${() => void this.reshape(edge, DependencyTrigger.OnBegin, constraint)}>
					Satisfied when it begins
				</button>
			`}
			${sourceIsCheckpoint || targetIsCheckpoint ? nothing : html`<div class='menu-separator'></div>`}
			${targetIsCheckpoint ? nothing : html`
				<button class='menu-item' aria-pressed=${constraint === DependencyConstraint.ToBegin}
					@click=${() => void this.reshape(edge, trigger, DependencyConstraint.ToBegin)}>
					Gates the dependant's begin
				</button>
				<button class='menu-item' aria-pressed=${constraint === DependencyConstraint.ToFinish}
					@click=${() => void this.reshape(edge, trigger, DependencyConstraint.ToFinish)}>
					Gates the dependant's finish
				</button>
			`}
			<div class='menu-separator'></div>
			<button class='menu-item' @click=${() => void this.run(async () => await deleteDependency(edge))}>
				Remove dependency
			</button>
		`
	}

	private nodeMenuTemplate(nodeKey: string) {
		const node = this.graph.nodes.find(candidate => candidate.key === nodeKey)
		if (!node) {
			return nothing
		}

		const isCheckpoint = node.ref.kind === DependencyEndpointKind.Checkpoint
		return html`
			<div class='menu-title'>${node.entity.title}</div>
			<button class='menu-item' @click=${() => void this.run(async () => this.openDetails(node))}>
				Details
			</button>
			${isCheckpoint ? this.checkpointMenu(node) : this.entityMenu(node)}
		`
	}

	private entityMenu(node: CanvasNode) {
		return html`
			<button class='menu-item' @click=${() => void this.run(async () => { await navigateToEntity(node.entity.id) })}>
				Open note
			</button>
			${node.ghostly ? html`
				<div class='menu-note'>A prerequisite outside this Onrush. It goes when the block is resolved.</div>
			` : html`
				<button class='menu-item' @click=${() => void this.run(async () => await removeObjectiveFromOnrush(node.entity.id))}>
					Remove from Onrush
				</button>
			`}
			<div class='menu-separator'></div>
			<button class='menu-item' @click=${() => void this.run(async () => await deleteEntity(node))}>
				Delete
			</button>
		`
	}

	private checkpointMenu(node: CanvasNode) {
		// A milestone stands for the sprint's completion and is bound to it; it offers nothing to remove — only
		// its details. A ghostly checkpoint is context, not a member. Everything else the sprint tracks.
		if (node.milestone) {
			return html`<div class='menu-note'>The sprint's milestone — it stays for the sprint's life.</div>`
		}

		if (node.ghostly) {
			return html`<div class='menu-note'>A checkpoint outside this Onrush, shown because it blocks a member.</div>`
		}

		return html`
			<div class='menu-separator'></div>
			<button class='menu-item' @click=${() => void this.run(async () => await deleteCheckpoint(node.entity.id))}>
				Delete checkpoint
			</button>
		`
	}

	private get additions(): ExpandingAction[] {
		return [
			{
				key: 'objective',
				icon: 'objective',
				label: 'Add objective',
				run: async () => await this.addObjective()
			},
			{
				key: 'checkpoint',
				icon: 'checkpoint',
				label: 'Add checkpoint',
				run: async () => await this.addCheckpoint()
			}
		]
	}

	@eventListener('requestLinkStart')
	protected onRequestLinkStart(e: CustomEvent<CanvasNodePointer>) {
		e.stopPropagation()
		const detail = e.detail
		this.selected = detail.nodeKey
		this.gesture = {
			sort: 'link',
			pointerId: detail.pointerId,
			nodeKey: detail.nodeKey,
			at: this.toCanvas(detail.clientX, detail.clientY)
		}
		this.capture(detail.pointerId)
	}

	@eventListener('requestNodeMenu')
	protected onRequestNodeMenu(e: CustomEvent<CanvasNodePointer>) {
		e.stopPropagation()
		const element = this.shadowRoot?.querySelector(`.node[data-key="${CSS.escape(e.detail.nodeKey)}"]`)
		const anchor = element?.getBoundingClientRect()
		void this.openMenu(anchor?.left ?? 0, anchor?.bottom ?? 0, { sort: 'node', nodeKey: e.detail.nodeKey })
	}

	/** Opens the node's context menu at the pointer, the right-click way in rather than through the notch. */
	private onNodeContextMenu(e: MouseEvent, nodeKey: string) {
		e.preventDefault()
		e.stopPropagation()
		this.selected = nodeKey
		void this.openMenu(e.clientX, e.clientY, { sort: 'node', nodeKey })
	}

	/** Opens the entity behind a node in its banner, to view or edit. */
	private openDetails(node: CanvasNode) {
		new EntityDetailModal(getApp(), node.ref.kind, node.entity, { milestone: node.milestone === true }).open()
	}

	private setMode(mode: CanvasContextMode) {
		if (this.mode === mode) {
			return
		}

		this.mode = mode
		// A different context is a different graph; positions from the last one mean nothing in it, and its
		// saved layout is adopted afresh once the new sprint resolves.
		this.overrides = new Map()
		this.layoutSprintId = undefined
		this.selected = undefined
		this.activeKey = undefined
		this.framed = false
	}

	/** Puts every node back where the layout placed it, forgets the saved arrangement, and reframes. */
	private reset(layout: CanvasLayout) {
		this.overrides = new Map()
		this.frame(layout)
		this.scheduleLayoutSave()
	}

	private async addObjective() {
		const sprint = this.sprint
		if (!sprint) {
			new Notice(`There is no ${contextModeLabels[this.mode].toLowerCase()} to add to.`)
			return
		}

		const present = new Set(this.graph.nodes.map(node => node.ref.id))
		const objective = await SelectObjectiveModal.prompt(present)
		if (objective) {
			await addObjectiveToOnrush(objective.id, sprint)
		}
	}

	private async addCheckpoint() {
		const sprint = this.sprint
		if (!sprint) {
			new Notice(`There is no ${contextModeLabels[this.mode].toLowerCase()} to add to.`)
			return
		}

		await addCheckpointToOnrush(sprint)
	}

	private async reshape(edge: CanvasEdge, trigger: DependencyTrigger, constraint: DependencyConstraint) {
		await this.run(async () => await reshapeDependency(edge, trigger, constraint))
	}

	/** Runs a menu action and closes the menu, whatever the outcome. */
	private async run(operation: () => Promise<unknown>) {
		this.menuElement.hidePopover()
		await operation()
	}

	private async openMenu(clientX: number, clientY: number, target: MenuTarget) {
		this.menu = target
		// The size to place against is the size of the content just assigned, which is not in the DOM until
		// the update it triggered has run.
		await this.updateComplete
		const panel = this.menuElement
		panel.showPopover()
		const margin = 8
		const width = panel.offsetWidth
		const height = panel.offsetHeight
		panel.style.left = `${Math.max(margin, Math.min(clientX, window.innerWidth - width - margin))}px`
		panel.style.top = `${clientY + height + margin > window.innerHeight ? Math.max(margin, clientY - height - margin) : clientY + margin}px`
	}

	private onMenuToggle(e: Event) {
		if ((e as Event & { newState?: string }).newState === 'closed') {
			this.menu = undefined
		}
	}

	private onPointerDown(e: PointerEvent) {
		if (e.button !== 0 || !this.isBackground(e.target)) {
			return
		}

		// Pressing the backdrop drops focus: the active node's contents go inert again, and it can be moved.
		this.selected = undefined
		this.activeKey = undefined
		this.gesture = {
			sort: 'pan',
			pointerId: e.pointerId,
			originX: e.clientX,
			originY: e.clientY,
			fromX: this.pan.x,
			fromY: this.pan.y
		}
		this.capture(e.pointerId)
	}

	/**
	 * Whether a pointer landed on the empty backdrop rather than on something with behaviour of its own.
	 *
	 * Panning captures the pointer, and a captured pointer never delivers a click to what it started over.
	 * Beginning a pan for every pointer that reaches the viewport therefore silently disables the action
	 * button, the edges, and anything else placed in the viewport later — so only the two elements that
	 * genuinely *are* the backdrop pan.
	 */
	private isBackground(target: EventTarget | null): boolean {
		return target === this.viewportElement
			|| (target instanceof Element && target.classList.contains('surface'))
	}

	private onNodePointerDown(e: PointerEvent, nodeKey: string, box: NodeBox) {
		if (e.button !== 0) {
			return
		}

		// The active node's body is live, so a press there belongs to whatever control is under it — dragging
		// would fight it. Its handles still start edges; moving it means dropping focus (a backdrop press) first.
		if (this.activeKey === nodeKey) {
			return
		}

		e.stopPropagation()
		const point = this.toCanvas(e.clientX, e.clientY)
		this.selected = nodeKey
		this.dragged = false
		this.gesture = {
			sort: 'drag',
			pointerId: e.pointerId,
			nodeKey,
			offsetX: point.x - box.x,
			offsetY: point.y - box.y,
			originX: e.clientX,
			originY: e.clientY
		}
		this.capture(e.pointerId)
	}

	/**
	 * Resolves a click that landed on a node.
	 *
	 * A finished drag leaves a click behind — releasing a dragged node over its own title would otherwise
	 * open the note — so that one is swallowed. A quick second click on the same node opens its details,
	 * wherever it lands. Otherwise a clean click on a node that is not yet the active one focuses it: the
	 * first click brings the node forward and its contents come alive, and only a second click, now reaching
	 * those live contents, acts on them. Caught in the capture phase, ahead of the contents, because until the
	 * node is active the click is the canvas's to interpret, not the item's.
	 */
	private onNodeClick(e: Event) {
		if (this.dragged) {
			e.stopPropagation()
			e.preventDefault()
			this.dragged = false
			return
		}

		const key = (e.currentTarget as HTMLElement | null)?.getAttribute('data-key') ?? undefined
		const now = Date.now()
		if (key !== undefined && key === this.lastClickKey && now - this.lastClickAt <= doubleClickWindow) {
			e.stopPropagation()
			e.preventDefault()
			this.lastClickKey = undefined
			this.lastClickAt = 0
			const node = this.graph.nodes.find(candidate => candidate.key === key)
			if (node) {
				this.openDetails(node)
			}

			return
		}

		this.lastClickKey = key
		this.lastClickAt = now
		if (key !== undefined && this.activeKey !== key) {
			e.stopPropagation()
			e.preventDefault()
			this.selected = key
			this.activeKey = key
		}
	}

	private onPointerMove(e: PointerEvent) {
		const gesture = this.gesture
		if (!gesture || gesture.pointerId !== e.pointerId) {
			return
		}

		switch (gesture.sort) {
			case 'pan':
				this.pan = {
					x: gesture.fromX + (e.clientX - gesture.originX),
					y: gesture.fromY + (e.clientY - gesture.originY)
				}
				return
			case 'drag': {
				// Below the threshold this is still a click being held, and moving the node — or swallowing
				// the click that follows — would be reading intent into an unsteady hand.
				if (!this.dragged && Math.hypot(e.clientX - gesture.originX, e.clientY - gesture.originY) < dragThreshold) {
					return
				}

				this.dragged = true
				const point = this.toCanvas(e.clientX, e.clientY)
				const next = new Map(this.overrides)
				next.set(gesture.nodeKey, { x: point.x - gesture.offsetX, y: point.y - gesture.offsetY })
				this.overrides = next
				return
			}
			case 'link':
				this.gesture = { ...gesture, at: this.toCanvas(e.clientX, e.clientY) }
				return
		}
	}

	private async onPointerUp(e: PointerEvent) {
		const gesture = this.gesture
		if (!gesture || gesture.pointerId !== e.pointerId) {
			return
		}

		this.endGesture()
		if (gesture.sort === 'link') {
			await this.completeLink(gesture.nodeKey, e.clientX, e.clientY)
		}
		else if (gesture.sort === 'drag' && this.dragged) {
			// A node was moved to rest — remember where, so the arrangement survives a reopen.
			this.scheduleLayoutSave()
		}
	}

	private endGesture() {
		const gesture = this.gesture
		this.gesture = undefined
		if (gesture && this.viewportElement?.hasPointerCapture(gesture.pointerId)) {
			this.viewportElement.releasePointerCapture(gesture.pointerId)
		}
	}

	/**
	 * Finishes an edge drawn onto whatever node it was released over.
	 *
	 * Both refusals the core would make are made here first — a loop, and an edge that already exists — so
	 * the gesture is answered where it happened rather than by a write coming back rejected.
	 */
	private async completeLink(sourceKey: string, clientX: number, clientY: number) {
		const targetKey = this.nodeKeyAt(clientX, clientY)
		if (!targetKey || targetKey === sourceKey) {
			return
		}

		const graph = this.graph
		const source = graph.nodes.find(node => node.key === sourceKey)
		const target = graph.nodes.find(node => node.key === targetKey)
		if (!source || !target) {
			return
		}

		if (graph.edges.some(edge => edge.source === sourceKey && edge.target === targetKey)) {
			new Notice('Those are already connected.')
			return
		}

		if (wouldCycle(graph.edges, sourceKey, targetKey)) {
			new Notice('That dependency would close a loop.')
			return
		}

		await createDependency(source.ref, target.ref)
	}

	private onWheel(e: WheelEvent) {
		e.preventDefault()
		const next = Math.min(maximumScale, Math.max(minimumScale, this.scale * Math.exp(-e.deltaY * 0.0015)))
		if (next === this.scale) {
			return
		}

		// Zoom about the cursor: whatever is under it stays under it.
		const rect = this.viewportElement.getBoundingClientRect()
		const x = e.clientX - rect.left
		const y = e.clientY - rect.top
		const ratio = next / this.scale
		this.pan = {
			x: x - (x - this.pan.x) * ratio,
			y: y - (y - this.pan.y) * ratio
		}
		this.scale = next
	}

	private capture(pointerId: number) {
		if (pointerId >= 0) {
			this.viewportElement?.setPointerCapture(pointerId)
		}
	}

	/** Turns a viewport point into a point on the surface, undoing the pan and the zoom. */
	private toCanvas(clientX: number, clientY: number): Point {
		const rect = this.viewportElement?.getBoundingClientRect()
		if (!rect) {
			return { x: 0, y: 0 }
		}

		return {
			x: (clientX - rect.left - this.pan.x) / this.scale,
			y: (clientY - rect.top - this.pan.y) / this.scale
		}
	}

	/** Which node, if any, sits under a viewport point. */
	private nodeKeyAt(clientX: number, clientY: number): string | undefined {
		const element = this.shadowRoot?.elementFromPoint(clientX, clientY)
		return element?.closest('.node')?.getAttribute('data-key') ?? undefined
	}

	protected override updated() {
		this.adoptSavedLayout()

		// Framed once, when there is finally something to frame, and never again — refitting on every change
		// would move the graph out from under someone reading it.
		if (!this.framed && this.layoutCache && this.layoutCache.layout.nodes.size > 0 && this.viewportElement) {
			this.framed = true
			this.frame(this.layoutCache.layout)
		}
	}

	/**
	 * Takes the sprint's saved positions as the starting overrides when a sprint first appears or changes.
	 *
	 * Keyed by node key, so a saved position applies to a node still present and is ignored for one that is
	 * gone; a node with no saved position keeps its dagre placement. That is the whole of the new-and-removed
	 * handling — the layout is a hint, never a requirement, and never wiped for a membership change.
	 */
	private adoptSavedLayout() {
		const sprint = this.sprint
		if (!sprint || this.layoutSprintId === sprint.id) {
			return
		}

		this.layoutSprintId = sprint.id
		this.overrides = parsePositions(sprint.graphLayout)
	}

	/** Writes the current overrides to the sprint after a short rest, coalescing a run of drags into one save. */
	private scheduleLayoutSave() {
		const sprint = this.sprint
		if (!sprint) {
			return
		}

		window.clearTimeout(this.layoutSaveTimer)
		const id = sprint.id
		const payload = serializePositions(this.overrides)
		this.layoutSaveTimer = window.setTimeout(() => void saveGraphLayout(id, payload), 600)
	}

	/** Centres the graph and zooms out far enough to hold it, never past life size. */
	private frame(layout: CanvasLayout) {
		const rect = this.viewportElement?.getBoundingClientRect()
		if (!rect || layout.width === 0 || layout.height === 0) {
			return
		}

		const scale = Math.min(1, Math.max(minimumScale, Math.min(rect.width / layout.width, rect.height / layout.height)))
		this.scale = scale
		this.pan = {
			x: (rect.width - layout.width * scale) / 2,
			y: (rect.height - layout.height * scale) / 2
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-dependency-canvas': DependencyCanvas
	}
}
