namespace Pleiades.Orbits;

// C# port of @pleiades/orbits parser.ts — keep behavior identical to the TypeScript parser.

/// <summary>
/// Parses orbit notation strings into AST nodes.
/// </summary>
public sealed class OrbitParser(string input)
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
			return ParseZShorthandModifiers();
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

	private OrbitTimeUnitNode ParseZShorthandModifiers()
	{
		if (!Match('{'))
		{
			throw new FormatException("Expected '{' after 'z' shorthand");
		}

		var hIndex = ParseNumber();
		if (!Match(':'))
		{
			throw new FormatException("Expected ':' inside 'z' shorthand");
		}

		var mIndex = ParseNumber();

		int? sIndex = null;
		if (Match(':'))
		{
			sIndex = ParseNumber();
		}

		if (!Match('}'))
		{
			throw new FormatException("Expected '}' closing 'z' shorthand");
		}

		// Build nested z structure: h{A}[m{B}[s{C}]]
		var rootNode = new OrbitTimeUnitNode
		{
			Unit = OrbitUnit.Hour,
			Indices = new OrbitIndexSpec { Kind = OrbitIndexKind.List, Values = [hIndex] },
		};

		var minuteNode = new OrbitTimeUnitNode
		{
			Unit = OrbitUnit.Minute,
			Indices = new OrbitIndexSpec { Kind = OrbitIndexKind.List, Values = [mIndex] },
		};
		rootNode.Child = minuteNode;

		if (sIndex is not null)
		{
			minuteNode.Child = new OrbitTimeUnitNode
			{
				Unit = OrbitUnit.Second,
				Indices = new OrbitIndexSpec { Kind = OrbitIndexKind.List, Values = [sIndex.Value] },
			};
		}

		// Shorthand gets modifiers applied to the root 'h' node (e.g. z{12:00}%2 -> interval 2 on 'h')
		ParseModifiers(rootNode);

		return rootNode;
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

	private void ParseModifiers(OrbitTimeUnitNode node)
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
				node.Limits.Add(new OrbitLimitSpec { Kind = OrbitLimitKind.Before, Timestamp = ParseTimestamp() });
			}
			else if (Match('>'))
			{
				node.Limits.Add(new OrbitLimitSpec { Kind = OrbitLimitKind.After, Timestamp = ParseTimestamp() });
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

	private string ParseTimestamp()
	{
		var start = _pos;
		// Timestamps read until they hit an operator or structural character.
		// NOTE: '-' is intentionally NOT a stop char so ISO dates like 2026-07-15
		// (and 2026-07-15T12:30:00Z) parse. A '-' exclusion directly after a
		// timestamp must therefore be whitespace-separated (e.g. "d>2026-01-01 - d{1}").
		while (_pos < _input.Length
			&& Peek() is not ('%' or '*' or '@' or '<' or '>' or '=' or '+' or '&' or '^' or '[' or ']' or '(' or ')' or '{' or '}')
			&& !char.IsWhiteSpace(Peek()))
		{
			_pos++;
		}

		if (_pos == start)
		{
			throw new FormatException($"Expected timestamp at position {_pos}");
		}

		return _input[start.._pos];
	}

	private void SkipWhitespace()
	{
		while (_pos < _input.Length && char.IsWhiteSpace(Peek()))
		{
			_pos++;
		}
	}
}
