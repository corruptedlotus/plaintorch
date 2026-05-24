using System.Reflection;
using Pleiades.Puck;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault;

/// <summary>
/// Validates that vault-backed storage shapes and shared scan roots are path-distinguishable before activation.
/// </summary>
public sealed class VaultStorageTopologyValidator(
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	PuckPathDiscriminabilityService pathDiscriminabilityService)
{
	/// <summary>
	/// Validates the current vault storage topology.
	/// </summary>
	public void Validate()
	{
		var models = pathSyncModelCatalog.GetModels();
		foreach (var model in models)
		{
			ValidateStorageAttribute(model.EntityType);
		}

		var byRoot = models
			.SelectMany(model => model.ScanRoots.Select(root => (root, model)))
			.GroupBy(item => item.root, item => item.model, StringComparer.OrdinalIgnoreCase);

		foreach (var group in byRoot)
		{
			var candidates = group.Distinct().ToArray();
			for (var leftIndex = 0; leftIndex < candidates.Length; leftIndex++)
			{
				for (var rightIndex = leftIndex + 1; rightIndex < candidates.Length; rightIndex++)
				{
					var left = candidates[leftIndex];
					var right = candidates[rightIndex];
					if (left.Shape != right.Shape)
					{
						continue;
					}

					if (!pathDiscriminabilityService.AreDistinguishable(left.EntityType, right.EntityType))
					{
						throw new InvalidOperationException($"Vault activation denied because '{left.EntityName}' and '{right.EntityName}' share scan root '{group.Key}' with indistinguishable single-shape path identities.");
					}
				}
			}
		}
	}

	private static void ValidateStorageAttribute(Type entityType)
	{
		var attribute = entityType.GetCustomAttribute<VaultStorageAttribute>()
			?? throw new InvalidOperationException($"Type '{entityType.Name}' must declare {nameof(VaultStorageAttribute)}.");

		if (!string.IsNullOrWhiteSpace(attribute.ParentIdProperty))
		{
			var parentIdProperty = entityType.GetProperty(attribute.ParentIdProperty, BindingFlags.Public | BindingFlags.Instance)
				?? throw new InvalidOperationException($"Type '{entityType.Name}' declares unknown parent id property '{attribute.ParentIdProperty}'.");

			if (parentIdProperty.PropertyType != typeof(string))
			{
				throw new InvalidOperationException($"Parent id property '{entityType.Name}.{attribute.ParentIdProperty}' must be a string.");
			}

			if (attribute.ParentEntityType is null)
			{
				throw new InvalidOperationException($"Type '{entityType.Name}' must declare {nameof(VaultStorageAttribute.ParentEntityType)} when {nameof(VaultStorageAttribute.ParentIdProperty)} is used.");
			}
		}

		if (!string.IsNullOrWhiteSpace(attribute.ParentDirectoryProperty))
		{
			var directoryProperty = entityType.GetProperty(attribute.ParentDirectoryProperty, BindingFlags.Public | BindingFlags.Instance)
				?? throw new InvalidOperationException($"Type '{entityType.Name}' declares unknown parent directory property '{attribute.ParentDirectoryProperty}'.");

			if (directoryProperty.PropertyType != typeof(string))
			{
				throw new InvalidOperationException($"Parent directory property '{entityType.Name}.{attribute.ParentDirectoryProperty}' must be a string.");
			}
		}

		if (!string.IsNullOrWhiteSpace(attribute.PartitionUnder))
		{
			if (string.IsNullOrWhiteSpace(attribute.ParentIdProperty) || attribute.ParentEntityType is null)
			{
				throw new InvalidOperationException($"Type '{entityType.Name}' declares {nameof(VaultStorageAttribute.PartitionUnder)} but does not declare a parent relation through {nameof(VaultStorageAttribute.ParentIdProperty)} and {nameof(VaultStorageAttribute.ParentEntityType)}.");
			}

			var partition = attribute.PartitionUnder.Trim();
			if (Path.IsPathRooted(partition)
				|| partition.Contains(Path.DirectorySeparatorChar)
				|| partition.Contains(Path.AltDirectorySeparatorChar)
				|| partition.Contains("..", StringComparison.Ordinal))
			{
				throw new InvalidOperationException($"{nameof(VaultStorageAttribute.PartitionUnder)} for '{entityType.Name}' must be a single safe subdirectory name.");
			}
		}
	}
}