import { ObjectiveCollege } from "@pleiades/sdk";
import { createChild, sleep, SuggestModalBase } from "../../host"
import { collegeDescriptors, collegeDescriptorOf } from "../entities/collegeDescriptors";
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise";

export class SelectCollegeModal extends SuggestModalBase<ObjectiveCollege> {
	protected dpe?: DeferredPromiseExecutor<ObjectiveCollege | undefined>

	static prompt = (_currentValue?: ObjectiveCollege) => {
		const modal = new SelectCollegeModal()
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override getSuggestions(_query: string) {
		return Object.values(collegeDescriptors).map(x => x.value)
	}

	renderSuggestion(college: ObjectiveCollege, el: HTMLElement) {
		const item = createChild(el, 'p7t-icon-item')
		const collegeSpec = collegeDescriptorOf(college)
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