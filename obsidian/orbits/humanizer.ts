// @ts-nocheck
// Vendored from @pleiades/orbits (orbit-scheduler, packages/node/src/humanizer.ts).
// Kept verbatim so it stays in sync with upstream; edit upstream and re-vendor
// rather than diverging here. @ts-nocheck keeps the plugin's strict tsconfig off
// upstream code (mirrors assets/icons/index.ts).

import { ASTNode, TimeUnitNode, DateTimeLiteralNode, TimeUnit } from './ast';
import { CalendarSystem } from './calendar';

export class OrbitHumanizer {
	private calendar: CalendarSystem;
	private readonly UNIT_LABELS: Record<TimeUnit, string> = { y: 'year', M: 'month', w: 'week', d: 'day', h: 'hour', m: 'minute', s: 'second' };

	constructor(calendar: CalendarSystem) {
		this.calendar = calendar;
	}

	public serialize(node: ASTNode, isRoot = true): string {
		if (node.kind === 'SetOperationNode') {
			const left = this.serialize(node.left, isRoot);
			const right = this.serialize(node.right, isRoot);
			switch (node.operator) {
				case '+': return `${left}, and ${right}`;
				case '&': return `${left} that are also ${right}`;
				case '-': return `${left}, excluding ${right}`;
				case '^': return `${left}, differing from ${right}`;
			}
		}
		if (node.kind === 'DateTimeLiteralNode') {
			return this.serializeDateTimeLiteral(node, isRoot);
		}
		return this.serializeTimeUnit(node, isRoot);
	}

	// A z/Z literal reads directly off its components: a time-only literal is a daily
	// clock ("every day at 12:00" / "at 12:00"), a dated one an absolute datetime
	// ("5 June 2027" / "5 June 2027 at 18:00").
	private serializeDateTimeLiteral(node: DateTimeLiteralNode, isRoot: boolean): string {
		const time = node.hour !== undefined
			? (node.second !== undefined
				? `${this.pad(node.hour)}:${this.pad(node.minute!)}:${this.pad(node.second)}`
				: `${this.pad(node.hour)}:${this.pad(node.minute!)}`)
			: null;

		if (node.year !== undefined) {
			const monthName = this.calendar.getUnitName('M', node.month!, 'y') ?? `${node.month}`;
			const date = `${node.day} ${monthName} ${node.year}`;
			return time ? `${date} at ${time}` : date;
		}
		return (isRoot ? 'every day at ' : 'at ') + time;
	}

	private serializeTimeUnit(node: TimeUnitNode, isRoot: boolean, parentUnit?: TimeUnit): string {
		// 1. Lookahead: Weekly Day Shorthand (e.g., w{1}[d{1}] -> "the 1st Monday")
		const weeklyDayFormat = this.tryFormatAsWeeklyDay(node);
		if (weeklyDayFormat) {
			return weeklyDayFormat;
		}

		// 2. Lookahead: Static Clock Shorthand (e.g., h{12}[m{30}] -> "at 12:30")
		if (node.unit === 'h' && node.indices && node.indices.type === 'list' && !node.interval) {
			const timeFormat = this.tryFormatAsClockTime(node);
			if (timeFormat) {
				let text = timeFormat.rawTimeText;
				if (timeFormat.remainingChild) {
					const childText = this.serialize(timeFormat.remainingChild, false);
					return `${childText} at ${text}`;
				}
				return (isRoot ? "every day at " : "at ") + text;
			}
		}

		// 3. Regular Unit Assembly
		let indexText = '';
		const hasIndices = !!node.indices;
		const hasInterval = node.interval && node.interval > 1;

		if (hasIndices) {
			const spec = node.indices!;
			if (spec.type === 'random') {
				indexText = `${spec.count} random ${this.UNIT_LABELS[node.unit]}s`;
			} else if (spec.type === 'range') {
				const startName = this.calendar.getUnitName(node.unit, spec.start, parentUnit);
				const endName = this.calendar.getUnitName(node.unit, spec.end, parentUnit);
				indexText = (startName && endName)
					? `${startName} through ${endName}`
					: `${this.UNIT_LABELS[node.unit]} ${spec.start} through ${spec.end}`;
			} else if (spec.type === 'list') {
				const formatted = spec.values.map(v => this.calendar.getUnitName(node.unit, v, parentUnit) || this.toOrdinal(v));
				indexText = this.joinList(formatted);
				const hasNames = formatted.some(f => isNaN(Number(f.replace(/(st|nd|rd|th)/, ''))));
				if (!hasNames && !hasInterval) {
					indexText += ` ${this.UNIT_LABELS[node.unit]}${spec.values.length > 1 ? 's' : ''}`;
				}
			}
		}

		let text = '';
		if (hasInterval && hasIndices) {
			const isNamed = indexText.match(/^[A-Za-z]/) && !indexText.startsWith(this.UNIT_LABELS[node.unit]);
			text = `every ${node.interval} ${this.UNIT_LABELS[node.unit]}s starting from ${isNamed ? '' : 'the '}${indexText}`;
		} else if (hasInterval) {
			text = `every ${node.interval} ${this.UNIT_LABELS[node.unit]}s`;
		} else if (hasIndices) {
			text = indexText;
		} else {
			text = isRoot ? `every ${this.UNIT_LABELS[node.unit]}` : `the ${this.UNIT_LABELS[node.unit]}`;
		}

		if (node.child) {
			const childText = this.serialize(node.child, false);
			return `${childText} of ${text}`;
		}

		return text;
	}

