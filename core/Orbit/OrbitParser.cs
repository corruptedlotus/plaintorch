namespace Pleiades.Orbits;

// C# port of @pleiades/orbits parser.ts — keep behavior identical to the TypeScript parser.

/// <summary>
/// Parses orbit notation strings into AST nodes.
/// </summary>
public sealed partial class OrbitParser(string input)
{
	private int _pos;
	private readonly string _input = input ?? throw new ArgumentNullException(nameof(input));

	private static readonly char[] TimeUnitChars = ['y', 'M', 'w', 'd', 'h', 'm', 's'];

	/// <summary>
	/// Parses the notation, throwing <see cref="FormatException"/> on any syntax error.
	/// </summary>
	public OrbitAstNode Parse()
	{
		_pos = 0;
		var result = ParseExpression();
		SkipWhitespace();
		if (_pos < _input.Length)
		{
			throw new FormatException($"Unexpected character at position {_pos}: '{_input[_pos]}'");
		}

		return result;
	}

	// Parses + and - (Union, Exclusion)
	private OrbitAstNode ParseExpression()
	{
		var node = ParseTerm();
		SkipWhitespace();

		while (_pos < _input.Length)
		{
			var character = Peek();
			if (character is '+' or '-')
			{
				Consume();
				var right = ParseTerm();
				node = new OrbitSetOperationNode
				{
					Operator = character == '+' ? OrbitSetOperator.Union : OrbitSetOperator.Exclusion,
					Left = node,
					Right = right,
				};
				SkipWhitespace();
			}
			else
			{
				break;
			}
		}

		return node;
	}

	// Parses & and ^ (Intersection, Difference — higher precedence)
	private OrbitAstNode ParseTerm()
	{
		var node = ParseFactor();
		SkipWhitespace();

		while (_pos < _input.Length)
		{
			var character = Peek();
			if (character is '&' or '^')
			{
				Consume();
				var right = ParseFactor();
				node = new OrbitSetOperationNode
				{
					Operator = character == '&' ? OrbitSetOperator.Intersection : OrbitSetOperator.SymmetricDifference,
					Left = node,
					Right = right,
				};
				SkipWhitespace();
			}
			else
			{
				break;
			}
		}

		return node;
	}

	// Parses parentheses or a base time-unit node
	private OrbitAstNode ParseFactor()
	{
		SkipWhitespace();
		if (Match('('))
		{
			var node = ParseExpression();
			if (!Match(')'))
			{
				throw new FormatException($"Expected ')' at position {_pos}");
			}

			return node;
		}

		return ParseNode();
	}

	// Core parsing logic for a time-unit node and its modifiers
	private OrbitAstNode ParseNode()
	{
		SkipWhitespace();
		var character = Peek();

		// Handle shorthand
		if (character == 'z')
		{
			Consume();
			return ParseTimeLiteral();
		}

		// Fixed-datetime shorthand: Z{y/M/d[Th:m[:s]]}
		if (character == 'Z')
		{
			Consume();
			return ParseDateTimeLiteral();
		}

		// Standard time unit
		if (!OrbitUnits.TryFromChar(character, out var unit))
		{
			throw new FormatException($"Expected time unit at position {_pos}, got '{character}'");
		}

		Consume();
		var node = new OrbitTimeUnitNode { Unit = unit };

		// 1. Indexing
		SkipWhitespace();
		if (Match('{'))
		{
			node.Indices = ParseIndexSpec();
		}

		// 2. Child nodes
		SkipWhitespace();
		if (Match('['))
		{
			node.Child = ParseExpression(); // allows robust nesting like d[h{1}+h{3}]
			if (!Match(']'))
			{
				throw new FormatException($"Expected ']' at position {_pos}");
			}
		}

		// 3. Modifiers (intervals and limits)
		ParseModifiers(node);

		return node;
	}

	// z{h:m[:s]} -> a time-of-day literal (no date part; recurs every day at that time).
	private OrbitDateTimeLiteralNode ParseTimeLiteral()
	{
		if (!Match('{'))
		{
			throw new FormatException("Expected '{' after 'z' shorthand");
		}

		var hour = ParseNumber();
		if (!Match(':'))
		{
			throw new FormatException("Expected ':' inside 'z' shorthand");
		}

		var minute = ParseNumber();

		int? second = null;
		if (Match(':'))
		{
			second = ParseNumber();
		}

		if (!Match('}'))
		{
			throw new FormatException("Expected '}' closing 'z' shorthand");
		}

		var node = new OrbitDateTimeLiteralNode { Hour = hour, Minute = minute, Second = second };
		// The literal carries the modifiers the shorthand used to hang on its root 'h'
		// (e.g. z{12:00}%2 -> interval 2, z{12:00}=2h -> a 2h span from 12:00).
		ParseModifiers(node);
		return node;
	}

	// Z{y/M/d[Th:m[:s]]} -> a fixed calendar datetime literal (one exact moment). The
	// time part is optional; the deepest present component fixes the granularity.
	private OrbitDateTimeLiteralNode ParseDateTimeLiteral()
	{
		if (!Match('{'))
		{
			throw new FormatException("Expected '{' after 'Z' shorthand");
		}

		var moment = ParseMoment("'Z' shorthand");
		if (!Match('}'))
		{
			throw new FormatException("Expected '}' closing 'Z' shorthand");
		}

		var node = new OrbitDateTimeLiteralNode
		{
			Year = moment.Year, Month = moment.Month, Day = moment.Day, Hour = moment.Hour, Minute = moment.Minute, Second = moment.Second,
		};
		ParseModifiers(node);
		return node;
	}

