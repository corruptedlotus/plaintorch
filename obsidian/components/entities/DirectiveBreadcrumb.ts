import { Component, component, css, html, nothing, property, repeat } from '@a11d/lit'
import { Directive } from '@pleiades/sdk'
import { core, DerivedRef, navigateToEntity } from '..'

/**
 * A reverse breadcrumb of a directive's ancestry: `owner/parent ‹ parent's-parent ‹ …`, each crumb a
 * {@link DirectiveItem} and each gap a lucide chevron. Given a starting directive id — a directive's own parent,
 * or the directive owning an objective — it walks up the hierarchy and lists every ancestor.
 *
 * The chain is resolved client-side from the directive listing rather than a loaded `parentDirective` nav:
 * every directive carries its `parentDirectiveId` scalar and the whole hierarchy is in the store, so a walk up
 * the ids needs no recursive server load and works for either kind. The listing is observed, so the crumbs stay
 * current as directives are reparented, renamed, or arrive.
 *
 * Styling is deliberately minimal — `crumb` and `chevron` parts are exposed for the surface to dress.
 */
@component('p7t-directive-breadcrumb')
export class DirectiveBreadcrumb extends Component {
	/** The first directive in the chain — a directive's own parent id, or an objective's owning directive id. */
	@property() rootId?: string

	/** Shown when there is no chain at all (no parent / no owning directive). */
	@property() placeholder?: string

	private readonly directives = new DerivedRef(this, core.repos.directiveList)

	/** The chain from {@link rootId} up to the root ancestor, resolved against the loaded directive listing. */
	private get chain(): Directive[] {
		const all = this.directives.value
		if (!this.rootId || !all) {
			return []
		}

		const byId = new Map(all.map(directive => [directive.id, directive]))
		const chain: Directive[] = []
		const seen = new Set<string>()
		let id: string | undefined = this.rootId
		// Guarded against a cycle in the parent chain — a corrupt hierarchy must not loop forever.
		while (id && !seen.has(id)) {
			seen.add(id)
			const directive = byId.get(id)
			if (!directive) {
				break
			}

			chain.push(directive)
			id = directive.parentDirectiveId
		}

		return chain
	}

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: center;
				flex-wrap: wrap;
				gap: .35em;
			}

			.crumb {
				cursor: pointer;
			}

			.chevron {
				width: 1em;
				height: 1em;
				flex: 0 0 auto;
				opacity: .5;
			}
		`
	}

	protected override get template() {
		const chain = this.chain
		if (chain.length === 0) {
			return html`<p7t-directive-item placeholder=${this.placeholder ?? 'World Quest'}></p7t-directive-item>`
		}

		return html`
			${repeat(chain, directive => directive.id, (directive, index) => html`
				${index === 0 ? nothing : html`<p7t-icon class='chevron' part='chevron' icon='lucide:chevron-left'></p7t-icon>`}
				<span class='crumb' part='crumb' role='link' @click=${() => void navigateToEntity(directive.id)}>
					<p7t-directive-item .directive=${directive}></p7t-directive-item>
				</span>
			`)}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-directive-breadcrumb': DirectiveBreadcrumb
	}
}
