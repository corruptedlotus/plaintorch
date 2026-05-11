using System.Reflection;

namespace Pleiades.Puck;

/// <summary>
/// Determines whether two PUCK-managed types are distinguishable from path identity alone.
/// </summary>
public sealed class PuckPathDiscriminabilityService(PuckNotationParser notationParser)
{
	/// <summary>
	/// Determines whether two PUCK-managed types are distinguishable from their identifier prefixes.
	/// </summary>
	public bool AreDistinguishable(Type leftType, Type rightType)
	{
		ArgumentNullException.ThrowIfNull(leftType);
		ArgumentNullException.ThrowIfNull(rightType);

		var leftNotation = notationParser.Parse(GetNotation(leftType));
		var rightNotation = notationParser.Parse(GetNotation(rightType));
		var leftFirst = leftNotation.Segments.FirstOrDefault();
		var rightFirst = rightNotation.Segments.FirstOrDefault();
		if (leftFirst is null || rightFirst is null)
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(leftFirst.StaticDiscriminator) && !string.IsNullOrWhiteSpace(rightFirst.StaticDiscriminator))
		{
			return !string.Equals(leftFirst.StaticDiscriminator, rightFirst.StaticDiscriminator, StringComparison.OrdinalIgnoreCase);
		}

		if (!string.IsNullOrWhiteSpace(leftFirst.StaticDiscriminator) || !string.IsNullOrWhiteSpace(rightFirst.StaticDiscriminator))
		{
			return true;
		}

		return false;
	}

	private static string GetNotation(Type entityType)
	{
		var attribute = entityType.GetCustomAttribute<PuckFormatAttribute>()
			?? throw new InvalidOperationException($"Type '{entityType.Name}' is not decorated with {nameof(PuckFormatAttribute)}.");

		return attribute.Notation;
	}
}