	private OrbitIndexSpec ParseIndexSpec()
	{
		SkipWhitespace();
		if (Match('#'))
		{
			var count = ParseNumber();
			if (!Match('}'))
			{
				throw new FormatException("Expected '}' after random count");
			}

			return new OrbitIndexSpec { Kind = OrbitIndexKind.Random, Count = count };
		}

		var first = ParseNumber();
		SkipWhitespace();

		if (Match('~'))
		{
			var end = ParseNumber();
			SkipWhitespace();
			if (!Match('}'))
			{
				throw new FormatException("Expected '}' after range");
			}

			return new OrbitIndexSpec { Kind = OrbitIndexKind.Range, Start = first, End = end };
		}

		var values = new List<int> { first };
		while (Match(','))
		{
			SkipWhitespace();
			values.Add(ParseNumber());
		}

		SkipWhitespace();
		if (!Match('}'))
		{
			throw new FormatException("Expected '}' after index list");
		}

		return new OrbitIndexSpec { Kind = OrbitIndexKind.List, Values = values };
	}

	private void ParseModifiers(IOrbitModifiable node)
	{
		while (_pos < _input.Length)
		{
			SkipWhitespace();
			if (Match('%'))
			{
				node.Interval = ParseNumber();
			}
			else if (Match('*'))
			{
				node.Limits.Add(new OrbitLimitSpec { Kind = OrbitLimitKind.Iterations, Count = ParseNumber() });
			}
			else if (Match('@'))
			{
				node.Limits.Add(new OrbitLimitSpec { Kind = OrbitLimitKind.Instances, Count = ParseNumber() });
			}
			else if (Match('<'))
			{
				node.Limits.Add(ParseBound(OrbitLimitKind.Before));
			}
			else if (Match('>'))
			{
				node.Limits.Add(ParseBound(OrbitLimitKind.After));
			}
			else if (Match('='))
			{
				node.Duration = ParseDuration();
			}
			else
			{
				break; // No more recognized modifiers
			}
		}
	}

	// A span duration: one or more <count><unit> parts, e.g. `2h`, `90m`, `1d6h`.
	private List<OrbitDurationPart> ParseDuration()
	{
		var parts = new List<OrbitDurationPart>();
		do
		{
			var count = ParseNumber();
			var unitChar = Peek();
			if (!TimeUnitChars.Contains(unitChar))
			{
				throw new FormatException($"Expected time unit in duration at position {_pos}, got '{unitChar}'");
			}

			Consume();
			OrbitUnits.TryFromChar(unitChar, out var unit);
			parts.Add(new OrbitDurationPart(unit, count));
		}
		while (_pos < _input.Length && char.IsAsciiDigit(Peek()));
		return parts;
	}

	// --- Utility methods ---

	private char Peek()
	{
		return _pos < _input.Length ? _input[_pos] : '\0';
	}

	private char Consume()
	{
		return _input[_pos++];
	}

	private bool Match(char character)
	{
		if (Peek() == character)
		{
			_pos++;
			return true;
		}

		return false;
	}

	private int ParseNumber()
	{
		var start = _pos;
		while (_pos < _input.Length && char.IsAsciiDigit(Peek()))
		{
			_pos++;
		}

		if (_pos == start)
		{
			throw new FormatException($"Expected number at position {_pos}");
		}

		return int.Parse(_input[start.._pos], System.Globalization.CultureInfo.InvariantCulture);
	}

	// y/M/d[Th:m[:s]] -> a calendar date, as Z{…} writes one and a bound does.
	private OrbitCalendarMoment ParseMoment(string context)
	{
		var year = ParseNumber();
		if (!Match('/'))
		{
			throw new FormatException($"Expected '/' after year in {context}");
		}

		var month = ParseNumber();
		if (!Match('/'))
		{
			throw new FormatException($"Expected '/' after month in {context}");
		}

		var day = ParseNumber();

		int? hour = null, minute = null, second = null;
		if (Match('T'))
		{
			hour = ParseNumber();
			if (!Match(':'))
			{
				throw new FormatException($"Expected ':' in the time part of {context}");
			}

			minute = ParseNumber();
			if (Match(':'))
			{
				second = ParseNumber();
			}
		}

		return new OrbitCalendarMoment(year, month, day, hour, minute, second);
	}

	// <t / >t -> a bound, a calendar date written as Z{…} writes one (>2026/7/15, <2026/7/15T12:30). An ISO timestamp
	// (>2026-07-15, >2026-07-15T12:30:00Z), the older form, still reads as a UTC instant; it ends with its last ISO
	// field, so a '-' right after it is the set difference.
	private OrbitLimitSpec ParseBound(OrbitLimitKind kind)
	{
		var start = _pos;
		var iso = IsoBound().Match(_input, _pos);
		if (iso.Success)
		{
			_pos += iso.Length;
			return new OrbitLimitSpec { Kind = kind, Timestamp = iso.Value };
		}

		var moment = ParseMoment("a bound");
		return new OrbitLimitSpec { Kind = kind, Timestamp = _input[start.._pos], Moment = moment };
	}

	[System.Text.RegularExpressions.GeneratedRegex(@"\G[0-9]{4}-[0-9]{2}-[0-9]{2}(T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]+)?)?(Z|[+-][0-9]{2}:?[0-9]{2})?)?")]
	private static partial System.Text.RegularExpressions.Regex IsoBound();

	private void SkipWhitespace()
	{
		while (_pos < _input.Length && char.IsWhiteSpace(Peek()))
		{
			_pos++;
		}
	}
}
