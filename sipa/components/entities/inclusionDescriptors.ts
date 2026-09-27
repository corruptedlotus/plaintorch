import { TimeframeInclusion } from '@pleiades/sdk'
import { IconName } from '../PleiadesIcon'

/** How one timeframe auto-inclusion mode is named and drawn (PEP100 patch 2). */
export interface InclusionDescriptor {
	/** The mode itself, as the numeric {@link TimeframeInclusion} the core stores and sends. */
	value: TimeframeInclusion
	/** Short name — "None", "College", "Availability". */
	name: string
	/** Full label, read as what the mode does — "Manual only", "By college", "Directive availability". */
	fullName: string
	/** The glyph the mode is drawn with in the editor's mode control and the picker. */
	icon: IconName
}

/**
 * The single source of truth for how each {@link TimeframeInclusion} mode reads and draws (PEP100 patch 2), mirroring
 * {@link collegeDescriptors}: the timeframe editor's mode control, the mode picker and the timeframe details all read
 * from here.
 */
export const inclusionDescriptors: Record<keyof typeof TimeframeInclusion, InclusionDescriptor> = {
	None: { value: TimeframeInclusion.None, name: 'None', fullName: 'Manual only', icon: 'lucide:circle-off' },
	College: { value: TimeframeInclusion.College, name: 'College', fullName: 'By college', icon: 'lucide:layers' },
	Availability: { value: TimeframeInclusion.Availability, name: 'Availability', fullName: 'Directive availability', icon: 'lucide:calendar-check' },
}

/** The descriptor for an inclusion mode, defaulting to {@link TimeframeInclusion.None} for an unknown value. */
export function inclusionDescriptorOf(inclusion: TimeframeInclusion | undefined): InclusionDescriptor {
	return inclusion === undefined
		? inclusionDescriptors.None
		: inclusionDescriptors[TimeframeInclusion[inclusion] as keyof typeof TimeframeInclusion] ?? inclusionDescriptors.None
}
