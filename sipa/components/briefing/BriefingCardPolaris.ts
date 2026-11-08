import { component, css, eventListener, html, nothing, state, type PropertyValues } from "@a11d/lit"
import { PolarisBriefingBase } from "./PolarisBriefingBase"
import { Objective, ObjectiveStatus, Reflective } from "@pleiades/sdk"
import { addObjectiveToPolaris, concludeConfirmation, confirmAction, core, isObjectiveInCycle, revalidatePolarisCycles, TransferController } from ".."
import { toast } from "../../host"

/**
 * The briefing's Polaris card: the cycle of the day being worked — its timer, its reflectives, its executives and
 * the progress against their estimates, and Conclude. The list, the creation row and the recycle bin are the shared
 * {@link PolarisBriefingBase}.
 *
 * The card shows whatever the briefing calls the current cycle, which is today's cycle when none is active — so a day
 * planned ahead (on the forecast) and not yet begun shows here too, as a plan: no timer yet, and Begin in place of
 * Conclude.
 */
@component('p7t-briefing-polaris')
export class BriefingCardPolaris extends PolarisBriefingBase {
	override readonly icon = 'polaris'
	override readonly backdrop = 'rising-stars'

	// The self-watch now lives on BriefingCard, so every card re-renders on an in-place edit — this one no
	// longer needs its own.

	@state() private reflectivesExpanded = false

	/** Whether the cycle shown has begun — a cycle planned for today has not, until Begin. */
	private get begun() {
		return !!this.data?.startTime
	}

	/**
	 * Takes objectives dropped from any host offering them (the Onrush card). The receive is translated: rather
	 * than the default append into a bag, an accepted objective is planned into the current cycle through the
	 * one add-to-polaris path every surface shares, so the repository — not this card — owns what the cycle
	 * then shows. Candidacy is decided at drag start: a cycle must be running, and the objective must be live
	 * and not already in it (a cycle holds one instance of an objective).
	 *
	 * Once taken, the new row asks for its estimate. The drop settles without waiting for the answer.
	 */
	protected readonly transfer = new TransferController<Objective>(this, {
		accepts: ['Objective'],
		canAccept: objective => !!this.data
			&& objective.status < ObjectiveStatus.Archived
			&& !isObjectiveInCycle(objective.id, this.data),
		accept: async objective => {
			const executive = await addObjectiveToPolaris(objective)
			if (executive) {
				void this.offerEstimation(executive)
			}

			return !!executive
		},
	})

	static override get styles() {
		return css`
			${super.styles}

			/* The timer with the timeframe(s) in play beside it, at chip scale against the heading's size. */
			.heading-line {
				display: inline-flex;
				align-items: baseline;
				gap: .8ch;
			}

			.heading-line p7t-active-timeframes {
				font-size: .5em;
				font-weight: 400;
				line-height: 1;
			}

			/* A planned cycle has no time to show yet: its state, faint, where the timer will run. */
			.heading-line .unbegun {
				color: var(--text-muted);
				font-weight: 300;
			}

			.group-header {
				display: flex;
				align-items: center;
				gap: .5em;
				padding: .4em .5em;
				border-radius: 12px;
				cursor: pointer;
				user-select: none;
				transition: background-color .3s ease;
			}

			.group-header:hover {
				background-color: color-mix(in srgb, var(--text-normal) 8%, transparent);
			}

			.group-icon {
				width: 1.6em;
				height: 1.6em;
				opacity: .85;
			}

			.group-title {
				flex: 1;
				font-weight: 500;
				font-size: 1.05em;
			}

			.group-count {
				font-family: var(--font-text);
				font-weight: 500;
				opacity: .7;
			}

			.chevron {
				width: 1.2em;
				height: 1.2em;
				opacity: .6;
				transition: transform .3s ease;
			}

			.chevron.expanded {
				transform: rotate(90deg);
			}

			.reflectives-list {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				padding-inline-start: 1.4em;
				margin-block: .1em .3em;
			}
		`
	}

	protected override willUpdate(changed: PropertyValues<this>) {
		super.willUpdate(changed)
		this.preHeading = this.begun || !this.data ? 'Active Polaris Cycle' : 'Planned Polaris Cycle'
	}

	protected override get offlineTemplate() {
		return html`
			<span class='no-data'>The Polaris Rests in the Void</span>
			<p7t-button @click=${() => this.begin()} large class='start-button' icon='polaris'>Begin Cycle</p7t-button>
		`
	}

