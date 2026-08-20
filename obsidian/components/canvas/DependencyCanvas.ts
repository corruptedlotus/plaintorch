import { Component, component, css, event, eventListener, html, nothing, property, query, repeat, state, svg } from '@a11d/lit'
import { DependencyConstraint, DependencyEndpointKind, DependencyTrigger, entityKey, type EndpointHit, type EntitySubscription } from '@pleiades/sdk'
import { Notice } from 'obsidian'
import { ContextMenu, core, DerivedRef, getApp, navigateToEntity, type ContextMenuEntry, type ContextMenuSpec, type ExpandingAction, type IconName } from '..'
import { activatePlanningOnrush, addCheckpointToOnrush, addObjectiveToOnrush, concludeOnrush, createDependency, createPlanningOnrush, deleteCheckpoint, deleteDependency, deleteEntity, deletePlanningOnrush, removeObjectiveFromOnrush, reshapeDependency, saveGlobalContextToFile, saveGraphLayout, startActiveOnrush } from './canvasActions'
import { EntityDetailModal } from './EntityDetailModal'
import { OnrushDetailModal } from './OnrushDetailModal'
import { contextModeLabels, edgeEndpoints, globalContext, onrushContext, type CanvasContextMode, type EndpointResolver } from './graphContext'
import { edgeCurve, entryPoint, exitPoint, layoutGraph, parsePositions, serializePositions, type CanvasLayout, type NodeBox, type Point } from './graphLayout'
import { describeEdge, effectiveConstraint, effectiveTrigger, endpointKey, endpointTypeName, sourceRef, targetRef, wouldCycle, type CanvasEdge, type CanvasEntity, type CanvasGraph, type CanvasNode } from './graphModel'
import { SelectObjectiveModal } from './SelectObjectiveModal'
import { SelectEndpointModal } from './SelectEndpointModal'
import type { CanvasNodePointer, NodeLock } from './CanvasNodeItem'

/** A global planning context's persistable state — its pinned set and the positions they were dragged to. */
export interface GlobalContextSnapshot {
	readonly pinned: readonly EndpointHit[]
	readonly layout: string | undefined
}

/** Which gesture a pointer is currently carrying out. */
type Gesture =
	| { readonly sort: 'pan', readonly pointerId: number, readonly originX: number, readonly originY: number, readonly fromX: number, readonly fromY: number }
	| { readonly sort: 'drag', readonly pointerId: number, readonly nodeKey: string, readonly offsetX: number, readonly offsetY: number, readonly originX: number, readonly originY: number }
	| { readonly sort: 'link', readonly pointerId: number, readonly nodeKey: string, readonly at: Point }

