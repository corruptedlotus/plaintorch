using System.Reflection;
using Pleiades.Puck;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Migration;

/// <summary>
/// Reads vault markdown files under a supplied <see cref="VaultConventionSet"/> and reconstructs entity state.
/// </summary>
/// <remarks>
/// This is the "loader" phase of a vault migration: it interprets on-disk files using the conventions of the version
/// the vault is stored at, so that the current engine can subsequently re-emit that state under the current conventions.
/// Enumeration reuses the live model catalog; only identity resolution is convention-specific (filename-embedded for
/// <see cref="VaultPuckStorage.Index"/>, frontmatter for <see cref="VaultPuckStorage.Quiet"/>). Loading is confined to
/// declared territory: a filename-embedded identity is trusted only inside a specific (non-vault-root) scan root or a
/// folder hosted by a parent entity that exists in the database, so a user's own note whose name merely carries a
/// PUCK-shaped prefix is never migrated into an entity.
/// </remarks>
public sealed class VaultLoader(
	VaultLayout layout,
	VaultPathSyncModelCatalog modelCatalog,
	VaultFamilyInstantiationResolver familyInstantiationResolver,
	VaultEntityModelCatalog entityModelCatalog,
	PuckIdentityGate identityGate,
	MarkdownFrontMatterSerializer markdownSerializer,
	VaultWatcherPathPolicy pathPolicy,
	PlainfraContext context)
{
	/// <summary>
	/// Loads entity state from the vault under the supplied conventions.
	/// </summary>
	/// <param name="conventions">The conventions to read the vault with.</param>
	/// <param name="entityTypes">The entity types to load; when <see langword="null"/>, every type in the set is loaded.</param>
	/// <param name="cancellationToken">A token used to cancel loading.</param>
	public async Task<IReadOnlyList<LoadedVaultEntity>> LoadAsync(
		VaultConventionSet conventions,
		IReadOnlyCollection<Type>? entityTypes = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(conventions);

		// Authority outside a model's own granted roots derives only from a hosting entity that actually exists.
		// The hosting types are derived from the catalog (every distinct declared ParentEntityType), never hard-coded.
		var knownHostIdsByType = await LoadKnownHostIdsAsync(cancellationToken);

		var results = new List<LoadedVaultEntity>();
		foreach (var model in modelCatalog.GetModels())
		{
			if (entityTypes is not null && !entityTypes.Contains(model.EntityType))
			{
				continue;
			}

			if (!conventions.TryGet(model.EntityType, out var convention))
			{
				continue;
			}

			foreach (var path in modelCatalog.EnumerateCandidateMarkdownPaths(model))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!File.Exists(path))
				{
					continue;
				}

				if (!IsWithinDeclaredTerritory(model, path, knownHostIdsByType))
				{
					continue;
				}

				var loaded = await LoadOneAsync(model, convention, path, cancellationToken);
				if (loaded is not null)
				{
					results.Add(loaded);
				}
			}
		}

		return results;
	}

	/// <summary>
	/// Loads the known identifiers of every distinct hosting (parent) entity type declared across the catalog, so the
	/// territory check can confirm a hosting entity actually exists without hard-coding any particular type.
	/// </summary>
	private async Task<IReadOnlyDictionary<Type, HashSet<string>>> LoadKnownHostIdsAsync(CancellationToken cancellationToken)
	{
		var hostTypes = entityModelCatalog.GetModels()
			.Select(model => model.Storage?.ParentEntityType)
			.Where(type => type is not null)
			.Distinct()
			.Cast<Type>();

		var result = new Dictionary<Type, HashSet<string>>();
		foreach (var hostType in hostTypes)
		{
			result[hostType] = await VaultEntityGateway.LoadKnownIdsAsync(context, hostType, cancellationToken);
		}

		return result;
	}

	/// <summary>
	/// Determines whether a candidate file sits within territory the model has been granted authority over: one of its
	/// specific (non-vault-root) scan roots, or a folder hosted by a parent entity that exists in the database. The
	/// whole-vault scan root is a detection net for identity-driven models, not a grant of authority, so a file reachable
	/// only through it belongs to the user unless a known hosting entity says otherwise.
	/// </summary>
	private bool IsWithinDeclaredTerritory(VaultPathSyncModel model, string path, IReadOnlyDictionary<Type, HashSet<string>> knownHostIdsByType)
	{
		var fullPath = Path.GetFullPath(path);
		var vaultRoot = Path.GetFullPath(layout.VaultRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		foreach (var root in model.ScanRoots)
		{
			var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (string.Equals(fullRoot, vaultRoot, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		var parentType = entityModelCatalog.TryGet(model.EntityType, out var declared)
			? declared.Storage?.ParentEntityType
			: null;
		if (parentType is null || !knownHostIdsByType.TryGetValue(parentType, out var knownHostIds))
		{
			return false;
		}

		var hostingId = pathPolicy.TryResolveContainingDirectiveId(fullPath);
		return hostingId is not null && knownHostIds.Contains(hostingId);
	}

	private async Task<LoadedVaultEntity?> LoadOneAsync(
		VaultPathSyncModel model,
		VaultEntityConvention convention,
		string path,
		CancellationToken cancellationToken)
	{
		var markdown = await File.ReadAllTextAsync(path, cancellationToken);
		var frontMatter = markdownSerializer.ParseFrontMatter(markdown);
		var (id, title) = ResolveIdentity(convention, path, frontMatter);
		if (!string.IsNullOrWhiteSpace(id)
			&& convention.PuckStorage == VaultPuckStorage.Index
			&& !IsHierarchicalIdentity(convention.EntityType)
			&& !identityGate.IsMintable(convention.EntityType, id))
		{
			// Notation-gate a flat Index identity: a filename prefix that does not tokenize against the entity's
			// declared PUCK is a user's ordinary note (its name merely contains " - "), not an entity. Dropping the id
			// leaves it untouched below rather than migrating it into an invalid-PUCK entity. Hierarchical identities
			// (lore) are path-composed from the folder tree, not this flat filename token, so they are exempt.
			id = null;
		}

		if (string.IsNullOrWhiteSpace(id))
		{
			// A file without resolvable identity under these conventions is not a migratable entity (e.g. a
			// title-only pre-PUCK draft); it is left untouched.
			return null;
		}

		var instantiationType = familyInstantiationResolver.ResolveInstantiationType(model, id);
		var entity = Activator.CreateInstance(instantiationType)
			?? throw new InvalidOperationException($"Could not construct entity '{instantiationType.Name}' during vault load.");
		HydrateFields(entity, instantiationType, markdown);
		if (entity is IPuckNamedEntity namedEntity)
		{
			namedEntity.Id = id;
			namedEntity.Title = title;
		}

		return new LoadedVaultEntity(
			model.EntityType,
			entity,
			id,
			title,
			Path.GetFullPath(path),
			Path.GetRelativePath(layout.VaultRoot, path),
			ExtractBody(markdown),
			frontMatter);
	}

	// A hierarchical identity (e.g. lore's "Era{?}/Cha{?}/Act{?}/p{?}") is assembled from the folder tree, so its
	// flat filename token is only a partial identity and must not be notation-gated as if it were the whole PUCK.
	private bool IsHierarchicalIdentity(Type entityType)
		=> entityModelCatalog.TryGet(entityType, out var model)
			&& model?.PuckDeclaration is { } declaration
			&& declaration.Contains('/');

	private static (string? Id, string Title) ResolveIdentity(
		VaultEntityConvention convention,
		string path,
		IReadOnlyDictionary<string, string> frontMatter)
	{
		if (convention.PuckStorage == VaultPuckStorage.Quiet)
		{
			var title = Path.GetFileNameWithoutExtension(path);
			if (frontMatter.TryGetValue(convention.PuckFrontMatterKey, out var rawPuck) && !string.IsNullOrWhiteSpace(rawPuck))
			{
				return (rawPuck.Trim().Trim('"'), title);
			}

			return (null, title);
		}

		// Index: identity is embedded in the filename as "{id}{separator}{title}".
		var (parsedId, parsedTitle) = PuckNamedIdentity.ParseLoosePath(path);
		return (parsedId, parsedTitle);
	}

	private void HydrateFields(object entity, Type entityType, string markdown)
	{
		var method = typeof(MarkdownFrontMatterSerializer)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.Single(candidate => candidate.Name == nameof(MarkdownFrontMatterSerializer.DeserializePreservingDefaults)
				&& candidate.IsGenericMethodDefinition
				&& candidate.GetParameters().Length == 2);

		method.MakeGenericMethod(entityType).Invoke(markdownSerializer, [markdown, entity]);
	}

	private static string ExtractBody(string markdown)
	{
		using var reader = new StringReader(markdown);
		var firstLine = reader.ReadLine();
		if (firstLine is not null && firstLine.Length > 0 && firstLine[0] == '﻿')
		{
			firstLine = firstLine[1..];
		}

		if (!string.Equals(firstLine?.Trim(), "---", StringComparison.Ordinal))
		{
			return markdown;
		}

		while (reader.ReadLine() is { } line)
		{
			if (line == "---")
			{
				break;
			}
		}

		return reader.ReadToEnd().TrimStart('\r', '\n');
	}
}
