import { LifecycleState, type StateCompensation, type StateEnforcementHandler, type StateEnforcementProblem } from '@pleiades/sdk/plaintorch'
import { createChild, ModalBase, toast } from '../../host'

/**
 * Puts a change the core's state enforcement holds back to the user (PEP103): what blocks it, the items going ahead would
 * also move, and the choice to go ahead or cancel.
 *
 * A caller awaits the answer rather than threading callbacks: `true` to go ahead — the core client then repeats the change
 * forcing the compensations — and `false` when cancelled or dismissed. The content is plain light-DOM markup, the confirming
 * button marked `mod-cta`, so it takes the host's own form styling (Obsidian's, or the standalone shell's stylesheet).
 */
export class StateEnforcementModal extends ModalBase {
	private answer?: (confirmed: boolean) => void

	private constructor(private readonly problem: StateEnforcementProblem) {
		super()
	}

	/** Asks whether to go ahead with a held-back change. */
	static confirm(problem: StateEnforcementProblem): Promise<boolean> {
		const modal = new StateEnforcementModal(problem)
		return new Promise<boolean>(resolve => {
			modal.answer = resolve
			modal.open()
		})
	}

	override onOpen() {
		this.setTitle('Go ahead with this change?')
		createChild(this.contentEl, 'p', { text: this.problem.message })

		if (this.problem.compensations.length > 0) {
			createChild(this.contentEl, 'p', { text: 'Going ahead also changes:' })
			const list = createChild(this.contentEl, 'ul')
			for (const move of this.problem.compensations) {
				createChild(list, 'li', { text: describeMove(move) })
			}
		}

		const buttons = createChild(this.contentEl, 'div')
		buttons.style.cssText = 'display: flex; justify-content: flex-end; gap: 0.5em; margin-top: 1em;'
		const proceed = createChild(buttons, 'button', { text: 'Go ahead', cls: 'mod-cta' })
		proceed.addEventListener('click', () => this.settle(true))
		createChild(buttons, 'button', { text: 'Cancel' })
			.addEventListener('click', () => this.settle(false))
		window.setTimeout(() => proceed.focus(), 0)
	}

	override onClose() {
		// Dismissed by Escape, the backdrop or the close button: the change is cancelled.
		this.answer?.(false)
		this.answer = undefined
	}

	private settle(confirmed: boolean) {
		this.answer?.(confirmed)
		this.answer = undefined
		this.close()
	}
}

/**
 * The UI's answer to the core's state enforcement (PEP103): a held-back change is put to the user in a
 * {@link StateEnforcementModal}, and a refusal is shown as a warning.
 */
export const stateEnforcementPrompt: StateEnforcementHandler = {
	confirm: problem => StateEnforcementModal.confirm(problem),
	refused: problem => toast(problem.message, 'warning'),
}

/** One move in words: "Finish Directive 'Foundation' (Fulfilled)". */
export function describeMove(move: StateCompensation): string {
	const verb = move.to === LifecycleState.Started ? 'Start'
		: move.to === LifecycleState.Finished && move.from === LifecycleState.Finished ? 'Reopen'
			: move.to === LifecycleState.Finished ? 'Finish'
				: move.to === LifecycleState.Deferred ? 'Set aside'
					: 'Reopen'
	const kind = kindLabels[move.subject.kind] ?? move.subject.kind
	const status = move.status ? ` (${move.status})` : ''
	return `${verb} ${kind} '${move.subject.title ?? move.subject.id}'${status}`
}

const kindLabels: Record<string, string> = {
	StellarDirective: 'Directive',
	LunarDirective: 'Lunar directive',
}
