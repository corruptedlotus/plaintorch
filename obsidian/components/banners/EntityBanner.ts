import { component, css, html, nothing, property, state } from '@a11d/lit'
import { CardComponent } from 'components/design'
import { IconName } from 'components/PleiadesIcon'
import { App } from 'obsidian'
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

	protected get entityRepository() {
		return core.repos.forTypeName<T & object>(this.entityTypeName)
	}

	/**
	 * Captures the entity before a two-way binding writes an edit into it, so a rejected write can be undone.
	 *
	 * Delegated to the reference, which owns the edit cycle now; kept as the named step every banner's binder
	 * already calls in its `sourceUpdate`.
	 */
	protected beginEntityEdit(): void {
		this.ref.beginEdit()
	}

	/**
	 * Sends an edit already applied to the canonical instance and rolls it back if the core rejects it.
	 *
	 * The whole cycle — broadcast, send, rollback, failure notice — lives on the reference; this remains as
	 * the name every banner's binder (and the odd direct caller) already invokes. With no resolved entity to
	 * edit optimistically — a directly-provided entity with no PUCK — it simply sends.
	 */
	protected async commitEntityEdit(send: () => Promise<unknown>): Promise<boolean> {
		if (!this.entityRepository || !this.puck) {
			return isSuccessfulMutation(await send())
		}

		return await this.ref.commit(() => send())
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

				& .notch-icon,
				& .notch-start,
				& .notch-end {
					display: none;
				}
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
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 80%, transparent);
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
					border-top: 1px solid var(--p7t-flare-accent, var(--interactive-accent));
					align-self: start;
					margin: .8em 1em;

					& .notch-icon {
						position: absolute;
						height: 1em;
						width: 1em;
						margin-block: -.52em -.5em;
						inset-inline-start: -.7em;
						color: var(--p7t-flare-accent, var(--interactive-accent));
					}

					& .notch-start,
					& .notch-end {

						&::before, &::after {
							content: '';
							position: absolute;
							height: 1.5em;
							border-inline-start: 1px solid var(--p7t-flare-accent, var(--interactive-accent));
							inset-inline-start: -.26em;
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
							inset-inline-end: -.26em;
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
					opacity: .9;
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