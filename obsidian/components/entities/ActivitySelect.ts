import { component, css, html, property, type HTMLTemplateResult } from "@a11d/lit"
import { DecreeStatus, ObjectiveStatus, type Activity } from "@pleiades/sdk"
import { core } from ".."
import { fuzzyFilter } from "../editing/fuzzy"
import { SelectBase, type SelectOption } from "../editing/SelectBase"

/**
 * What an activity select resolves to: an existing activity, or — with creation allowed — the intent to make a
 * new one under a typed title. A new one carries no entity yet; whoever commits the choice creates it, so a
 * search that is abandoned never leaves a stray objective or decree behind.
 */
export interface ActivityChoice extends Activity {
	readonly isNew?: boolean
}

/** How many matches the list shows at once; the typing narrows what the cap leaves out. */
const listLimit = 12

/**
 * Picks an activity — an objective or a decree — as an inline select.
 *
 * The whole activity listing is fetched once per opening and matched here, fuzzily: every typed character must
 * appear in the title in order but need not be adjacent, case-insensitively, so "plr" finds "Polaris" and a
 * misremembered middle still lands. Rows and the chosen face draw the activity in its compact one-line form.
 *
 * With {@link allowCreation}, typing a title that no activity carries offers to create it, one option per kind,
 * at the top of the list: choosing one resolves to an {@link ActivityChoice} flagged `isNew`, to be made by the
 * committing surface. {@link exclude} hides what the surface cannot take (an objective already in the cycle,
 * say) without the select knowing why.
 */
@component('p7t-activity-select')
export class ActivitySelect extends SelectBase<ActivityChoice> {
	/** Offers to create a new objective or decree under the typed title when no activity carries it. */
	@property({ type: Boolean }) allowCreation = false

	/** Hides activities the surface cannot accept. */
	@property({ attribute: false }) exclude?: (activity: Activity) => boolean

	override placeholder = 'Activity…'

	private activities?: Promise<Activity[]>

	static override get styles() {
		return css`
			${super.styles}

			.create {
				display: inline-flex;
				align-items: center;
				gap: .5ch;
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.create p7t-icon {
				width: 1.1em;
				height: 1.1em;
			}

			.create .title {
				color: var(--text-normal);
				font-weight: 500;
			}

			.row {
				pointer-events: none;
				min-width: 0;
			}

			.new {
				display: inline-flex;
				align-items: center;
				gap: .5ch;
			}

			.new p7t-icon {
				width: 1.1em;
				height: 1.1em;
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}
		`
	}

	public override open() {
		// A fresh listing per opening: what was added since is offered, what was archived is not.
		this.activities = undefined
		super.open()
	}

	protected override async search(query: string): Promise<readonly SelectOption<ActivityChoice>[]> {
		this.activities ??= core.activities.list()
		const trimmed = query.trim()
		// Only what can still be added: a live objective or an active decree, minus what the surface excludes.
		const usable = (await this.activities)
			.filter(activity => activity.kind === 'decree'
				? activity.decree?.status === DecreeStatus.Active
				: (activity.objective?.status ?? ObjectiveStatus.Archived) < ObjectiveStatus.Archived)
			.filter(activity => !this.exclude?.(activity))

		const matches = fuzzyFilter(trimmed, usable, activity => activity.title).slice(0, listLimit)
		const options: SelectOption<ActivityChoice>[] = matches.map(activity => ({
			key: `${activity.kind}:${activity.objective?.id ?? activity.decree?.id}`,
			value: activity,
			template: html`<div class='row'>${renderActivity(activity)}</div>`
		}))

		const taken = usable.some(activity => activity.title.trim().toLowerCase() === trimmed.toLowerCase())
		if (this.allowCreation && trimmed.length > 0 && !taken) {
			options.unshift(
				{
					key: 'create:objective',
					value: { kind: 'objective', title: trimmed, isNew: true },
					template: html`<span class='create'><p7t-icon icon='objective'></p7t-icon>New objective <span class='title'>${trimmed}</span></span>`
				},
				{
					key: 'create:decree',
					value: { kind: 'decree', title: trimmed, isNew: true },
					template: html`<span class='create'><p7t-icon icon='decree'></p7t-icon>New decree <span class='title'>${trimmed}</span></span>`
				}
			)
		}

		return options
	}

	protected override renderValue(value: ActivityChoice): HTMLTemplateResult {
		if (value.isNew) {
			return html`
				<span class='new'>
					<p7t-icon icon=${value.kind === 'decree' ? 'decree' : 'objective'}></p7t-icon>
					<span>${value.title}</span>
				</span>
			`
		}

		return html`<div class='row'>${renderActivity(value)}</div>`
	}
}

/** An activity as the compact one-line row its kind draws, inert — the select owns the interaction. */
function renderActivity(activity: Activity): HTMLTemplateResult {
	return activity.kind === 'decree'
		? html`<p7t-decree-item compact .entity=${activity.decree}></p7t-decree-item>`
		: html`<p7t-objective-item compact .entity=${activity.objective}></p7t-objective-item>`
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-activity-select': ActivitySelect
	}
}
