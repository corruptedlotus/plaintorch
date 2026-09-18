import { component, html, property, type HTMLTemplateResult } from "@a11d/lit"
import { DecreeStatus, ObjectiveStatus, type Activity } from "@pleiades/sdk"
import { core } from ".."
import { fuzzyFilter } from "../editing/fuzzy"
import { SelectBase, type SelectOption } from "../editing/SelectBase"
import "./MiniActivityItem"

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
 * misremembered middle still lands. Rows and the chosen face draw the activity as a {@link MiniActivityItem}.
 *
 * With {@link allowCreation}, typing a title that no activity carries offers to create it, one option per kind,
 * at the top of the list: choosing one resolves to an {@link ActivityChoice} flagged `isNew`, to be made by the
 * committing surface.
 *
 * {@link unavailable} marks what the surface cannot take — an objective already in the cycle, say — by giving
 * the reason. Such an activity is still *found*: it lists after the available matches, dimmed and unchoosable,
 * wearing the reason as a tag. Hiding it instead made the search look as though it had never seen it.
 */
@component('p7t-activity-select')
export class ActivitySelect extends SelectBase<ActivityChoice> {
	/** Offers to create a new objective or decree under the typed title when no activity carries it. */
	@property({ type: Boolean }) allowCreation = false

	/** Why the surface cannot accept an activity ("In cycle"), or nothing when it can. */
	@property({ attribute: false }) unavailable?: (activity: Activity) => string | undefined

	override placeholder = 'Activity…'

	private activities?: Promise<Activity[]>

	public override open() {
		// A fresh listing per opening: what was added since is offered, what was archived is not.
		this.activities = undefined
		super.open()
	}

	protected override async search(query: string): Promise<readonly SelectOption<ActivityChoice>[]> {
		this.activities ??= core.activities.list()
		const trimmed = query.trim()
		// Only what is live: a live objective or an active decree.
		const usable = (await this.activities)
			.filter(activity => activity.kind === 'decree'
				? activity.decree?.status === DecreeStatus.Active
				: (activity.objective?.status ?? ObjectiveStatus.Archived) < ObjectiveStatus.Archived)

		// Best match first, but what cannot be taken sinks below what can — found, shown, out of the way.
		const matches = fuzzyFilter(trimmed, usable, activity => activity.title)
			.map(activity => ({ activity, reason: this.unavailable?.(activity) }))
			.sort((a, b) => Number(a.reason !== undefined) - Number(b.reason !== undefined))
			.slice(0, listLimit)
		const options: SelectOption<ActivityChoice>[] = matches.map(({ activity, reason }) => ({
			key: `${activity.kind}:${activity.objective?.id ?? activity.decree?.id}`,
			value: activity,
			disabled: reason !== undefined,
			template: html`
				<p7t-mini-activity-item small .activity=${activity}>
					${reason === undefined ? html`` : html`<span slot='chips'>${reason}</span>`}
				</p7t-mini-activity-item>
			`
		}))

		const taken = usable.some(activity => activity.title.trim().toLowerCase() === trimmed.toLowerCase())
		if (this.allowCreation && trimmed.length > 0 && !taken) {
			options.unshift(creationOption('objective', trimmed), creationOption('decree', trimmed))
		}

		return options
	}

	protected override renderValue(value: ActivityChoice): HTMLTemplateResult {
		return value.isNew
			? html`<p7t-icon-item small chipped icon=${value.kind}>${value.title}<span slot='chips'>New ${value.kind}</span></p7t-icon-item>`
			: html`<p7t-mini-activity-item small .activity=${value}></p7t-mini-activity-item>`
	}
}

/** The "create it" row for one kind — a plain icon chip with a hand-slotted tag, so it styles itself in the list. */
function creationOption(kind: 'objective' | 'decree', title: string): SelectOption<ActivityChoice> {
	return {
		key: `create:${kind}`,
		value: { kind, title, isNew: true },
		template: html`<p7t-icon-item small chipped icon=${kind}>${title}<span slot='chips'>New ${kind}</span></p7t-icon-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-activity-select': ActivitySelect
	}
}
