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
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	VaultEntityGateway entityGateway,
	MarkdownFrontMatterSerializer markdownSerializer)
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
		return new PuckEntityExistence(normalizedId, true, match.Type.Name, PuckEntityAttribute.ResolveKind(match.Type), match.Entity, associatedNote);
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

	private Task<object?> FindEntityByIdAsync(Type entityType, string id, CancellationToken cancellationToken)
	{
		// Candidates arrive tokenization-gated: a declaration only reaches this lookup with ids it can mint, so
		// each type (including abstract family anchors, which span their whole discriminated family) queries its
		// own set without per-type filtering. Cross-declaration ambiguity is rejected by the caller.
		return entityGateway.FindByIdAsync(entityType, id, track: false, cancellationToken);
	}

	private string? ResolveAssociatedNotePath(Type entityType, string id)
	{
		// A path-sync model may anchor a polymorphic family under an abstract base while composing a concrete
		// subtype (e.g. the directive model anchors Directive and composes StellarDirective), so match the concrete
		// type, the composed instantiation type, or the family anchor — the last resolves lunar directives too.
		var model = pathSyncModelCatalog.GetModels()
			.FirstOrDefault(item => item.EntityType == entityType
				|| item.InstantiationType == entityType
				|| item.EntityType.IsAssignableFrom(entityType));
		if (model is null)
		{
			return null;
		}

		foreach (var path in EnumerateResolutionCandidatePaths(model))
		{
			if (!PathMatchesIdentity(entityType, path, id))
			{
				continue;
			}

			return Path.GetRelativePath(layout.VaultRoot, path).Replace(Path.DirectorySeparatorChar, '/');
		}

		return null;
	}

	/// <summary>
	/// Enumerates the markdown files a stored entity of this model could occupy, for identity resolution. Freeform
	/// entities are identity-driven and deliberately excluded from the path-shape scan (their <c>IsCandidatePath</c>
	/// is <see langword="false"/>), so they are located by enumerating self-named markdown files under the model's
	/// roots and matching frontmatter identity — a non-path-composition interaction, by design.
	/// </summary>
	private IEnumerable<string> EnumerateResolutionCandidatePaths(VaultPathSyncModel model)
	{
		if (model.Mode != VaultStorageMode.Freeform)
		{
			return pathSyncModelCatalog.EnumerateCandidateMarkdownPaths(model);
		}

		return model.ScanRoots
			.Where(Directory.Exists)
			.SelectMany(root => Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
			.Where(MarkdownFileLocator.IsPrimarySelfNamedFile)
			.Distinct(StringComparer.OrdinalIgnoreCase);
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

		// Index storage keeps the PUCK in the filename.
		var parsed = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(path);
		if (!string.IsNullOrWhiteSpace(parsed.Id) && string.Equals(parsed.Id, id, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		// Quiet/freeform storage keeps the PUCK in frontmatter, not the filename.
		return FrontMatterPuckMatches(path, id);
	}

	private bool FrontMatterPuckMatches(string path, string id)
	{
		if (!File.Exists(path))
		{
			return false;
		}

		var frontMatter = markdownSerializer.ParseFrontMatter(File.ReadAllText(path));
		return frontMatter.TryGetValue("puck", out var rawPuck)
			&& !string.IsNullOrWhiteSpace(rawPuck)
			&& string.Equals(rawPuck.Trim().Trim('"'), id, StringComparison.OrdinalIgnoreCase);
	}
}
