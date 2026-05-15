using System.Reflection;
using System.Text;

namespace Pleiades.Plaintorch.Cli;

/// <summary>
/// Renders human-readable help for the root or a specific command node.
/// </summary>
public sealed class CliHelpTextRenderer(CliCommandRegistry registry)
{
	public string RenderRootHelp()
	{
		var builder = new StringBuilder();
		builder.AppendLine("PLAINTORCH CLI");
		builder.AppendLine();
		builder.AppendLine("Commands:");
		foreach (var child in registry.Root.Children.Values.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase))
		{
			builder.Append("  ");
			builder.Append(child.Name.PadRight(20));
			builder.AppendLine(GetDescription(child.CommandType));
		}

		builder.AppendLine();
		builder.AppendLine("Use '<command> --help' for command-specific help.");
		return builder.ToString().TrimEnd();
	}

	public string RenderCommandHelp(CliCommandNode node)
	{
		ArgumentNullException.ThrowIfNull(node);
		var builder = new StringBuilder();
		builder.AppendLine($"Command: {node.Path}");
		var description = GetDescription(node.CommandType);
		if (!string.IsNullOrWhiteSpace(description))
		{
			builder.AppendLine(description);
		}

		builder.AppendLine();
		builder.Append("Usage: ");
		builder.Append(node.Path);
		var optionsType = ResolveOptionsType(node.CommandType);
		if (optionsType is not null)
		{
			var positionalArguments = optionsType
				.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Select(property => new { Property = property, Attribute = property.GetCustomAttribute<CliArgumentAttribute>() })
				.Where(item => item.Attribute is not null)
				.OrderBy(item => item.Attribute!.Position)
				.ToArray();

			foreach (var argument in positionalArguments)
			{
				builder.Append(' ');
				builder.Append(argument.Attribute!.Required ? '<' : '[');
				builder.Append(argument.Attribute.Name ?? argument.Property.Name);
				builder.Append(argument.Attribute.Required ? '>' : ']');
			}

			if (optionsType.GetProperties().Any(property => property.GetCustomAttribute<CliOptionAttribute>() is not null))
			{
				builder.Append(" [options]");
			}
		}

		if (node.Children.Count > 0)
		{
			builder.Append(" <subcommand>");
		}

		builder.AppendLine();

		if (optionsType is not null)
		{
			RenderArguments(builder, optionsType);
			RenderOptions(builder, optionsType);
		}

		if (node.Children.Count > 0)
		{
			builder.AppendLine();
			builder.AppendLine("Subcommands:");
			foreach (var child in node.Children.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
			{
				builder.Append("  ");
				builder.Append(child.Name.PadRight(20));
				builder.AppendLine(GetDescription(child.CommandType));
			}
		}

		return builder.ToString().TrimEnd();
	}

	private static void RenderArguments(StringBuilder builder, Type optionsType)
	{
		var arguments = optionsType
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => new { Property = property, Attribute = property.GetCustomAttribute<CliArgumentAttribute>() })
			.Where(item => item.Attribute is not null)
			.OrderBy(item => item.Attribute!.Position)
			.ToArray();

		if (arguments.Length == 0)
		{
			return;
		}

		builder.AppendLine();
		builder.AppendLine("Arguments:");
		foreach (var argument in arguments)
		{
			builder.Append("  ");
			builder.Append((argument.Attribute!.Name ?? argument.Property.Name).PadRight(20));
			builder.AppendLine(argument.Attribute.Description ?? GetDescription(argument.Property));
		}
	}

	private static void RenderOptions(StringBuilder builder, Type optionsType)
	{
		var options = optionsType
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => new { Property = property, Attribute = property.GetCustomAttribute<CliOptionAttribute>() })
			.Where(item => item.Attribute is not null)
			.ToArray();

		if (options.Length == 0)
		{
			return;
		}

		builder.AppendLine();
		builder.AppendLine("Options:");
		foreach (var option in options)
		{
			var switchText = option.Attribute!.ShortName is null
				? $"--{option.Attribute.LongName}"
				: $"-{option.Attribute.ShortName.TrimStart('-')}, --{option.Attribute.LongName}";
			builder.Append("  ");
			builder.Append(switchText.PadRight(20));
			builder.AppendLine(option.Attribute.Description ?? GetDescription(option.Property));
		}

		builder.Append("  ");
		builder.Append("-h, --help".PadRight(20));
		builder.AppendLine("Display command help.");
	}

	private static Type? ResolveOptionsType(Type? commandType)
	{
		if (commandType is null)
		{
			return null;
		}

		var defaultMethod = commandType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.FirstOrDefault(method => method.GetCustomAttribute<CliDefaultCommandAttribute>() is not null);
		if (defaultMethod is null)
		{
			return null;
		}

		return defaultMethod.GetParameters()
			.Select(parameter => parameter.ParameterType)
			.FirstOrDefault(type => type != typeof(CliInvocationContext) && type != typeof(CancellationToken));
	}

	private static string GetDescription(MemberInfo? member)
	{
		return member?.GetCustomAttribute<CliDescriptionAttribute>()?.Text ?? string.Empty;
	}
}
