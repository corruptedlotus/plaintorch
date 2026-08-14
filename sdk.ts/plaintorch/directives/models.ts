import { model } from "@a11d/api-dotnet"
import type { Objective } from "../objectives/models"
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
	/** Polymorphic discriminator emitted by the core: "stellar" or "lunar". */
	$type?: DirectiveKind
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

	get isLunar() {
		return this.$type === 'lunar'
	}

	get isStellar() {
		return this.$type === 'stellar'
	}
}

model('LunarDirective')(Directive)

/** Directive-level definition of a portion of the day (PEP100). Belongs to a lunar directive; purely semantic. */
export interface Timeframe {
	id: number
	directiveId: string
	title: string
	startTime: string
	endTime: string
	orbit: string | undefined
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

/** Stellar directive update, including scheduling dates. */
export interface StellarDirectiveUpdate {
	title?: string | undefined
	codename?: string | undefined
	parentDirectiveId?: string | undefined
	tags?: string[] | undefined
	due?: string | undefined
	startDate?: string | undefined
	endDate?: string | undefined
}

/** Lunar directive update. Lunar directives are everglow and carry no scheduling dates (PEP100). */
export interface LunarDirectiveUpdate {
	title?: string | undefined
	codename?: string | undefined
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
}

export interface TimeframeUpdate {
	title?: string | undefined
	startTime?: string | undefined
	endTime?: string | undefined
	orbit?: string | undefined
	clearOrbit?: boolean
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

/** An uploaded media file for a directive icon or banner (PEP105); the bytes travel as Base64. */
export interface MediaUpload {
	fileName: string
	contentBase64: string
}

/** Sets a directive's icon (PEP105): supply a raw reference key, an uploaded image, or clear it. */
export interface DirectiveIconRequest {
	/** A raw key to set directly: a glyph/lucide name, `media:file`, or `vault:file`. */
	reference?: string | undefined
	upload?: MediaUpload | undefined
	/** Store the upload in the vault root's shared asset folder rather than the directive's own. */
	vault?: boolean
	clear?: boolean
}

/** Sets a directive's banner image (PEP105): supply a raw reference key, an uploaded image, or clear it. */
export interface DirectiveBannerRequest {
	reference?: string | undefined
	upload?: MediaUpload | undefined
	vault?: boolean
	clear?: boolean
}
