import { ObjectiveCollege } from '@pleiades/sdk'
import { IconName } from '../PleiadesIcon'

/** How one college is named and drawn. */
export interface CollegeDescriptor {
	value: ObjectiveCollege
	/** Short name — "Creation", or "None" for the unspecified college. */
	name: string
	/** Full label — "College of Creation", or "No College". */
	fullName: string
	icon: IconName
}

/**
 * The single source of truth for how each college reads and draws — previously re-derived (as
 * `ObjectiveCollege[x].toLowerCase()`) in the objective item, the objective banner, and the timeframes editor, each
 * a copy that could drift. The college chip and the college picker both read from here now.
 */
export const collegeDescriptors: Record<keyof typeof ObjectiveCollege, CollegeDescriptor> = {
	Unspecified: { value: ObjectiveCollege.Unspecified, name: 'None', fullName: 'No College', icon: 'college-none' },
	Creation: { value: ObjectiveCollege.Creation, name: 'Creation', fullName: 'College of Creation', icon: 'college-creation' },
	Eloquence: { value: ObjectiveCollege.Eloquence, name: 'Eloquence', fullName: 'College of Eloquence', icon: 'college-eloquence' },
	Glamour: { value: ObjectiveCollege.Glamour, name: 'Glamour', fullName: 'College of Glamour', icon: 'college-glamour' },
	Lore: { value: ObjectiveCollege.Lore, name: 'Lore', fullName: 'College of Lore', icon: 'college-lore' },
	Swords: { value: ObjectiveCollege.Swords, name: 'Swords', fullName: 'College of Swords', icon: 'college-swords' },
}

/** The descriptor for a college value, defaulting to the unspecified college. */
export function collegeDescriptorOf(college: ObjectiveCollege): CollegeDescriptor {
	return collegeDescriptors[ObjectiveCollege[college] as keyof typeof ObjectiveCollege] ?? collegeDescriptors.Unspecified
}
