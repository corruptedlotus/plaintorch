import { component, css, html, nothing, state, type PropertyValues } from '@a11d/lit'
import {
	DecreeStatus,
	DependencyConstraint,
	DependencyEndpointKind,
	DependencyOrigin,
	DependencyTrigger,
	DirectiveStatus,
	enteredStates,
	FateStatus,
	LifecycleState,
	lifecycleStatus,
	LunarDirectiveStatus,
	ObjectiveStatus,
	StateDecisionOutcome,
	typeNameOf,
	type LifecycleKind,
	type LifecycleStatus,
	type StateEnforcementProblem,
} from '@pleiades/sdk'
import { InteractionBubbleBase, type InteractionBubbleAnchor, type InteractionBubbleOptions } from '../design/InteractionBubbleBase'
import { core } from '../data/coreProvider'
import { DerivedRef } from '../data/DerivedRef'
import { EntityRef } from '../data/EntityRef'
import { describeMove } from '../system/StateEnforcementModal'
import { statusDescriptors, type Status } from './StatusItem'

/** An item whose state the state bubble shifts: an objective, a stellar or lunar directive, a fate or a decree. */
export interface StatefulEntity {
	readonly id: string
	readonly title?: string
	readonly status: number
}

/** What a state bubble is raised with. */
export interface StateBubbleOptions extends InteractionBubbleOptions<number> {
	/** The item whose state the bubble shifts. */
	readonly entity: StatefulEntity
}

/** What the lower drawer shows: a held-back change to confirm, or a message that goes by itself. */
type StateNotice =
	| { readonly kind: 'confirm', readonly status: number, readonly problem: StateEnforcementProblem }
	| { readonly kind: 'message', readonly text: string }

/** How long a refusal, or any other failure, stays in the drawer. */
const messageMs = 2000

/** How each kind's state is written: a workflow shift for objectives and directives, the update for the declaratives. */
const stateWrites: Readonly<Record<string, (id: string, status: number) => Promise<unknown>>> = {
	Objective: (id, status) => core.objectives.shiftWorkflow(id, { status: status as ObjectiveStatus }),
	StellarDirective: (id, status) => core.directives.shiftStellarWorkflow(id, { status: status as DirectiveStatus }),
	LunarDirective: (id, status) => core.directives.shiftLunarWorkflow(id, { status: status as LunarDirectiveStatus }),
	Fate: (id, status) => core.declaratives.updateFate(id, { status: status as FateStatus }),
	Decree: (id, status) => core.declaratives.updateDecree(id, { status: status as DecreeStatus }),
}

/** The dependency endpoint each kind is a target as; a kind that is none is never held back by an edge. */
const endpointKinds: Readonly<Record<string, DependencyEndpointKind>> = {
	Objective: DependencyEndpointKind.Objective,
	StellarDirective: DependencyEndpointKind.Directive,
	Fate: DependencyEndpointKind.Fate,
}

/**
 * Changes an item's state (PEP103): every status its kind has, in a row, the main line joined by arrows that shorten when
 * the row is crowded, and the statuses that set the item aside after a divider. The status it is in carries the accent.
 *
 * Where the client holds the dependency graph, a status the item's dependencies would hold back — an unmet edge into a
 * state the move enters, or an open child of a directive becoming Over — is greyed, whatever the enforcement level, but
 * still offered: picking it lets the core decide.
 *
 * Picking a status sends the change through the item's reference and waits for the core's answer, the thin ribbon along
 * the top running meanwhile. The answer is the bubble's to show, never the client-wide prompt's
 * ({@link EntityRef.awaitCommit}):
 * - accepted: the bubble closes with the new status;
 * - held back for confirmation (Warn): the drawer below opens with what blocks the change and what going ahead would also
 *   move, to go ahead with — the change repeated, forcing the compensations — or to cancel;
 * - refused (Block, or nothing the core could move would resolve it), or failed: the drawer shows why for two seconds.
 *
 * Nothing changes on screen before the core accepts, so a row whose state filters it out of a view stays until it has
 * really moved.
 */
