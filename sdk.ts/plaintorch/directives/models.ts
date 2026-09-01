import { model, ModelValueConstructor } from "@a11d/api-dotnet"
import type { Objective, ObjectiveCollege } from "../objectives/models"
export enum DirectiveStatus {
	Planned = 0,
	Committed = 1,
	Active = 2,
	Fulfilled = 3,
	Over = 4,
	Failed = 5
}

export enum LunarDirectiveStatus {
	OnHold = 0,
	Active = 1,
	Stale = 2
}

/** The kind discriminator used to filter directives (PEP100). */
export type DirectiveKind = 'stellar' | 'lunar'

/**
 * Both directive kinds share this model, so it is registered under both runtime type names the core
 * actually emits. `Directive` itself is abstract on the core side and never appears on the wire — a
 * registration under that name matches nothing, which is why these getters were silently missing from
 * every directive the API returned.
 */
@model('StellarDirective')
export class Directive {
	id!: string
	title!: string
	codename: string | undefined
	parentDirectiveId: string | undefined
	parentDirective?: Directive | undefined
	subdirectives: Directive[] = []
	/**
	 * Workflow state. Stellar directives carry a {@link DirectiveStatus} lifecycle value; lunar directives carry a
	 * {@link LunarDirectiveStatus} moonlight value (both are emitted on the same `status` field, PEP100).
	 */
	status: DirectiveStatus | LunarDirectiveStatus = DirectiveStatus.Planned
	tags: string[] = []
	objectives: Objective[] = []
	/** Scheduling dates; only present on stellar directives. */
	due?: string | undefined
	startDate?: string | undefined
	endDate?: string | undefined
	/** Timeframes; only present on lunar directives (PEP100). */
	timeframes?: Timeframe[]
	/** Icon key (PEP105): a glyph/lucide name, a `media:` self image, or a `vault:` shared image. */
	icon?: string | undefined
	/** Banner image key (PEP105): a `media:` self image or a `vault:` shared image. */
	banner?: string | undefined
	/** Resolved companion of {@link icon} (PEP105) — its kind and, for custom media, its vault-relative path. */
	iconMedia?: MediaReference | undefined
	/** Resolved companion of {@link banner} (PEP105). */
	bannerMedia?: MediaReference | undefined

	/** The runtime kind, keyed off the `@type` the core stamps (the same discriminator api-dotnet reconstructs by). */
	get isLunar() {
		return (this as Record<string, unknown>)[ModelValueConstructor.typeNameKey] === 'LunarDirective'
	}

	get isStellar() {
		return (this as Record<string, unknown>)[ModelValueConstructor.typeNameKey] === 'StellarDirective'
	}
}

model('LunarDirective')(Directive)

/**
 * How a timeframe auto-includes Polaris workitems (PEP100 patch). Numeric to match the wire form. College is the
 * only criterion for now; the enum is the extension point for further ones.
 */
export enum TimeframeInclusion {
	None = 0,
	College = 1
}

/** Directive-level definition of a portion of the day (PEP100). Belongs to a lunar directive; purely semantic. */
export interface Timeframe {
	id: number
	directiveId: string
	title: string
	startTime: string
	endTime: string
	orbit: string | undefined
	/** Icon key (PEP100 patch): a glyph/lucide name or a `vault:` shared image. Stands in for Celestron on an affined executive. */
	icon?: string | undefined
	/** Resolved companion of {@link icon} (PEP100 patch). */
	iconMedia?: MediaReference | undefined
	/** How this timeframe auto-includes workitems (PEP100 patch). */
	autoInclusion: TimeframeInclusion
	/** The colleges driving college-based auto-inclusion (PEP100 patch). */
	autoInclusionColleges: ObjectiveCollege[]
}

