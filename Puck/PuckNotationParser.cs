namespace Pleiades.Puck;

/// <summary>
/// Parses PUCK declaration strings into structured notation records.
/// </summary>
public sealed class PuckNotationParser
{
	/// <summary>
	/// Parses a PUCK declaration.
	/// </summary>
	/// <param name="declaration">The declaration text to parse.</param>
	/// <returns>The parsed notation model.</returns>
	public PuckNotation Parse(string declaration)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(declaration);

		var segments = new List<PuckSegmentPattern>();
		var cursor = 0;
		while (cursor < declaration.Length)
		{
			var usesDynamicDiscriminator = false;
			string? staticDiscriminator = null;
			if (declaration[cursor] == '*')
			{
				usesDynamicDiscriminator = true;
				cursor++;
			}
			else
			{
				var discriminatorStart = cursor;
				while (cursor < declaration.Length && declaration[cursor] != '{' && declaration[cursor] != '-' && declaration[cursor] != '/')
				{
					cursor++;
				}

				if (cursor > discriminatorStart)
				{
					staticDiscriminator = declaration[discriminatorStart..cursor];
				}
			}

			var numerator = ParseNumerator(declaration, ref cursor);
			var forceNesting = false;
			if (cursor < declaration.Length && declaration[cursor] == '!')
			{
				forceNesting = true;
				cursor++;
			}

			var nesting = PuckNestingKind.None;
			var repetition = PuckRepetitionMode.None;
			if (cursor < declaration.Length)
			{
				nesting = declaration[cursor] switch
				{
					'-' => PuckNestingKind.Telescope,
					'/' => PuckNestingKind.Filesystem,
					_ => PuckNestingKind.None,
				};

				if (nesting != PuckNestingKind.None)
				{
					cursor++;
					if (cursor < declaration.Length)
					{
						repetition = declaration[cursor] switch
						{
							'%' => PuckRepetitionMode.RepeatPreviousSegment,
							'@' => PuckRepetitionMode.RepeatWholePattern,
							_ => PuckRepetitionMode.None,
						};
						if (repetition != PuckRepetitionMode.None)
						{
							cursor++;
						}
					}
				}
			}

			segments.Add(new PuckSegmentPattern(staticDiscriminator, usesDynamicDiscriminator, numerator, nesting, forceNesting, repetition));
		}

		return new PuckNotation(segments);
	}

	private static PuckNumeratorPattern ParseNumerator(string declaration, ref int cursor)
	{
		if (cursor >= declaration.Length || declaration[cursor] != '{')
		{
			return new PuckNumeratorPattern(PuckNumeratorKind.Manual);
		}

		var closingBrace = declaration.IndexOf('}', cursor);
		if (closingBrace < 0)
		{
			throw new FormatException("PUCK declaration contains an unterminated numerator token.");
		}

		var token = declaration[(cursor + 1)..closingBrace];
		cursor = closingBrace + 1;

		if (token == "?")
		{
			return new PuckNumeratorPattern(PuckNumeratorKind.Manual);
		}

		if (token == "D")
		{
			return new PuckNumeratorPattern(PuckNumeratorKind.DateStamp);
		}

		if (token.StartsWith("S:", StringComparison.Ordinal))
		{
			var width = int.Parse(token[2..], System.Globalization.CultureInfo.InvariantCulture);
			return new PuckNumeratorPattern(PuckNumeratorKind.Spiritgem, width);
		}

		if (token.StartsWith("I:", StringComparison.Ordinal))
		{
			var parts = token.Split(':');
			if (parts.Length != 3)
			{
				throw new FormatException("Incremental PUCK notation must look like {I:k:i}.");
			}

			return new PuckNumeratorPattern(
				PuckNumeratorKind.Incremental,
				int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
				long.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
		}

		throw new FormatException($"Unsupported PUCK numerator token '{{{token}}}'.");
	}
}
