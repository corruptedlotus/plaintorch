namespace Pleiades.Plaintorch.Cli;

/// <summary>
/// Marks the default execution method for a command class.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CliDefaultCommandAttribute : Attribute
{
}

/// <summary>
/// Marks the help-switch execution method for a command class.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CliHelpSwitchAttribute : Attribute
{
}

/// <summary>
/// Declares a long/short switch that maps into a semantic options object.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CliOptionAttribute : Attribute
{
	public CliOptionAttribute(string longName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(longName);
		LongName = Normalize(longName);
	}

	public string LongName { get; }

	public string? ShortName { get; init; }

	public string? Description { get; init; }

	public bool Required { get; init; }

	private static string Normalize(string value)
	{
		return value.TrimStart('-');
	}
}

/// <summary>
/// Declares a positional argument that maps into a semantic options object.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CliArgumentAttribute : Attribute
{
	public CliArgumentAttribute(int position)
	{
		if (position < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(position));
		}

		Position = position;
	}

	public int Position { get; }

	public string? Name { get; init; }

	public string? Description { get; init; }

	public bool Required { get; init; }
}

/// <summary>
/// Supplies human-readable help text for a command class or option-bearing property.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class CliDescriptionAttribute(string text) : Attribute
{
	public string Text { get; } = text;
}