/** A timeframe paired with a summary of the lunar directive that owns it (global timeframe listing, PEP100). */
export interface DirectiveTimeframeRecord {
	id: number
	directiveId: string
	directiveTitle: string
	directiveCodename?: string | undefined
	directiveStatus: LunarDirectiveStatus
	title: string
	startTime: string
	endTime: string
	orbit?: string | undefined
	/** Icon key (PEP100 patch). */
	icon?: string | undefined
	/** Resolved companion of {@link icon} (PEP100 patch). */
	iconMedia?: MediaReference | undefined
	/** How this timeframe auto-includes workitems (PEP100 patch). */
	autoInclusion: TimeframeInclusion
	/** The colleges driving college-based auto-inclusion (PEP100 patch). */
	autoInclusionColleges: ObjectiveCollege[]
}

export interface CreateDirectiveRequest {
	title: string
	id?: string | undefined
	codename?: string | undefined
	parentDirectiveId?: string | undefined
}

export interface InitDirectiveRequest {
	path: string
}

/** Stellar directive update, including scheduling dates. Nullable fields: omit to keep, a value to set, `null` to clear. */
export interface StellarDirectiveUpdate {
	title?: string | undefined
	codename?: string | null | undefined
	parentDirectiveId?: string | undefined
	tags?: string[] | undefined
	due?: string | null | undefined
	startDate?: string | null | undefined
	endDate?: string | null | undefined
}

/** Lunar directive update. Lunar directives are everglow and carry no scheduling dates (PEP100). */
export interface LunarDirectiveUpdate {
	title?: string | undefined
	codename?: string | null | undefined
	parentDirectiveId?: string | undefined
	tags?: string[] | undefined
}

export interface StellarDirectiveWorkflowShift {
	status: DirectiveStatus
}

export interface CreateLunarDirectiveRequest {
	title: string
	codename?: string | undefined
	parentDirectiveId?: string | undefined
}

export interface LunarDirectiveWorkflowShift {
	status: LunarDirectiveStatus
}

export interface TimeframePlan {
	title: string
	startTime: string
	endTime: string
	orbit?: string | undefined
	/** Icon key (PEP100 patch): a glyph/lucide name or a `vault:` shared image. */
	icon?: string | undefined
	/** How this timeframe auto-includes workitems (PEP100 patch); defaults to none. */
	autoInclusion?: TimeframeInclusion
	/** The colleges driving college-based auto-inclusion (PEP100 patch). */
	autoInclusionColleges?: ObjectiveCollege[] | undefined
}

export interface TimeframeUpdate {
	title?: string | undefined
	startTime?: string | undefined
	endTime?: string | undefined
	/** Orbit notation. Omit to keep, a value to set, `null` to clear. */
	orbit?: string | null | undefined
	/** Icon key (PEP100 patch). Omit to keep, a value to set, `null` to clear. */
	icon?: string | null | undefined
	/** How this timeframe auto-includes workitems (PEP100 patch). Leave undefined to keep the current mechanism. */
	autoInclusion?: TimeframeInclusion
	/** The colleges driving college-based auto-inclusion (PEP100 patch). Omit to keep; any list (empty to clear) replaces. */
	autoInclusionColleges?: ObjectiveCollege[] | undefined
}

/** How a media key resolves (PEP105): a built-in glyph, self/level media, or vault-level shared media. */
export type MediaReferenceType = 'icon' | 'media' | 'vault'

/** The resolved companion an entity carries beside a media key (PEP105). */
export interface MediaReference {
	/** The raw stored key, e.g. `media:crest.png`, `vault:logo.png`, or `lucide:star`. */
	key: string
	/** How the key resolves. */
	type: MediaReferenceType
	/** The vault-relative path for custom media; undefined for a glyph. */
	path?: string | undefined
}

/**
 * Selects a directive's icon (PEP105): a raw `reference` key — a glyph/lucide name, or a `media:`/`vault:` file
 * already stored through the media domain — or `clear` to remove it. Storing an image is the media domain's job;
 * a field only ever references a key.
 */
export interface DirectiveIconRequest {
	/** A raw key to set directly: a glyph/lucide name, `media:file`, or `vault:file`. */
	reference?: string | undefined
	clear?: boolean
}

/** Selects a directive's banner image (PEP105): a raw `reference` key already stored through the media domain, or `clear`. */
export interface DirectiveBannerRequest {
	reference?: string | undefined
	clear?: boolean
}
