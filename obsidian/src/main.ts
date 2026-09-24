import { addIcon, Notice, Plugin, type WorkspaceLeaf } from "obsidian"
import { PlaintorchBriefingView, PLAINTORCH_BRIEFING_VIEW_TYPE } from "./briefing/PlaintorchBriefingView"
import { PlaintorchCanvasView, PLAINTORCH_CANVAS_VIEW_TYPE } from "./canvas/PlaintorchCanvasView"
import { PlaintorchGlobalFileView, PLAINTORCH_GLOBAL_VIEW_TYPE } from "./canvas/PlaintorchGlobalFileView"
import { PageBannerRenderer } from "./banner/PageBannerRenderer"

import '@pleiades/sipa'
import { GLOBAL_CONTEXT_EXTENSION, provideCore, provideHost } from "@pleiades/sipa"
import { plaintorchNodeCoreClient } from "@pleiades/sdk/plaintorch/node"
import { ObsidianHost } from "./host/ObsidianHost"

export default class PlaintorchObsidianPlugin extends Plugin {
	public override async onload(): Promise<void> {
		// Before anything renders: the SIPA components reach for the host while they draw, not only when acted on,
		// and take their repositories from the core client as they are built.
		provideHost(new ObsidianHost(this.app))
		provideCore(plaintorchNodeCoreClient)

		addIcon("plaintorch", `
			<?xml version="1.0" encoding="UTF-8"?>
				<svg id="Layer_1" data-name="Layer 1" xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="40 40 960 960">
				<defs>
					<style>

					.cls-3 {
						fill: currentColor;
					}

					.cls-4 {
						fill: none;
					}
					</style>
				</defs>
				<g class="cls-2">
					<circle class="cls-4" cx="500" cy="500" r="500"/>
				</g>
				<g class="cls-1">
					<circle class="cls-4" cx="500" cy="500" r="500"/>
				</g>
				<path class="cls-3" d="M725.03,477.41l-110.71-50c-18.53-8.37-33.36-23.2-41.73-41.73l-50-110.71c-8.78-19.44-36.39-19.44-45.18,0l-50,110.71c-8.37,18.53-23.2,33.36-41.73,41.73l-110.71,50c-19.44,8.78-19.44,36.39,0,45.18l110.71,50c18.53,8.37,33.36,23.2,41.73,41.73l50,110.71c8.78,19.44,36.39,19.44,45.18,0l50-110.71c8.37-18.53,23.2-33.36,41.73-41.73l110.71-50c19.44-8.78,19.44-36.39,0-45.18ZM591.81,505.54l-51.1,23.08c-5.36,2.42-9.66,6.72-12.08,12.08l-23.08,51.1c-2.15,4.77-8.93,4.77-11.08,0l-23.08-51.1c-2.42-5.36-6.72-9.66-12.08-12.08l-51.1-23.08c-4.77-2.15-4.77-8.93,0-11.08l51.1-23.08c5.36-2.42,9.66-6.72,12.08-12.08l23.08-51.1c2.15-4.77,8.93-4.77,11.08,0l23.08,51.1c2.42,5.36,6.72,9.66,12.08,12.08l51.1,23.08c4.77,2.15,4.77,8.93,0,11.08Z"/>
				<g>
					<path class="cls-3" d="M806.63,356.63s0,0-.01-.01c-1.63-1.76-3.56-3-5.79-3.72-2.15-1.13-4.46-1.64-6.94-1.55-2.48-.09-4.79.42-6.94,1.55-2.23.73-4.16,1.97-5.79,3.72l-2.81,3.64c-1.63,2.81-2.45,5.84-2.46,9.08l.64,4.79c.86,3.05,2.4,5.7,4.63,7.94,0,0,0,0,.01.01,1.63,1.76,3.56,3,5.79,3.72,2.15,1.13,4.46,1.64,6.94,1.55,2.48.09,4.79-.42,6.94-1.55,2.23-.73,4.16-1.97,5.79-3.72l2.81-3.64c1.63-2.81,2.45-5.84,2.46-9.08l-.64-4.79c-.86-3.05-2.4-5.7-4.63-7.94,0,0,0,0-.01-.01-8.49,8.49-16.97,16.97-25.46,25.46,0,0,0,0,.01.01,1.63,1.76,3.56,3,5.79,3.72,2.15,1.13,4.46,1.64,6.94,1.55,2.48.09,4.79-.42,6.94-1.55,2.23-.73,4.16-1.97,5.79-3.72l2.81-3.64c1.63-2.81,2.45-5.84,2.46-9.08l-.64-4.79c-.86-3.05-2.4-5.7-4.63-7.94Z"/>
					<path class="cls-3" d="M806.62,356.62c-3.66-3.85-7.32-7.7-10.98-11.55-8.49,8.49-16.97,16.97-25.46,25.46,3.83,3.69,7.48,7.53,10.97,11.54,6.2,7.13,19.19,6.83,25.46,0,7.15-7.79,6.37-17.64,0-25.46-58.12-71.3-166.71-68.17-237.68-18.49-19.56,13.69-36.17,30.09-50.23,49.37-5.57,7.64-1.54,20.4,6.46,24.63,9.35,4.94,18.69,1.69,24.63-6.46.61-.83,1.22-1.65,1.83-2.48,2.91-3.98-1.46,1.68.48-.62,1.33-1.56,2.59-3.19,3.92-4.74,3-3.51,6.14-6.9,9.4-10.17,3.08-3.09,6.27-6.07,9.56-8.93,1.55-1.35,3.12-2.67,4.71-3.97.8-.65,1.61-1.28,2.4-1.93-3.07,2.49.92-.67,1.26-.92,7.03-5.18,14.42-9.88,22.09-14.05,3.84-2.09,7.74-4.04,11.71-5.86.93-.43,1.88-.82,2.81-1.26,2.48-1.17-2.6,1.02-.05,0,2.39-.94,4.77-1.9,7.18-2.78,8.44-3.07,17.1-5.54,25.89-7.37,2.01-.42,4.03-.8,6.05-1.15,1.35-.23,6.72-.99,2.82-.5,4.58-.58,9.19-1,13.8-1.22,4.88-.24,9.78-.27,14.67-.1,2.38.08,4.75.22,7.13.4,1.19.09,2.37.2,3.55.31.65.06,5.42.66,3.81.42-2.08-.3,1.58.27,2.01.34,1.17.2,2.34.4,3.51.63,2.62.5,5.23,1.07,7.82,1.71,5.17,1.28,10.28,2.84,15.28,4.7,1.11.41,2.21.84,3.31,1.28-2.31-.93.52.24,1.41.65,2.65,1.22,5.26,2.53,7.82,3.93,4.67,2.56,9.12,5.46,13.45,8.56,2.84,2.03.96.7.36.23,1.32,1.04,2.61,2.11,3.88,3.2,2.12,1.82,4.19,3.71,6.19,5.66,4.02,3.91,7.76,8.08,11.3,12.42,8.49-8.49,16.97-16.97,25.46-25.46-3.49-4.01-7.14-7.86-10.97-11.54-6.79-6.53-18.79-7.26-25.46,0s-6.94,18.16,0,25.46c3.66,3.85,7.32,7.7,10.98,11.55,6.5,6.84,18.99,7.04,25.46,0,6.73-7.34,6.94-18.16,0-25.46h0Z"/>
					<path class="cls-3" d="M713.82,881.62c10.37-13.84,19.36-28.87,28.36-43.6,17.79-29.13,34.43-59,49.71-89.52,18.75-37.46,35.69-76.06,48.92-115.84,12.46-37.49,21.91-76.9,22.78-116.57.59-26.63-3.57-53.66-11.89-78.95-4.85-14.74-11.05-29.17-18.57-42.74-3.51-6.33-7.34-12.47-11.48-18.4-4.57-6.55-9.3-13.77-15.02-19.37-6.73-6.59-18.83-7.22-25.46,0-7.21,7.86-6.28,17.56,0,25.46,3.43,4.32-1.85-2.8,1.31,1.73.97,1.39,1.97,2.75,2.92,4.16,1.59,2.34,3.1,4.73,4.55,7.16,1.74,2.92,3.38,5.9,4.91,8.92.77,1.52,1.51,3.04,2.23,4.59.29.63,2.24,5.12.91,1.93,2.39,5.75,4.38,11.67,6.01,17.68.89,3.29,1.67,6.6,2.33,9.95.23,1.14.44,2.27.64,3.41.12.66,1.05,7.52.62,3.79,1.54,13.28,1.33,26.5,0,39.78-.57,5.64.66-3.49-.28,2.1-.19,1.14-.36,2.29-.56,3.43-.59,3.43-1.28,6.84-2.06,10.23-1.56,6.79-3.5,13.5-5.78,20.09-2.41,6.95-3.8,10.27-6.04,15.07-1.83,3.91-3.78,7.75-5.85,11.53-3.68,6.74-7.68,13.31-11.85,19.77-5.06,7.84-10.39,15.5-15.89,23.03-2.95,4.04-5.96,8.03-9,12.01,2.69-3.52-.26.32-.68.86-.75.95-1.49,1.9-2.24,2.85-2,2.53-4.01,5.04-6.05,7.54-16.03,19.72-33.06,38.64-50.81,56.82-20.4,20.9-42.19,40.28-63.35,60.39-35.05,33.3-69.17,68.06-99.28,105.95-11.88,14.94-23.58,30.3-32.95,46.98-6.48,11.53-12.78,24.57-12.92,38.09-.13,12.7,11.55,19.19,22.79,17.36,67.3-11.01,134.88-23.43,198.27-49.4,8.68-3.56,15.81-12.12,12.57-22.14-2.71-8.37-12.81-16.39-22.14-12.57-63.39,25.97-130.98,38.39-198.27,49.4,7.6,5.79,15.19,11.57,22.79,17.36,0-.86.21-1.78.15-2.63.39,5.76-.65,4.15-.13,1.66.46-2.21,1.13-4.37,1.88-6.49.38-1.07,1-2.18,1.26-3.28-1.44,6.07-.79,1.84.15-.15,1.32-2.82,2.77-5.59,4.28-8.31,3.47-6.25,7.33-12.3,11.35-18.21,2.23-3.27,4.52-6.5,6.85-9.7,1.13-1.54,2.26-3.08,3.41-4.61.71-.94,1.42-1.88,2.13-2.82.48-.64.97-1.27,1.46-1.91-1.58,2.04-1.77,2.29-.57.75,29.85-37.86,63.47-72.66,98.28-105.97,21.34-20.42,43.49-39.97,64.23-61.02,18.56-18.83,36.35-38.44,53.06-58.92,25.74-31.54,50.66-64.92,65.42-103.18,11.12-28.84,15.98-60.98,12.37-91.75-3.86-32.96-16.33-62.81-36.98-88.77l-25.46,25.46c1.94,1.9,3.59,4.27,5.27,6.4-2.31-2.92,1.04,1.4,1.21,1.64.66.89,1.31,1.79,1.95,2.69,3.87,5.44,7.46,11.07,10.79,16.85,3.07,5.33,5.9,10.79,8.5,16.35.71,1.52,1.39,3.06,2.08,4.59,2.28,5.02-1.21-3.24.8,1.89,1.23,3.13,2.45,6.27,3.58,9.44,8.43,23.8,11.91,42.65,12.26,65.72.13,8.78-.26,17.57-1.06,26.31-.22,2.35-.46,4.7-.73,7.05-.09.79-.19,1.58-.28,2.37-.52,4.19-.64.22.02-.03-.53.2-.95,6.38-1.08,7.18-.39,2.42-.8,4.83-1.24,7.23-3.49,19.17-8.46,38.06-14.32,56.63-6.08,19.25-13.13,38.19-20.85,56.84-.5,1.22-1.01,2.43-1.52,3.65-1.68,4.02,1.14-2.6.02-.06-.97,2.2-1.9,4.43-2.86,6.64-2.08,4.79-4.21,9.56-6.38,14.32-4.08,8.95-8.29,17.84-12.62,26.67-15.14,30.85-31.69,61.01-49.41,90.46-10.28,17.09-20.59,34.56-32.57,50.55-5.67,7.57-1.47,20.44,6.46,24.63,9.45,4.99,18.58,1.62,24.63-6.46Z"/>
				</g>
				<g>
					<path class="cls-3" d="M193.37,643.37s0,0,.01.01c1.63,1.76,3.56,3,5.79,3.72,2.15,1.13,4.46,1.64,6.94,1.55,2.48.09,4.79-.42,6.94-1.55,2.23-.73,4.16-1.97,5.79-3.72l2.81-3.64c1.63-2.81,2.45-5.84,2.46-9.08l-.64-4.79c-.86-3.05-2.4-5.7-4.63-7.94,0,0,0,0-.01-.01-1.63-1.76-3.56-3-5.79-3.72-2.15-1.13-4.46-1.64-6.94-1.55-2.48-.09-4.79.42-6.94,1.55-2.23.73-4.16,1.97-5.79,3.72l-2.81,3.64c-1.63,2.81-2.45,5.84-2.46,9.08l.64,4.79c.86,3.05,2.4,5.7,4.63,7.94,0,0,0,0,.01.01,8.49-8.49,16.97-16.97,25.46-25.46,0,0,0,0-.01-.01-1.63-1.76-3.56-3-5.79-3.72-2.15-1.13-4.46-1.64-6.94-1.55-2.48-.09-4.79.42-6.94,1.55-2.23.73-4.16,1.97-5.79,3.72l-2.81,3.64c-1.63,2.81-2.45,5.84-2.46,9.08l.64,4.79c.86,3.05,2.4,5.7,4.63,7.94Z"/>
					<path class="cls-3" d="M193.38,643.38c3.66,3.85,7.32,7.7,10.98,11.55,8.49-8.49,16.97-16.97,25.46-25.46-3.83-3.69-7.48-7.53-10.97-11.54-6.2-7.13-19.19-6.83-25.46,0-7.15,7.79-6.37,17.64,0,25.46,58.12,71.3,166.71,68.17,237.68,18.49,19.56-13.69,36.17-30.09,50.23-49.37,5.57-7.64,1.54-20.4-6.46-24.63-9.35-4.94-18.69-1.69-24.63,6.46-.61.83-1.22,1.65-1.83,2.48-2.91,3.98,1.46-1.68-.48.62-1.33,1.56-2.59,3.19-3.92,4.74-3,3.51-6.14,6.9-9.4,10.17-3.08,3.09-6.27,6.07-9.56,8.93-1.55,1.35-3.12,2.67-4.71,3.97-.8.65-1.61,1.28-2.4,1.93,3.07-2.49-.92.67-1.26.92-7.03,5.18-14.42,9.88-22.09,14.05-3.84,2.09-7.74,4.04-11.71,5.86-.93.43-1.88.82-2.81,1.26-2.48,1.17,2.6-1.02.05,0-2.39.94-4.77,1.9-7.18,2.78-8.44,3.07-17.1,5.54-25.89,7.37-2.01.42-4.03.8-6.05,1.15-1.35.23-6.72.99-2.82.5-4.58.58-9.19,1-13.8,1.22-4.88.24-9.78.27-14.67.1-2.38-.08-4.75-.22-7.13-.4-1.19-.09-2.37-.2-3.55-.31-.65-.06-5.42-.66-3.81-.42,2.08.3-1.58-.27-2.01-.34-1.17-.2-2.34-.4-3.51-.63-2.62-.5-5.23-1.07-7.82-1.71-5.17-1.28-10.28-2.84-15.28-4.7-1.11-.41-2.21-.84-3.31-1.28,2.31.93-.52-.24-1.41-.65-2.65-1.22-5.26-2.53-7.82-3.93-4.67-2.56-9.12-5.46-13.45-8.56-2.84-2.03-.96-.7-.36-.23-1.32-1.04-2.61-2.11-3.88-3.2-2.12-1.82-4.19-3.71-6.19-5.66-4.02-3.91-7.76-8.08-11.3-12.42-8.49,8.49-16.97,16.97-25.46,25.46,3.49,4.01,7.14,7.86,10.97,11.54,6.79,6.53,18.79,7.26,25.46,0s6.94-18.16,0-25.46c-3.66-3.85-7.32-7.7-10.98-11.55-6.5-6.84-18.99-7.04-25.46,0-6.73,7.34-6.94,18.16,0,25.46h0Z"/>
					<path class="cls-3" d="M286.18,118.38c-10.37,13.84-19.36,28.87-28.36,43.6-17.79,29.13-34.43,59-49.71,89.52-18.75,37.46-35.69,76.06-48.92,115.84-12.46,37.49-21.91,76.9-22.78,116.57-.59,26.63,3.57,53.66,11.89,78.95,4.85,14.74,11.05,29.17,18.57,42.74,3.51,6.33,7.34,12.47,11.48,18.4,4.57,6.55,9.3,13.77,15.02,19.37,6.73,6.59,18.83,7.22,25.46,0,7.21-7.86,6.28-17.56,0-25.46-3.43-4.32,1.85,2.8-1.31-1.73-.97-1.39-1.97-2.75-2.92-4.16-1.59-2.34-3.1-4.73-4.55-7.16-1.74-2.92-3.38-5.9-4.91-8.92-.77-1.52-1.51-3.04-2.23-4.59-.29-.63-2.24-5.12-.91-1.93-2.39-5.75-4.38-11.67-6.01-17.68-.89-3.29-1.67-6.6-2.33-9.95-.23-1.14-.44-2.27-.64-3.41-.12-.66-1.05-7.52-.62-3.79-1.54-13.28-1.33-26.5,0-39.78.57-5.64-.66,3.49.28-2.1.19-1.14.36-2.29.56-3.43.59-3.43,1.28-6.84,2.06-10.23,1.56-6.79,3.5-13.5,5.78-20.09,2.41-6.95,3.8-10.27,6.04-15.07,1.83-3.91,3.78-7.75,5.85-11.53,3.68-6.74,7.68-13.31,11.85-19.77,5.06-7.84,10.39-15.5,15.89-23.03,2.95-4.04,5.96-8.03,9-12.01-2.69,3.52.26-.32.68-.86.75-.95,1.49-1.9,2.24-2.85,2-2.53,4.01-5.04,6.05-7.54,16.03-19.72,33.06-38.64,50.81-56.82,20.4-20.9,42.19-40.28,63.35-60.39,35.05-33.3,69.17-68.06,99.28-105.95,11.88-14.94,23.58-30.3,32.95-46.98,6.48-11.53,12.78-24.57,12.92-38.09.13-12.7-11.55-19.19-22.79-17.36-67.3,11.01-134.88,23.43-198.27,49.4-8.68,3.56-15.81,12.12-12.57,22.14,2.71,8.37,12.81,16.39,22.14,12.57,63.39-25.97,130.98-38.39,198.27-49.4-7.6-5.79-15.19-11.57-22.79-17.36,0,.86-.21,1.78-.15,2.63-.39-5.76.65-4.15.13-1.66-.46,2.21-1.13,4.37-1.88,6.49-.38,1.07-1,2.18-1.26,3.28,1.44-6.07.79-1.84-.15.15-1.32,2.82-2.77,5.59-4.28,8.31-3.47,6.25-7.33,12.3-11.35,18.21-2.23,3.27-4.52,6.5-6.85,9.7-1.13,1.54-2.26,3.08-3.41,4.61-.71.94-1.42,1.88-2.13,2.82-.48.64-.97,1.27-1.46,1.91,1.58-2.04,1.77-2.29.57-.75-29.85,37.86-63.47,72.66-98.28,105.97-21.34,20.42-43.49,39.97-64.23,61.02-18.56,18.83-36.35,38.44-53.06,58.92-25.74,31.54-50.66,64.92-65.42,103.18-11.12,28.84-15.98,60.98-12.37,91.75,3.86,32.96,16.33,62.81,36.98,88.77,8.49-8.49,16.97-16.97,25.46-25.46-1.94-1.9-3.59-4.27-5.27-6.4,2.31,2.92-1.04-1.4-1.21-1.64-.66-.89-1.31-1.79-1.95-2.69-3.87-5.44-7.46-11.07-10.79-16.85-3.07-5.33-5.9-10.79-8.5-16.35-.71-1.52-1.39-3.06-2.08-4.59-2.28-5.02,1.21,3.24-.8-1.89-1.23-3.13-2.45-6.27-3.58-9.44-8.43-23.8-11.91-42.65-12.26-65.72-.13-8.78.26-17.57,1.06-26.31.22-2.35.46-4.7.73-7.05.09-.79.19-1.58.28-2.37.52-4.19.64-.22-.02.03.53-.2.95-6.38,1.08-7.18.39-2.42.8-4.83,1.24-7.23,3.49-19.17,8.46-38.06,14.32-56.63s13.13-38.19,20.85-56.84c.5-1.22,1.01-2.43,1.52-3.65,1.68-4.02-1.14,2.6-.02.06.97-2.2,1.9-4.43,2.86-6.64,2.08-4.79,4.21-9.56,6.38-14.32,4.08-8.95,8.29-17.84,12.62-26.67,15.14-30.85,31.69-61.01,49.41-90.46,10.28-17.09,20.59-34.56,32.57-50.55,5.67-7.57,1.47-20.44-6.46-24.63-9.45-4.99-18.58-1.62-24.63,6.46Z"/>
				</g>
			</svg>
		`)

		const renderer = new PageBannerRenderer(this.app)
		this.registerEditorExtension(renderer.createEditorExtension())
		this.registerMarkdownPostProcessor(renderer.readingModeRenderer)

		this.registerView(
			PLAINTORCH_BRIEFING_VIEW_TYPE,
			(leaf: WorkspaceLeaf) => new PlaintorchBriefingView(leaf)
		)

		this.registerView(
			PLAINTORCH_CANVAS_VIEW_TYPE,
			(leaf: WorkspaceLeaf) => new PlaintorchCanvasView(leaf)
		)

		// A saved global planning context is a `.p7tpx` file; binding the extension to its view is what makes
		// opening one — from the file explorer, a link, anywhere — open it in the canvas.
		this.registerView(
			PLAINTORCH_GLOBAL_VIEW_TYPE,
			(leaf: WorkspaceLeaf) => new PlaintorchGlobalFileView(leaf)
		)
		this.registerExtensions([GLOBAL_CONTEXT_EXTENSION], PLAINTORCH_GLOBAL_VIEW_TYPE)

		// Core/watcher health indicator: a status-bar dot + hover tooltip listing active statuses (PEP108).
		this.addStatusBarItem().appendChild(document.createElement("p7t-watcher-status"))

		this.addRibbonIcon("plaintorch", "PLAINTORCH briefing", () => {
			void this.activateBriefingView()
		})

		this.addCommand({
			id: "open-plaintorch-briefing",
			name: "Open PLAINTORCH briefing",
			callback: () => {
				void this.activateBriefingView()
			}
		})

		this.addCommand({
			id: "open-plaintorch-dependency-canvas",
			name: "Open PLAINTORCH dependency canvas",
			callback: () => {
				void this.activateCanvasView()
			}
		})

		this.addCommand({
			id: "open-plaintorch-global-planning",
			name: "New global planning (new leaf)",
			callback: () => {
				void this.openGlobalPlanning()
			}
		})

		this.addCommand({
			id: "init-directive-from-current-file",
			name: "Initialize directive from current file",
			callback: () => {
				void this.initializeDirectiveFromCurrentFile()
			}
		})

		this.addCommand({
			id: "init-objective-from-current-file",
			name: "Initialize objective from current file",
			callback: () => {
				void this.initializeObjectiveFromCurrentFile()
			}
		})

		this.startChangeFeed()
	}

