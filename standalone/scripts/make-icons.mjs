// Builds the PLAINTORCH branding the installer and the executables embed, from the marks in standalone/assets.
// Runs under Electron itself, so there is no image-library dependency:
//
//   npm run make-icons            (in standalone/; or: node_modules/.bin/electron scripts/make-icons.mjs)
//
// Inputs (the established PLAINTORCH marks): assets/plaintorch-full.png (the 512px full-colour mark) and
// ../sipa/assets/design/plaintorch-bgx.png (the splash art without its lettering).
//
// Outputs, all under ../branding (installer and build resources — never copied into the app bundle):
// - plaintorch.ico: 16…256 px of the full-colour mark (classic 32-bit DIB entries up to 128, a PNG entry at 256), for
//   the shell's executable, installer, uninstaller and shortcuts, and for the core and CLI executables.
// - installer-sidebar.bmp (164×314) and installer-header.bmp (150×57): the NSIS welcome/finish sidebar and page header.
import { app, BrowserWindow, nativeImage } from "electron"
import { mkdirSync, readFileSync, writeFileSync } from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"

const here = path.dirname(fileURLToPath(import.meta.url))
const assets = path.join(here, "..", "assets")
const branding = path.join(here, "..", "..", "branding")
const markPath = path.join(assets, "plaintorch-full.png")
const artPath = path.join(here, "..", "..", "sipa", "assets", "design", "plaintorch-bgx.png")

const icoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
const background = "#1f1a22"

/**
 * Straight-alpha BGRA pixels of an image, top-down; ICO wants straight alpha. Chromium's bitmaps are usually
 * premultiplied — recognisable because no channel then exceeds its alpha — and are converted only when they are.
 */
function straightBgra(image) {
	const pixels = Buffer.from(image.toBitmap())
	let premultiplied = true
	for (let i = 0; i < pixels.length && premultiplied; i += 4) {
		premultiplied = pixels[i] <= pixels[i + 3] && pixels[i + 1] <= pixels[i + 3] && pixels[i + 2] <= pixels[i + 3]
	}

	for (let i = 0; premultiplied && i < pixels.length; i += 4) {
		const alpha = pixels[i + 3]
		if (alpha > 0 && alpha < 255) {
			for (let c = 0; c < 3; c++) {
				pixels[i + c] = Math.min(255, Math.round(pixels[i + c] * 255 / alpha))
			}
		}
	}
	return pixels
}

/**
 * One ICO image: a BITMAPINFOHEADER, the XOR bitmap bottom-up, and the AND mask derived from alpha — a set bit wherever
 * a pixel is fully transparent — for whatever draws an icon by its mask rather than its alpha (low colour depths,
 * masked image lists), which would otherwise paint the transparent corners black.
 */
function dibEntry(image, size) {
	const pixels = straightBgra(image)
	const header = Buffer.alloc(40)
	header.writeUInt32LE(40, 0)
	header.writeInt32LE(size, 4)
	header.writeInt32LE(size * 2, 8)
	header.writeUInt16LE(1, 12)
	header.writeUInt16LE(32, 14)
	header.writeUInt32LE(0, 16)
	header.writeUInt32LE(size * size * 4, 20)
	const xor = Buffer.alloc(size * size * 4)
	for (let row = 0; row < size; row++) {
		pixels.copy(xor, (size - 1 - row) * size * 4, row * size * 4, (row + 1) * size * 4)
	}
	const maskStride = Math.ceil(size / 32) * 4
	const mask = Buffer.alloc(maskStride * size)
	for (let row = 0; row < size; row++) {
		for (let x = 0; x < size; x++) {
			if (pixels[(row * size + x) * 4 + 3] === 0) {
				mask[(size - 1 - row) * maskStride + (x >> 3)] |= 0x80 >> (x & 7)
			}
		}
	}
	return Buffer.concat([header, xor, mask])
}

function buildIco(mark) {
	const images = icoSizes.map(size => {
		const scaled = mark.resize({ width: size, height: size, quality: "best" })
		return { size, data: size >= 256 ? scaled.toPNG() : dibEntry(scaled, size) }
	})
	const header = Buffer.alloc(6)
	header.writeUInt16LE(0, 0)
	header.writeUInt16LE(1, 2)
	header.writeUInt16LE(images.length, 4)
	const directory = Buffer.alloc(16 * images.length)
	let offset = 6 + directory.length
	images.forEach(({ size, data }, index) => {
		const entry = index * 16
		directory.writeUInt8(size >= 256 ? 0 : size, entry)
		directory.writeUInt8(size >= 256 ? 0 : size, entry + 1)
		directory.writeUInt8(0, entry + 2)
		directory.writeUInt8(0, entry + 3)
		directory.writeUInt16LE(1, entry + 4)
		directory.writeUInt16LE(32, entry + 6)
		directory.writeUInt32LE(data.length, entry + 8)
		directory.writeUInt32LE(offset, entry + 12)
		offset += data.length
	})
	return Buffer.concat([header, directory, ...images.map(image => image.data)])
}

