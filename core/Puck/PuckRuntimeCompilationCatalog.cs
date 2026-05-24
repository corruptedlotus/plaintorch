using System.Collections.Concurrent;
using System.Reflection;

namespace Pleiades.Puck;

/// <summary>
/// Compiles PUCK declarations for all PUCK-managed entities and keeps them cached for runtime reuse.
/// </summary>
public sealed class PuckRuntimeCompilationCatalog(PuckNotationParser notationParser)
{
	private readonly ConcurrentDictionary<Type, PuckCompiledModel> _compiledByType = new();

	/// <summary>
	/// Compiles all PUCK-managed entity declarations found in the current application domain.
	/// </summary>
	public void CompileForActiveVault()
	{
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

		foreach (var puckType in puckTypes)
		{
			_compiledByType[puckType] = CompileType(puckType);
		}
	}

	/// <summary>
	/// Removes all compiled PUCK models from memory.
	/// </summary>
	public void Purge()
	{
		_compiledByType.Clear();
	}

	/// <summary>
	/// Gets a compiled PUCK model for an entity type.
	/// </summary>
	/// <param name="entityType">The PUCK-managed entity type.</param>
	/// <returns>The compiled PUCK model.</returns>
	public PuckCompiledModel GetCompiled(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return _compiledByType.GetOrAdd(entityType, CompileType);
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
}
