import { Component, component, css, html, HTMLTemplateResult, nothing, property, type PropertyValues } from "@a11d/lit"
import type { DeclarativeCalendar, Directive, Occurrence } from "@pleiades/sdk"
import { ContextMenuController, tooltip, type ContextMenuSpec } from ".."
import { CalendarRef } from "../data/CalendarRef"
import { itemLayoutStyles } from "./itemStyles"
import { occurrenceSourceDescriptors, occurrenceSourceOf, type OccurrenceDescriptor } from "./occurrenceDescriptors"
import { occurrenceTime } from "./occurrenceTime"
import { orbitTooltip } from "./orbitTooltip"

/**
 * The schedule an occurrence is born of: its owner's orbit and the calendar the owner reads it on. A fate or a decree
 * is one as it stands, so a row hands its owner over.
 */
export interface OccurrenceSchedule {
	readonly orbit?: string
	readonly calendar?: DeclarativeCalendar
}

/**
 * List-row base for the non-PUCK occurrence records — reflectives, attentives, eventives.
 *
 * These are numeric-keyed rows served inside an aggregate (a Polaris cycle, the agenda), not PUCK-tracked
 * entities, so they cannot ride on {@link EntityItem} (which resolves and watches by PUCK identity). This
 * reproduces the same notch + toplane + title grid — via the shared {@link itemLayoutStyles} — and exposes
 * the small set of hooks each occurrence overrides: its heading, its relevant directive, its notch, and
 * what the notch does.
 *
 * A {@link mini} row folds that grid into one dense line — the occurrence's state, its time and its source — for a
 * host that already names the owner.
 */
@component('p7t-occurrence-item')
export class OccurrenceItem extends Component {
	@property({ type: Boolean, reflect: true }) interactive = false

	/**
	 * Draws the row as one line: the state glyph (the notch, still clickable), the occurrence's time, its state and its
	 * source (orbit or manual). For a host that already names the owner — a declarative's own agenda beneath its banner
	 * — where the title and directive lines would repeat it. The context menu and click-to-edit stay.
	 */
	@property({ type: Boolean, reflect: true }) mini = false

	/**
	 * The owning declarative's calendar, supplied by a host that knows it when the occurrence arrives without its owner
	 * (a declarative's own agenda lists them bare). The occurrence's own owner wins when it carries one.
	 */
	@property({ type: Number }) calendar?: DeclarativeCalendar

	/** The calendar the owner names, if any: the {@link schedule}'s own, else the one the host supplied. */
	protected get ownerCalendar(): DeclarativeCalendar | undefined {
		return this.schedule?.calendar ?? this.calendar
	}

	/** The calendar the owner's orbit is read on — {@link ownerCalendar}, else the vault's preferred one — while the mini line tips its source with it. */
	private readonly scheduleCalendars = new CalendarRef(this, () => this.ownerCalendar, () => this.mini && !!this.schedule?.orbit)

	get disabled() { return false }

	/**
	 * Whether the row wears the flare — the side glass glow of {@link itemLayoutStyles} (PEP100 patch 3), as on an
	 * entity row. Derived rather than set, so it is reflected to the host's `flare` attribute once each render; none
	 * by default.
	 */
	protected get flaring() { return false }

	/**
	 * Raises the occurrence's context menu on right-click. As a controller it needs no template handler; it
	 * withholds the menu (passing the event through) whenever {@link contextMenuSpec} returns nothing. Subclasses
	 * override the spec to offer their own actions.
	 */
	protected readonly contextMenu = new ContextMenuController(this, () => this.contextMenuSpec())

	/** The context menu this occurrence raises; none by default. */
	protected contextMenuSpec(): ContextMenuSpec | undefined { return undefined }

	static override get styles() {
		return css`
			${itemLayoutStyles}

			.toplane {
				font-size: .9em;
			}

			:host([mini]) {
				padding-block: 2px;
				border-radius: 8px;
			}

			.line {
				flex: 1;
				min-width: 0;
				display: flex;
				align-items: center;
				gap: .6ch;
				font-family: var(--font-text);

				& .notch {
					align-self: center;
					padding: 2px;

					& p7t-icon {
						width: 1.3em;
						height: 1.3em;
					}
				}

				& .moment {
					min-width: 0;
					cursor: pointer;
				}

				& .filler {
					flex: 1;
				}

				&.disabled {
					opacity: .4;
				}
			}

			.tag {
				display: inline-flex;
				align-items: center;
				gap: .35ch;
				flex: 0 0 auto;
				font-size: .8em;
				color: color-mix(in srgb, var(--text-normal) 60%, transparent);

				& p7t-icon {
					width: 1.1em;
					height: 1.1em;
				}
			}
		`
	}

