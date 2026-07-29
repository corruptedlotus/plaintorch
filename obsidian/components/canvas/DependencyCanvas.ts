import { Component, component, css, eventListener, html, nothing, property, query, repeat, state, svg } from '@a11d/lit'
import { DependencyConstraint, DependencyTrigger, type EntitySubscription } from '@pleiades/sdk'
import { Notice } from 'obsidian'
import { core, DerivedRef, navigateToEntity, type ExpandingAction } from '..'
import { addObjectiveToOnrush, createDependency, deleteDependency, removeObjectiveFromOnrush, reshapeDependency } from './canvasActions'
import { contextModeLabels, onrushContext, type CanvasContextMode } from './graphContext'
import { edgeCurve, entryPoint, exitPoint, layoutGraph, type CanvasLayout, type NodeBox, type Point } from './graphLayout'
import { describeEdge, effectiveConstraint, effectiveTrigger, wouldCycle, type CanvasEdge, type CanvasGraph } from './graphModel'
import { SelectObjectiveModal } from './SelectObjectiveModal'
import type { CanvasNodePointer } from './CanvasNodeItem'

/** Which gesture a pointer is currently carrying out. */
type Gesture =
	| { readonly sort: 'pan', readonly pointerId: number, readonly originX: number, readonly originY: number, readonly fromX: number, readonly fromY: number }
	| { readonly sort: 'drag', readonly pointerId: number, readonly nodeKey: string, readonly offsetX: number, readonly offsetY: number }
	| { readonly sort: 'link', readonly pointerId: number, readonly nodeKey: string, readonly at: Point }

/** What the popover is showing, when it is showing anything. */
type MenuTarget =
	| { readonly sort: 'edge', readonly edge: CanvasEdge }
	| { readonly sort: 'node', readonly nodeKey: string }