/** An opaque image as a 24-bit bottom-up BMP, the format NSIS takes for its sidebar and header. */
function buildBmp(image) {
	const { width, height } = image.getSize()
	const pixels = image.toBitmap()
	const stride = Math.ceil(width * 3 / 4) * 4
	const data = Buffer.alloc(stride * height)
	for (let y = 0; y < height; y++) {
		for (let x = 0; x < width; x++) {
			const source = (y * width + x) * 4
			const target = (height - 1 - y) * stride + x * 3
			data[target] = pixels[source]
			data[target + 1] = pixels[source + 1]
			data[target + 2] = pixels[source + 2]
		}
	}
	const header = Buffer.alloc(54)
	header.write("BM", 0, "ascii")
	header.writeUInt32LE(54 + data.length, 2)
	header.writeUInt32LE(54, 10)
	header.writeUInt32LE(40, 14)
	header.writeInt32LE(width, 18)
	header.writeInt32LE(height, 22)
	header.writeUInt16LE(1, 26)
	header.writeUInt16LE(24, 28)
	header.writeUInt32LE(data.length, 34)
	header.writeInt32LE(2835, 38)
	header.writeInt32LE(2835, 42)
	return Buffer.concat([header, data])
}

const dataUri = file => `data:image/png;base64,${readFileSync(file).toString("base64")}`

/** Draws the sidebar and header on canvases in a hidden window: the art as a cover, the mark on top. */
const compose = () => `
	const load = src => new Promise((resolve, reject) => {
		const image = new Image()
		image.onload = () => resolve(image)
		image.onerror = () => reject(new Error('An input image does not decode.'))
		image.src = src
	})
	const draw = async (width, height, paint) => {
		const canvas = document.createElement('canvas')
		canvas.width = width
		canvas.height = height
		const context = canvas.getContext('2d')
		context.imageSmoothingQuality = 'high'
		context.fillStyle = ${JSON.stringify(background)}
		context.fillRect(0, 0, width, height)
		await paint(context, width, height)
		return canvas.toDataURL('image/png')
	}
	;(async () => {
		const [art, mark] = await Promise.all([load(${JSON.stringify(dataUri(artPath))}), load(${JSON.stringify(dataUri(markPath))})])
		const sidebar = await draw(164, 314, (context, width, height) => {
			const scale = height / art.height
			context.drawImage(art, (width - art.width * scale) / 2, 0, art.width * scale, height)
			const shade = context.createLinearGradient(0, 0, 0, height)
			shade.addColorStop(0, 'rgba(31, 26, 34, 0.15)')
			shade.addColorStop(1, 'rgba(31, 26, 34, 0.65)')
			context.fillStyle = shade
			context.fillRect(0, 0, width, height)
			context.drawImage(mark, (width - 104) / 2, 56, 104, 104)
		})
		const header = await draw(150, 57, (context, width, height) => {
			context.drawImage(mark, width - 49, (height - 45) / 2, 45, 45)
		})
		return { sidebar, header }
	})()
`

async function main() {
	mkdirSync(branding, { recursive: true })
	const mark = nativeImage.createFromPath(markPath)
	if (mark.isEmpty()) {
		throw new Error(`Cannot read ${markPath}`)
	}

	const script = compose()
	writeFileSync(path.join(branding, "plaintorch.ico"), buildIco(mark))

	const window = new BrowserWindow({ show: false, webPreferences: { offscreen: true } })
	await window.loadURL("about:blank")
	const { sidebar, header } = await window.webContents.executeJavaScript(script)
	writeFileSync(path.join(branding, "installer-sidebar.bmp"), buildBmp(nativeImage.createFromDataURL(sidebar)))
	writeFileSync(path.join(branding, "installer-header.bmp"), buildBmp(nativeImage.createFromDataURL(header)))

	console.log(`Wrote plaintorch.ico (${icoSizes.join(", ")}), installer-sidebar.bmp and installer-header.bmp to ${branding}`)
}

// Electron does not exit on a failure of its own accord, least of all with a hidden window open: a missing or broken
// input must end the run, with a status a script can see, rather than leave it hanging.
app.whenReady()
	.then(main)
	.then(() => app.exit(0), error => {
		console.error(error instanceof Error ? error.message : error)
		app.exit(1)
	})
