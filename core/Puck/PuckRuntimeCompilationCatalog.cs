using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Pleiades.Puck;

/// <summary>
/// Compiles PUCK declarations for all PUCK-managed entities and keeps them cached for runtime reuse.
/// </summary>
public sealed class PuckRuntimeCompilationCatalog(PuckNotationParser notationParser)
{
	private readonly ConcurrentDictionary<Type, PuckCompiledModel> _compiledByType = new();
	private readonly ConcurrentDictionary<string, Type> _typeByDeclaration = new(StringComparer.Ordinal);

	/// <summary>
	/// Compiles all PUCK-managed entity declarations found in the current application domain.
	/// </summary>
	public void CompileForActiveVault(IReadOnlyModel? model = null)
	{
		_ = model;

		var puckTypes = AppDomain.CurrentDomain.GetAssemblies()
			.Where(assembly => !assembly.IsDynamic)
			.SelectMany(static assembly =>
			{
				try
				{
					return assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException exception)
				{
					return exception.Types.Where(type => type is not null).Cast<Type>();
				}
			})
			.Where(type => type.GetCustomAttribute<PuckFormatAttribute>() is not null)
			.Distinct();

		var compiled = puckTypes
			.Select(CompileType)
			.ToArray();

		ValidateDeclarationUniqueness(compiled);
		ValidateParseSpaceUniqueness(compiled);

		_compiledByType.Clear();
		_typeByDeclaration.Clear();
		foreach (var entry in compiled)
		{
			_compiledByType[entry.EntityType] = entry;
			_typeByDeclaration[entry.Declaration] = entry.EntityType;
		}
	}

	/// <summary>
	/// Removes all compiled PUCK models from memory.
	/// </summary>
	public void Purge()
	{
		_compiledByType.Clear();
		_typeByDeclaration.Clear();
	}

	/// <summary>
	/// Gets a compiled PUCK model for an entity type.
	/// </summary>
	/// <param name="entityType">The PUCK-managed entity type.</param>
	/// <returns>The compiled PUCK model.</returns>
	public PuckCompiledModel GetCompiled(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		var compiled = _compiledByType.GetOrAdd(entityType, CompileType);
		_typeByDeclaration.TryAdd(compiled.Declaration, compiled.EntityType);
		return compiled;
	}

	/// <summary>
	/// Gets all currently compiled PUCK models.
	/// </summary>
	public IReadOnlyList<PuckCompiledModel> GetAllCompiled()
	{
		return _compiledByType.Values
			.OrderBy(item => item.EntityType.FullName, StringComparer.Ordinal)
			.ToArray();
	}

	/// <summary>
	/// Tries to resolve a compiled PUCK model by its declaration string.
	/// </summary>
	public bool TryGetByDeclaration(string declaration, out PuckCompiledModel? compiled)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(declaration);

		compiled = null;
		if (!_typeByDeclaration.TryGetValue(declaration, out var entityType))
		{
			return false;
		}

		compiled = GetCompiled(entityType);
		return true;
	}

	private PuckCompiledModel CompileType(Type entityType)
	{
		var format = entityType.GetCustomAttribute<PuckFormatAttribute>();
		if (format is null)
		{
			throw new InvalidOperationException($"Type '{entityType.Name}' is not decorated with {nameof(PuckFormatAttribute)}.");
		}

		var notation = notationParser.Parse(format.Notation);
		var requiresCallerInput = notation.Segments.Any(static segment =>
			segment.UsesDynamicDiscriminator || segment.Numerator.Kind == PuckNumeratorKind.Manual);

		return new PuckCompiledModel(
			entityType,
			format.Notation,
			notation,
			requiresCallerInput);
	}

	private static void ValidateDeclarationUniqueness(IEnumerable<PuckCompiledModel> compiledModels)
	{
		var duplicates = compiledModels
			.GroupBy(item => BuildDeclarationSignature(item.Notation), StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.ToArray();

		if (duplicates.Length == 0)
		{
			return;
		}

		var collisions = string.Join(", ", duplicates.Select(group => string.Join("|", group.Select(item => item.EntityType.Name).OrderBy(name => name, StringComparer.Ordinal))));
		throw new InvalidOperationException($"PUCK declaration uniqueness validation failed. Ambiguous signatures detected for: {collisions}.");
	}

	private static void ValidateParseSpaceUniqueness(IEnumerable<PuckCompiledModel> compiledModels)
	{
		var duplicates = compiledModels
			.GroupBy(item => BuildZeroFilledSample(item.Notation), StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.ToArray();

		if (duplicates.Length == 0)
		{
			return;
		}

		var collisions = string.Join(", ", duplicates.Select(group =>
			$"'{group.Key}' => {string.Join("|", group.Select(item => item.EntityType.Name).OrderBy(name => name, StringComparer.Ordinal))}"));
		throw new InvalidOperationException($"PUCK parse-space uniqueness validation failed. Declarations can intersect under zero-filled matching: {collisions}.");
	}

	private static string BuildDeclarationSignature(PuckNotation notation)
	{
		return string.Join(";", notation.Segments.Select(segment => string.Join(",",
			Normalize(segment.StaticDiscriminator),
			segment.UsesDynamicDiscriminator ? "d1" : "d0",
			$"n{(int)segment.Numerator.Kind}",
			$"w{segment.Numerator.Width}",
			$"s{segment.Numerator.Seed}",
			$"k{(int)segment.Numerator.DateStampKind}",
			$"j{(int)segment.Nesting}",
			segment.ForceNesting ? "f1" : "f0",
			$"r{(int)segment.Repetition}")));
	}

	private static string Normalize(string? value)
	{
		return string.IsNullOrWhiteSpace(value)
			? string.Empty
			: value.Trim().ToLowerInvariant();
	}

	private static string BuildZeroFilledSample(PuckNotation notation)
	{
		var sample = new System.Text.StringBuilder();
		for (var index = 0; index < notation.Segments.Count; index++)
		{
			var segment = notation.Segments[index];
			if (segment.UsesDynamicDiscriminator)
			{
				sample.Append('x');
			}
			else
			{
				sample.Append(Normalize(segment.StaticDiscriminator));
			}

			sample.Append(new string('0', ResolveZeroFillLength(segment.Numerator)));

			if (segment.Nesting == PuckNestingKind.Telescope)
			{
				sample.Append('-');
			}
			else if (segment.Nesting == PuckNestingKind.Filesystem)
			{
				sample.Append('/');
			}
		}

		return sample.ToString();
	}

	private static int ResolveZeroFillLength(PuckNumeratorPattern numerator)
	{
		return numerator.Kind switch
		{
			PuckNumeratorKind.Spiritgem => Math.Max(1, numerator.Width),
			PuckNumeratorKind.Incremental => Math.Max(1, numerator.Width),
			PuckNumeratorKind.DateStamp => numerator.DateStampKind == PuckDateStampKind.Gregorian ? 8 : 6,
			_ => 1,
		};
	}
}