	private tryFormatAsWeeklyDay(node: TimeUnitNode): string | null {
		if (node.unit !== 'w' || !node.indices || node.interval) return null;

		let weekNum: number;
		if (node.indices.type === 'list' && node.indices.values.length === 1) weekNum = node.indices.values[0];
		else if (node.indices.type === 'range' && node.indices.start === node.indices.end) weekNum = node.indices.start;
		else return null;

		const child = node.child;
		if (!child || child.kind !== 'TimeUnitNode' || child.unit !== 'd' || !child.indices || child.interval) return null;

		let dayNum: number;
		if (child.indices.type === 'list' && child.indices.values.length === 1) dayNum = child.indices.values[0];
		else if (child.indices.type === 'range' && child.indices.start === child.indices.end) dayNum = child.indices.start;
		else return null;

		const dayName = this.calendar.getUnitName('d', dayNum, 'w');
		if (!dayName) return null;

		const text = `${this.toOrdinal(weekNum)} ${dayName}`;
		if (child.child) {
			const subChildText = this.serialize(child.child, false);
			return `${subChildText} of the ${text}`;
		}
		return `the ${text}`;
	}

	private tryFormatAsClockTime(node: TimeUnitNode) {
		const hours = node.indices!.values as number[];
		let minutes: number[] = [0], seconds: number[] = [];
		let current = node.child, consumedM = false;

		if (current && current.kind === 'TimeUnitNode' && current.unit === 'm' && !current.interval && current.indices?.type === 'list') {
			minutes = current.indices.values; consumedM = true; current = current.child;
		}
		if (consumedM && current && current.kind === 'TimeUnitNode' && current.unit === 's' && !current.interval && current.indices?.type === 'list') {
			seconds = current.indices.values; current = current.child;
		}
		if (!consumedM) return null;

		const formattedStrings: string[] = [];
		for (const h of hours) {
			for (const m of minutes) {
				formattedStrings.push(seconds.length > 0
					? seconds.map(s => `${this.pad(h)}:${this.pad(m)}:${this.pad(s)}`).join(', ')
					: `${this.pad(h)}:${this.pad(m)}`
				);
			}
		}
		return { rawTimeText: this.joinList(formattedStrings), remainingChild: current };
	}

	private pad(n: number): string { return n.toString().padStart(2, '0'); }
	private joinList(l: string[]): string {
		if (l.length === 0) return '';
		if (l.length === 1) return l[0]!;
		if (l.length === 2) return `${l[0]} and ${l[1]}`;
		return `${l.slice(0, -1).join(', ')}, and ${l[l.length - 1]}`;
	}
	private toOrdinal(n: number): string {
		const s = ["th", "st", "nd", "rd"], v = n % 100;
		return n + (s[(v - 20) % 10] || s[v] || s[0])!;
	}
}
