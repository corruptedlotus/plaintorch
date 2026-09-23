import { component, html, nothing } from '@a11d/lit'
import { Checkpoint, CheckpointUpdate } from '@pleiades/sdk'
import { Notice } from 'obsidian'
import { core, IconName, ReactiveBinder } from '..'
import { EntityBanner } from './EntityBanner'

/**
 * Banner for a checkpoint (PEP101/102).
 *
 * A checkpoint has no lifecycle, so this is mostly its name — the one part the milestone in particular needs —
 * which is now editable. Beyond that it carries only its own state: whether it has unlocked, and the Celestron
 * toll or external condition still standing between it and that. Both of those are editable too: a toll is set
 * by typing a value and removed by clearing it to nothing; a condition is required, met, or removed outright.
 */
@component('p7t-checkpoint-banner')
export class CheckpointBanner extends EntityBanner<Checkpoint> {
	override icon: IconName = 'checkpoint'

	protected override readonly entityTypeName = 'Checkpoint' as const

	/** Set by the opener when this checkpoint is the milestone of the sprint being planned. */
	milestone = false

	/**
	 * Two-way binds the name and toll to the checkpoint, persisting each on commit. A toll edited down to
	 * nothing removes the toll, since a zero toll is no toll.
	 */
	protected binder = new ReactiveBinder<Checkpoint>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const checkpoint = this.entity!
			await this.commitEntityEdit(async () => {
				if (keyPath === 'celestronToll') {
					const toll = checkpoint.celestronToll ?? 0
					return await core.dependencies.updateCheckpoint(checkpoint.id, { celestronToll: toll > 0 ? toll : null })
				}

				return await core.dependencies.updateCheckpoint(checkpoint.id, { title: checkpoint.title })
			})
		}
	})

	protected override get resolvedIcon(): IconName {
		return this.milestone ? 'milestone' : 'checkpoint'
	}

	protected override get preHeadingTemplate() {
		return html`<span>${this.milestone ? 'Onrush Milestone' : 'Checkpoint'}</span>`
	}

	protected override get headingTemplate() {
		return html`<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>`
	}

	protected override get secondary() {
		const checkpoint = this.entity
		if (!checkpoint) {
			return html``
		}

		return html`<span>${checkpoint.unlocked ? 'Unlocked' : 'Locked'}</span>`
	}

	protected override get info() {
		const checkpoint = this.entity
		if (!checkpoint) {
			return html``
		}

		// The core sends an absent toll or condition as null, not a missing field, so both are read through the
		// nullish guard: a checkpoint with no condition must read as `none`, not as a condition sitting unmet.
		const hasToll = (checkpoint.celestronToll ?? 0) > 0
		const condition = checkpoint.externalCondition ?? undefined

		return html`
			<div style='display: flex; align-items: center; gap: .3em'>
				<span>Toll:</span>
				<p7t-editable-starfire nullable ${this.binder.bind('celestronToll')}></p7t-editable-starfire>
				${hasToll && checkpoint.tollPaid ? html`<span style='opacity: .6'>paid</span>` : nothing}
			</div>
			<div style='display: flex; align-items: center; gap: .3em'>
				<span>Condition: ${condition === undefined ? 'none' : condition ? 'met' : 'not met'}</span>
			</div>
		`
	}

	protected override get actions() {
		const checkpoint = this.entity
		if (!checkpoint) {
			return html``
		}

		// Read through the nullish guard, as in `info`: a toll of null owes nothing, a condition of null is none.
		const owesToll = (checkpoint.celestronToll ?? 0) > 0 && !checkpoint.tollPaid
		const condition = checkpoint.externalCondition ?? undefined

		return html`
			${!owesToll ? nothing : html`
				<p7t-button large icon='starfire' @click=${() => this.payToll()}>
					<span>Pay toll</span>
				</p7t-button>
			`}
			${condition === undefined ? html`
				<p7t-button large icon='state-zero' @click=${() => this.applyUpdate({ externalCondition: false })}>
					<span>Require condition</span>
				</p7t-button>
			` : html`
				<p7t-button large icon=${condition ? 'state-done' : 'state-zero'} @click=${() => this.applyUpdate({ externalCondition: !condition })}>
					<span>${condition ? 'Mark unmet' : 'Mark met'}</span>
				</p7t-button>
				<p7t-button large icon='lucide:x' @click=${() => this.applyUpdate({ externalCondition: null })}>
					<span>Remove condition</span>
				</p7t-button>
			`}
		`
	}

	private async payToll() {
		const checkpoint = this.entity
		if (!checkpoint) {
			return
		}

		const paid = await core.repos.checkpoints.mutate(checkpoint.id, async () => await core.dependencies.payToll(checkpoint.id))
		new Notice(paid ? 'Toll paid.' : 'Could not pay the toll.')
	}

	/** Persists a checkpoint change made through an action rather than an inline edit. */
	private async applyUpdate(update: CheckpointUpdate) {
		const checkpoint = this.entity
		if (!checkpoint) {
			return
		}

		await core.repos.checkpoints.mutate(checkpoint.id, async () => await core.dependencies.updateCheckpoint(checkpoint.id, update))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-checkpoint-banner': CheckpointBanner
	}
}
