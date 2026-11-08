import { component, css, html, nothing, property, type HTMLTemplateResult, type PropertyValues } from '@a11d/lit'
import type { IconName } from '../PleiadesIcon'
import { InteractionBubbleBase, type InteractionBubbleOptions } from './InteractionBubbleBase'

/** How much weight a confirmed action carries. It tints the bubble's glyph and its confirming button. */
export type ConfirmationSeverity = 'default' | 'danger' | 'positive'

/** What a confirmation bubble says. Every part is optional and falls back to a plain default. */
export interface ConfirmationOptions extends InteractionBubbleOptions<boolean> {
	/** The question put to the user, plain or rich. Falls back to "Are you sure?". */
	readonly message?: string | HTMLTemplateResult
	/** The confirming button's label, best the action's own verb ("Delete", "Conclude"). Falls back to "Confirm". */
	readonly action?: string
	/** A glyph on the confirming button. None by default. */
	readonly actionIcon?: IconName
	/** The declining button's label. Falls back to "Cancel". */
	readonly cancel?: string
	/** `danger` (the error red), `positive` (green) or `default` (the accent), which is also the fallback for an unknown one. */
	readonly severity?: ConfirmationSeverity
	/** The glyph beside the message. Falls back to the severity's own; `false` shows none. */
	readonly icon?: IconName | false
}

const severities: readonly ConfirmationSeverity[] = ['default', 'danger', 'positive']

/** The glyph each severity shows beside its message, unless the options name another. */
const severityIcons: Record<ConfirmationSeverity, IconName> = {
	default: 'lucide:circle-help',
	danger: 'lucide:triangle-alert',
	positive: 'lucide:circle-check',
}

const fallbackMessage = 'Are you sure?'

/**
 * A confirmation bubble: a message, a declining and a confirming button, tinted by severity, pointing at what asked.
 * Raise one through {@link confirmAction}.
 *
 * Its data is whether the action was confirmed: `false` until the confirming button commits `true`, so a dismissal
 * resolves `false`. Focus lands on the confirming button, or on the declining one for a `danger` action, so a second
 * Enter cannot confirm it by accident.
 *
 * @cssprop --p7t-bubble-accent - the severity's colour; a host stylesheet may retint a severity through it, e.g.
 * `p7t-confirmation-bubble[severity=danger] { --p7t-bubble-accent: … }`.
 */
@component('p7t-confirmation-bubble')
export class ConfirmationBubble extends InteractionBubbleBase<boolean, ConfirmationOptions> {
	/** The options' severity, reflected for styling; an unknown one reads as `default`. */
	@property({ reflect: true }) severity: ConfirmationSeverity = 'default'

	constructor() {
		super(false)
	}

	static override get styles() {
		return css`
			${super.styles}

			:host([severity=danger]) {
				--p7t-bubble-accent: var(--text-error, var(--color-red, #e26d6d));
			}

			:host([severity=positive]) {
				--p7t-bubble-accent: var(--color-green, var(--text-success, #7cc48f));
			}

			.message {
				display: flex;
				align-items: flex-start;
				gap: .45rem;
				overflow-wrap: anywhere;
			}

			.message p7t-icon {
				flex: none;
				margin-top: .05em;
				color: var(--p7t-bubble-accent);
			}
		`
	}

	protected override get bodyRole() {
		return 'alertdialog' as const
	}

	protected override willUpdate(changed: PropertyValues<this>) {
		super.willUpdate(changed)
		if (changed.has('options')) {
			const severity = this.options?.severity
			this.severity = severity && severities.includes(severity) ? severity : 'default'
		}
	}

	protected override get content() {
		const options = this.options ?? {}
		const icon = options.icon === false ? undefined : options.icon ?? severityIcons[this.severity] ?? severityIcons.default
		const danger = this.severity === 'danger'
		return html`
			<div class='message'>
				${!icon ? nothing : html`<p7t-icon icon=${icon}></p7t-icon>`}
				<span id='label'>${options.message || fallbackMessage}</span>
			</div>
			${this.actionsTemplate(
				{ label: options.action, icon: options.actionIcon, autofocus: !danger },
				{ label: options.cancel, autofocus: danger },
			)}
		`
	}

	/** Confirms: commits `true`, unless told otherwise. */
	override commit(data = true) {
		super.commit(data)
	}
}

/**
 * Asks the user to confirm an action in a small bubble beside `anchor` — {@link ConfirmationBubble.prompt}, bound so it
 * can be called on its own. Resolves `true` once confirmed (after the options' `run`, when given, has finished), or
 * `false` once declined: by the cancel button, Escape, a press elsewhere, a second press of the anchor, or the anchor
 * leaving the screen.
 *
 * ```ts
 * html`<p7t-button danger icon='lucide:trash-2' @click=${(e: Event) => confirmAction(e, {
 * 	message: 'Delete this decree?',
 * 	action: 'Delete',
 * 	severity: 'danger',
 * 	run: () => deleteDecree(id),
 * })}></p7t-button>`
 *
 * if (await confirmAction(button, { message: 'Discard the draft?' })) { … }
 * ```
 */
export const confirmAction = (ConfirmationBubble.prompt<ConfirmationBubble>).bind(ConfirmationBubble)

/** Asks before deleting something: the danger tint and a "Delete" action under the bin glyph. */
export function deleteConfirmation(message: string | HTMLTemplateResult): ConfirmationOptions {
	return { message, action: 'Delete', actionIcon: 'lucide:trash-2', severity: 'danger' }
}

/**
 * Asks before taking something out of where it is (an Onrush, a Polaris cycle). Danger by default, for a removal that
 * loses something on the way; `default` for one the user can simply take back by adding it again.
 */
export function removeConfirmation(message: string | HTMLTemplateResult, severity: ConfirmationSeverity = 'danger'): ConfirmationOptions {
	return { message, action: 'Remove', actionIcon: 'lucide:x', severity }
}

/** Asks before concluding a running Onrush or Polaris cycle, under the concluded state's glyph. */
export function concludeConfirmation(message: string | HTMLTemplateResult): ConfirmationOptions {
	return { message, action: 'Conclude', actionIcon: 'state-archived' }
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-confirmation-bubble': ConfirmationBubble
	}
}
