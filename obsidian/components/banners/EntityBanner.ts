import { component, css, html, nothing, property, state } from '@a11d/lit'
import { CardComponent } from 'components/design'
import { IconName } from 'components/PleiadesIcon'
import { App, Notice } from 'obsidian'
import { EntityTypeName, isSuccessfulMutation } from '@pleiades/sdk'
import { core, EntityRef } from '..'

@component('p7t-entity-banner')
export class EntityBanner<T extends { id: string, title: string }> extends CardComponent {
	@property() xtype?: string
	@property() puck = ''

	app?: App

	readonly icon: string = 'plaintorch'

	/**
	 * The icon actually rendered. Overridable as a getter for banners whose icon depends on the resolved
	 * entity, which a plain field cannot express now that the entity arrives asynchronously.
	 */
	protected get resolvedIcon(): string {
		return this.icon
	}

	/**
	 * Runtime type name of the entity this banner renders. Subclasses declare it; the base resolves and
	 * observes the entity from it, so a subclass never fetches its own entity.
	 */
	protected readonly entityTypeName?: EntityTypeName

	/**
	 * Holds an entity handed in directly rather than resolved by PUCK, which is how the generic banner
	 * renders a note whose kind has no dedicated banner.
	 */
	@state() private providedEntity?: T

	protected readonly ref = new EntityRef<T & object>(
		this,
		() => core.repos.forTypeName<T & object>(this.entityTypeName),
		() => this.puck
	)

	get entity(): T | undefined {
		return this.ref.value ?? this.providedEntity
	}

	set entity(value: T | undefined) {
		this.providedEntity = value
	}

	/**
	 * Loads anything beyond the entity itself that the banner renders. Overridden by banners that also
	 * need, say, the active Polaris cycle or a declarative's materialized occurrences.
	 */
	protected async loadRelated(): Promise<void> {
	}

	/** Field values as they were before the current edit, kept so a rejected write can be undone. */
	private editSnapshot?: Record<string, unknown>

	protected get entityRepository() {
		return core.repos.forTypeName<T & object>(this.entityTypeName)
	}

	/**
	 * Announces an in-place edit of the entity to every other surface showing it.
	 *
	 * Two-way bindings write straight through to the canonical instance they were handed, so the change is
	 * already applied and there is nothing left for absorption to detect — without this it would stay
	 * invisible to everything except the banner the edit was made in.
	 */
	protected publishEntityEdit(): void {
		if (this.puck) {
			this.entityRepository?.touch(this.puck)
		}
	}

	/**
	 * Captures the entity before a two-way binding writes an edit into it.
	 *
	 * Bindings apply the edit before anything is sent, so this is the last moment the previous state still
	 * exists anywhere.
	 */
	protected beginEntityEdit(): void {
		this.editSnapshot = this.puck ? this.entityRepository?.snapshot(this.puck) : undefined
	}

	/**
	 * Publishes the edit, sends it, and puts the entity back if the core rejected it.
	 *
	 * Without the rollback a rejected write is indistinguishable from an accepted one: the edit is already
	 * on screen, and a failed request reports itself by returning nothing rather than throwing.
	 */
	protected async commitEntityEdit<R>(send: () => Promise<R>): Promise<R | undefined> {
		const repository = this.entityRepository
		if (!repository || !this.puck) {
			return await send()
		}

		this.publishEntityEdit()
		const snapshot = this.editSnapshot
		this.editSnapshot = undefined

		const result = await repository.mutate(this.puck, send, { rollbackTo: snapshot })
		if (!isSuccessfulMutation(result)) {
			new Notice('PLAINTORCH could not save that change.')
		}

		return result
	}

	protected override async initialized() {
		await this.loadRelated()
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				display: flex;
				flex-direction: column;
				align-items: stretch;
			}

			:host(.plaintorch-modal-content) {
				padding: 0;
				background: none;
				border: none;
				margin-bottom: 0;
				user-select: auto;
			}

			.banner-image {
				width: 100%;
				height: 132px;
				background-size: cover;
				background-position: center;
				border-radius: 8px;
				margin-bottom: .4em;
			}

			:host::part(header) {
				grid-area: header;
			}

