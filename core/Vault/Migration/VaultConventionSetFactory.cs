using System.Reflection;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Migration;

/// <summary>
/// Builds vault convention sets: the current set from live model metadata, and frozen sets for historical versions.
/// </summary>
/// <remarks>
/// Historical sets are expressed as overrides of the current conventions, capturing only what changed at each version.
/// This mirrors the way EF Core keeps a model snapshot in code rather than reconstructing old schemas from scratch.
/// </remarks>
public sealed class VaultConventionSetFactory(VaultPathSyncModelCatalog modelCatalog)
{
	/// <summary>
	/// Builds the convention set the current engine emits and reads.
	/// </summary>
	public VaultConventionSet BuildCurrent()
	{
		return new VaultConventionSet(VaultSchema.CurrentVersion, BuildCurrentConventions());
	}

	/// <summary>
	/// Builds the frozen convention set for a stored vault schema version.
	/// </summary>
	/// <param name="version">The stored vault schema version to describe.</param>
	public VaultConventionSet BuildForVersion(int version)
	{
		return version >= VaultSchema.CurrentVersion
			? BuildCurrent()
			: BuildBaseline();
	}

	/// <summary>
	/// Builds the baseline (pre-PEP091) convention set: objectives stored their PUCK in the filename under synced mode.
	/// </summary>
	private VaultConventionSet BuildBaseline()
	{
		var conventions = BuildCurrentConventions()
			.Select(convention => convention.EntityType == typeof(Objective)
				? convention with
				{
					PuckStorage = VaultPuckStorage.Index,
					Mode = VaultStorageMode.Synced,
				}
				: convention);

		return new VaultConventionSet(VaultSchema.BaselineVersion, conventions);
	}

	private IEnumerable<VaultEntityConvention> BuildCurrentConventions()
	{
		foreach (var model in modelCatalog.GetModels())
		{
			var storage = model.EntityType.GetCustomAttribute<VaultStorageAttribute>()
				?? throw new InvalidOperationException($"Type '{model.EntityType.Name}' is not configured for vault markdown storage.");

			yield return new VaultEntityConvention(
				model.EntityType,
				storage.Mode,
				storage.PuckStorage,
				MarkdownFrontMatterSerializer.QuietPuckFieldName,
				PuckNamedIdentity.Separator);
		}
	}
}
