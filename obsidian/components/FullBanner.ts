import { Component, component, css, html, nothing, property } from '@a11d/lit'
import { App } from 'obsidian'
import { plaintorchNodeCoreClient as core, EntityExistence } from '@pleiades/sdk/plaintorch/node'
import { DerivedRef, EntityRef } from 'components/data'
import { canDeleteEntity, deleteEntityByType } from 'components/entities/entityMenu'
import { canMaterializeKind, createEntityNote, openEntityNote } from 'components/grid/entityActions'

/** The minimal shape every entity a banner resolves shares — a PUCK identity and a title. */
interface BannerEntity {
	id: string
	title: string
}

/** The typed banner element each kind is drawn through — the same routing NoteBanner uses inline. */
const bannerTagByKind: Record<string, string> = {
	'stellar-directive': 'p7t-sdirective-banner',
	'lunar-directive': 'p7t-ldirective-banner',
	'objective': 'p7t-objective-banner',
	'fate': 'p7t-fate-banner',
	'decree': 'p7t-decree-banner',
	'onrush-sprint': 'p7t-onrush-banner',
	'executive-order': 'p7t-executive-order-banner',
	'polaris-cycle': 'p7t-polaris-banner',
	'lore-page': 'p7t-lore-banner'
}

/**
 * The runtime type name (`@type`) each kind is served by.
 *
 * Kinds are the kebab discriminator the core resolves a PUCK to; type names are what a repository is routed under. The
 * banner needs both: the kind to choose a banner, the type name to ask a repository for the entity directly and to test
 * whether it can be deleted.
 */
const typeNameByKind: Record<string, string> = {
	'stellar-directive': 'StellarDirective',
	'lunar-directive': 'LunarDirective',
	'objective': 'Objective',
	'fate': 'Fate',
	'decree': 'Decree',
	'onrush-sprint': 'OnrushSprint',
	'executive-order': 'ExecutiveOrder',
	'polaris-cycle': 'PolarisCycle',
	'lore-page': 'LorePage'
}

/** The reverse map, so a caller that hands over a runtime type name is understood as readily as one handing a kind. */
const kindByTypeName: Record<string, string> = Object.fromEntries(
	Object.entries(typeNameByKind).map(([kind, type]) => [type, kind])
)

/**
 * The whole editing surface for one entity: its banner, an action bar, and every special editor that entity carries —
 * the composition the per-entity detail modals used to hand-assemble (an onrush with its executive orders, a lunar
 * directive with its timeframes), gathered behind a single element.
 *
 * Like {@link NoteBanner} it is a dumb, identity-driven element: handed a PUCK it resolves the rest. If the caller
 * already knows the type it asks that repository for the entity directly; if it knows only the PUCK it resolves the
 * unknown token through the entity-resolution API to discover its type first. Either path funnels to one kind, which
 * chooses the banner, the editors beneath it, and which actions apply.
 */
@component('p7t-full-banner')
export class FullBanner extends Component {
	/** The entity's PUCK identity — the one input always required. */
	@property() puck = ''

	/**
	 * The entity's kind or runtime type name, when the caller already knows it (either spelling is accepted). Given, the
	 * entity is asked of its repository directly; omitted, the PUCK is resolved through the resolution API to find it.
	 */
	@property() xtype = ''

	/** The Obsidian app, handed down to the banner and its editors for note navigation. */
	app?: App

	/**
	 * Resolves an unknown PUCK to its typed entity — used only when no type was supplied. The source returns `undefined`
	 * in the type-known path so the controller stays idle rather than making the round-trip the caller let us skip.
	 */
	private readonly resolution = new DerivedRef<EntityExistence>(
		this,
		core.repos.entityResolution,
		() => this.xtype ? undefined : (this.puck || undefined)
	)

	/**
	 * Asks the responsible repository for the single entity directly — used only when the type is known. The repository
	 * and id thunks return `undefined` in the resolve path, leaving the reference idle there.
	 */
	private readonly ref = new EntityRef<BannerEntity>(
		this,
		() => this.xtype ? core.repos.forTypeName<BannerEntity>(this.clrType) : undefined,
		() => this.xtype ? (this.puck || undefined) : undefined
	)

	/** The kind resolved from a supplied type, or `undefined` when the supplied type is one we render no banner for. */
	private get providedKind(): string | undefined {
		if (!this.xtype) {
			return undefined
		}

		return bannerTagByKind[this.xtype] ? this.xtype : kindByTypeName[this.xtype]
	}

	/** The runtime type name to ask a repository under, derived from whichever spelling of the type was supplied. */
	private get clrType(): string | undefined {
		const kind = this.providedKind
		return kind ? typeNameByKind[kind] : undefined
	}

	/** The kind driving what is rendered: the supplied one, or the one the PUCK resolved to. */
	private get kind(): string | undefined {
		return this.xtype ? this.providedKind : this.resolution.value?.entityKind
	}

	/** The canonical entity, from whichever path resolved it, or `undefined` until it arrives. */
	private get entity(): BannerEntity | undefined {
		return this.ref.value ?? (this.resolution.value?.entity as BannerEntity | undefined)
	}

	private get title(): string {
		return this.entity?.title ?? ''
	}

