import { ObjectiveCollege } from "@pleiades/sdk";
import { SuggestModal } from "obsidian";
import { getApp } from ".";
import { IconName } from "components/PleiadesIcon";
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise";

type CollegeDescriptor = { value: ObjectiveCollege, name: string, fullName: string, icon: IconName }
const colleges: Record<keyof typeof ObjectiveCollege, CollegeDescriptor> = {
	'Unspecified': { value: ObjectiveCollege.Unspecified, name: 'None', fullName: 'No College', icon: 'college-none' },
	'Creation': { value: ObjectiveCollege.Creation, name: 'Creation', fullName: 'College of Creation', icon: 'college-creation' },
	'Eloquence': { value: ObjectiveCollege.Eloquence, name: 'Eloquence', fullName: 'College of Eloquence', icon: 'college-eloquence' },
	'Glamour': { value: ObjectiveCollege.Glamour, name: 'Glamour', fullName: 'College of Glamour', icon: 'college-glamour' },
	'Lore': { value: ObjectiveCollege.Lore, name: 'Lore', fullName: 'College of Lore', icon: 'college-lore' },
	'Swords': { value: ObjectiveCollege.Swords, name: 'Swords', fullName: 'College of Swords', icon: 'college-swords' },
}

export class SelectCollegeModal extends SuggestModal<ObjectiveCollege> {
	protected dpe?: DeferredPromiseExecutor<ObjectiveCollege | undefined>

	static prompt = (currentValue?: ObjectiveCollege) => {
		const modal = new SelectCollegeModal(getApp())
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override getSuggestions(query: string) {
		return Object.values(colleges).map(x => x.value)
	}

	renderSuggestion(college: ObjectiveCollege, el: HTMLElement) {
		const item = el.createEl('p7t-icon-item')
		const collegeSpec = colleges[ObjectiveCollege[college] as keyof typeof ObjectiveCollege]
		item.data = college
		item.icon = collegeSpec.icon
		item.text = collegeSpec.fullName
	}

	override async onClose() {
		await sleep(500)
		this.dpe?.reject()
	}

	override async onChooseSuggestion(item: ObjectiveCollege, _: MouseEvent | KeyboardEvent) {
		this.dpe?.resolve(item)
	}
}