// Dev preview tool: prints the long + short humanised reading of an Orbit notation, using the model-based
// humaniser (sipa/orbits/scheduleDescribe). No node_modules needed — the orbits files are self-contained.
//
//   npx tsx sipa/orbit-humanize.tsx "w[d{1,3,5}]"                       # one-shot (one or more notations)
//   npx tsx sipa/orbit-humanize.tsx --calendar pleiadean "y[M{6}[d{5}]]"
//   npx tsx sipa/orbit-humanize.tsx                                     # REPL: type a notation, see both forms
//
// --calendar / -c picks the reference calendar for naming (gregorian | pleiadean); default gregorian.

import { createInterface } from "node:readline"
import type { CalendarSystem } from "./orbits/calendar"
import { describeOrbit, resolveCalendar } from "./orbits/scheduleDescribe"

const EXAMPLES = [
	"w[d{1,3,5}]",
	"w[d{1}[h{5,16}[m{0}]]]",
	"z{12:00}",
	"M[d{1,15}]",
	"y[M{6}[d{5}]]",
	"w[d{5}[h{17}[m{30}]]]%2",
	"d{1}=2h",
	"w[d{1}]@5",
	"d{1}>2027-01-01",
]

function cap(text: string): string {
	return text.length === 0 ? text : text.charAt(0).toUpperCase() + text.slice(1)
}

function show(notation: string, calendar: CalendarSystem): void {
	const n = notation.trim()
	if (!n) return
	const desc = describeOrbit(n, calendar)
	process.stdout.write(`\n  ${n}\n`)
	if (desc.error) {
		process.stdout.write(`    (unsupported: ${desc.error})\n`)
		return
	}
	process.stdout.write(`    long   ${cap(desc.long ?? "")}\n`)
	process.stdout.write(`    short  ${cap(desc.short ?? "")}\n`)
}

let calendarName = "gregorian"
const notations: string[] = []
const rawArgs = process.argv.slice(2)
for (let i = 0; i < rawArgs.length; i++) {
	const arg = rawArgs[i]!
	if (arg === "--calendar" || arg === "-c") {
		calendarName = rawArgs[++i] ?? calendarName
	} else if (arg.startsWith("--calendar=")) {
		calendarName = arg.slice("--calendar=".length)
	} else {
		notations.push(arg)
	}
}
const calendar = resolveCalendar(calendarName)

if (notations.length > 0) {
	for (const notation of notations) show(notation, calendar)
	process.exit(0)
}

process.stdout.write(`Orbit humaniser preview (calendar: ${calendarName}) — type a notation, Enter to read it (Ctrl-D / Ctrl-C to quit).\n`)
process.stdout.write("Examples:\n")
for (const example of EXAMPLES) show(example, calendar)

const rl = createInterface({ input: process.stdin, output: process.stdout, prompt: "\norbit> " })
rl.prompt()
rl.on("line", line => {
	show(line, calendar)
	rl.prompt()
})
rl.on("close", () => {
	process.stdout.write("\nbye.\n")
	process.exit(0)
})
