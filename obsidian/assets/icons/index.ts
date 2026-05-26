import collegeCreation from "./college-creation.svg"
import collegeEloquence from "./college-eloquence.svg"
import collegeGlamour from "./college-glamour.svg"
import collegeLore from "./college-lore.svg"
import collegeNone from "./college-none.svg"
import collegeSwords from "./college-swords.svg"
import directiveLunar from "./directive-lunar.svg"
import directive from "./directive.svg"
import everglow from "./everglow.svg"
import execOrder from "./exec-order.svg"
import executive from "./executive.svg"
import loreAct from "./lore-act.svg"
import loreChapter from "./lore-chapter.svg"
import loreEra from "./lore-era.svg"
import lorePhase from "./lore-phase.svg"
import lorepage from "./lorepage.svg"
import objectiveLunar from "./objective-lunar.svg"
import objective from "./objective.svg"
import onrush from "./onrush.svg"
import plaintorch from "./plaintorch.svg"
import polaris from "./polaris.svg"
import puck from "./puck.svg"
import reflective from "./reflective.svg"
import starfire from "./starfire.svg"
import stateActive from "./state-active.svg"
import stateArchived from "./state-archived.svg"
import stateBlocked from "./state-blocked.svg"
import stateCommit from "./state-commit.svg"
import stateDone from "./state-done.svg"
import stateOnrush from "./state-onrush.svg"
import statePolaris from "./state-polaris.svg"
import stateZero from "./state-zero.svg"
import watcher from "./watcher.svg"

export const icons = {
	"college-creation": collegeCreation,
	"college-eloquence": collegeEloquence,
	"college-glamour": collegeGlamour,
	"college-lore": collegeLore,
	"college-none": collegeNone,
	"college-swords": collegeSwords,
	"directive-lunar": directiveLunar,
	directive,
	everglow,
	"exec-order": execOrder,
	executive,
	"lore-act": loreAct,
	"lore-chapter": loreChapter,
	"lore-era": loreEra,
	"lore-phase": lorePhase,
	lorepage,
	"objective-lunar": objectiveLunar,
	objective,
	onrush,
	plaintorch,
	polaris,
	puck,
	reflective,
	starfire,
	"state-active": stateActive,
	"state-archived": stateArchived,
	"state-blocked": stateBlocked,
	"state-commit": stateCommit,
	"state-done": stateDone,
	"state-onrush": stateOnrush,
	"state-polaris": statePolaris,
	"state-zero": stateZero,
	watcher
} satisfies Record<string, string>

export type IconName = keyof typeof icons

export default icons