	/**
	 * The resolution record the action bar reads note state from: the subscribed one in the resolve path, or a cached
	 * one in the type-known path (a `peek`, so it never triggers the fetch the type path was meant to avoid).
	 */
	private get noteRecord(): EntityExistence | undefined {
		return this.resolution.value ?? (this.puck ? core.repos.entityResolution.peek(this.puck) : undefined)
	}

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				gap: .8em;
			}

			.full-banner-actions {
				display: flex;
				flex-wrap: wrap;
				gap: .6em;
				justify-content: flex-end;
			}

			.loading {
				opacity: .6;
				padding: .5em;
			}
		`
	}

	protected override get template() {
		const kind = this.kind
		if (!kind) {
			// The resolve path is still waiting on the PUCK lookup; a resolved-but-unknown kind falls to the generic banner.
			if (!this.xtype && this.resolution.loading && !this.resolution.value) {
				return html`<div class='loading'>Loading…</div>`
			}

			return this.fallbackBanner
		}

		return html`
			${this.renderBanner(kind)}
			${this.renderActions(kind)}
			${this.renderEditors(kind)}
		`
	}

	/**
	 * The typed banner for a kind. Tag names cannot be interpolated into a template, so the mapping is a switch.
	 *
	 * The banner wears `plaintorch-modal-content` so it sheds its own card chrome and sits as one part of this composed
	 * panel — the same class the detail modals used to add when they assembled the banner and its editors by hand.
	 */
	private renderBanner(kind: string) {
		switch (kind) {
			case 'stellar-directive': return html`<p7t-sdirective-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-sdirective-banner>`
			case 'lunar-directive': return html`<p7t-ldirective-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-ldirective-banner>`
			case 'objective': return html`<p7t-objective-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-objective-banner>`
			case 'fate': return html`<p7t-fate-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-fate-banner>`
			case 'decree': return html`<p7t-decree-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-decree-banner>`
			case 'onrush-sprint': return html`<p7t-onrush-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-onrush-banner>`
			case 'executive-order': return html`<p7t-executive-order-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-executive-order-banner>`
			case 'polaris-cycle': return html`<p7t-polaris-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-polaris-banner>`
			case 'lore-page': return html`<p7t-lore-banner class='banner plaintorch-modal-content' .app=${this.app} puck=${this.puck}></p7t-lore-banner>`
			default: return this.fallbackBanner
		}
	}

	/** The generic banner for a kind with no dedicated one, handed the entity directly the way NoteBanner does. */
	private get fallbackBanner() {
		return html`<p7t-entity-banner
			class='banner plaintorch-modal-content'
			.app=${this.app}
			.puck=${this.puck}
			.xtype=${this.kind}
			.entity=${this.entity ?? { id: this.puck, title: this.title }}></p7t-entity-banner>`
	}

	/** The special in-place editors an entity carries beneath its banner, handed only the owner's PUCK, as the modals did. */
	private renderEditors(kind: string) {
		switch (kind) {
			case 'lunar-directive': return html`<p7t-timeframes-editor class='editor' .directiveId=${this.puck}></p7t-timeframes-editor>`
			case 'onrush-sprint': return html`<p7t-onrush-orders class='editor' .onrushId=${this.puck}></p7t-onrush-orders>`
			case 'fate': return html`<p7t-declarative-agenda class='editor' .fateId=${this.puck}></p7t-declarative-agenda>`
			case 'decree': return html`<p7t-declarative-agenda class='editor' .decreeId=${this.puck}></p7t-declarative-agenda>`
			default: return nothing
		}
	}

	/**
	 * The entity actions, each shown only when it applies.
	 *
	 * Open and Create are the two faces of one thing: an entity with a note opens it; an implicit one with none offers to
	 * begin it. When note state is not yet known (the type-known path skipped resolution) both are offered and the click
	 * handlers self-correct — opening reports "no note yet", creating resolves first and never begins a second time.
	 */
	private renderActions(kind: string) {
		const record = this.noteRecord
		const known = record !== undefined
		const exists = record ? record.exists : true
		const hasNote = !!record?.associatedNote

		const canDelete = canDeleteEntity(typeNameByKind[kind])
		const canOpen = exists && (hasNote || !known)
		const canCreate = canMaterializeKind(kind) && exists && !hasNote

		return html`
			<div class='full-banner-actions'>
				${!canOpen ? nothing : html`
					<p7t-button ghost icon='lucide:file-text' @click=${() => this.openNote()}><span>Open note</span></p7t-button>
				`}
				${!canCreate ? nothing : html`
					<p7t-button ghost icon='lucide:file-plus' @click=${() => this.createNote(kind)}><span>Create note</span></p7t-button>
				`}
				${!canDelete ? nothing : html`
					<p7t-button ghost danger icon='lucide:trash-2' @click=${() => this.deleteEntity(kind)}><span>Delete</span></p7t-button>
				`}
			</div>
		`
	}

	private openNote() {
		void openEntityNote({ id: this.puck })
	}

	private createNote(kind: string) {
		void createEntityNote({ id: this.puck }, kind)
	}

	private deleteEntity(kind: string) {
		void deleteEntityByType(typeNameByKind[kind], this.puck, this.title)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-full-banner': FullBanner
	}
}
