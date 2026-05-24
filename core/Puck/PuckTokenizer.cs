using System.Globalization;

namespace Pleiades.Puck;

/// <summary>
/// Tokenizes concrete PUCK values using the original PUCK declaration as the single source of truth.
/// </summary>
public sealed class PuckTokenizer(PuckNotationParser notationParser, PuckRuntimeCompilationCatalog compilationCatalog)
{
	/// <summary>
	/// Tokenizes a PUCK value for a type decorated with <see cref="PuckFormatAttribute"/>.
	/// </summary>
	/// <typeparam name="T">The PUCK-managed CLR type.</typeparam>
	/// <param name="puck">The concrete PUCK value.</param>
	/// <returns>The baseline tokenization.</returns>
	public PuckTokenization TokenizeFor<T>(string puck)
	{
		return TokenizeFor(typeof(T), puck);
	}

	/// <summary>
	/// Tokenizes a PUCK value for a type decorated with <see cref="PuckFormatAttribute"/>.
	/// </summary>
	/// <param name="entityType">The PUCK-managed CLR type.</param>
	/// <param name="puck">The concrete PUCK value.</param>
	/// <returns>The baseline tokenization.</returns>
	public PuckTokenization TokenizeFor(Type entityType, string puck)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return Tokenize(compilationCatalog.GetCompiled(entityType).Notation, puck);
	}

	/// <summary>
	/// Tokenizes a concrete PUCK value using a precompiled notation model.
	/// </summary>
	/// <param name="notation">The precompiled notation.</param>
	/// <param name="puck">The concrete PUCK value.</param>
	/// <returns>The baseline tokenization.</returns>
	public PuckTokenization Tokenize(PuckNotation notation, string puck)
	{
		ArgumentNullException.ThrowIfNull(notation);
		ArgumentException.ThrowIfNullOrWhiteSpace(puck);

		var segments = SplitSegments(puck);
		var patterns = ExpandPatterns(notation, segments.Count);
		var tokens = new List<PuckSegmentToken>(segments.Count);

		for (var index = 0; index < segments.Count; index++)
		{
			tokens.Add(ParseSegment(index, segments[index], patterns[index]));
		}

		return new PuckTokenization(puck, notation, tokens);
	}

	/// <summary>
	/// Tokenizes a concrete PUCK value using the supplied declaration.
	/// </summary>
	/// <param name="declaration">The PUCK declaration.</param>
	/// <param name="puck">The concrete PUCK value.</param>
	/// <returns>The baseline tokenization.</returns>
	public PuckTokenization Tokenize(string declaration, string puck)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(declaration);
		return Tokenize(notationParser.Parse(declaration), puck);
	}

	private static IReadOnlyList<string> SplitSegments(string puck)
	{
		var segments = new List<string>();
		var start = 0;
		for (var index = 0; index < puck.Length; index++)
		{
			if (puck[index] != '-' && puck[index] != '/')
			{
				continue;
			}

			if (index == start)
			{
				throw new FormatException("PUCK contains an empty segment.");
			}

			segments.Add(puck[start..index]);
			start = index + 1;
		}

		if (start >= puck.Length)
		{
			throw new FormatException("PUCK cannot terminate with a nesting separator.");
		}

		segments.Add(puck[start..]);
		return segments;
	}

	private static IReadOnlyList<PuckSegmentPattern> ExpandPatterns(PuckNotation notation, int segmentCount)
	{
		if (notation.Segments.Count == 0)
		{
			throw new FormatException("PUCK notation does not contain any segments.");
		}

		if (segmentCount <= notation.Segments.Count)
		{
			var truncated = notation.Segments.Take(segmentCount).ToArray();
			if (truncated.Length > 0)
			{
				truncated[^1] = truncated[^1] with
				{
					Nesting = PuckNestingKind.None,
					Repetition = PuckRepetitionMode.None,
				};
			}

			return truncated;
		}

		var repeatedSegmentIndex = notation.Segments
			.Select((segment, index) => (segment, index))
			.FirstOrDefault(pair => pair.segment.Repetition != PuckRepetitionMode.None)
			.index;

		if (repeatedSegmentIndex == 0 && notation.Segments[0].Repetition == PuckRepetitionMode.None)
		{
			throw new FormatException("PUCK contains more segments than the declaration allows.");
		}

		if (repeatedSegmentIndex != notation.Segments.Count - 1)
		{
			throw new NotSupportedException("Only terminal PUCK repetition markers are currently supported during tokenization.");
		}

		var expanded = notation.Segments.ToList();
		var repeatedSegment = notation.Segments[repeatedSegmentIndex];

		switch (repeatedSegment.Repetition)
		{
			case PuckRepetitionMode.RepeatPreviousSegment:
				while (expanded.Count < segmentCount)
				{
					expanded.Add(repeatedSegment with { Repetition = PuckRepetitionMode.RepeatPreviousSegment });
				}
				break;

			case PuckRepetitionMode.RepeatWholePattern:
				var cycle = notation.Segments.ToArray();
				while (expanded.Count < segmentCount)
				{
					var cycleIndex = (expanded.Count - notation.Segments.Count) % cycle.Length;
					expanded.Add(cycle[cycleIndex]);
				}
				break;
		}

		return expanded;
	}

	private static PuckSegmentToken ParseSegment(int index, string rawSegment, PuckSegmentPattern pattern)
	{
		var discriminator = ResolveDiscriminator(rawSegment, pattern, out var numerator);
		ValidateNumerator(numerator, pattern.Numerator);

		long? numericValue = null;
		DateOnly? dateValue = null;
		if (long.TryParse(numerator, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedNumber))
		{
			numericValue = parsedNumber;
		}

		if (pattern.Numerator.Kind == PuckNumeratorKind.DateStamp)
		{
			dateValue = DateOnly.ParseExact(numerator, "yyyyMMdd", CultureInfo.InvariantCulture);
		}

		return new PuckSegmentToken(
			index,
			rawSegment,
			discriminator,
			numerator,
			pattern.Numerator.Kind,
			pattern.Nesting,
			pattern.ForceNesting,
			pattern.Repetition,
			numericValue,
			dateValue);
	}

	private static string? ResolveDiscriminator(string rawSegment, PuckSegmentPattern pattern, out string numerator)
	{
		if (pattern.UsesDynamicDiscriminator)
		{
			var discriminatorLength = 0;
			while (discriminatorLength < rawSegment.Length && !char.IsDigit(rawSegment[discriminatorLength]))
			{
				discriminatorLength++;
			}

			if (discriminatorLength == 0 || discriminatorLength == rawSegment.Length)
			{
				throw new FormatException($"Dynamic discriminator segment '{rawSegment}' must contain both a discriminator prefix and a numeric numerator.");
			}

			numerator = rawSegment[discriminatorLength..];
			return rawSegment[..discriminatorLength];
		}

		if (!string.IsNullOrWhiteSpace(pattern.StaticDiscriminator))
		{
			if (!rawSegment.StartsWith(pattern.StaticDiscriminator, StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException($"Segment '{rawSegment}' does not match the expected static discriminator '{pattern.StaticDiscriminator}'.");
			}

			numerator = rawSegment[pattern.StaticDiscriminator.Length..];
			return pattern.StaticDiscriminator;
		}

		numerator = rawSegment;
		return null;
	}

	private static void ValidateNumerator(string numerator, PuckNumeratorPattern pattern)
	{
		switch (pattern.Kind)
		{
			case PuckNumeratorKind.Manual:
				if (!string.IsNullOrEmpty(numerator) && !numerator.All(char.IsDigit))
				{
					throw new FormatException($"Manual PUCK numerator '{numerator}' must be numeric.");
				}
				break;

			case PuckNumeratorKind.Spiritgem:
			case PuckNumeratorKind.Incremental:
				if (numerator.Length != pattern.Width || !numerator.All(char.IsDigit))
				{
					throw new FormatException($"PUCK numerator '{numerator}' must be exactly {pattern.Width} numeric characters.");
				}
				break;

			case PuckNumeratorKind.DateStamp:
				if (numerator.Length != 8 || !numerator.All(char.IsDigit))
				{
					throw new FormatException($"Date-stamp PUCK numerator '{numerator}' must be an 8-digit date.");
				}

				DateOnly.ParseExact(numerator, "yyyyMMdd", CultureInfo.InvariantCulture);
				break;
		}
	}
}