/** A node's actual rendered size, measured from its element rather than assumed from the layout. */
interface NodeSize {
	readonly width: number
	readonly height: number
}

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

	/**
	 * The curated node set of a global context (PEP102). Each pin carries its own title so the graph draws
	 * before anything is fetched — a context restored from a `.p7tpx` file is legible immediately. Ignored
	 * outside global mode. Driven by the host: the main canvas keeps it as scratch state, the file view
	 * feeds it from the file.
	 */
	@property({ attribute: false }) pinned: readonly EndpointHit[] = []

	/**
	 * A global context's saved node positions (a `serializePositions` blob), adopted once when it loads.
	 * Set by the file view from the file; the scratch tab leaves it undefined.
	 */
	@property({ attribute: false }) savedLayout?: string

	/**
	 * Whether this canvas *is* a global context rather than hosting the onrush tabs. The file view sets it, so
	 * a `.p7tpx` leaf shows only its one context, no mode switcher.
	 */
	@property({ type: Boolean, reflect: true }) fileBacked = false

	/** Announces a global context change (pins or layout) so a file-backed host can persist it. */
	@event({ bubbles: true, composed: true }) contextChanged!: EventDispatcher<GlobalContextSnapshot>

	/** Named for what it is rather than `translate`, which is an element property of its own. */
	@state() private pan: Point = { x: 0, y: 0 }
	@state() private scale = 1
	/** Node positions the reader has moved, which win over the layout until it is reset. */
	@state() private overrides: ReadonlyMap<string, Point> = new Map()
	@state() private selected?: string
	/** The node clicked into, whose own contents take their clicks. At most one at a time. */
	@state() private activeKey?: string
	@state() private gesture?: Gesture
	/**
	 * Each node's actual rendered size, keyed by node key — what the edges are drawn against.
	 *
	 * The layout gives every node the same nominal box, but a checkpoint is a fraction of an objective's width
	 * and a long title grows one taller, so an edge drawn to the nominal box detaches from the element it was
	 * meant to touch. Measured from the elements and kept current by a {@link ResizeObserver}.
	 */
	@state() private measured: ReadonlyMap<string, NodeSize> = new Map()

	@query('.viewport') private readonly viewportElement!: HTMLElement

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
	private resizeObserver?: ResizeObserver
	/** The node set the observer is currently watching, so it is re-pointed only when that set changes. */
	private observedSignature?: string
	private layoutCache?: { readonly signature: string, readonly layout: CanvasLayout }
	private framed = false
	/** Whether the drag in progress has actually moved, which is what tells a drag from a click. */
	private dragged = false
	/** The last node clicked and when, so a quick second click on it is read as a double-click. */
	private lastClickKey?: string
	private lastClickAt = 0
	/** The sprint whose saved layout is currently loaded into {@link overrides}. */
	private layoutSprintId?: string
	/** Whether the global {@link savedLayout} has been adopted into overrides yet, so it is taken once. */
	private globalLayoutAdopted = false
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

			/* The empty-state offer: buttons live in the notice, so their layer takes clicks the notice waives. */
			.empty-state {
				flex-direction: column;
				gap: 1em;
			}

			.empty-actions {
				display: flex;
				flex-wrap: wrap;
				justify-content: center;
				gap: .8em;
				pointer-events: auto;
			}

			/*
			 * The management tray, bottom-left: the sprint's tallies above, then its details and its lifecycle
			 * actions. It mirrors the add-FAB across the viewport, and stands only while a sprint is on screen.
			 */
			.tray {
				position: absolute;
				inset-block-end: 1em;
				inset-inline-start: 1em;
				z-index: 5;
				display: flex;
				flex-direction: column;
				align-items: flex-start;
				gap: .5em;
			}

			.counts {
				display: flex;
				align-items: center;
				gap: .9em;
				padding: .3em .7em;
				border-radius: 10px;
				border: 1px solid color-mix(in srgb, var(--text-normal) 12%, transparent);
				background-color: color-mix(in srgb, var(--background-secondary, #2b2b2b) 88%, transparent);
				font-variant-numeric: tabular-nums;
			}

			.count {
				display: flex;
				align-items: center;
				gap: .3em;
				font-size: .9em;
				opacity: .8;

				& p7t-icon {
					width: 1.2em;
					height: 1.2em;
				}
			}

			.tray-actions {
				display: flex;
				align-items: center;
				gap: .3em;
				padding: .25em;
				border-radius: 12px;
				border: 1px solid color-mix(in srgb, var(--text-normal) 12%, transparent);
				background-color: var(--background-secondary, #2b2b2b);
				box-shadow: 0 4px 14px rgb(0 0 0 / .25);
			}

			.tray-btn {
				display: flex;
				align-items: center;
				justify-content: center;
				min-width: 2.2em;
				min-height: 2.2em;
				padding: .25em;
				border: none;
				border-radius: 8px;
				background: transparent;
				color: inherit;
				cursor: pointer;
				transition: background-color .2s ease;

				& p7t-icon {
					width: 1.3em;
					height: 1.3em;
				}

				&:hover {
					background-color: color-mix(in srgb, var(--text-normal) 12%, transparent);
				}
			}

		`
	}

	protected override connected() {
		// A node's own state — an objective moving to Done — changes nothing about the listings the graph is
		// built from, so no listing subscription would report it. The same reasoning as the entity grid.
		this.storeSubscription = core.store.subscribeAll(() => this.requestUpdate())
		// A node grows when its title is edited or its content loads, and that is not a canvas render on its own —
		// the observer is what keeps the edges attached to it through changes the canvas never hears about.
		this.resizeObserver = new ResizeObserver(entries => this.onNodesResized(entries))
	}

	protected override disconnected() {
		this.storeSubscription?.()
		this.storeSubscription = undefined
		this.resizeObserver?.disconnect()
		this.resizeObserver = undefined
		this.observedSignature = undefined
		window.clearTimeout(this.layoutSaveTimer)
	}

	/** The sprint the current mode reads. */
	private get sprint() {
		return this.mode === 'onrush-planning' ? this.planningSprint.value : this.activeSprint.value
	}

	private get graph(): CanvasGraph {
		return this.mode === 'global'
			? globalContext(this.pinned, this.dependencies.value ?? [], this.resolveEndpoint)
			: onrushContext(this.sprint, this.dependencies.value ?? [], this.resolveEndpoint)
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
		// A global context has no sprint to wait on — it is its pins, empty or not.
		return this.mode !== 'global' && this.dependencies.value === undefined && this.sprint === undefined
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

	/**
	 * Where a node actually is, and how big it actually is.
	 *
	 * Position is what the reader dragged it to, else where it was laid out. Size is the measured size when there
	 * is one, so the edges — which read a box's width and height for where to meet it — attach to the element's
	 * real trailing and leading edges rather than the layout's uniform guess. Until a node is measured its layout
	 * size stands, which is close enough for the frame or two before the observer reports.
	 */
	private boxesFor(layout: CanvasLayout): Map<string, NodeBox> {
		const boxes = new Map<string, NodeBox>()
		for (const [key, box] of layout.nodes) {
			const override = this.overrides.get(key)
			const placed = override ? { ...box, x: override.x, y: override.y } : box
			const size = this.measured.get(key)
			boxes.set(key, size ? { ...placed, width: size.width, height: size.height } : placed)
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
				${this.fileBacked ? nothing : (['onrush-active', 'onrush-planning', 'global'] as const).map(mode => html`
					<button
						class='mode'
						aria-pressed=${this.mode === mode}
						@click=${() => this.setMode(mode)}>
						${contextModeLabels[mode]}
					</button>
				`)}
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
								@click=${{ handleEvent: (e: Event) => this.onNodeClick(e), capture: true }}>
								${this.nodeTemplate(node, locks.get(node.key) ?? 'none')}
							</div>
						`
					})}
				</div>
				${graph.nodes.length > 0 ? nothing : this.emptyOverlay}
					${this.sprint || this.mode === 'global' ? this.trayTemplate : nothing}
					${this.sprint || this.mode === 'global' ? html`
						<p7t-expanding-actions
							class='fab'
							large
							actionLabel='Add to the canvas'
							.actions=${this.additions}>
						</p7t-expanding-actions>
					` : nothing}
			</div>
		`
	}

	/**
	 * What the mid-screen shows when the graph is empty (PEP102.5).
	 *
	 * A sprint with nothing in it is only missing members, so it is nudged to add one. No sprint at all is a
	 * different offer: an active graph can start one now or step over to planning; a planning graph can create
	 * one. The notice waives pointer events, so its buttons live in a layer that takes them back.
	 */
	private get emptyOverlay() {
		if (this.loading) {
			return html`<div class='notice'>Loading…</div>`
		}

		if (this.mode === 'global') {
			return html`
				<div class='notice empty-state'>
					<span>Nothing here yet — add directives, objectives or fates to plan across the whole backlog.</span>
					<div class='empty-actions'>
						<p7t-button emphasis icon='lucide:plus' @click=${() => void this.addEndpoint()}>
							<span>Add a node</span>
						</p7t-button>
					</div>
				</div>
			`
		}

		if (this.sprint) {
			return html`<div class='notice'>Nothing in this Onrush yet — add an objective to begin.</div>`
		}

		return html`
			<div class='notice empty-state'>
				<span>There is no ${contextModeLabels[this.mode].toLowerCase()}.</span>
				<div class='empty-actions'>
					${this.mode === 'onrush-active' ? html`
						<p7t-button emphasis icon='state-onrush' @click=${() => void this.onStartNow()}>
							<span>Start now</span>
						</p7t-button>
						<p7t-button icon='lucide:arrow-right' @click=${() => this.setMode('onrush-planning')}>
							<span>Go to planning</span>
						</p7t-button>
					` : html`
						<p7t-button emphasis icon='lucide:star' @click=${() => void this.onCreatePlanning()}>
							<span>Create new</span>
						</p7t-button>
					`}
				</div>
			</div>
		`
	}

	/**
	 * The management tray, shown whenever a sprint is on screen (PEP102.5).
	 *
	 * Three tallies — objectives, checkpoints (the milestone among them), executive orders — sit above a row
	 * that opens the sprint's own detail window and offers the lifecycle actions its mode admits.
	 */
	private get trayTemplate() {
		const sprint = this.sprint
		return html`
			<div class='tray'>
				${!sprint ? nothing : html`
					<div class='counts'>
						<span class='count'><p7t-icon icon='objective'></p7t-icon>${sprint.objectives?.length ?? 0}</span>
						<span class='count'><p7t-icon icon='checkpoint'></p7t-icon>${sprint.checkpoints?.length ?? 0}</span>
						<span class='count'><p7t-icon icon='exec-order'></p7t-icon>${sprint.executiveOrders?.length ?? 0}</span>
					</div>
				`}
				<div class='tray-actions'>
					${!sprint ? nothing : html`
						<button class='tray-btn' aria-label='Onrush details' @click=${() => this.openOnrushDetails()}>
							<p7t-icon icon='lucide:pen'></p7t-icon>
						</button>
					`}
					<button class='tray-btn' aria-label='Reset layout' @click=${() => this.resetLayout()}>
						<p7t-icon icon='lucide:rotate-ccw'></p7t-icon>
					</button>
					${this.mode === 'global' && !this.fileBacked ? html`
						<button class='tray-btn' aria-label='Save to file' @click=${() => void this.onSaveToFile()}>
							<p7t-icon icon='lucide:save'></p7t-icon>
						</button>
					` : nothing}
					${this.lifecycleActions.length === 0 ? nothing : html`
						<p7t-expanding-actions
							icon='lucide:ellipsis-vertical'
							actionLabel='Onrush actions'
							.actions=${this.lifecycleActions}>
						</p7t-expanding-actions>
					`}
				</div>
			</div>
		`
	}

	/** The sprint-lifecycle actions the tray's menu offers, which differ by mode; empty in a global context. */
	private get lifecycleActions(): ExpandingAction[] {
		if (this.mode === 'onrush-planning' && this.sprint) {
			return [
				{ key: 'activate', icon: 'state-onrush', label: 'Activate', run: () => this.onActivate() },
				{ key: 'delete', icon: 'lucide:trash-2', label: 'Delete', run: () => this.onDelete() }
			]
		}

		if (this.sprint) {
			return [{ key: 'conclude', icon: 'state-archived', label: 'Conclude', run: () => this.onConclude() }]
		}

		return []
	}

	/** Puts every node back to its computed position and forgets the saved arrangement. */
	private resetLayout() {
		this.reset(this.layoutFor(this.graph))
	}

	/** Opens the sprint's own detail window — its banner and its executive orders. */
	private openOnrushDetails() {
		const sprint = this.sprint
		if (sprint) {
			new OnrushDetailModal(getApp(), sprint).open()
		}
	}

	private async onStartNow() {
		if (await startActiveOnrush()) {
			// A new graph to frame; the active mode is already the one on screen.
			this.framed = false
		}
	}

	private async onCreatePlanning() {
		if (await createPlanningOnrush()) {
			this.framed = false
		}
	}

	/** Activates this planning sprint and, on success, shows the active graph it has become. */
	private async onActivate() {
		const sprint = this.sprint
		if (sprint && await activatePlanningOnrush(sprint.id)) {
			this.setMode('onrush-active')
		}
	}

	private async onDelete() {
		const sprint = this.sprint
		if (sprint) {
			await deletePlanningOnrush(sprint.id)
		}
	}

	private async onConclude() {
		const sprint = this.sprint
		if (sprint) {
			await concludeOnrush(sprint.id)
		}
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
					.menu=${this.nodeMenuSpecFor(node)}
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
				.menu=${this.nodeMenuSpecFor(node)}
				.nodeKey=${shared.nodeKey}
				.entity=${shared.entity}
				.kind=${shared.kind}
				.lock=${lock}
				?typed=${this.mode === 'global'}
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
			<path class='edge-hit' d=${path} @click=${(e: MouseEvent) => ContextMenu.open(e.clientX, e.clientY, this.edgeMenuSpec(edge))}></path>
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

	private edgeMenuSpec(edge: CanvasEdge): ContextMenuSpec {
		const trigger = effectiveTrigger(edge.dependency)
		const constraint = effectiveConstraint(edge.dependency)
		// A checkpoint has no begin or finish, so it offers no trigger on its source side and no constraint on
		// its target side — those are empty by rule, not a choice, so the menu withholds them entirely.
		const sourceIsCheckpoint = edge.dependency.sourceKind === DependencyEndpointKind.Checkpoint
		const targetIsCheckpoint = edge.dependency.targetKind === DependencyEndpointKind.Checkpoint
		const entries: ContextMenuEntry[] = []
		if (!sourceIsCheckpoint) {
			entries.push(
				{ label: 'Satisfied when it finishes', icon: 'lucide:flag', pressed: trigger === DependencyTrigger.OnFinish, run: () => this.reshape(edge, DependencyTrigger.OnFinish, constraint) },
				{ label: 'Satisfied when it begins', icon: 'lucide:play', pressed: trigger === DependencyTrigger.OnBegin, run: () => this.reshape(edge, DependencyTrigger.OnBegin, constraint) })
		}

		if (!sourceIsCheckpoint && !targetIsCheckpoint) {
			entries.push({ separator: true })
		}

		if (!targetIsCheckpoint) {
			entries.push(
				{ label: "Gates the dependant's begin", icon: 'lucide:play', pressed: constraint === DependencyConstraint.ToBegin, run: () => this.reshape(edge, trigger, DependencyConstraint.ToBegin) },
				{ label: "Gates the dependant's finish", icon: 'lucide:flag', pressed: constraint === DependencyConstraint.ToFinish, run: () => this.reshape(edge, trigger, DependencyConstraint.ToFinish) })
		}

		entries.push({ separator: true }, { label: 'Remove dependency', icon: 'lucide:unlink', danger: true, run: () => deleteDependency(edge) })
		return { title: `This ${describeEdge(edge.dependency)}`, entries }
	}

	/**
	 * The context menu for a node, built from the canvas state it needs (the mode, the node's role). Set as each
	 * node's `menu` so its own inherited controller raises it on a right-click — the canvas parametrizes the node's
	 * menu rather than intercepting the event with a handler of its own.
	 */
	private nodeMenuSpecFor(node: CanvasNode): ContextMenuSpec {
		const isCheckpoint = node.ref.kind === DependencyEndpointKind.Checkpoint
		return {
			title: node.entity.title,
			entries: [
				{ label: 'Details', icon: 'lucide:pen', run: () => this.openDetails(node) },
				...(this.mode === 'global'
					? this.globalMenuEntries(node)
					: isCheckpoint
						? this.checkpointMenuEntries(node)
						: this.entityMenuEntries(node))
			]
		}
	}

	/**
	 * A global node's menu: removing only *hides* it, whichever kind it is.
	 *
	 * The edge it carried is untouched and reappears when the node is pinned again — that is the whole of what
	 * global add and remove do. Deleting the entity is a separate, destructive step kept behind a separator.
	 */
	private globalMenuEntries(node: CanvasNode): ContextMenuEntry[] {
		const entries: ContextMenuEntry[] = []
		if (node.ref.kind !== DependencyEndpointKind.Checkpoint) {
			entries.push({ label: 'Open note', icon: 'lucide:file-text', run: () => navigateToEntity(node.entity.id) })
		}

		entries.push(
			{ label: 'Remove from view', icon: 'lucide:eye-off', run: () => this.removeFromView(node, false) },
			{ label: 'Remove with connected group', icon: 'lucide:git-fork', run: () => this.removeFromView(node, true) },
			{ separator: true },
			{ label: 'Delete', icon: 'lucide:trash-2', danger: true, run: () => deleteEntity(node) }
		)
		return entries
	}

	private entityMenuEntries(node: CanvasNode): ContextMenuEntry[] {
		return [
			{ label: 'Open note', icon: 'lucide:file-text', run: () => navigateToEntity(node.entity.id) },
			node.ghostly
				? { note: 'A prerequisite outside this Onrush. It goes when the block is resolved.' }
				: { label: 'Remove from Onrush', icon: 'onrush', run: () => removeObjectiveFromOnrush(node.entity.id) },
			{ separator: true },
			{ label: 'Delete', icon: 'lucide:trash-2', danger: true, run: () => deleteEntity(node) }
		]
	}

	private checkpointMenuEntries(node: CanvasNode): ContextMenuEntry[] {
		// A milestone stands for the sprint's completion and is bound to it; it offers nothing to remove — only
		// its details. A ghostly checkpoint is context, not a member. Everything else the sprint tracks.
		if (node.milestone) {
			return [{ note: "The sprint's milestone — it stays for the sprint's life." }]
		}

		if (node.ghostly) {
			return [{ note: 'A checkpoint outside this Onrush, shown because it blocks a member.' }]
		}

		return [
			{ separator: true },
			{ label: 'Delete checkpoint', icon: 'lucide:trash-2', danger: true, run: () => deleteCheckpoint(node.entity.id) }
		]
	}

	private get additions(): ExpandingAction[] {
		if (this.mode === 'global') {
			return [
				{
					key: 'endpoint',
					icon: 'lucide:plus',
					label: 'Add to view',
					run: async () => await this.addEndpoint()
				},
				{
					key: 'endpoint-deps',
					icon: 'lucide:git-fork',
					label: 'Add with dependencies',
					run: async () => await this.addEndpoint(true)
				}
			]
		}

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

	/** The notch's way in: open the node's menu anchored to it. Right-click is the node's own controller's job. */
	@eventListener('requestNodeMenu')
	protected onRequestNodeMenu(e: CustomEvent<CanvasNodePointer>) {
		e.stopPropagation()
		const node = this.graph.nodes.find(candidate => candidate.key === e.detail.nodeKey)
		if (!node) {
			return
		}

		const element = this.shadowRoot?.querySelector(`.node[data-key="${CSS.escape(e.detail.nodeKey)}"]`)
		const anchor = element?.getBoundingClientRect()
		ContextMenu.open(anchor?.left ?? 0, anchor?.bottom ?? 0, this.nodeMenuSpecFor(node))
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
		// saved layout is adopted afresh once the new sprint (or the global snapshot) resolves.
		this.overrides = new Map()
		this.layoutSprintId = undefined
		this.globalLayoutAdopted = false
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

	/**
	 * Pins a node into the global context, optionally pulling its dependency neighbours in with it.
	 *
	 * A pin is a `kind:id` key; the picker excludes what is already pinned. With `withDependencies`, every
	 * endpoint one hop away along an edge touching the new node is pinned too, resolved to a title from the
	 * edge itself so it draws before anything is fetched.
	 */
	private async addEndpoint(withDependencies = false) {
		const present = new Set(this.pinned.map(hit => endpointKey({ kind: hit.kind, id: hit.id })))
		const hit = await SelectEndpointModal.prompt(present)
		if (!hit) {
			return
		}

		const additions = new Map<string, EndpointHit>()
		additions.set(endpointKey({ kind: hit.kind, id: hit.id }), hit)

		if (withDependencies) {
			const key = endpointKey({ kind: hit.kind, id: hit.id })
			for (const dependency of this.dependencies.value ?? []) {
				const ends = edgeEndpoints(dependency)
				const sourceKey = endpointKey(sourceRef(dependency))
				const targetKey = endpointKey(targetRef(dependency))
				const neighbour = sourceKey === key ? { key: targetKey, hit: ends.target }
					: targetKey === key ? { key: sourceKey, hit: ends.source }
						: undefined
				if (neighbour && !present.has(neighbour.key) && !additions.has(neighbour.key)) {
					additions.set(neighbour.key, neighbour.hit)
				}
			}
		}

		this.pinned = [...this.pinned, ...additions.values()]
		this.emitContextChanged()
	}

	/**
	 * Hides a node from the global context — a pure removal from the shown set, never a delete.
	 *
	 * With `withGroup`, its whole connected group of currently-pinned nodes goes with it, walked over the edges
	 * between pins. The dependencies themselves are untouched, so re-pinning any of these nodes brings its edges
	 * back.
	 */
	private removeFromView(node: CanvasNode, withGroup: boolean) {
		const doomed = withGroup ? this.connectedPins(node.key) : new Set([node.key])
		this.pinned = this.pinned.filter(hit => !doomed.has(endpointKey({ kind: hit.kind, id: hit.id })))
		this.emitContextChanged()
	}

	/** The keys of the pinned nodes reachable from a start key over the edges between pins (both directions). */
	private connectedPins(start: string): Set<string> {
		const pinnedKeys = new Set(this.pinned.map(hit => endpointKey({ kind: hit.kind, id: hit.id })))
		const adjacency = new Map<string, string[]>()
		const link = (a: string, b: string) => adjacency.set(a, [...(adjacency.get(a) ?? []), b])
		for (const dependency of this.dependencies.value ?? []) {
			const source = endpointKey(sourceRef(dependency))
			const target = endpointKey(targetRef(dependency))
			if (pinnedKeys.has(source) && pinnedKeys.has(target)) {
				link(source, target)
				link(target, source)
			}
		}

		const group = new Set<string>()
		const pending = [start]
		while (pending.length > 0) {
			const current = pending.pop()!
			if (!group.add(current)) {
				continue
			}

			pending.push(...adjacency.get(current) ?? [])
		}

		return group
	}

	/** Graduates a scratch global context into a `.p7tpx` file and opens it. */
	private async onSaveToFile() {
		await saveGlobalContextToFile(this.pinned, serializePositions(this.overrides))
	}

	private async reshape(edge: CanvasEdge, trigger: DependencyTrigger, constraint: DependencyConstraint) {
		await reshapeDependency(edge, trigger, constraint)
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
		this.syncNodeMeasurement()

		// Framed once, when there is finally something to frame, and never again — refitting on every change
		// would move the graph out from under someone reading it.
		if (!this.framed && this.layoutCache && this.layoutCache.layout.nodes.size > 0 && this.viewportElement) {
			this.framed = true
			this.frame(this.layoutCache.layout)
		}
	}

	/**
	 * Points the resize observer at the current node elements and takes a first measurement of them.
	 *
	 * Re-pointed only when the node set changes — pan, zoom and selection re-render without touching which
	 * elements exist, so the observer keeps watching the ones already on screen. Measuring here as well as in
	 * the observer's callback closes the gap on the render a node first appears in, so its edges attach without
	 * waiting for the asynchronous report.
	 */
	private syncNodeMeasurement() {
		const observer = this.resizeObserver
		if (!observer) {
			return
		}

		const signature = this.layoutCache?.signature
		if (signature === this.observedSignature) {
			return
		}

		this.observedSignature = signature
		observer.disconnect()

		const measured = new Map<string, NodeSize>()
		for (const node of Array.from(this.shadowRoot?.querySelectorAll<HTMLElement>('.node') ?? [])) {
			observer.observe(node)
			const key = node.getAttribute('data-key')
			if (key) {
				measured.set(key, { width: node.offsetWidth, height: node.offsetHeight })
			}
		}

		this.measured = measured
	}

	/** Re-reads any node whose size changed, so the edges follow a title edit or a late-loading content. */
	private onNodesResized(entries: readonly ResizeObserverEntry[]) {
		const next = new Map(this.measured)
		let changed = false
		for (const entry of entries) {
			const node = entry.target as HTMLElement
			const key = node.getAttribute('data-key')
			if (!key) {
				continue
			}

			const previous = next.get(key)
			if (!previous || previous.width !== node.offsetWidth || previous.height !== node.offsetHeight) {
				next.set(key, { width: node.offsetWidth, height: node.offsetHeight })
				changed = true
			}
		}

		if (changed) {
			this.measured = next
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
		if (this.mode === 'global') {
			// The file view feeds pins and layout together; adopt the positions once, then leave the reader's
			// drags to own them. A scratch tab has no saved layout and simply starts from the dagre placement.
			if (!this.globalLayoutAdopted) {
				this.globalLayoutAdopted = true
				this.overrides = parsePositions(this.savedLayout)
			}

			return
		}

		const sprint = this.sprint
		if (!sprint || this.layoutSprintId === sprint.id) {
			return
		}

		this.layoutSprintId = sprint.id
		this.overrides = parsePositions(sprint.graphLayout)
	}

	/**
	 * Persists the current arrangement after a short rest, coalescing a run of drags into one write.
	 *
	 * An onrush stores its layout in its own column; a global context has no column, so it hands its whole
	 * snapshot — pins and positions — to whatever host is listening, which is the file view when there is a
	 * file and nobody when the tab is scratch.
	 */
	private scheduleLayoutSave() {
		if (this.mode === 'global') {
			window.clearTimeout(this.layoutSaveTimer)
			this.layoutSaveTimer = window.setTimeout(() => this.emitContextChanged(), 600)
			return
		}

		const sprint = this.sprint
		if (!sprint) {
			return
		}

		window.clearTimeout(this.layoutSaveTimer)
		const id = sprint.id
		const payload = serializePositions(this.overrides)
		this.layoutSaveTimer = window.setTimeout(() => void saveGraphLayout(id, payload), 600)
	}

	/** Hands the current global snapshot to the host. Fired on a pin change at once, on a drag after a rest. */
	private emitContextChanged() {
		this.contextChanged.dispatch({ pinned: this.pinned, layout: serializePositions(this.overrides) })
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
