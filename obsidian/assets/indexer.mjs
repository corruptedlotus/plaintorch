import fs from 'fs';
import path from 'path';

const assetsDir = path.join(process.cwd(), 'assets')

function findAssetFolders(dir) {
	const entries = fs.readdirSync(dir, { withFileTypes: true })
	const folders = entries.filter(entry => entry.isDirectory()).map(entry => entry.name)
	return folders
}

const assetFolders = findAssetFolders(assetsDir);

console.log('> Found asset folders:', assetFolders)

const exportTemplate = (name, relativePath) => `export { default as '${name}' } from '${relativePath}'\n`;

assetFolders.forEach(folder => {
	console.log(`> Indexing assets in folder: ${folder}`)
	const folderPath = path.join(assetsDir, folder)
	const files = fs.readdirSync(folderPath)
	const svgFiles = files.filter(file => file.endsWith('.svg'))
	const pngFiles = files.filter(file => file.endsWith('.png'))
	const indexPath = path.join(folderPath, 'index.ts')
	let indexContent = '// @ts-nocheck\n'
	let i = 0

	svgFiles.forEach(file => {
		const name = path.basename(file, '.svg')
		const relativePath = `./${file}`
		indexContent += exportTemplate(name, relativePath)
		i++
	})

	pngFiles.forEach(file => {
		const name = path.basename(file, '.png')
		const relativePath = `./${file}`
		indexContent += exportTemplate(`${name}-png`, relativePath)
		i++
	})

	console.log(`> Indexed ${i} assets in folder: ${folder}`)
	fs.writeFileSync(indexPath, indexContent)
})