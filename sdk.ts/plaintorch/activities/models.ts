import type { Decree } from "../declaratives/models"
import type { Objective } from "../objectives/models"

/** Which incentive kind an {@link Activity} wraps. */
export type ActivityKind = "objective" | "decree"

/**
 * A searchable activity — something that can be added to a Polaris cycle — unifying the two incentive kinds a cycle
 * accepts: an objective (which becomes an executive) or a decree (which materializes an attentive). Exactly one of
 * {@link objective} / {@link decree} is set, matching {@link kind}.
 */
export interface Activity {
	kind: ActivityKind
	/** The title of the wrapped incentive, for ordering and display. */
	title: string
	objective?: Objective | undefined
	decree?: Decree | undefined
}