const minimumScale = 0.3
const maximumScale = 2.5

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
	@state() private gesture?: Gesture
	@state() private menu?: MenuTarget

	@query('.viewport') private readonly viewportElement!: HTMLElement
	@query('.menu') private readonly menuElement!: HTMLElement

	private readonly dependencies = new DerivedRef(this, core.repos.dependencyList)
	private readonly activeSprint = new DerivedRef(this, core.repos.onrushCurrent)
	private readonly planningSprint = new DerivedRef(this, core.repos.onrushPlanning)

	private storeSubscription?: EntitySubscription
	private layoutCache?: { readonly signature: string, readonly layout: CanvasLayout }
	private framed = false
	/** Whether the drag in progress has actually moved, which is what tells a drag from a click. */
	private dragged = false

	static override get styles() {
		return css`
			:host {
				display: grid;
				grid-template-rows: auto 1fr;
				height: 100%;
				min-height: 0;
				font-family: var(--font-interface);
				--p7t-canvas-node-width: 15em;
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
				stroke: color-mix(in srgb, var(--text-normal) 45%, transparent);
			}

			.edge.satisfied {
				stroke: color-mix(in srgb, var(--text-success, seagreen) 70%, var(--text-normal));
			}

			.edge.linking {
				stroke: var(--p7t-flare-accent, var(--interactive-accent));
				stroke-dasharray: 5 4;
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
	}

	/** The sprint the current mode reads. */
	private get sprint() {
		return this.mode === 'onrush-planning' ? this.planningSprint.value : this.activeSprint.value
	}

	private get graph(): CanvasGraph {
		return onrushContext(this.sprint, this.dependencies.value ?? [])
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

	protected override get template() {
		const graph = this.graph
		const layout = this.layoutFor(graph)
		const boxes = this.boxesFor(layout)
		// An edge that is not yet satisfied is holding its dependant back. Only edges inside this context are
		// counted, so a node blocked from outside the sprint does not read as blocked here.
		const blocked = new Set(graph.edges.filter(edge => !edge.dependency.satisfied).map(edge => edge.target))

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
								<path d='M 0 0 L 8 4 L 0 8 z' fill='color-mix(in srgb, var(--text-normal) 45%, transparent)'></path>
							</marker>
							<marker id='arrow-satisfied' viewBox='0 0 8 8' refX='7' refY='4' markerWidth='7' markerHeight='7' orient='auto-start-reverse'>
								<path d='M 0 0 L 8 4 L 0 8 z' fill='color-mix(in srgb, var(--text-success, seagreen) 70%, var(--text-normal))'></path>
							</marker>
						</defs>
						${repeat(graph.edges, edge => edge.key, edge => this.edgeTemplate(edge, boxes))}
						${this.linkTemplate(boxes)}
					</svg>
					${repeat(graph.nodes, node => node.key, node => {
						const box = boxes.get(node.key)
						return !box ? nothing : html`
							<div
								class='node'
								data-key=${node.key}
								style='transform: translate(${box.x}px, ${box.y}px)'
								@pointerdown=${(e: PointerEvent) => this.onNodePointerDown(e, node.key, box)}
								@click=${{ handleEvent: (e: Event) => this.onNodeClick(e), capture: true }}>
								<p7t-canvas-node
									interactive
									.nodeKey=${node.key}
									.entity=${node.entity}
									.kind=${node.ref.kind}
									?selected=${this.selected === node.key}
									?blocked=${blocked.has(node.key)}
									?linking=${this.gesture?.sort === 'link' && this.gesture.nodeKey !== node.key}>
								</p7t-canvas-node>
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

	private edgeTemplate(edge: CanvasEdge, boxes: ReadonlyMap<string, NodeBox>) {
		const source = boxes.get(edge.source)
		const target = boxes.get(edge.target)
		if (!source || !target) {
			return nothing
		}

		const path = edgeCurve(exitPoint(source), entryPoint(target))
		const satisfied = edge.dependency.satisfied
		return svg`
			<path class='edge ${satisfied ? 'satisfied' : 'pending'}' d=${path} marker-end='url(#${satisfied ? 'arrow-satisfied' : 'arrow-pending'})'></path>
			<path class='edge-hit' d=${path} @click=${(e: MouseEvent) => void this.openMenu(e.clientX, e.clientY, { sort: 'edge', edge })}></path>
		`
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
		return html`
			<div class='menu-title'>This ${describeEdge(edge.dependency)}</div>
			<button class='menu-item' aria-pressed=${trigger === DependencyTrigger.OnFinish}
				@click=${() => void this.reshape(edge, DependencyTrigger.OnFinish, constraint)}>
				Satisfied when it finishes
			</button>
			<button class='menu-item' aria-pressed=${trigger === DependencyTrigger.OnBegin}
				@click=${() => void this.reshape(edge, DependencyTrigger.OnBegin, constraint)}>
				Satisfied when it begins
			</button>
			<div class='menu-separator'></div>
			<button class='menu-item' aria-pressed=${constraint === DependencyConstraint.ToBegin}
				@click=${() => void this.reshape(edge, trigger, DependencyConstraint.ToBegin)}>
				Gates the dependant's begin
			</button>
			<button class='menu-item' aria-pressed=${constraint === DependencyConstraint.ToFinish}
				@click=${() => void this.reshape(edge, trigger, DependencyConstraint.ToFinish)}>
				Gates the dependant's finish
			</button>
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

		return html`
			<div class='menu-title'>${node.entity.title}</div>
			<button class='menu-item' @click=${() => void this.run(async () => { await navigateToEntity(node.entity.id) })}>
				Open note
			</button>
			<button class='menu-item' @click=${() => void this.run(async () => await removeObjectiveFromOnrush(node.entity.id))}>
				Remove from Onrush
			</button>
		`
	}

	private get additions(): ExpandingAction[] {
		return [{
			key: 'objective',
			icon: 'objective',
			label: 'Add objective',
			run: async () => await this.addObjective()
		}]
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

	private setMode(mode: CanvasContextMode) {
		if (this.mode === mode) {
			return
		}

		this.mode = mode
		// A different context is a different graph; positions from the last one mean nothing in it.
		this.overrides = new Map()
		this.selected = undefined
		this.framed = false
	}

	/** Puts every node back where the layout placed it, and frames the graph again. */
	private reset(layout: CanvasLayout) {
		this.overrides = new Map()
		this.frame(layout)
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
		if (e.button !== 0) {
			return
		}

		// Only the empty space behind the graph reaches here; a node stops its own pointerdown.
		this.selected = undefined
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

	private onNodePointerDown(e: PointerEvent, nodeKey: string, box: NodeBox) {
		if (e.button !== 0) {
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
			offsetY: point.y - box.y
		}
		this.capture(e.pointerId)
	}

	/**
	 * Swallows the click a finished drag leaves behind.
	 *
	 * Releasing a dragged node over its own title would otherwise reach the title's handler and open the
	 * note. Caught on the way down, before it reaches the item at all, because the item is what would act
	 * on it.
	 */
	private onNodeClick(e: Event) {
		if (!this.dragged) {
			return
		}

		e.stopPropagation()
		e.preventDefault()
		this.dragged = false
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
		// Framed once, when there is finally something to frame, and never again — refitting on every change
		// would move the graph out from under someone reading it.
		if (!this.framed && this.layoutCache && this.layoutCache.layout.nodes.size > 0 && this.viewportElement) {
			this.framed = true
			this.frame(this.layoutCache.layout)
		}
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