	protected override updated(changed: PropertyValues) {
		super.updated?.(changed)
		this.toggleAttribute('flare', this.flaring)
	}

	protected override get template() {
		if (this.mini) {
			return this.miniTemplate
		}

		return html`
			<div class='grid ${this.disabled ? 'disabled' : ''}'>
				<div @click=${async () => await this.notchAction()} class='notch part'>${this.notchTemplate}</div>
				<div class='toplane'>
					${this.preTitle}
					<div class='filler'></div>
					${this.info}
				</div>
				<div class='title'>
					<span @click=${() => this.navigate()}>${this.heading}</span>
				</div>
			</div>
		`
	}

	/**
	 * The {@link mini} line. The notch keeps its action and draws the state glyph, which the state's name repeats in
	 * words at the end beside the source; the time opens the editor as the title does on a full row.
	 */
	protected get miniTemplate(): HTMLTemplateResult {
		const state = this.state
		const occurrence = this.occurrence
		// The time reads on this device's clock; a zoned occurrence names its zone beside it, its own wall time on hover.
		const anchor = occurrence ? occurrenceTime(occurrence.epoch).anchor : undefined
		return html`
			<div class='line ${this.disabled ? 'disabled' : ''}'>
				<div class='notch part' @click=${async () => await this.notchAction()} ${!state ? nothing : tooltip(state.label)}>
					${this.miniNotchTemplate}
				</div>
				<span class='moment' @click=${() => this.navigate()}>${this.moment}</span>
				${!anchor ? nothing : html`<span class='tag' ${tooltip(anchor.label)}><p7t-icon icon='lucide:globe'></p7t-icon>${anchor.city}</span>`}
				<div class='filler'></div>
				${!state ? nothing : html`<span class='tag'>${state.label}</span>`}
				${!occurrence ? nothing : this.sourceTemplate(occurrenceSourceOf(occurrence))}
			</div>
		`
	}

	/**
	 * The source tag. An orbit-born occurrence's carries its schedule's orbit tooltip — the reading the schedule chip
	 * gives — so the orbit it came of is a hover away.
	 */
	private sourceTemplate(source: OccurrenceDescriptor) {
		const orbit = source === occurrenceSourceDescriptors.orbit ? this.schedule?.orbit : undefined
		return html`
			<span class='tag' ${!orbit ? nothing : tooltip(() => orbitTooltip(orbit, this.scheduleCalendars.orbitCalendar))}>
				<p7t-icon icon=${source.icon}></p7t-icon>${source.label}
			</span>
		`
	}

	/** The occurrence this row draws, for its source; a reflective is not one, so none by default. */
	protected get occurrence(): Occurrence | undefined { return undefined }

	/** The schedule the occurrence is born of — its owner, when the row has it; none by default. */
	protected get schedule(): OccurrenceSchedule | undefined { return undefined }

	/** The occurrence's state, as the mini line names it; none by default. */
	protected get state(): OccurrenceDescriptor | undefined { return undefined }

	/** The mini line's notch: the state glyph, or the full row's notch when there is no state. */
	protected get miniNotchTemplate(): HTMLTemplateResult {
		const state = this.state
		return state ? html`<p7t-icon icon=${state.icon}></p7t-icon>` : this.notchTemplate
	}

	/** When the occurrence falls, as the mini line reads it; the toplane's info by default. */
	protected get moment(): HTMLTemplateResult | typeof nothing { return this.info }

	/** The row's primary text. */
	protected get heading(): string { return '' }

	/** The relevant directive to surface in the toplane, if any. */
	protected get directive(): Directive | undefined { return undefined }

	protected get preTitle(): HTMLTemplateResult | typeof nothing {
		const directive = this.directive
		// An occurrence with no directive shows nothing here (not the objective's "World Quest" placeholder). It is drawn
		// small, as the entity rows (objective, decree, executive) draw theirs, so the two kinds of row read alike.
		if (!directive) return nothing
		return html`<p7t-directive-item small .directive=${directive}></p7t-directive-item>`
	}

	protected get info(): HTMLTemplateResult | typeof nothing { return nothing }

	protected get notchTemplate(): HTMLTemplateResult {
		return html`<p7t-icon icon='state-active'></p7t-icon>`
	}

	protected async notchAction(): Promise<void> { }

	protected navigate(): void { }
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-occurrence-item': OccurrenceItem
	}
}