	public override onunload(): void {
		plaintorchNodeCoreClient.repos.changeFeed.stop()
		plaintorchNodeCoreClient.repos.stopEvictionSweep()
		this.app.workspace.detachLeavesOfType(PLAINTORCH_BRIEFING_VIEW_TYPE)
		this.app.workspace.detachLeavesOfType(PLAINTORCH_CANVAS_VIEW_TYPE)
	}

	/**
	 * Listens for writes made outside the plugin, so a markdown edit the watcher picks up or a change made
	 * by the CLI reaches whatever is on screen.
	 *
	 * The feed is an optimization, never a requirement — revalidating on leaf activation covers the case
	 * where it cannot be established at all, and is what the plugin relied on before it existed.
	 */
	private startChangeFeed(): void {
		const coreClient = plaintorchNodeCoreClient
		coreClient.repos.changeFeed.start()
		// Bound the identity map over a long session. Housekeeping, so it rides the same session lifecycle as
		// the feed rather than earning its own; stopped in onunload.
		coreClient.repos.startEvictionSweep()

		this.registerEvent(this.app.workspace.on("active-leaf-change", () => {
			if (!coreClient.repos.changeFeed.connected) {
				void coreClient.repos.revalidateObserved()
			}
		}))
	}

	private async activateBriefingView(): Promise<void> {
		const workspace = this.app.workspace
		workspace.detachLeavesOfType(PLAINTORCH_BRIEFING_VIEW_TYPE)
		const leaf = workspace.getLeaf(true)

		await leaf.setViewState({
			type: PLAINTORCH_BRIEFING_VIEW_TYPE,
			active: true
		})

		workspace.revealLeaf(leaf)
	}

