import { bind, BindDirectiveParametersOptions, Binder, ReactiveElement } from "@a11d/lit"

type BinderParameters<T> =
	| [keyPath: KeyPath.Of<T>]
	| [options: BindDirectiveParametersOptions<T>]

type BinderHook<T> = (value: T, keyPath?: KeyPath.Of<T>) => void

export class ReactiveBinder<T> extends Binder<T> {
	protected sourceUpdate?: BinderHook<T>
	protected sourceUpdated?: BinderHook<T>

	constructor(host: ReactiveElement, key: string, hooks?: { sourceUpdate?: BinderHook<T>, sourceUpdated?: BinderHook<T> }) {
		super(host, key)
		this.sourceUpdate = hooks?.sourceUpdate
		this.sourceUpdated = hooks?.sourceUpdated
	}

	override bind = (...[parameter]: BinderParameters<T>) => {
		const key = this.key as keyof ReactiveElement
		const parameters = (typeof parameter === 'string' ? { keyPath: parameter } : parameter) as BindDirectiveParametersOptions<T>
		return bind(this.host, key, this.getParameters(parameters) as unknown as BindDirectiveParametersOptions<ReactiveElement[keyof ReactiveElement]>)
	}

	protected override getParameters(parameters: BindDirectiveParametersOptions<T>) {
		const params = {...parameters} as BindDirectiveParametersOptions<T>

		if (!params.sourceUpdate) params.sourceUpdate = value => this.sourceUpdate?.(value, params.keyPath)
		if (!params.sourceUpdated) params.sourceUpdated = value => this.sourceUpdated?.(value, params.keyPath)
		return params
	}
}