namespace Pleiades.Orbits;

// This file is part of the C# port of @pleiades/orbits (the orbit-scheduler repository,
// packages/node/src). Keep the semantics bit-identical with the TypeScript engine; the
// port is intended to graduate into orbit-scheduler's packages/dotnet.

/// <summary>
/// The calendar units an orbit notation can address. Numeric values double as
/// granularity weights (larger = finer).
/// </summary>
public enum OrbitUnit
{
	Year = 1,
	Month = 2,
	Week = 3,
	Day = 4,
	Hour = 5,
	Minute = 6,
	Second = 7,
}

/// <summary>
/// Helpers for mapping <see cref="OrbitUnit"/> to and from notation characters.
/// </summary>
public static class OrbitUnits
{
	/// <summary>
	/// Tries to map a notation character (<c>y M w d h m s</c>) to its unit.
	/// </summary>
	public static bool TryFromChar(char value, out OrbitUnit unit)
	{
		switch (value)
		{
			case 'y': unit = OrbitUnit.Year; return true;
			case 'M': unit = OrbitUnit.Month; return true;
			case 'w': unit = OrbitUnit.Week; return true;
			case 'd': unit = OrbitUnit.Day; return true;
			case 'h': unit = OrbitUnit.Hour; return true;
			case 'm': unit = OrbitUnit.Minute; return true;
			case 's': unit = OrbitUnit.Second; return true;
			default: unit = default; return false;
		}
	}

	/// <summary>
	/// Maps a unit back to its notation character.
	/// </summary>
	public static char ToChar(OrbitUnit unit) => unit switch
	{
		OrbitUnit.Year => 'y',
		OrbitUnit.Month => 'M',
		OrbitUnit.Week => 'w',
		OrbitUnit.Day => 'd',
		OrbitUnit.Hour => 'h',
		OrbitUnit.Minute => 'm',
		OrbitUnit.Second => 's',
		_ => throw new ArgumentOutOfRangeException(nameof(unit)),
	};
}

/// <summary>
/// The supported index specifications inside <c>{...}</c>.
/// </summary>
public enum OrbitIndexKind
{
	List,
	Range,
	Random,
}

/// <summary>
/// An index specification attached to a time-unit node.
/// </summary>
public sealed class OrbitIndexSpec
{
	public required OrbitIndexKind Kind { get; init; }
	public List<int> Values { get; init; } = [];
	public int Start { get; init; }
	public int End { get; init; }
	public int Count { get; init; }
}

/// <summary>
/// The supported limit kinds.
/// </summary>
public enum OrbitLimitKind
{
	/// <summary>
	/// <c>*x</c>: the node's repetition. Each of its runs keeps its first x values: a bare unit is one run from the
	/// start of the node's life (<c>d*5</c> is five days, <c>w[d*3]</c> the first three days of every week), and an
	/// index is a run of one value unless <c>%</c> steps it (<c>d{5}%3*4</c> is the 5th, 8th, 11th and 14th), so on
	/// an index <c>%</c> does not step — a list or a range — it does nothing.
	/// </summary>
	Iterations,
	/// <summary>
	/// <c>@x</c>: the node's emission. Only its first x instances in each life fire — per period of its written
	/// parent when nested (<c>M[w[d{1}%2]@7]</c> fires up to seven times a month), once in all at the top level.
	/// </summary>
	Instances,
	/// <summary><c>&lt;t</c>: until before a timestamp.</summary>
	Before,
	/// <summary><c>&gt;t</c>: since after a timestamp.</summary>
	After,
}

/// <summary>
/// A limit attached to a time-unit node.
/// </summary>
public sealed class OrbitLimitSpec
{
	public required OrbitLimitKind Kind { get; init; }
	public int Count { get; init; }
	public string Timestamp { get; init; } = string.Empty;
}

/// <summary>
/// One component of a span duration, e.g. <c>2h</c>.
/// </summary>
public readonly record struct OrbitDurationPart(OrbitUnit Unit, int Count);

/// <summary>
/// Base of the orbit AST: either a time-unit node or a set operation.
/// </summary>
public abstract class OrbitAstNode
{
}

/// <summary>
/// The modifiers a base node accepts after its body: <c>%interval</c>, limits (<c>*x @x &lt;t &gt;t</c>)
/// and a span <c>=&lt;dur&gt;</c>. Both <see cref="OrbitTimeUnitNode"/> and <see cref="OrbitDateTimeLiteralNode"/>
/// carry them, so the parser can apply modifiers to either through this surface.
/// </summary>
public interface IOrbitModifiable
{
	int? Interval { get; set; }
	List<OrbitLimitSpec> Limits { get; }
	List<OrbitDurationPart>? Duration { get; set; }
}

/// <summary>
/// A time-unit node with optional indexing, child, interval, limits, and span duration.
/// </summary>
public sealed class OrbitTimeUnitNode : OrbitAstNode, IOrbitModifiable
{
	public required OrbitUnit Unit { get; init; }
	public OrbitIndexSpec? Indices { get; set; }
	public OrbitAstNode? Child { get; set; }
	public int? Interval { get; set; }
	public List<OrbitLimitSpec> Limits { get; } = [];
	public List<OrbitDurationPart>? Duration { get; set; }
}

/// <summary>
/// A literal moment from the <c>z</c>/<c>Z</c> shorthand: <c>z{h:m[:s]}</c> is a daily time-of-day
/// (no date part), <c>Z{y/M/d[Th:m[:s]]}</c> a fixed calendar datetime. Kept as its own node so a
/// humanizer can render it directly; the engine expands it into the equivalent nested
/// <see cref="OrbitTimeUnitNode"/> chain (deepest present component fixes the granularity) before
/// resolving, so the solver needs no literal awareness.
/// </summary>
public sealed class OrbitDateTimeLiteralNode : OrbitAstNode, IOrbitModifiable
{
	public int? Year { get; init; }
	public int? Month { get; init; }
	public int? Day { get; init; }
	public int? Hour { get; init; }
	public int? Minute { get; init; }
	public int? Second { get; init; }
	public int? Interval { get; set; }
	public List<OrbitLimitSpec> Limits { get; } = [];
	public List<OrbitDurationPart>? Duration { get; set; }
}

/// <summary>
/// The supported set operators.
/// </summary>
public enum OrbitSetOperator
{
	Union,
	Intersection,
	SymmetricDifference,
	Exclusion,
}

/// <summary>
/// A set operation over two subtrees.
/// </summary>
public sealed class OrbitSetOperationNode : OrbitAstNode
{
	public required OrbitSetOperator Operator { get; init; }
	public required OrbitAstNode Left { get; init; }
	public required OrbitAstNode Right { get; init; }
}
