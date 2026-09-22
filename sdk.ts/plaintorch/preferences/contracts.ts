/** The value shape of a preference, so a settings surface can render and validate it (PEP116). */
export type PreferenceKind = "Integer" | "Boolean" | "Enum" | "String"

/** One preference as the settings surface sees it: its metadata, current resolved value, and default. */
export interface PreferenceView {
	key: string
	label: string
	group: string
	description?: string
	kind: PreferenceKind
	/** The current resolved value — a number, boolean, or string, matching {@link kind}. */
	value: number | boolean | string
	/** The code-owned default, same shape as {@link value}. */
	default: number | boolean | string
	/** For an {@link PreferenceKind | Enum}, the allowed option names; otherwise absent. */
	options?: string[]
}
