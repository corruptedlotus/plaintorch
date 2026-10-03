import { createDeferredExecutor, DeferredPromiseExecutor } from '@open-draft/deferred-promise'
import { DependencyEndpointKind, type Directive, type EndpointHit } from '@pleiades/sdk'
import { createChild, sleep, SuggestModalBase } from '../../host'
import { core, type IconName } from '..'
import { fuzzyFilter } from '../editing/fuzzy'
import { directiveSearchText } from '../entities/directiveSearch'

/** The type icon each endpoint kind is drawn with, matching the canvas nodes. */
const kindIcons: Record<DependencyEndpointKind, IconName> = {
	[DependencyEndpointKind.Directive]: 'directive',
	[DependencyEndpointKind.Objective]: 'objective',
	[DependencyEndpointKind.Fate]: 'eventive',
	[DependencyEndpointKind.Eventive]: 'eventive',
	[DependencyEndpointKind.Checkpoint]: 'checkpoint'
}

const kindLabels: Record<DependencyEndpointKind, string> = {
	[DependencyEndpointKind.Directive]: 'Directive',
	[DependencyEndpointKind.Objective]: 'Objective',
	[DependencyEndpointKind.Fate]: 'Fate',
	[DependencyEndpointKind.Eventive]: 'Eventive',
	[DependencyEndpointKind.Checkpoint]: 'Checkpoint'
}

/**
 * Asks for an endpoint to bring onto a global context — a directive, objective, or fate, whatever kind.
 *
 * Backed by the cross-kind endpoint listing rather than a single kind's, since a global context draws from
 * anywhere in the backlog; the type icon on each row is what tells the kinds apart, the same distinction the
 * global nodes themselves draw. The listing is fetched once per opening and matched by title with
 * {@link fuzzyFilter}; an empty query offers all of it, so the modal is useful before typing.
 */
export class SelectEndpointModal extends SuggestModalBase<EndpointHit> {
	private dpe?: DeferredPromiseExecutor<EndpointHit | undefined>
	private excluded: ReadonlySet<string> = new Set()
	private endpoints?: Promise<EndpointHit[]>
	/** The directive listing by id, for the codename a directive hit does not carry. */
	private directives?: Promise<Map<string, Directive>>

	/** Prompts for an endpoint, resolving to nothing when dismissed. `excluded` holds the `kind:id` keys already pinned. */
	public static prompt(excluded: ReadonlySet<string> = new Set()): Promise<EndpointHit | undefined> {
		const modal = new SelectEndpointModal()
		modal.excluded = excluded
		modal.dpe = createDeferredExecutor()
		modal.setPlaceholder('Search directives, objectives, fates, checkpoints…')
		modal.open()
		return new Promise(modal.dpe)
	}

	/**
	 * The hits matching the query. A directive hit carries only its title, so its codename is read off the directive
	 * listing — read alongside the endpoints — and a directive is found by either, as everywhere else directives are
	 * searched ({@link directiveSearchText}).
	 */
	override async getSuggestions(query: string): Promise<EndpointHit[]> {
		this.endpoints ??= core.dependencies.listEndpoints()
		this.directives ??= core.repos.directiveList.get().then(directives => new Map((directives ?? []).map(directive => [directive.id, directive])))
		const [endpoints, directives] = await Promise.all([this.endpoints, this.directives])
		const offered = endpoints.filter(hit => !this.excluded.has(`${DependencyEndpointKind[hit.kind]}:${hit.id}`))
		return fuzzyFilter(query, offered, hit => hit.kind === DependencyEndpointKind.Directive
			? directiveSearchText(directives.get(hit.id) ?? hit)
			: hit.title)
	}

	/** A row is the endpoint's title behind its kind icon, its kind chipped beside it for the kinds that share an icon. */
	override renderSuggestion(hit: EndpointHit, el: HTMLElement) {
		const item = createChild(el, 'p7t-icon-item')
		item.data = hit
		item.icon = kindIcons[hit.kind]
		item.text = hit.title
		item.chipped = true
		createChild(item, 'span', { text: kindLabels[hit.kind] }).slot = 'chips'
	}

	override async onClose() {
		// The suggestion handler runs after the close, so resolving has to lose the race deliberately.
		await sleep(500)
		this.dpe?.resolve(undefined)
	}

	override onChooseSuggestion(hit: EndpointHit, _: MouseEvent | KeyboardEvent) {
		this.dpe?.resolve(hit)
	}
}
