// Rasterizes assets/plaintorch.svg into the PNG icons Electron needs (nativeImage cannot load SVG for a tray or a
// window icon). Runs under Electron itself, so there is no image-library dependency:
//
//   node_modules/.bin/electron scripts/make-icons.mjs      (or: npm run make-icons)
//
// Outputs, all under assets/: icon.png (256, the brand mark on transparent, for the window/taskbar/installer) and
// tray.png / tray@2x.png / tray@3x.png (16/32/48, the mark on a dark rounded disc so it stays visible on both light
// and dark taskbars). The mark is white line-art; replace plaintorch.svg and re-run to refresh every size.
import { app, BrowserWindow, nativeImage } from "electron"
import { readFileSync, writeFileSync } from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"

const here = path.dirname(fileURLToPath(import.meta.url))
const assets = path.join(here, "..", "assets")
const svg = readFileSync(path.join(assets, "plaintorch.svg"), "utf8")
const svgDataUri = `data:image/svg+xml;base64,${Buffer.from(svg).toString("base64")}`

// Everything renders in ONE 512x256 window and is captured once, then cropped and downscaled: creating several
// windows or downsizing the window itself proved unreliable here (Windows clamps tiny windows; a second capture
// could hang). The left half is the transparent mark for the window/installer icon; the right half is the mark on a
// dark rounded disc for the tray, so it stays visible on light taskbars too.
const cell = 256

function cellDiv(x, disc, pad) {
	const inner = Math.round(cell * (1 - pad * 2))
	const offset = Math.round(cell * pad)
	const discLayer = disc
		? `<div style="position:absolute;inset:0;background:#241e29;border-radius:${Math.round(cell * 0.28)}px"></div>`
		: ""
	return `<div style="position:absolute;left:${x}px;top:0;width:${cell}px;height:${cell}px">${discLayer}<img style="position:absolute;left:${offset}px;top:${offset}px;width:${inner}px;height:${inner}px" src="${svgDataUri}"></div>`
}

function page() {
	return `<!doctype html><html><head><meta charset="utf-8"><style>html,body{margin:0;padding:0;background:transparent}</style></head><body>${cellDiv(0, false, 0.08)}${cellDiv(cell, true, 0.14)}</body></html>`
}

/** Renders the combined sheet once and returns the captured NativeImage. */
function renderSheet() {
	return new Promise((resolve, reject) => {
		const window = new BrowserWindow({
			width: cell * 2,
			height: cell,
			show: false,
			frame: false,
			transparent: true,
			useContentSize: true,
			backgroundColor: "#00000000"
		})
		const timer = setTimeout(() => {
			window.destroy()
			reject(new Error("timed out rendering the icon sheet"))
		}, 8_000)
		window.webContents.once("did-finish-load", () => {
			setTimeout(async () => {
				try {
					const image = await window.webContents.capturePage()
					clearTimeout(timer)
					window.destroy()
					resolve(image)
				}
				catch (error) {
					clearTimeout(timer)
					window.destroy()
					reject(error)
				}
			}, 300)
		})
		void window.loadURL(`data:text/html;base64,${Buffer.from(page()).toString("base64")}`)
	})
}

app.whenReady().then(async () => {
	try {
		const sheet = await renderSheet()
		// capturePage returns a bitmap at the display's scale factor (a 512x256 window can come back 1024x512 on a
		// 200% display), so the split is computed from the actual captured size, not the DIP cell width.
		const { width, height } = sheet.getSize()
		const half = Math.floor(width / 2)
		const iconBase = sheet.crop({ x: 0, y: 0, width: half, height })
		const trayBase = sheet.crop({ x: half, y: 0, width: half, height })

		writeFileSync(path.join(assets, "icon.png"), iconBase.toPNG())
		console.log("wrote icon.png (256px)")
		for (const size of [16, 32, 48]) {
			const scaled = trayBase.resize({ width: size, height: size, quality: "best" })
			writeFileSync(path.join(assets, size === 16 ? "tray.png" : `tray@${size / 16}x.png`), scaled.toPNG())
			console.log(`wrote tray ${size}px`)
		}
	}
	catch (error) {
		console.error("make-icons failed:", error)
	}
	finally {
		app.exit(0)
	}
})