/** The state a dependency needs of its source: the finish for a finish trigger (the default), the start for a begin one. */
function needed(edge: { trigger?: DependencyTrigger | undefined }): LifecycleState {
	return (edge.trigger ?? DependencyTrigger.OnFinish) === DependencyTrigger.OnFinish ? LifecycleState.Finished : LifecycleState.Started
}

@component('p7t-state-bubble')
export class StateBubble extends InteractionBubbleBase<number, StateBubbleOptions> {
	/** The status being sent, while the core's answer is awaited. */
	@state() private pending?: number
	@state() private notice?: StateNotice
	private noticeTimer?: ReturnType<typeof setTimeout>

	/** The item, observed: a shift made anywhere redraws the bubble. */
	private readonly ref = new EntityRef<StatefulEntity>(this, () => core.repos.forTypeName<StatefulEntity>(this.typeName), () => this.options?.entity.id)
	private readonly lifecycles = new DerivedRef(this, core.repos.lifecycles)
	/** The dependency graph as the client holds it, read only for an item that can be the target of an edge. */
	private readonly dependencies = new DerivedRef(this, core.repos.dependencyList, () => this.endpointKind === undefined ? undefined : '')

	constructor() {
		super(0)
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				max-width: min(calc(100vw - 16px), 38rem);
				padding: .5rem .55rem .45rem;
			}

			.body {
				gap: 0;
			}

			.label {
				position: absolute;
				width: 1px;
				height: 1px;
				overflow: hidden;
				clip-path: inset(50%);
				white-space: nowrap;
			}

			/* A thin indeterminate bar along the top edge while an answer is awaited. */
			.ribbon {
				position: absolute;
				inset-inline: 12px;
				top: 0;
				height: 2px;
				overflow: hidden;
				border-radius: 2px;
				opacity: 0;
				pointer-events: none;
				transition: opacity .2s ease;
			}

			.ribbon[active] {
				opacity: 1;
			}

			.ribbon::before {
				content: '';
				position: absolute;
				inset-block: 0;
				width: 35%;
				border-radius: inherit;
				background: var(--p7t-bubble-accent);
				animation: ribbon 1.1s ease-in-out infinite;
			}

			@keyframes ribbon {
				from { transform: translateX(-100%); }
				to { transform: translateX(290%); }
			}

			.states {
				display: flex;
				align-items: flex-start;
				min-width: 0;
			}

			.state {
				flex: 0 1 auto;
				min-width: 0;
				display: flex;
				flex-direction: column;
				align-items: center;
				gap: .15rem;
				padding: .3rem .4rem;
				border: 1px solid transparent;
				border-radius: 7px;
				background: none;
				font-size: .72rem;
				transition: background-color .15s ease, color .15s ease, opacity .15s ease, border-color .15s ease;
			}

			.state p7t-icon {
				width: 1.15rem;
				height: 1.15rem;
			}

			.state .name {
				max-width: 100%;
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
			}

			.state:hover {
				background-color: color-mix(in srgb, currentColor 9%, transparent);
			}

			/* A light ring: the accent marks the state the item is in, not where the focus is. */
			.state:focus-visible {
				outline: 1px solid color-mix(in srgb, var(--p7t-bubble-accent) 65%, transparent);
				outline-offset: 0;
			}

