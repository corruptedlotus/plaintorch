import { component, css, eventListener, html, nothing, state } from "@a11d/lit"
import { BriefingCard } from "./BriefingCard"
import { Attentive, Objective, ObjectiveStatus, PolarisCycle, Reflective } from "@pleiades/sdk"
import { addObjectiveToPolaris, core, isObjectiveInCycle, TransferController, type CreationRowCreated, type PolarisActivityCreated } from ".."

@component('p7t-briefing-polaris')
export class BriefingCardPolaris extends BriefingCard<PolarisCycle> {
	override readonly icon = 'polaris'
	override readonly preHeading = 'Active Polaris Cycle'

	// The self-watch now lives on BriefingCard, so every card re-renders on an in-place edit — this one no
	// longer needs its own.

	@state() private reflectivesExpanded = false

	/** Whether the inline creation row is open at the foot of the list. */
	@state() private creating = false

	/**
	 * Takes objectives dropped from any host offering them (the Onrush card). The receive is translated: rather
	 * than the default append into a bag, an accepted objective is planned into the current cycle through the
	 * one add-to-polaris path every surface shares, so the repository — not this card — owns what the cycle
	 * then shows. Candidacy is decided at drag start: a cycle must be running, and the objective must be live
	 * and not already in it (a cycle holds one instance of an objective).
	 */
	protected readonly transfer = new TransferController<Objective>(this, {
		accepts: ['Objective'],
		canAccept: objective => !!this.data
			&& objective.status < ObjectiveStatus.Archived
			&& !isObjectiveInCycle(objective.id, this.data),
		accept: objective => addObjectiveToPolaris(objective),
	})

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--p7t-flare-accent: var(--p7t-accent-polaris);
				outline: 2px dashed transparent;
				outline-offset: -2px;
				transition: outline-color .3s ease, box-shadow .3s ease;
			}

			/* A compatible objective is in the air: show the card as a place it can land. */
			:host([transfer-target]) {
				outline-color: color-mix(in srgb, var(--p7t-flare-accent) 55%, transparent);
			}

			/* ...and it is over the card. */
			:host([transfer-over]) {
				outline-style: solid;
				outline-color: var(--p7t-flare-accent);
				box-shadow: inset 0 0 0 100vmax color-mix(in srgb, var(--p7t-flare-accent) 8%, transparent);
			}

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

			.add-button {
				margin: .4em 1.2em;

				&::part(icon) {
					height: 1.4em;
					width: 1.4em;
				}
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

	protected override get offlineTemplate() {
		return html`
			<span class='no-data'>The Polaris Rests in the Void</span>
			<p7t-button @click=${() => this.begin()} large class='start-button' icon='polaris'>Begin Cycle</p7t-button>
		`
	}

	override get headingTemplate() {
		return html`
			<span class='heading-line'>
				<p7t-elapsed-view .epoch=${this.data!.startTime}></p7t-elapsed-view>
				<p7t-active-timeframes></p7t-active-timeframes>
			</span>
		`
	}

	protected override get listContent() {
		return html`
			${this.reflectivesGroup}
			${this.data!.executives.map(executive => html`
				<p7t-objective-item-exec interactive .entity=${executive.objective} .executive=${executive}></p7t-objective-item-exec>
			`)}
			${(this.data!.attentives ?? []).map(attentive => html`
				<p7t-decree-item-attentive interactive .entity=${attentive.decree} .attentive=${attentive}></p7t-decree-item-attentive>
			`)}
			${!this.creating ? html`
				<p7t-button @click=${() => this.addActivity()} icon='lucide:plus' class='add-button'>Add Activity</p7t-button>
			` : html`
				<p7t-polaris-creation-row
					.cycle=${this.data}
					@cancel=${() => this.creating = false}
					@created=${(e: CustomEvent<CreationRowCreated<PolarisActivityCreated>>) => this.onActivityCreated(e)}>
				</p7t-polaris-creation-row>
			`}
		`
	}

	/** A committed row: the cycle re-reads itself through the row's own revalidation; the row stays only to make another. */
	private onActivityCreated(e: CustomEvent<CreationRowCreated<PolarisActivityCreated>>) {
		e.stopPropagation()
		if (e.detail.mode !== 'again') {
			this.creating = false
		}
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

	protected override get footer() {
		const totalEstimation = this.data!.executives.reduce((acc, executive) => acc + (executive.estimation ?? 0), 0)
		const totalElapsed = this.data!.executives.reduce((acc, executive) => acc + (executive.elapsed ?? 0), 0)
		return html`
			<p7t-value-progress icon='state-polaris' value=${totalElapsed} max=${totalEstimation}
				.valueTemplate=${(value: number) => html`<p7t-time-unit .value=${value}></p7t-time-unit>`}
				.maxTemplate=${(max: number) => html`<p7t-time-unit .value=${max}></p7t-time-unit>`}
			></p7t-value-progress>
			<p7t-button @click=${() => this.conclude()}>Conclude</p7t-button>
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

	/** A bound attentive toggled its resolution: reconcile the local copy, then revalidate. */
	@eventListener('attentivechange')
	protected onAttentiveChange(e: CustomEvent<Attentive>) {
		e.stopPropagation()
		if (!this.data) return
		this.data = {
			...this.data,
			attentives: (this.data.attentives ?? []).map(attentive => attentive.id === e.detail.id ? e.detail : attentive),
		}
		void core.repos.briefing.revalidateIfObserved()
	}

	private begin() {
		if (this.data) return
		core.polaris.startNew().then(polaris => this.data = polaris)
	}

	private conclude() {
		if (!this.data) return
		core.polaris.end(new Date().toISOString()).then(polaris => {
			if (!!polaris) this.data = undefined
		})
	}

	/** Opens the inline creation row at the foot of the list, in place of the picker modal it replaced. */
	private addActivity() {
		if (!this.data) return
		this.creating = true
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-polaris': BriefingCardPolaris
	}
}