			:host::part(pre-heading) {
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 70%, transparent);
				font-size: .8em;
				line-height: .8;
			}

			:host::part(sub-heading) {
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 50%, var(--text-normal));
			}

			.render-grid {
				margin: 5px;
				display: grid;
				grid-template-columns: 48px 1fr;
				grid-template-rows: auto auto 1fr auto;
				grid-template-areas:
					'icon		header'
					'horizon	secondary'
					'stamp		info'
					'puck		actions';
				gap: 1.2em .6em;
				align-items: center;

				& .icon {
					grid-area: icon;
					align-self: center;
					width: 48px;
					height: 48px;
				}

				& .indicator {
					grid-area: horizon;
					display: block;
					border-top: 2px solid var(--p7t-flare-accent, var(--interactive-accent));
					align-self: start;
					margin: .8em 1em;

					& .notch-icon {
						position: absolute;
						height: 1em;
						width: 1em;
						margin-block: -.51em -.5em;
						inset-inline-start: -.6em;
						color: var(--p7t-flare-accent, var(--interactive-accent));
					}

					& .notch-start,
					& .notch-end {

						&::before, &::after {
							content: '';
							position: absolute;
							height: 1.5em;
							border-inline-start: 1px solid var(--p7t-flare-accent, var(--interactive-accent));
							inset-inline-start: -.16em;
						}

						&::before {
							margin-top: 1.5em;
						}
	
						&::after {
							margin-top: -3em;
						}
					}

					& .notch-end {
						&::before, &::after {
							inset-inline-start: unset;
							inset-inline-end: -.16em;
						}
					}
				}

				& .secondary {
					color: var(--p7t-flare-accent, var(--interactive-accent));
					grid-area: secondary;
					display: flex;
					flex-direction: column;
					font-weight: 400;
					font-size: 1.1em;
					font-family: var(--font-interface);
					align-items: flex-start;
				}

				& .info {
					grid-area: info;
					display: flex;
					flex-direction: column;
					font-weight: 400;
					font-size: 1.06em;
					font-family: var(--font-text);
					align-items: flex-start;
				}

				& .actions {
					grid-area: actions;
					display: flex;
					gap: .8em;
					justify-content: flex-end;
				}
			}

			.stamp {
				grid-area: stamp;
				height: 40px;
				width: auto;
			}

			.puck {
				grid-area: puck;
				opacity: .6;
				display: flex;
				font-weight: 200;
				flex-direction: row;
				align-items: center;
				align-self: flex-end;
				gap: 3px;
				margin-bottom: -.4rem;

				& p7t-icon {
					height: 32px;
					width: 32px;
					flex: 0 0 32px;
				}

				& pre {
					margin: 0;
					font-size: .6em;
				}
			}
		`
	}

	/**
	 * An optional full-width header image rendered above the identity grid (PEP105). Defaults to none; a banner
	 * whose entity carries a banner image (a directive with a banner asset) overrides this.
	 */
	protected get bannerImageTemplate(): unknown {
		return nothing
	}

	/**
	 * The entity's icon, rendered in the identity grid. Defaults to a display-only glyph or image; a banner that
	 * lets its icon be edited (a directive) overrides this with an editable media control.
	 */
	protected get iconTemplate(): unknown {
		return html`<p7t-icon class='icon' icon='${this.resolvedIcon}'></p7t-icon>`
	}

	protected override get template() {
		return !this.entity ? html`` : html`
			${this.bannerImageTemplate}
			<div class='render-grid'>
				${this.iconTemplate}
				${this.headerTemplate}

				<span class='indicator'>
					<span class='notch-start'></span>
					<p7t-icon class='notch-icon' icon='lucide:sparkle'></p7t-icon>
					<span class='notch-end'></span>
				</span>
				<div class='secondary'>
					${this.secondary}
				</div>

				${!this.stamp ? nothing : html`<p7t-icon class='stamp' icon=${this.stamp}></p7t-icon>`}
				<div class='info'>
					${this.info}
				</div>
				<div class='actions'>
					${this.actions}
				</div>
				<div class='puck'>
					<p7t-icon class='icon' icon='puck'></p7t-icon>
					<pre>${this.puck}</pre>
				</div>
			</div>
		`
	}

	protected override get headingTemplate() {
		return html`<span>${this.entity?.title}</span>`
	}

	protected override get preHeadingTemplate() {
		return html`<span>Pleiades Entity</span>`
	}

	protected get info() {
		return html`
		`
	}
	
	protected get secondary() {
		return html`
			<span>Type: ${this.xtype}</span>
		`
	}

	protected get actions() {
		return html`
			
		`
	}

	protected get stamp() : IconName | undefined {
		return undefined
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-banner': EntityBanner<any>
	}
}