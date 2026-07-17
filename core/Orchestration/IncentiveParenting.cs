namespace Pleiades.Orchestration;

/// <summary>
/// Enforces the PEP100 incentive parent system:
/// objectives may parent objectives (subtasks) or specify a fate as their parent, fates may parent fates,
/// and decrees are exempt from the parent system entirely (they neither parent nor get parented).
/// </summary>
public static class IncentiveParenting
{
	/// <summary>
	/// Validates that <paramref name="parent"/> is an acceptable parent for <paramref name="child"/>.
	/// </summary>
	/// <param name="child">The incentive receiving a parent.</param>
	/// <param name="parent">The prospective parent incentive.</param>
	public static void EnsureValidParent(Incentive child, Incentive parent)
	{
		ArgumentNullException.ThrowIfNull(child);
		ArgumentNullException.ThrowIfNull(parent);

		if (string.Equals(child.Id, parent.Id, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Incentive '{child.Id}' cannot parent itself.");
		}

		if (child is Decree)
		{
			throw new InvalidOperationException("Decrees are exempt from the parent system and cannot specify a parent.");
		}

		if (parent is Decree)
		{
			throw new InvalidOperationException("Decrees are exempt from the parent system and cannot be parents.");
		}

		switch (child)
		{
			case Objective when parent is Objective or Fate:
			case Fate when parent is Fate:
				return;
			default:
				throw new InvalidOperationException(
					$"'{parent.GetType().Name}' is not a valid parent kind for '{child.GetType().Name}': objectives accept objective or fate parents, and fates accept fate parents.");
		}
	}
}
