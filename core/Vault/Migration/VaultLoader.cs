using System.Reflection;
using Pleiades.Puck;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Migration;

/// <summary>
/// Reads vault markdown files under a supplied <see cref="VaultConventionSet"/> and reconstructs entity state.
/// </summary>
/// <remarks>
/// This is the "loader" phase of a vault migration: it interprets on-disk files using the conventions of the version
/// the vault is stored at, so that the current engine can subsequently re-emit that state under the current conventions.
/// Enumeration reuses the live model catalog; only identity resolution is convention-specific (filename-embedded for
/// <see cref="VaultPuckStorage.Index"/>, frontmatter for <see cref="VaultPuckStorage.Quiet"/>).
/// </remarks>
public sealed class VaultLoader(
	VaultLayout layout,
	VaultPathSyncModelCatalog modelCatalog,
	MarkdownFrontMatterSerializer markdownSerializer)
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

				var loaded = await LoadOneAsync(model, convention, path, cancellationToken);
				if (loaded is not null)
				{
					results.Add(loaded);
				}
			}
		}

		return results;
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
		if (string.IsNullOrWhiteSpace(id))
		{
			// A file without resolvable identity under these conventions is not a migratable entity (e.g. a
			// title-only pre-PUCK draft); it is left untouched.
			return null;
		}

		var entity = Activator.CreateInstance(model.EntityType)
			?? throw new InvalidOperationException($"Could not construct entity '{model.EntityType.Name}' during vault load.");
		HydrateFields(entity, model.EntityType, markdown);
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