	/**
	 * Opens the dependency canvas in its own leaf, reusing the one already open rather than replacing it.
	 *
	 * Unlike the briefing, this is a surface to keep beside whatever is being planned, so an existing canvas
	 * is revealed instead of detached and rebuilt — which would throw away its pan, zoom and arrangement.
	 */
	private async activateCanvasView(): Promise<void> {
		const workspace = this.app.workspace
		const [existing] = workspace.getLeavesOfType(PLAINTORCH_CANVAS_VIEW_TYPE)
		const leaf = existing ?? workspace.getLeaf(true)

		if (!existing) {
			await leaf.setViewState({
				type: PLAINTORCH_CANVAS_VIEW_TYPE,
				active: true
			})
		}

		workspace.revealLeaf(leaf)
	}

	/**
	 * Opens a fresh scratch global planning context in a new leaf.
	 *
	 * Always a new leaf, never reused: unlike a saved `.p7tpx`, a scratch context has no identity, so opening
	 * one is always opening a blank one. It starts unsaved; the canvas's own "Save to file" is how it becomes
	 * durable.
	 */
	private async openGlobalPlanning(): Promise<void> {
		const workspace = this.app.workspace
		const leaf = workspace.getLeaf(true)
		await leaf.setViewState({
			type: PLAINTORCH_CANVAS_VIEW_TYPE,
			active: true
		})

		const view = leaf.view
		if (view instanceof PlaintorchCanvasView) {
			view.setMode("global")
		}

		workspace.revealLeaf(leaf)
	}

