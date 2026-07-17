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
	/// <summary><c>*x</c>: iterations per enclosing parent cycle.</summary>
	Iterations,
	/// <summary><c>@x</c>: total instances across the stream.</summary>
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
/// A time-unit node with optional indexing, child, interval, limits, and span duration.
/// </summary>
public sealed class OrbitTimeUnitNode : OrbitAstNode
{
	public required OrbitUnit Unit { get; init; }
	public OrbitIndexSpec? Indices { get; set; }
	public OrbitAstNode? Child { get; set; }
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
