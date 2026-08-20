import { ObjectiveCollege } from "@pleiades/sdk";
import { SuggestModal } from "obsidian";
import { getApp } from ".";
import { collegeDescriptors, collegeDescriptorOf } from "../entities/collegeDescriptors";
import { createDeferredExecutor, DeferredPromiseExecutor } from "@open-draft/deferred-promise";

export class SelectCollegeModal extends SuggestModal<ObjectiveCollege> {
	protected dpe?: DeferredPromiseExecutor<ObjectiveCollege | undefined>

	static prompt = (_currentValue?: ObjectiveCollege) => {
		const modal = new SelectCollegeModal(getApp())
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	override getSuggestions(_query: string) {
		return Object.values(collegeDescriptors).map(x => x.value)
	}

	renderSuggestion(college: ObjectiveCollege, el: HTMLElement) {
		const item = el.createEl('p7t-icon-item')
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