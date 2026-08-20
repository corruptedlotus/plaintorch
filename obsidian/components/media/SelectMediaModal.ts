import { Notice, SuggestModal } from 'obsidian'
import { createDeferredExecutor, DeferredPromiseExecutor } from '@open-draft/deferred-promise'
import { core, getApp } from '..'
import { IconName } from 'components/PleiadesIcon'
import { ASSET_FOLDER, pickImageFile, resolveMediaUrl } from './mediaAssets'
import { searchIconKeys } from './iconCatalog'

/**
 * Identifies the entity a media field belongs to, for entity-level (`media:`) uploads and browsing. Omit it for a
 * field whose entity keeps no asset folder of its own (a timeframe, say) — the modal then leaves out the entity
 * option and offers only vault media, icons, and removal.
 */
export interface MediaEntityRef {
	entityType: string
	entityId: string
}

/** The `media:` (entity) and `vault:` (shared) schemes an asset key carries. */
type AssetScope = 'self' | 'vault'

/** One row of the modal. Navigation rows switch mode and keep the modal open; every other row is terminal. */
type MediaItem =
	| { kind: 'nav'; to: AssetScope | 'icon'; label: string; icon: string }
	| { kind: 'remove' }
	| { kind: 'upload'; scope: AssetScope }
	| { kind: 'asset'; scope: AssetScope; file: string }
	| { kind: 'icon'; key: string }

const asIcon = (value: string): IconName => value as IconName

const schemeOf = (scope: AssetScope): string => (scope === 'self' ? 'media:' : 'vault:')

/**
 * Picks a media key for a media field (PEP105). One suggest box drives four modes off its text: its empty root
 * offers entity media (`media:`), vault media (`vault:`), an icon search, and removal; typing a `media:`/`vault:`
 * scheme browses that asset folder (upload always first); typing anything else searches the Pleiades and lucide
 * icon catalogs, with lucide hits carrying their `lucide:` scheme. Uploads and listings go through the media domain
 * (`core.media`); this modal only ever resolves to a key — to `null` to clear the field, or rejects when dismissed.
 */
export class SelectMediaModal extends SuggestModal<MediaItem> {
	private dpe?: DeferredPromiseExecutor<string | null | undefined>
	private entity?: MediaEntityRef
	private settled = false
	private forcedIcon = false
	private selfAssetsCache?: string[]

	/**
	 * Opens the picker for a media field. `entity` enables the entity-media option (omit it for a field with no
	 * self asset folder). Resolves to the chosen media key, `null` to clear, or `undefined` when dismissed.
	 */
	static prompt = (entity?: MediaEntityRef): Promise<string | null | undefined> => {
		const modal = new SelectMediaModal(getApp())
		modal.entity = entity
		modal.setPlaceholder('Media, icon, or a media:/vault: file…')
		modal.dpe = createDeferredExecutor()
		modal.open()
		return new Promise(modal.dpe)
	}

	private get hasSelf(): boolean {
		return !!this.entity
	}

	override async getSuggestions(query: string): Promise<MediaItem[]> {
		if (query.startsWith('media:')) {
			return this.hasSelf ? this.assetItems('self', query.slice('media:'.length)) : this.rootItems()
		}

		if (query.startsWith('vault:')) {
			return this.assetItems('vault', query.slice('vault:'.length))
		}

		if (query.startsWith('lucide:') || this.forcedIcon || query.trim().length > 0) {
			return searchIconKeys(query).map(key => ({ kind: 'icon', key } as MediaItem))
		}

		return this.rootItems()
	}

	private rootItems(): MediaItem[] {
		const items: MediaItem[] = []
		if (this.hasSelf) {
			items.push({ kind: 'nav', to: 'self', label: 'Entity media…', icon: 'lucide:folder' })
		}

		items.push({ kind: 'nav', to: 'vault', label: 'Vault media…', icon: 'lucide:library' })
		items.push({ kind: 'nav', to: 'icon', label: 'Icon…', icon: 'lucide:shapes' })
		items.push({ kind: 'remove' })
		return items
	}

	private async assetItems(scope: AssetScope, filter: string): Promise<MediaItem[]> {
		const files = scope === 'self' ? await this.selfAssets() : await core.media.listVault()
		const needle = filter.trim().toLowerCase()
		const matched = needle ? files.filter(name => name.toLowerCase().includes(needle)) : files
		return [{ kind: 'upload', scope }, ...matched.map(file => ({ kind: 'asset', scope, file } as MediaItem))]
	}

	private async selfAssets(): Promise<string[]> {
		if (this.selfAssetsCache === undefined) {
			this.selfAssetsCache = this.entity
				? await core.media.listEntity(this.entity.entityType, this.entity.entityId)
				: []
		}

		return this.selfAssetsCache
	}

	override renderSuggestion(item: MediaItem, el: HTMLElement) {
		const row = el.createEl('p7t-icon-item')
		switch (item.kind) {
			case 'nav':
				row.icon = asIcon(item.icon)
				row.text = item.label
				return
			case 'remove':
				row.icon = asIcon('lucide:trash-2')
				row.text = 'Remove media'
				return
			case 'upload':
				row.icon = asIcon('lucide:upload')
				row.text = 'Upload a new image…'
				return
			case 'asset': {
				const preview = item.scope === 'vault'
					? resolveMediaUrl(getApp(), `${ASSET_FOLDER}/${item.file}`)
					: undefined
				row.icon = asIcon(preview ?? 'lucide:image')
				row.text = item.file
				return
			}
			case 'icon':
				row.icon = asIcon(item.key)
				row.text = item.key
				return
		}
	}

	/** Overridden so a navigation row re-drives the search in a new mode instead of closing the modal. */
	override selectSuggestion(item: MediaItem, evt: MouseEvent | KeyboardEvent) {
		if (item.kind === 'nav') {
			this.navigateTo(item.to)
			return
		}

		this.onChooseSuggestion(item, evt)
	}

	override onChooseSuggestion(item: MediaItem, _evt: MouseEvent | KeyboardEvent) {
		switch (item.kind) {
			case 'remove':
				this.settle(null)
				return
			case 'icon':
				this.settle(item.key)
				return
			case 'asset':
				this.settle(`${schemeOf(item.scope)}${item.file}`)
				return
			case 'upload':
				void this.upload(item.scope)
				return
			case 'nav':
				this.navigateTo(item.to)
				return
		}
	}

	private navigateTo(to: AssetScope | 'icon') {
		this.forcedIcon = to === 'icon'
		this.setQuery(to === 'icon' ? '' : schemeOf(to))
	}

	/** Rewrites the search box and re-runs the suggestion query, as if the text had been typed. */
	private setQuery(value: string) {
		this.inputEl.value = value
		this.inputEl.dispatchEvent(new Event('input'))
		this.inputEl.focus()
	}

	private async upload(scope: AssetScope) {
		const picked = await pickImageFile()
		if (!picked) {
			return
		}

		try {
			const key = scope === 'vault'
				? await core.media.uploadVault(picked.fileName, picked.bytes)
				: this.entity
					? await core.media.uploadEntity(this.entity.entityType, this.entity.entityId, picked.fileName, picked.bytes)
					: undefined
			if (!key) {
				new Notice('PLAINTORCH could not store that image.')
				return
			}

			this.settle(key)
		} catch {
			new Notice('PLAINTORCH could not store that image.')
		}
	}

	private settle(key: string | null) {
		if (this.settled) {
			return
		}

		this.settled = true
		this.dpe?.resolve(key)
		this.close()
	}

	override async onClose() {
		await sleep(500)
		if (!this.settled) {
			this.dpe?.reject()
		}
	}
}
