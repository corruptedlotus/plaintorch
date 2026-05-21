import { Component, component, css, CSSResultGroup, html, property } from "@a11d/lit";
import { plaintorchCoreClient as core } from '@pleiades/sdk'

@component('p7t-icon')
export class PleiadesIcon extends Component {
	@property() icon = ''

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: stretch;
				justify-content: stretch;
				width: 48px;
				height: 48px;
				position: relative;
			}

			img {
				width: 100%;
				height: 100%;
				object-fit: contain;
			}
		`
	}

	protected override render() {
		return html`
			<img src=${core.icon(this.icon)} />
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-icon': PleiadesIcon
	}
}