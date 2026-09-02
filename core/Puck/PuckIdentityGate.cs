namespace Pleiades.Puck;

/// <summary>
/// Gates whether a candidate identifier is a genuine PUCK for an entity type — i.e. it tokenizes against that type's
/// declared PUCK notation. Filename-derived identities (Index storage's <c>{id} - {title}</c> convention) must pass
/// this gate before they are trusted, so an ordinary user note whose name merely contains the <c>" - "</c> separator
/// is never materialized or purged as an invalid-PUCK entity.
/// </summary>
public sealed class PuckIdentityGate(PuckTokenizer puckTokenizer)
{
	/// <summary>
	/// Determines whether <paramref name="candidateId"/> tokenizes against <paramref name="entityType"/>'s declared
	/// PUCK notation (i.e. the declaration could itself have minted it).
	/// </summary>
	public bool IsMintable(Type entityType, string? candidateId)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		if (string.IsNullOrWhiteSpace(candidateId))
		{
			return false;
		}

		try
		{
			puckTokenizer.TokenizeFor(entityType, candidateId);
			return true;
		}
		catch (Exception exception) when (exception is FormatException or InvalidOperationException or NotSupportedException)
		{
			return false;
		}
	}
}