			.state[active] {
				color: var(--text-accent, #a68bd6);
				background-color: color-mix(in srgb, var(--interactive-accent, #71549c) 16%, transparent);
			}

			/* Held back by what the item depends on: greyed, still offered. */
			.state[blocked] {
				opacity: .42;
			}

			.state[blocked]:hover,
			.state[blocked]:focus-visible {
				opacity: .75;
			}

			.state[pending] {
				border-color: color-mix(in srgb, var(--p7t-bubble-accent) 60%, transparent);
			}

			/*
			 * A thin arrow level with the glyphs, giving way first when the row is crowded: it shrinks a hundred times as
			 * readily as a status, so the labels only give way once every arrow is down to its stub. Its length comes from
			 * an empty child, since the row is sized by what its items hold and an empty arrow would hold nothing.
			 */
			.arrow {
				flex: 0 100 auto;
				min-width: .35rem;
				height: 1px;
				margin: .875rem .1rem 0;
				position: relative;
				background: currentColor;
				opacity: .35;
			}

			.arrow > i {
				display: block;
				width: 1.4rem;
			}

			.arrow::after {
				content: '';
				position: absolute;
				right: -1px;
				top: -2.5px;
				border-block: 3px solid transparent;
				border-inline-start: 4px solid currentColor;
			}

			/* The statuses that set the item aside are no step on the way: a divider, not an arrow. */
			.aside {
				flex: none;
				width: 1px;
				align-self: stretch;
				margin-inline: .45rem;
				background: currentColor;
				opacity: .2;
			}

			/* Folded under the row until there is something to say. */
			.drawer {
				display: grid;
				grid-template-rows: 0fr;
				transition: grid-template-rows .22s ease;
			}

			.drawer[open] {
				grid-template-rows: 1fr;
			}

			.notice {
				min-height: 0;
				overflow: hidden;
				display: flex;
				flex-direction: column;
				gap: .45rem;
			}

			.drawer[open] .notice {
				margin-top: .45rem;
				padding-top: .5rem;
				border-top: 1px solid color-mix(in srgb, var(--text-normal, #f2ebe4) 14%, transparent);
			}

			.message {
				margin: 0;
				overflow-wrap: anywhere;
			}

			.refusal {
				color: var(--text-warning, #e0a95f);
			}

			.moves {
				margin: 0;
				padding-inline-start: 1.1rem;
				color: var(--text-muted, #b9aec3);
			}
		`
	}

	/** The item's kind, as its runtime type name. */
	private get typeName() {
		return typeNameOf(this.options?.entity)
	}

	private get endpointKind() {
		const typeName = this.typeName
		return typeName === undefined ? undefined : endpointKinds[typeName]
	}

	/** The item's status as it stands, followed live. */
	private get current() {
		return this.ref.value?.status ?? this.options?.entity.status
	}

	private get lifecycle(): LifecycleKind | undefined {
		return this.lifecycles.value?.find(kind => kind.kind === this.typeName)
	}

	protected override get bodyRole() {
		return 'alertdialog' as const
	}

	protected override get content() {
		const statuses = this.lifecycle?.statuses ?? []
		const main = statuses.filter(status => status.state !== LifecycleState.Deferred)
		const aside = statuses.filter(status => status.state === LifecycleState.Deferred)
		const title = this.ref.value?.title ?? this.options?.entity.title
		return html`
			<div class='ribbon' ?active=${this.pending !== undefined || this.lifecycles.loading}></div>
			<span id='label' class='label'>Change the state${title ? ` of ${title}` : ''}</span>
			<div class='states' @keydown=${(e: KeyboardEvent) => this.moveFocus(e)}>
				${main.map((status, index) => html`${index === 0 ? nothing : html`<span class='arrow' aria-hidden='true'><i></i></span>`}${this.optionTemplate(status)}`)}
				${aside.length === 0 || main.length === 0 ? nothing : html`<span class='aside' aria-hidden='true'></span>`}
				${aside.map(status => this.optionTemplate(status))}
			</div>
			<div class='drawer' ?open=${!!this.notice}>
				<div class='notice'>${this.noticeTemplate}</div>
			</div>
		`
	}

	private optionTemplate(status: LifecycleStatus) {
		const descriptor = statusDescriptors[status.name as Status]
		const label = descriptor?.label ?? status.name
		const active = status.value === this.current
		const hold = active ? undefined : this.holdOf(status)
		return html`
			<button class='state'
				?active=${active}
				?blocked=${!!hold}
				?pending=${status.value === this.pending}
				?autofocus=${active}
				aria-pressed=${active ? 'true' : 'false'}
				title=${hold ? `${label}: ${hold}` : label}
				@click=${() => void this.pick(status.value)}>
				<p7t-icon icon=${descriptor?.icon ?? 'state-zero'}></p7t-icon>
				<span class='name'>${label}</span>
			</button>
		`
	}

	private get noticeTemplate() {
		const notice = this.notice
		if (!notice) {
			return nothing
		}

		if (notice.kind === 'message') {
			return html`<p class='message refusal' role='status'>${notice.text}</p>`
		}

		const moves = notice.problem.compensations
		return html`
			<p class='message' role='status'>${notice.problem.message}</p>
			${moves.length === 0 ? nothing : html`<ul class='moves'>${moves.map(move => html`<li>${describeMove(move)}</li>`)}</ul>`}
			<div class='actions'>
				<button class='dismiss' @click=${() => this.clearNotice()}>Cancel</button>
				<button class='commit' @click=${() => void this.goAhead()}>Go ahead</button>
			</div>
		`
	}

	protected override updated(changed: PropertyValues<this>) {
		super.updated(changed)
		// The confirmation is the decision now in front of the user, so it takes the focus as it opens.
		if ((changed as PropertyValues).has('notice') && this.notice?.kind === 'confirm') {
			this.renderRoot.querySelector<HTMLElement>('.drawer .commit')?.focus({ preventScroll: true })
		}
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		clearTimeout(this.noticeTimer)
	}

	/**
	 * Why moving to a status would be held back by what the item depends on, read off the graph the client holds — an
	 * unmet edge constrained to a state the move enters, or, for a directive becoming Over, an open child — or nothing
	 * when nothing holds it or the graph is not at hand. The enforcement level plays no part: this is what the rules say.
	 */
	private holdOf(status: LifecycleStatus): string | undefined {
		const lifecycle = this.lifecycle
		const endpoint = this.endpointKind
		const graph = this.dependencies.value
		const current = lifecycle && this.current !== undefined ? lifecycleStatus(lifecycle, this.current) : undefined
		const id = this.options?.entity.id
		if (!lifecycle || endpoint === undefined || !graph || !current || !id) {
			return undefined
		}

		// A move back out of what a dependency needs, while its target already relies on it, reopens it (PEP103 patch 4).
		// Setting an item aside never does.
		const back = status.state < current.state && status.state !== LifecycleState.Deferred
		const relying = !back ? [] : graph.filter(edge => edge.reliedOn
			&& edge.sourceKind === endpoint
			&& edge.sourceId === id
			&& needed(edge) > status.state
			&& needed(edge) <= current.state)
		if (relying.length > 0) {
			return `relied on by ${relying.length} ${relying.length === 1 ? 'item' : 'items'}`
		}

		const entered = enteredStates(lifecycle, current.state, status.state)
		const becomesOver = this.typeName === 'StellarDirective' && status.value === DirectiveStatus.Over
		const holding = graph.filter(edge => !edge.satisfied
			&& edge.targetKind === endpoint
			&& edge.targetId === id
			&& ((entered.includes(LifecycleState.Started) && (edge.constraint ?? DependencyConstraint.ToBegin) === DependencyConstraint.ToBegin)
				|| (entered.includes(LifecycleState.Finished) && edge.constraint === DependencyConstraint.ToFinish)
				|| (becomesOver && edge.origin === DependencyOrigin.Intrinsic)))
		if (holding.length === 0) {
			return undefined
		}

		const children = holding.filter(edge => edge.origin === DependencyOrigin.Intrinsic).length
		const edges = holding.length - children
		const parts = [
			edges === 0 ? undefined : `${edges} ${edges === 1 ? 'dependency' : 'dependencies'}`,
			children === 0 ? undefined : `${children} open ${children === 1 ? 'child' : 'children'}`,
		].filter(part => part !== undefined)
		return `blocked by ${parts.join(' and ')}`
	}

	private async pick(status: number) {
		if (this.pending !== undefined) {
			return
		}

		// Picking the state it is in changes nothing.
		if (status === this.current) {
			this.dismiss()
			return
		}

		this.clearNotice()
		await this.send(status, false)
	}

	private async goAhead() {
		const notice = this.notice
		if (notice?.kind !== 'confirm') {
			return
		}

		this.clearNotice()
		await this.send(notice.status, true)
	}

	/** Sends the change through the item's reference, awaiting the core's answer for it alone. */
	private async send(status: number, force: boolean) {
		const typeName = this.typeName
		const write = typeName === undefined ? undefined : stateWrites[typeName]
		if (!write) {
			this.flash('PLAINTORCH cannot change the state of this item.')
			return
		}

		this.pending = status
		const { saved, enforcement } = await this.ref.awaitCommit(entity => write(entity.id, status), { force })
		this.pending = undefined
		if (saved) {
			this.commit(status)
			return
		}

		if (enforcement?.outcome === StateDecisionOutcome.Confirm && !force) {
			this.notice = { kind: 'confirm', status, problem: enforcement }
			return
		}

		this.flash(enforcement?.message ?? 'PLAINTORCH could not save that change.')
	}

	/** Shows a message in the drawer for a moment. */
	private flash(text: string) {
		clearTimeout(this.noticeTimer)
		this.notice = { kind: 'message', text }
		this.noticeTimer = setTimeout(() => {
			if (this.notice?.kind === 'message') {
				this.notice = undefined
			}
		}, messageMs)
	}

	private clearNotice() {
		clearTimeout(this.noticeTimer)
		this.notice = undefined
	}

	/** Left and right step along the row, Home and End go to its ends. */
	private moveFocus(e: KeyboardEvent) {
		const options = Array.from(this.renderRoot.querySelectorAll<HTMLElement>('.state'))
		const index = options.findIndex(option => option === this.shadowRoot?.activeElement)
		const next = e.key === 'ArrowRight' ? index + 1
			: e.key === 'ArrowLeft' ? index - 1
			: e.key === 'Home' ? 0
			: e.key === 'End' ? options.length - 1
			: undefined
		if (next === undefined || options.length === 0) {
			return
		}

		e.preventDefault()
		options[Math.max(0, Math.min(options.length - 1, next))]?.focus()
	}
}

/**
 * Changes an item's state through a {@link StateBubble} pointing at `anchor` — best the element pressed, or the event
 * whose handler calls this, before anything is awaited. Resolves with the status the item is in once the bubble closes:
 * the one it was moved to, or its own when the bubble was dismissed.
 */
export function shiftState(anchor: InteractionBubbleAnchor, entity: StatefulEntity): Promise<number> {
	return StateBubble.prompt(anchor, { entity }, entity.status)
}

/**
 * A `p7t-editable`'s edit through the state bubble, pointing at the field: resolves with the status the item was moved
 * to, and rejects — a cancel, to the field — when it was left as it was. The bubble saves the change itself, so the field
 * carries no binding for it.
 *
 * ```ts
 * html`<p7t-editable .value=${entity.status} .doEdit=${(_: unknown, field: HTMLElement) => editState(field, entity)}>…</p7t-editable>`
 * ```
 */
export async function editState(field: HTMLElement, entity: StatefulEntity | undefined): Promise<number> {
	if (!entity) {
		throw new Error('There is no item to change the state of.')
	}

	const before = entity.status
	const after = await shiftState(field, entity)
	if (after === before) {
		throw new Error('The state was left as it was.')
	}

	return after
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-state-bubble': StateBubble
	}
}
