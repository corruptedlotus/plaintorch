namespace Pleiades.Puck;

/// <summary>
/// Projects baseline PUCK tokenization data into higher-level semantic models without competing with the original notation.
/// </summary>
public sealed class PuckSemanticProjector
{
	/// <summary>
	/// Projects baseline PUCK tokens into a semantic dictionary using explicit mappings.
	/// </summary>
	/// <param name="tokenization">The baseline tokenization.</param>
	/// <param name="mappings">The semantic mappings to apply.</param>
	/// <returns>The semantic projection.</returns>
	public PuckSemanticProjection Project(PuckTokenization tokenization, IEnumerable<PuckSemanticMapping> mappings)
	{
		ArgumentNullException.ThrowIfNull(tokenization);
		ArgumentNullException.ThrowIfNull(mappings);

		var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var mapping in mappings)
		{
			if (mapping.SegmentIndex < 0 || mapping.SegmentIndex >= tokenization.Segments.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(mappings), $"Segment index {mapping.SegmentIndex} is outside the available PUCK token range.");
			}

			values[mapping.Key] = ExtractValue(tokenization.Segments[mapping.SegmentIndex], mapping.Source);
		}

		return new PuckSemanticProjection(tokenization.RawValue, tokenization, values);
	}

	/// <summary>
	/// Projects baseline PUCK tokens into a canonical dictionary keyed by their resolved discriminators.
	/// </summary>
	/// <param name="tokenization">The baseline tokenization.</param>
	/// <returns>The semantic projection keyed by discriminator.</returns>
	public PuckSemanticProjection ProjectByDiscriminator(PuckTokenization tokenization)
	{
		ArgumentNullException.ThrowIfNull(tokenization);

		var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var segment in tokenization.Segments)
		{
			if (string.IsNullOrWhiteSpace(segment.Discriminator))
			{
				continue;
			}

			values[segment.Discriminator] = segment.DateValue is not null
				? segment.DateValue
				: segment.NumericValue is not null
					? segment.NumericValue
					: segment.Numerator;
		}

		return new PuckSemanticProjection(tokenization.RawValue, tokenization, values);
	}

	private static object? ExtractValue(PuckSegmentToken token, PuckSemanticValueSource source)
	{
		return source switch
		{
			PuckSemanticValueSource.RawValue => token.RawValue,
			PuckSemanticValueSource.Discriminator => token.Discriminator,
			PuckSemanticValueSource.Numerator => token.Numerator,
			PuckSemanticValueSource.NumericValue => token.NumericValue,
			PuckSemanticValueSource.DateValue => token.DateValue,
			_ => throw new InvalidOperationException($"Unsupported PUCK semantic value source '{source}'."),
		};
	}
}