namespace Pleiades.Plaintorch.Cli;

/// <summary>
/// Parses raw CLI tokens into switches and positional arguments.
/// </summary>
public sealed class CliArgumentParser
{
	public CliParsedArguments Parse(IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(args);
		var options = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
		var positionals = new List<string>();

		for (var index = 0; index < args.Count; index++)
		{
			var token = args[index];
			if (string.IsNullOrWhiteSpace(token))
			{
				continue;
			}

			if (token.StartsWith("--", StringComparison.Ordinal))
			{
				ParseLongOption(args, ref index, token, options);
				continue;
			}

			if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1)
			{
				ParseShortOption(args, ref index, token, options);
				continue;
			}

			positionals.Add(token);
		}

		return new CliParsedArguments(options, positionals);
	}

	private static void ParseLongOption(IReadOnlyList<string> args, ref int index, string token, IDictionary<string, List<string?>> options)
	{
		var body = token[2..];
		var separatorIndex = body.IndexOf('=');
		if (separatorIndex >= 0)
		{
			AddOption(options, body[..separatorIndex], body[(separatorIndex + 1)..]);
			return;
		}

		var value = TryConsumeNextValue(args, ref index);
		AddOption(options, body, value);
	}

	private static void ParseShortOption(IReadOnlyList<string> args, ref int index, string token, IDictionary<string, List<string?>> options)
	{
		var body = token[1..];
		var separatorIndex = body.IndexOf('=');
		if (separatorIndex >= 0)
		{
			AddOption(options, body[..separatorIndex], body[(separatorIndex + 1)..]);
			return;
		}

		if (body.Length > 1)
		{
			foreach (var character in body)
			{
				AddOption(options, character.ToString(), null);
			}

			return;
		}

		var value = TryConsumeNextValue(args, ref index);
		AddOption(options, body, value);
	}

	private static string? TryConsumeNextValue(IReadOnlyList<string> args, ref int index)
	{
		if (index + 1 >= args.Count)
		{
			return null;
		}

		var nextToken = args[index + 1];
		if (string.IsNullOrWhiteSpace(nextToken) || nextToken.StartsWith("-", StringComparison.Ordinal))
		{
			return null;
		}

		index++;
		return nextToken;
	}

	private static void AddOption(IDictionary<string, List<string?>> options, string name, string? value)
	{
		var normalizedName = name.Trim().TrimStart('-');
		if (!options.TryGetValue(normalizedName, out var values))
		{
			values = [];
			options.Add(normalizedName, values);
		}

		values.Add(value);
	}
}

/// <summary>
/// Represents parsed option and positional data.
/// </summary>
public sealed record CliParsedArguments(
	IReadOnlyDictionary<string, List<string?>> Options,
	IReadOnlyList<string> Positionals);
