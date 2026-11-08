import { AttentiveResolution, EventiveResolution, TerminativeResolution, type Occurrence } from '@pleiades/sdk'
import type { IconName } from '../PleiadesIcon'

/** How one occurrence state or source is named and drawn. */
export interface OccurrenceDescriptor {
	/** The word the state or source reads as — "Pending", "Missed", "Orbit". */
	label: string
	/** The glyph it is drawn with. */
	icon: IconName
}

/**
 * How each {@link AttentiveResolution} reads and draws. The occurrence rows read their state from here rather than
 * from the entity statuses, whose labels ("Standby", "Failed") name something else.
 */
export const attentiveStateDescriptors: Record<AttentiveResolution, OccurrenceDescriptor> = {
	[AttentiveResolution.Pending]: { label: 'Pending', icon: 'state-zero' },
	[AttentiveResolution.Done]: { label: 'Done', icon: 'state-done' },
	[AttentiveResolution.Skipped]: { label: 'Skipped', icon: 'state-archived' },
}

/** How each {@link EventiveResolution} reads and draws, alongside {@link attentiveStateDescriptors}. */
export const eventiveStateDescriptors: Record<EventiveResolution, OccurrenceDescriptor> = {
	[EventiveResolution.Pending]: { label: 'Pending', icon: 'state-zero' },
	[EventiveResolution.Missed]: { label: 'Missed', icon: 'state-failed' },
	[EventiveResolution.Cancelled]: { label: 'Cancelled', icon: 'state-failed' },
	[EventiveResolution.OptOut]: { label: 'Opted out', icon: 'state-archived' },
}

/**
 * How each {@link TerminativeResolution} reads and draws (PEP103 patch 7). The core sets it from the origin's lifecycle:
 * resolved once the origin is finished or set aside.
 */
export const terminativeStateDescriptors: Record<TerminativeResolution, OccurrenceDescriptor> = {
	[TerminativeResolution.Pending]: { label: 'Pending', icon: 'state-zero' },
	[TerminativeResolution.Resolved]: { label: 'Resolved', icon: 'state-done' },
}

/**
 * Where an occurrence came from: generated from its owner's orbit, added by hand, or spawned by an item's due (a
 * terminative, PEP103 patch 7). The orbit glyph is the one the schedule control draws a recurring schedule with.
 */
export const occurrenceSourceDescriptors = {
	orbit: { label: 'Orbit', icon: 'lucide:repeat' },
	manual: { label: 'Manual', icon: 'lucide:hand' },
	due: { label: 'Due', icon: 'terminative' },
} as const satisfies Record<string, OccurrenceDescriptor>

/**
 * The source of an occurrence. A manual one carries no RECURRENCE-ID; `recurrenceId` is read alongside the
 * server-computed `isManual` so a row copied field by field (the modal folds updates over a spread) still reads right.
 */
export function occurrenceSourceOf(occurrence: Occurrence): OccurrenceDescriptor {
	return occurrence.isManual || !occurrence.recurrenceId ? occurrenceSourceDescriptors.manual : occurrenceSourceDescriptors.orbit
}
