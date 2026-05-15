using System.Globalization;
using System.Reflection;

namespace Pleiades.Plaintorch.Cli;

/// <summary>
/// Binds parsed CLI tokens into typed semantic options objects.
/// </summary>
public sealed class CliOptionsBinder(CliArgumentParser parser)
{
	public object? Bind(Type? optionsType, IReadOnlyList<string> args)
	{
		if (optionsType is null)
		{
			return null;
		}

		ArgumentNullException.ThrowIfNull(args);
		var parsed = parser.Parse(args);
		var options = Activator.CreateInstance(optionsType)
			?? throw new InvalidOperationException($"Could not create options type '{optionsType.Name}'.");

		foreach (var property in optionsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (!property.CanWrite)
			{
				continue;
			}

			var optionAttribute = property.GetCustomAttribute<CliOptionAttribute>();
			if (optionAttribute is not null)
			{
				BindOption(property, optionAttribute, options, parsed);
				continue;
			}

			var argumentAttribute = property.GetCustomAttribute<CliArgumentAttribute>();
			if (argumentAttribute is not null)
			{
				BindArgument(property, argumentAttribute, options, parsed);
			}
		}

		return options;
	}

	private static void BindOption(PropertyInfo property, CliOptionAttribute optionAttribute, object options, CliParsedArguments parsed)
	{
		var values = LookupOptionValues(parsed.Options, optionAttribute);
		if (values.Count == 0)
		{
			if (optionAttribute.Required)
			{
				throw new InvalidOperationException($"Required option '--{optionAttribute.LongName}' was not provided.");
			}

			return;
		}

		property.SetValue(options, ConvertValues(property.PropertyType, values, optionAttribute.LongName));
	}

	private static void BindArgument(PropertyInfo property, CliArgumentAttribute argumentAttribute, object options, CliParsedArguments parsed)
	{
		if (argumentAttribute.Position >= parsed.Positionals.Count)
		{
			if (argumentAttribute.Required)
			{
				var name = argumentAttribute.Name ?? property.Name;
				throw new InvalidOperationException($"Required positional argument '{name}' was not provided.");
			}

			return;
		}

		property.SetValue(options, ConvertSingleValue(property.PropertyType, parsed.Positionals[argumentAttribute.Position], argumentAttribute.Name ?? property.Name));
	}

	private static IReadOnlyList<string?> LookupOptionValues(IReadOnlyDictionary<string, List<string?>> options, CliOptionAttribute attribute)
	{
		if (options.TryGetValue(attribute.LongName, out var longValues))
		{
			return longValues;
		}

		if (!string.IsNullOrWhiteSpace(attribute.ShortName) && options.TryGetValue(attribute.ShortName.TrimStart('-'), out var shortValues))
		{
			return shortValues;
		}

		return Array.Empty<string?>();
	}

	private static object? ConvertValues(Type targetType, IReadOnlyList<string?> values, string optionName)
	{
		if (targetType == typeof(bool) || targetType == typeof(bool?))
		{
			var lastValue = values.LastOrDefault();
			return lastValue is null ? true : ConvertSingleValue(targetType, lastValue, optionName);
		}

		if (targetType == typeof(string[]))
		{
			return values.Where(value => value is not null).Cast<string>().ToArray();
		}

		if (targetType == typeof(List<string>))
		{
			return values.Where(value => value is not null).Cast<string>().ToList();
		}

		var scalarValue = values.LastOrDefault();
		if (scalarValue is null)
		{
			throw new InvalidOperationException($"Option '--{optionName}' requires a value.");
		}

		return ConvertSingleValue(targetType, scalarValue, optionName);
	}

	private static object? ConvertSingleValue(Type targetType, string rawValue, string fieldName)
	{
		var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (effectiveType == typeof(string))
		{
			return rawValue;
		}

		if (effectiveType == typeof(bool))
		{
			if (bool.TryParse(rawValue, out var boolValue))
			{
				return boolValue;
			}

			throw new InvalidOperationException($"Value '{rawValue}' is not valid for '{fieldName}'. Expected a boolean.");
		}

		if (effectiveType.IsEnum)
		{
			if (Enum.TryParse(effectiveType, rawValue, true, out var enumValue))
			{
				return enumValue;
			}

			throw new InvalidOperationException($"Value '{rawValue}' is not valid for '{fieldName}'. Expected one of: {string.Join(", ", Enum.GetNames(effectiveType))}.");
		}

		if (effectiveType == typeof(int))
		{
			return int.Parse(rawValue, CultureInfo.InvariantCulture);
		}

		if (effectiveType == typeof(long))
		{
			return long.Parse(rawValue, CultureInfo.InvariantCulture);
		}

		if (effectiveType == typeof(DateOnly))
		{
			return DateOnly.Parse(rawValue, CultureInfo.InvariantCulture);
		}

		if (effectiveType == typeof(DateTimeOffset))
		{
			return DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		}

		throw new InvalidOperationException($"Type '{targetType.Name}' is not supported for CLI binding on '{fieldName}'.");
	}
}
