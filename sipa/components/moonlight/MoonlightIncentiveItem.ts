import { component, css, html } from '@a11d/lit'
import { DecreeStatus, FateStatus, ObjectiveStatus, type Decree, type Fate, type Objective } from '@pleiades/sdk'
import { core, objectiveStatusDescriptors, SelectStatusModal } from '..'
import { EntityItem } from '../entities/EntityItem'
import { entityIcon, entityKindLabel, entityKindOf, entityStatusName } from '../grid/entityActions'
import type { GridEntity } from '../grid/entityTree'
import { toast } from '../../host'

/**
 * An incentive — an objective, a fate or a decree — as one tile of a Moonlight section, drawn in the shape of a
 * dependency-canvas node: a bordered card with its state in the notch, its kind above the title, and its reward or
 * next occurrence beside it.
 *
 * Unlike a canvas node it is a plain list item: the title opens the entity's full banner, a right-click its context
 * menu, and an objective's notch shifts its state. The directive it belongs to is the section it sits in, so it is not
 * repeated here.
 */
@component('p7t-moonlight-incentive-item')
export class MoonlightIncentiveItem extends EntityItem<GridEntity> {
	static override get styles() {
		return css`
			${super.styles}

			:host {
				box-sizing: border-box;
				min-width: 0;
				padding-block: 6px;
				background-color: var(--background-primary, var(--background-secondary));
				border: 1px solid color-mix(in srgb, var(--text-normal) 18%, transparent);
				border-radius: 12px;
			}

			:host([interactive]:hover) {
				border-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 45%, transparent);
			}

			.grid {
				grid-template-columns: 2.6em minmax(0, 1fr);
				min-width: 0;
			}

			.notch p7t-status-item::part(icon) {
				width: 1.6em;
				height: 1.6em;
			}

			.kind {
				flex: none;
				display: flex;
				align-items: center;
				gap: 4px;
				opacity: .5;
				font-family: var(--font-interface);
				font-size: .85em;
				line-height: .9;

				& p7t-icon {
					flex: 0 0 auto;
					width: 1.1em;
					height: 1.1em;
				}
			}

			/* The date gives way before the kind does: a tile is narrow, and a long weekday would run into the label. */
			.next {
				min-width: 0;
				overflow: hidden;
				opacity: .6;
				font-size: .8em;
				white-space: nowrap;
			}

			.title {
				min-width: 0;
				font-size: 1.05em;

				& span {
					overflow: hidden;
					text-overflow: ellipsis;
					white-space: nowrap;
				}
			}
		`
	}

	override get disabled() {
		const entity = this.entity
		switch (entity && entityKindOf(entity)) {
			case 'objective': return (entity as Objective).status > ObjectiveStatus.Done
			case 'fate': return (entity as Fate).status !== FateStatus.Active
			case 'decree': return (entity as Decree).status === DecreeStatus.Abandoned
			default: return false
		}
	}

	protected override get notchTemplate() {
		return !this.entity ? html`` : html`
			<p7t-status-item icon-only .status=${entityStatusName(this.entity)}></p7t-status-item>
		`
	}

	protected override get preTitle() {
		const entity = this.entity
		return !entity ? html`` : html`
			<span class='kind'>
				<p7t-icon icon=${entityIcon(entity)}></p7t-icon>
				${entityKindLabel(entityKindOf(entity))}
			</span>
		`
	}

	/** An objective's Celestron, a decree's per-attentive reward, a fate's next occurrence. */
	protected override get info() {
		const entity = this.entity
		switch (entity && entityKindOf(entity)) {
			case 'objective':
				return html`<p7t-celestron-item small .value=${(entity as Objective).celestronValue}></p7t-celestron-item>`
			case 'decree':
				return html`<p7t-celestron-item small .value=${(entity as Decree).activeCelestron}></p7t-celestron-item>`
			case 'fate': {
				const next = (entity as Fate).nextOccurrence
				return !next ? html`` : html`<p7t-datetime-view class='next' relative .date=${next}></p7t-datetime-view>`
			}
			default:
				return html``
		}
	}

	/** An objective's notch shifts its state, the way it does in every objective list. */
	protected override async notchAction() {
		const entity = this.entity
		if (!entity || entityKindOf(entity) !== 'objective') {
			return
		}

		let status: ObjectiveStatus | undefined
		try {
			status = await SelectStatusModal.prompt(objectiveStatusDescriptors)
		}
		catch {
			return
		}

		if (status === undefined) {
			return
		}

		// The response is absorbed into the canonical instance, so every surface showing the objective follows.
		const shifted = await core.repos.objectives.mutate(entity.id, async () =>
			await core.objectives.shiftWorkflow(entity.id, { status }))
		if (!shifted) {
			toast('Failed to update objective status.', 'error')
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-moonlight-incentive-item': MoonlightIncentiveItem
	}
}