	private async initializeDirectiveFromCurrentFile(): Promise<void> {
		const activeFile = this.app.workspace.getActiveFile()
		if (!activeFile || activeFile.extension.toLowerCase() !== "md") {
			new Notice("Open a markdown file to initialize a directive")
			return
		}

		try {
			const initialized = await plaintorchNodeCoreClient.directives.init({ path: activeFile.path })
			if (!initialized) {
				new Notice("Directive initialization did not return an entity")
				return
			}

			new Notice(`Directive initialized: ${initialized.id}`)
		}
		catch (error) {
			console.error("Failed to initialize directive from current file", error)
			new Notice(`Directive initialization failed: ${describeError(error)}`)
		}
	}

	private async initializeObjectiveFromCurrentFile(): Promise<void> {
		const activeFile = this.app.workspace.getActiveFile()
		if (!activeFile || activeFile.extension.toLowerCase() !== "md") {
			new Notice("Open a markdown file to initialize an objective")
			return
		}

		try {
			const initialized = await plaintorchNodeCoreClient.objectives.init({ path: activeFile.path })
			if (!initialized) {
				new Notice("Objective initialization did not return an entity")
				return
			}

			new Notice(`Objective initialized: ${initialized.id}`)
		}
		catch (error) {
			console.error("Failed to initialize objective from current file", error)
			new Notice(`Objective initialization failed: ${describeError(error)}`)
		}
	}
}

function describeError(error: unknown): string {
	if (error instanceof Error && error.message) {
		return error.message
	}

	return "unknown error"
}
