using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Saga;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Puck;

/// <summary>
/// Resolves PUCK identifiers to concrete entity instances and associated vault notes.
/// </summary>
public sealed class PuckEntityResolutionService(
	PlainfraContext context,
	VaultLayout layout,
	PuckRuntimeCompilationCatalog compilationCatalog,
	PuckTokenizer puckTokenizer,
	VaultPathSyncModelCatalog pathSyncModelCatalog)
{
	/// <summary>
	/// Resolves a PUCK identifier into its concrete entity and note association when available.
	/// </summary>
	public async Task<PuckEntityExistence> ResolveAsync(string id, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		var normalizedId = id.Trim();

		var candidates = await ResolveCandidatesAsync(normalizedId, cancellationToken);
		if (candidates.Count == 0)
		{
			return new PuckEntityExistence(normalizedId, false);
		}

		var matches = new List<(Type Type, object Entity)>();
		foreach (var candidate in candidates)
		{
			var entity = await FindEntityByIdAsync(candidate.EntityType, normalizedId, cancellationToken);
			if (entity is not null)
			{
				matches.Add((candidate.EntityType, entity));
			}
		}

		if (matches.Count == 0)
		{
			return new PuckEntityExistence(normalizedId, false);
		}

		if (matches.Count > 1)
		{
			throw new InvalidOperationException($"PUCK '{normalizedId}' resolved to multiple entity types ({string.Join(", ", matches.Select(item => item.Type.Name).OrderBy(name => name, StringComparer.Ordinal))}).");
		}

		var match = matches[0];
		var associatedNote = ResolveAssociatedNotePath(match.Type, normalizedId);
		return new PuckEntityExistence(normalizedId, true, match.Type.Name, match.Entity, associatedNote);
	}

	private async Task<IReadOnlyList<PuckCompiledModel>> ResolveCandidatesAsync(string id, CancellationToken cancellationToken)
	{
		if (compilationCatalog.GetAllCompiled().Count == 0)
		{
			compilationCatalog.CompileForActiveVault(context.Model);
		}

		var registry = await context.PuckRegistryEntries
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

		if (registry is not null && compilationCatalog.TryGetByDeclaration(registry.Declaration, out var compiled) && compiled is not null)
		{
			return [compiled];
		}

		var candidates = compilationCatalog.GetAllCompiled()
			.Where(compiledModel => CanTokenize(compiledModel, id))
			.ToArray();

		return candidates;
	}

	private bool CanTokenize(PuckCompiledModel compiledModel, string id)
	{
		try
		{
			puckTokenizer.Tokenize(compiledModel.Notation, id);
			return true;
		}
		catch (Exception exception) when (exception is FormatException or InvalidOperationException or NotSupportedException)
		{
			return false;
		}
	}

	private async Task<object?> FindEntityByIdAsync(Type entityType, string id, CancellationToken cancellationToken)
	{
		if (entityType == typeof(Directive))
		{
			// Lunar directives resolve through their own declaration; keep base resolution stellar-only.
			return await context.Directives.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id && !(item is LunarDirective), cancellationToken);
		}

		if (entityType == typeof(LunarDirective))
		{
			return await context.LunarDirectives.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(Objective))
		{
			return await context.Objectives.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(Fate))
		{
			return await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(Decree))
		{
			return await context.Decrees.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(OnrushSprint))
		{
			return await context.OnrushSprints.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(ExecutiveOrder))
		{
			return await context.ExecutiveOrders.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(PolarisCycle))
		{
			return await context.PolarisCycles.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		if (entityType == typeof(LorePage))
		{
			return await context.LorePages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
		}

		return null;
	}

	private string? ResolveAssociatedNotePath(Type entityType, string id)
	{
		var model = pathSyncModelCatalog.GetModels().FirstOrDefault(item => item.EntityType == entityType);
		if (model is null)
		{
			return null;
		}

		foreach (var path in pathSyncModelCatalog.EnumerateCandidateMarkdownPaths(model))
		{
			if (!PathMatchesIdentity(entityType, path, id))
			{
				continue;
			}

			return Path.GetRelativePath(layout.VaultRoot, path).Replace(Path.DirectorySeparatorChar, '/');
		}

		return null;
	}

	private bool PathMatchesIdentity(Type entityType, string path, string id)
	{
		if (entityType == typeof(LorePage))
		{
			var candidate = new LorePage
			{
				Id = string.Empty,
				Title = string.Empty,
			};

			return MarkdownFileLocator.ApplyLorePageCompositionFromPath(candidate, path, layout.VaultRoot, layout.SagaRoot)
				&& string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase);
		}

		var parsed = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(path);
		return string.Equals(parsed.Id, id, StringComparison.OrdinalIgnoreCase);
	}
}
