import { css, html, nothing, state, type PropertyValues } from "@a11d/lit"
import { Directive, DirectiveTimeframeRecord } from "@pleiades/sdk"
import { EntityBanner } from './EntityBanner'
import { core, SelectTimeframeModal, type TimeframeChoice } from ".."
import type { EditablePart } from "../editing/EditableDataLink"

/**
 * Shared base for the stellar and lunar directive banners (PEP105). The per-directive icon and the optional header
 * banner are both edited in place through the {@link EditableMedia} control: clicking either opens the media picker
 * — an entity or vault asset, an icon, or removal — and the chosen key is written back through the directives SDK.
 *
 * Setting a custom image stays two separate steps (media separation): the picker stores the file through the media
 * domain and hands back a key, and this banner only references that key onto the icon or banner field. The icon is a
 * contained square; the banner is a free-form covering image with an "add banner" affordance while it is empty.
 *
 * The `info` cell carries the directive's **availability** (PEP100 patch 2), for both kinds: an Availability-mode
 * timeframe picked through {@link SelectTimeframeModal.promptAvailability}, which executives created under the
 * directive or its descendants are auto-assigned to. The core serves only the id, so the chip's timeframe is resolved
 * from the global timeframe listing.
 *
 * That listing is fetched only while an availability is set (a directive without one, such as most inline note
 * banners, makes no request), and once per availability id: it is fetched again whenever the id changes, so an id
 * that arrives through the change feed, another view or a pick here is looked up afresh. It is neither polled nor
 * retried. When the listing comes back without the id, the chip reads "No Availability" until the id changes or the
 * banner is rebuilt; that covers a dangling id and a failed read alike, since the SDK resolves a transport or HTTP
 * failure as an empty listing. Likewise a rename or re-icon of the availability timeframe made elsewhere while the
 * banner stays open keeps the old face until then.
 */
export abstract class DirectiveBanner extends EntityBanner<Directive> {
	/** Every timeframe, for resolving {@link Directive.availabilityTimeframeId} to a record the chip can draw. */
	@state() private timeframeRecords: readonly DirectiveTimeframeRecord[] = []

	/**
	 * The availability id the timeframe listing was last fetched for. It is taken before the read and kept whatever the
	 * read returns, so an id the listing does not hold (dangling, or lost to a failed read) never refetches on a later
	 * render. It is reset when the availability is cleared, so setting it again refetches.
	 */
	private timeframesListedFor?: number

	static override get styles() {
		return css`
			${super.styles}

			p7t-editable-media.icon {
				grid-area: icon;
				align-self: center;
				width: 48px;
				height: 48px;
			}

			p7t-editable-media.banner {
				display: flex;
				width: auto;
				min-height: 0;
				border-radius: 8px;
				margin-block: -.6em 0;
				margin-inline: 0 -.5em;
				overflow: hidden;
				box-sizing: border-box;

				&::part(media) {
					position: absolute;
					width: 100%;
					height: 100%;
					object-fit: cover;
					object-position: center;
					inset: 0;
					border-radius: 16px;
					z-index: 0;
					mask-image: linear-gradient(to bottom, rgba(0, 0, 0, .5) -10%, rgba(0, 0, 0, .1) 95%);
					pointer-events: none;
				}
			}

			.top-wrapper {
				display: flex;
				justify-content: space-between;
				align-items: center;
				z-index: 0;

				& .stamp {
					height: auto;
					z-index: 1;
				}
			}

			.render-grid {
				z-index: 1;
			}

			:host(.plaintorch-modal-content) p7t-editable-media.banner::part(media) {
				margin: -3.5em -1em;
				width: calc(100% + 2em) !important;
				height: calc(100% + 4em) !important;
			}
		`
	}

	private get mediaEntity() {
		return { entityType: 'directive', entityId: this.entity?.id ?? '' }
	}

	/** Keeps the timeframe listing in step with the entity's availability id after every render. */
	protected override updated(changedProperties: PropertyValues<this>) {
		super.updated(changedProperties)
		this.syncTimeframes()
	}

	/**
	 * Fetches the timeframe listing when the availability id is set and differs from the one the listing was last
	 * fetched for. A cleared availability fetches nothing and forgets the marker.
	 */
	private syncTimeframes() {
		const timeframeId = this.entity?.availabilityTimeframeId ?? undefined
		if (timeframeId === undefined) {
			this.timeframesListedFor = undefined
			return
		}

		if (timeframeId === this.timeframesListedFor) {
			return
		}

		this.timeframesListedFor = timeframeId
		void this.loadTimeframes(timeframeId)
	}

