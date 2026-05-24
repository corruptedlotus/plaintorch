namespace Pleiades.Puck;

/// <summary>
/// Determines whether two PUCK-managed types are distinguishable from path identity alone.
/// </summary>
public sealed class PuckPathDiscriminabilityService(PuckRuntimeCompilationCatalog compilationCatalog)
{
	/// <summary>
	/// Determines whether two PUCK-managed types are distinguishable from their identifier prefixes.
	/// </summary>
	public bool AreDistinguishable(Type leftType, Type rightType)
	{
		ArgumentNullException.ThrowIfNull(leftType);
		ArgumentNullException.ThrowIfNull(rightType);

		var leftFirst = compilationCatalog.GetCompiled(leftType).FirstSegment;
		var rightFirst = compilationCatalog.GetCompiled(rightType).FirstSegment;
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

}