	override get headingTemplate() {
		return html`
			<span class='heading-line'>
				${!this.begun
					? html`<span class='unbegun'>Not Started</span>`
					: html`<p7t-elapsed-view .epoch=${this.data!.startTime}></p7t-elapsed-view>`}
				<p7t-active-timeframes></p7t-active-timeframes>
			</span>
		`
	}

	protected override get listContent() {
		return html`
			${this.reflectivesGroup}
			${this.executivesTemplate}
			${this.creationTemplate}
		`
	}

	private get reflectivesGroup() {
		const reflectives = this.data!.reflectives ?? []
		if (reflectives.length === 0) return nothing

		const done = reflectives.filter(reflective => reflective.executed).length
		return html`
			<div class='reflectives-group'>
				<div class='group-header' @click=${() => this.reflectivesExpanded = !this.reflectivesExpanded}>
					<p7t-icon class='group-icon' icon='reflective'></p7t-icon>
					<span class='group-title'>Daily Reflectives</span>
					<span class='group-count'>${done}/${reflectives.length}</span>
					<p7t-icon class='chevron ${this.reflectivesExpanded ? 'expanded' : ''}' icon='lucide:chevron-right'></p7t-icon>
				</div>
				${!this.reflectivesExpanded ? nothing : html`
					<div class='reflectives-list'>
						${reflectives.map(reflective => html`
							<p7t-reflective-item interactive .reflective=${reflective}></p7t-reflective-item>
						`)}
					</div>
				`}
			</div>
		`
	}

	protected override get footerTemplate() {
		return html`
			<p7t-value-progress icon='state-polaris' value=${this.totalElapsed} max=${this.totalEstimation}
				.valueTemplate=${(value: number) => html`<p7t-time-unit .value=${value}></p7t-time-unit>`}
				.maxTemplate=${(max: number) => html`<p7t-time-unit .value=${max}></p7t-time-unit>${this.totalEstimationHasUnknown ? html`<span>+</span>` : nothing}`}
			></p7t-value-progress>
			${this.begun
				? html`<p7t-button @click=${(e: Event) => this.conclude(e)}>Conclude</p7t-button>`
				: html`<p7t-button icon='polaris' @click=${() => void this.beginPlanned()}>Begin</p7t-button>`}
		`
	}

	/** A reflective toggled its executed flag: reconcile the local copy so `done/total` updates, then revalidate. */
	@eventListener('reflectivechange')
	protected onReflectiveChange(e: CustomEvent<Reflective>) {
		e.stopPropagation()
		if (!this.data) return
		this.data = {
			...this.data,
			reflectives: this.data.reflectives.map(reflective => reflective.id === e.detail.id ? e.detail : reflective),
		}
		void core.repos.briefing.revalidateIfObserved()
	}

	/**
	 * Starts the day's cycle. A cycle's id is its day, so once today's has concluded there is none to start: the core
	 * hands the concluded one back, which the card must not take up as if it ran again.
	 */
	private async begin() {
		if (this.data) return
		const polaris = await core.polaris.startNew()
		if (polaris?.startTime && !polaris.endTime) {
			this.data = polaris
		}
		else if (polaris?.endTime) {
			toast("Today's Polaris cycle has already concluded. The next one begins tomorrow.", 'info')
		}
	}

	/**
	 * Begins the day's planned cycle in place — what it holds is the day's plan, and it now runs. Its id is its day, so
	 * a new cycle would collide; the begin goes through the repository so the canonical instance updates with it.
	 */
	private async beginPlanned() {
		const cycle = this.data
		if (!cycle || this.begun) return
		const begun = await core.repos.polaris.mutate(cycle.id, () => core.polaris.beginCycle(cycle.id))
		if (!begun) {
			toast(`PLAINTORCH could not begin ${cycle.title}.`, 'error')
			return
		}

		this.data = begun
		await revalidatePolarisCycles()
	}

	/** Concludes the running cycle now, once confirmed beside the button. */
	private async conclude(e: Event) {
		if (!this.data || !this.begun) return
		if (!await confirmAction(e, concludeConfirmation(`Conclude "${this.data.title}"? It ends now.`)) || !this.data) return
		core.polaris.end(new Date().toISOString()).then(polaris => {
			if (!!polaris) {
				this.data = undefined
				// The briefing's current cycle falls back to today's, which may be a day planned ahead: show it at once.
				void revalidatePolarisCycles()
			}
		})
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-polaris': BriefingCardPolaris
	}
}