	/**
	 * Reads every timeframe for the given availability id. A response that returns after the id has moved on is
	 * dropped. The marker stays set whatever happens, so nothing retries: a transport or HTTP failure already arrives
	 * as an empty listing, and the one read that can still reject (a response body that does not parse) is logged and
	 * leaves the records as they were.
	 */
	private async loadTimeframes(forTimeframeId: number) {
		let records: DirectiveTimeframeRecord[]
		try {
			records = await core.directives.listAllTimeframes()
		}
		catch (error) {
			console.error('PLAINTORCH: reading the timeframes for a directive availability failed.', error)
			return
		}

		if (this.timeframesListedFor === forTimeframeId) {
			this.timeframeRecords = records
		}
	}

	/** The directive's availability timeframe, resolved by id; undefined when unset or not (yet) listed. */
	private get availability(): DirectiveTimeframeRecord | undefined {
		const timeframeId = this.entity?.availabilityTimeframeId
		return timeframeId === undefined || timeframeId === null
			? undefined
			: this.timeframeRecords.find(timeframe => timeframe.id === timeframeId)
	}

	/** The availability chip, editable through the Availability-mode timeframe picker (PEP100 patch 2). */
	protected override get info() {
		const availability = this.availability
		return html`
			<p7t-editable
				class='availability'
				.value=${availability}
				.doEdit=${SelectTimeframeModal.promptAvailability}
				@change=${(e: Event) => void this.saveAvailability(e)}>
				<p7t-timeframe-item small availability .timeframe=${availability}></p7t-timeframe-item>
			</p7t-editable>
		`
	}

	protected override get iconTemplate() {
		const directive = this.entity
		if (!directive) {
			return nothing
		}

		return html`
			<p7t-editable-media
				class='icon'
				icon
				.media=${directive.iconMedia}
				.value=${directive.icon ?? ''}
				.default=${this.icon}
				.entity=${this.mediaEntity}
				@change=${(e: Event) => void this.saveIcon(e)}>
			</p7t-editable-media>
		`
	}

	protected get stampTemplate() { return html`` }

	protected override get bannerImageTemplate() {
		const directive = this.entity
		if (!directive) {
			return nothing
		}

		return html`
			<div class='top-wrapper'>
				<span class='stamp'>
					${this.stampTemplate}
				</span>
				<p7t-editable-media
					class='banner ${directive.bannerMedia ? '' : 'empty'}'
					.media=${directive.bannerMedia}
					.value=${directive.banner ?? ''}
					.default=${''}
					.entity=${this.mediaEntity}
					.promptTemplate=${html`<p7t-icon-item small icon='lucide:image' text='Change Banner'>Change Banner</p7t-icon-item>`}
					@change=${(e: Event) => void this.saveBanner(e)}>
				</p7t-editable-media>
			</div>
		`
	}

	private async saveIcon(e: Event) {
		const directive = this.entity
		if (!directive) {
			return
		}

		const key = (e.target as EditablePart<string>).value?.trim() ?? ''
		await this.commitEntityEdit(async () =>
			key
				? await core.directives.setIconReference(directive.id, key)
				: await core.directives.clearIcon(directive.id))
		await this.entityRepository?.refresh(directive.id)
	}

	private async saveBanner(e: Event) {
		const directive = this.entity
		if (!directive) {
			return
		}

		const key = (e.target as EditablePart<string>).value?.trim() ?? ''
		await this.commitEntityEdit(async () =>
			key
				? await core.directives.setBannerReference(directive.id, key)
				: await core.directives.clearBanner(directive.id))
		await this.entityRepository?.refresh(directive.id)
	}

	/**
	 * Persists a picked availability (PEP100 patch 2): a timeframe sets it, `null` clears it, and a cancelled pick
	 * (undefined) does nothing. The refreshed entity carries the new id, and the id change refetches the timeframe
	 * listing, so a timeframe created since the banner loaded resolves for the chip once that read succeeds.
	 */
	private async saveAvailability(e: Event) {
		const directive = this.entity
		const choice = (e.target as EditablePart<TimeframeChoice>).value
		if (!directive || choice === undefined) {
			return
		}

		await this.commitEntityEdit(async () => await core.directives.setAvailability(directive.id, choice?.id ?? null))
		await this.entityRepository?.refresh(directive.id)
	}
}
