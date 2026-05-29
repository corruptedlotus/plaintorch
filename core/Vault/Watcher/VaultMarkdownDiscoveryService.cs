using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Scans vault-backed markdown paths, resolves path-based models, and builds validated sync candidates.
/// </summary>
public sealed class VaultMarkdownDiscoveryService(
	VaultLayout layout,
	PlainfraContext context,
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	VaultWatcherPathPolicy pathPolicy,
	MarkdownFrontMatterSerializer markdownSerializer,
	VaultAuditLogService auditLogService,
	VaultSyncDecisionService decisionService,
	PuckCreationService puckCreationService,
	PuckEntityResolutionService puckEntityResolutionService)
{
	/// <summary>
	/// Scans all catalog-backed markdown paths and produces sync candidates.
	/// </summary>
	/// <param name="origin">The logical source performing the scan (for audit metadata).</param>
	/// <param name="cancellationToken">A token used to cancel scan execution.</param>
	/// <returns>A scan result containing discovered candidates and scan counters.</returns>
	public async Task<VaultDiscoveryScanResult> ScanAsync(string origin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);
		var candidates = new List<VaultSyncCandidate>();
		var ignored = 0;
		var missing = 0;
		var knownIdsByType = await LoadKnownIdsAsync(cancellationToken);

		var allPaths = pathSyncModelCatalog.EnumerateCandidateMarkdownPaths();

		foreach (var path in allPaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (pathPolicy.ShouldIgnorePath(path))
			{
				ignored++;
				continue;
			}

			var candidate = await InspectPathCoreAsync(path, knownIdsByType, cancellationToken);
			if (candidate is null)
			{
				ignored++;
				continue;
			}

			candidates.Add(candidate);
		}

		await auditLogService.WriteAsync(
			"discovery",
			"startup-scan",
			details: new { origin, candidateCount = candidates.Count, invalidCount = candidates.Count(candidate => !candidate.IsValid), ignored, missing },
			cancellationToken: cancellationToken);

		return new VaultDiscoveryScanResult(candidates, ignored, missing);
	}

	/// <summary>
	/// Inspects a single path after a watcher event and returns a validated sync candidate when applicable.
	/// </summary>
	/// <param name="path">The filesystem path to inspect.</param>
	/// <param name="origin">The logical source performing inspection.</param>
	/// <param name="cancellationToken">A token used to cancel inspection.</param>
	/// <returns>A resolved sync candidate, or <see langword="null"/> when the path is not managed.</returns>
	public async Task<VaultSyncCandidate?> InspectPathAsync(string path, string origin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);
		var knownIdsByType = await LoadKnownIdsAsync(cancellationToken);
		var candidate = await InspectPathCoreAsync(path, knownIdsByType, cancellationToken);
		if (candidate is null)
		{
			await auditLogService.WriteAsync(
				"watcher",
				"ignored-path",
				details: new { origin, path },
				cancellationToken: cancellationToken);
			return null;
		}

		await auditLogService.WriteAsync(
			"watcher",
				candidate.IsValid ? "candidate-discovered" : "candidate-invalid",
				subjectType: candidate.Model.EntityName,
				subjectId: candidate.PathId,
				subjectTitle: candidate.PathTitle,
				details: new
				{
					origin,
					candidate.VaultRelativePath,
					candidate.SuggestedAction,
					candidate.SuggestedReason,
					issueCount = candidate.Issues.Count,
					issues = candidate.Issues.Select(issue => new { issue.FieldPath, issue.Message, issue.RawValue }).ToArray(),
				},
				cancellationToken: cancellationToken);

		return candidate;
	}

	/// <summary>
	/// Performs core inspection flow for a path by classifying, hydrating, validating, and deciding an action.
	/// </summary>
	/// <param name="path">The path to inspect.</param>
	/// <param name="knownIdsByType">Cached identifier lookups grouped by entity type.</param>
	/// <param name="cancellationToken">A token used to cancel inspection.</param>
	/// <returns>A populated candidate when the path maps to a managed markdown entity; otherwise <see langword="null"/>.</returns>
	private async Task<VaultSyncCandidate?> InspectPathCoreAsync(
		string path,
		IReadOnlyDictionary<Type, HashSet<string>> knownIdsByType,
		CancellationToken cancellationToken)
	{
		if (pathPolicy.ShouldIgnorePath(path))
		{
			return null;
		}

		if (!pathSyncModelCatalog.TryResolveWatchPath(path, out var resolvedPath, out var model)
			|| string.IsNullOrWhiteSpace(resolvedPath)
			|| model is null)
		{
			return null;
		}

		var fullPath = Path.GetFullPath(resolvedPath);
		var markdown = File.Exists(fullPath)
			? await File.ReadAllTextAsync(fullPath, cancellationToken)
			: string.Empty;
		var (pathId, pathTitle) = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(fullPath);
		var parsedModel = CreatePathComposedModel(model.EntityType, fullPath);
		if (parsedModel is IPuckNamedEntity namedEntity)
		{
			if (!string.IsNullOrWhiteSpace(namedEntity.Id))
			{
				pathId = namedEntity.Id;
			}

			if (!string.IsNullOrWhiteSpace(namedEntity.Title))
			{
				pathTitle = namedEntity.Title;
			}
		}

		var knownIds = knownIdsByType.TryGetValue(model.EntityType, out var ids)
			? ids
			: new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var isNewEntity = string.IsNullOrWhiteSpace(pathId) || !knownIds.Contains(pathId);
		var issues = DeserializeInto(parsedModel, model.EntityType, markdown, preserveDefaultsForMissingFields: isNewEntity)
			.Select(issue => issue)
			.ToList();
		ApplyPathAuthorities(parsedModel, fullPath, issues);
		if (parsedModel is IPuckNamedEntity resolvedNamedEntity)
		{
			if (!string.IsNullOrWhiteSpace(resolvedNamedEntity.Id))
			{
				pathId = resolvedNamedEntity.Id;
			}

			if (!string.IsNullOrWhiteSpace(resolvedNamedEntity.Title))
			{
				pathTitle = resolvedNamedEntity.Title;
			}
		}

		if (parsedModel is Directive freeformDirective
			&& model.Mode == VaultStorageMode.Freeform)
		{
			if (!pathPolicy.IsAllowedFreeformDirectiveAssertionPath(fullPath))
			{
				issues.Add(new MarkdownValidationIssue(
					"path",
					"Freeform directive path is under a managed root reserved for non-directive entities and cannot assert freeform ownership.",
					Path.GetRelativePath(layout.VaultRoot, fullPath)));
			}

			if (!string.IsNullOrWhiteSpace(pathId) && !knownIds.Contains(pathId))
			{
				var resolved = await puckEntityResolutionService.ResolveAsync(pathId, cancellationToken);
				if (resolved.Exists && !string.Equals(resolved.EntityType, nameof(Directive), StringComparison.Ordinal))
				{
					return null;
				}
			}

			freeformDirective.ParentDirectiveId = await ResolveFreeformDirectiveParentIdAsync(fullPath, pathId, knownIds, cancellationToken);
		}

		await ApplyDomainValidationsAsync(parsedModel, issues, cancellationToken);

		if (string.IsNullOrWhiteSpace(pathId) && puckCreationService.RequiresCallerInputFor(model.EntityType))
		{
			issues.Add(new MarkdownValidationIssue("id", "Path identity is missing required caller-provided PUCK input."));
		}

		var issueMessages = issues.Select(issue => $"{issue.FieldPath}: {issue.Message}").ToArray();
		var (action, reason) = decisionService.Decide(model, pathId, pathTitle, issueMessages, knownIds);

		return new VaultSyncCandidate(
			fullPath,
			Path.GetRelativePath(layout.VaultRoot, fullPath),
			model,
			pathId,
			pathTitle,
			parsedModel,
			issues,
			ComputeHash(ExtractBody(markdown)),
			File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath) : DateTime.UtcNow,
			action,
			reason);
	}

	/// <summary>
	/// Applies domain-specific validations that require cross-entity context not available in generic frontmatter validation.
	/// </summary>
	/// <param name="model">The parsed model being validated.</param>
	/// <param name="issues">The mutable issue collection to append to.</param>
	/// <param name="cancellationToken">A token used to cancel validation.</param>
	private async Task ApplyDomainValidationsAsync(object model, ICollection<MarkdownValidationIssue> issues, CancellationToken cancellationToken)
	{
		if (model is not LorePage lorePage)
		{
			return;
		}

		var siblings = await context.LorePages
			.AsNoTracking()
			.Where(item => item.ParentId == lorePage.ParentId && item.Id != lorePage.Id)
			.ToListAsync(cancellationToken);

		var ordered = siblings
			.Append(lorePage)
			.OrderBy(item => item, LorePage.NarrativeOrderComparer)
			.ToList();

		var position = ordered.FindIndex(item => string.Equals(item.Id, lorePage.Id, StringComparison.OrdinalIgnoreCase));
		if (position < 0)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(lorePage.ParentId))
		{
			var parent = await context.LorePages
				.AsNoTracking()
				.FirstOrDefaultAsync(item => item.Id == lorePage.ParentId, cancellationToken);

			if (parent is not null && position == 0 && lorePage.Beginning != parent.Beginning)
			{
				issues.Add(new MarkdownValidationIssue(
					"beginning",
					$"First child beginning must match parent beginning '{FormatDate(parent.Beginning)}'.",
					FormatDate(lorePage.Beginning)));
			}
		}

		if (lorePage.Beginning is null)
		{
			return;
		}

		var earlierWithLaterBeginning = ordered
			.Take(position)
			.FirstOrDefault(item => item.Beginning is not null && item.Beginning.Value > lorePage.Beginning.Value);

		if (earlierWithLaterBeginning is not null)
		{
			issues.Add(new MarkdownValidationIssue(
				"beginning",
				"Lore beginning cannot be earlier than an earlier-index sibling beginning.",
				FormatDate(lorePage.Beginning)));
		}

		var laterWithEarlierBeginning = ordered
			.Skip(position + 1)
			.FirstOrDefault(item => item.Beginning is not null && item.Beginning.Value < lorePage.Beginning.Value);

		if (laterWithEarlierBeginning is not null)
		{
			issues.Add(new MarkdownValidationIssue(
				"beginning",
				"Lore beginning cannot be later than a later-index sibling beginning.",
				FormatDate(lorePage.Beginning)));
		}
	}

	/// <summary>
	/// Formats a date value for human-readable validation payloads.
	/// </summary>
	/// <param name="date">The optional date to format.</param>
	/// <returns>An ISO yyyy-MM-dd string, or <see langword="null"/>.</returns>
	private static string? FormatDate(DateOnly? date)
	{
		return date?.ToString("yyyy-MM-dd");
	}

	/// <summary>
	/// Loads known identifiers for each configured path sync model.
	/// </summary>
	/// <param name="cancellationToken">A token used to cancel the database fetch.</param>
	/// <returns>A dictionary mapping entity types to their known identifiers.</returns>
	private async Task<IReadOnlyDictionary<Type, HashSet<string>>> LoadKnownIdsAsync(CancellationToken cancellationToken)
	{
		var result = new Dictionary<Type, HashSet<string>>();
		foreach (var model in pathSyncModelCatalog.GetModels())
		{
			result[model.EntityType] = await model.LoadKnownIdsAsync(context, cancellationToken);
		}

		return result;
	}

	/// <summary>
	/// Deserializes markdown frontmatter into a model instance using either strict or default-preserving behavior.
	/// </summary>
	/// <param name="model">The target model instance to hydrate.</param>
	/// <param name="modelType">The CLR type of the model instance.</param>
	/// <param name="markdown">The raw markdown text.</param>
	/// <param name="preserveDefaultsForMissingFields">Whether missing fields should keep pre-initialized defaults.</param>
	/// <returns>The validation issues produced during deserialization.</returns>
	private IEnumerable<MarkdownValidationIssue> DeserializeInto(object model, Type modelType, string markdown, bool preserveDefaultsForMissingFields)
	{
		var method = typeof(MarkdownFrontMatterSerializer)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.Single(candidate => candidate.Name == (preserveDefaultsForMissingFields
				? nameof(MarkdownFrontMatterSerializer.DeserializePreservingDefaults)
				: nameof(MarkdownFrontMatterSerializer.Deserialize))
				&& candidate.IsGenericMethodDefinition
				&& candidate.GetParameters().Length == 2);

		var closed = method.MakeGenericMethod(modelType);
		var result = closed.Invoke(markdownSerializer, [markdown, model])
			?? throw new InvalidOperationException($"Failed to deserialize markdown into '{modelType.Name}'.");

		var issuesProperty = result.GetType().GetProperty(nameof(MarkdownDeserializationResult<object>.Issues))
			?? throw new InvalidOperationException($"Deserialization result for '{modelType.Name}' does not expose issues.");

		return (IReadOnlyList<MarkdownValidationIssue>)(issuesProperty.GetValue(result)
			?? Array.Empty<MarkdownValidationIssue>());
	}

	/// <summary>
	/// Creates a path-composed model instance and applies path-derived authorities before frontmatter hydration.
	/// </summary>
	/// <param name="entityType">The entity CLR type to instantiate.</param>
	/// <param name="path">The candidate path supplying composition context.</param>
	/// <returns>A newly created model populated with path-derived defaults.</returns>
	private object CreatePathComposedModel(Type entityType, string path)
	{
		var model = Activator.CreateInstance(entityType)
			?? throw new InvalidOperationException($"Could not construct path-backed model '{entityType.Name}'.");

		switch (model)
		{
			case Directive directive:
				MarkdownFileLocator.ApplyDirectiveCompositionFromPath(directive, path);
				break;
			case Objective objective:
				MarkdownFileLocator.ApplyObjectiveCompositionFromPath(objective, path);
				break;
			case LorePage lorePage:
				MarkdownFileLocator.ApplyLorePageCompositionFromPath(lorePage, path, layout.VaultRoot, layout.SagaRoot);
				break;
			case IPuckNamedEntity namedEntity:
				MarkdownFileLocator.ApplyLoosePuckIdentityFromPath(namedEntity, path);
				break;
		}

		return model;
	}

	/// <summary>
	/// Applies authoritative path-derived relationships and identity fields over frontmatter values when conflicts occur.
	/// </summary>
	/// <param name="model">The hydrated model to normalize.</param>
	/// <param name="path">The candidate source path.</param>
	/// <param name="issues">The mutable issue collection to append relation mismatches to.</param>
	private void ApplyPathAuthorities(object model, string path, ICollection<MarkdownValidationIssue> issues)
	{
		switch (model)
		{
			case Directive directive:
			{
				var pathParentId = MarkdownFileLocator.TryGetContainingDirectiveId(path);
				if (!string.Equals(directive.ParentDirectiveId, pathParentId, StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrWhiteSpace(directive.ParentDirectiveId))
					{
						issues.Add(new MarkdownValidationIssue("parent", "Frontmatter parent relation does not match the path-derived directive parent.", directive.ParentDirectiveId));
					}

					directive.ParentDirectiveId = pathParentId;
				}

				break;
			}
			case Objective objective:
			{
				var pathDirectiveId = MarkdownFileLocator.TryGetContainingDirectiveId(path);
				if (!string.Equals(objective.DirectiveId, pathDirectiveId, StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrWhiteSpace(objective.DirectiveId))
					{
						issues.Add(new MarkdownValidationIssue("directive", "Frontmatter directive relation does not match the path-derived directive container.", objective.DirectiveId));
					}

					objective.DirectiveId = pathDirectiveId;
				}

				break;
			}
			case LorePage lorePage:
				lorePage.RelativePath = Path.GetRelativePath(layout.VaultRoot, path);
				break;
		}
	}

	private async Task<string?> ResolveFreeformDirectiveParentIdAsync(
		string path,
		string? currentDirectiveId,
		ISet<string> knownDirectiveIds,
		CancellationToken cancellationToken)
	{
		var currentDirectory = Path.GetDirectoryName(path);
		while (!string.IsNullOrWhiteSpace(currentDirectory)
			&& !string.Equals(currentDirectory, layout.VaultRoot, StringComparison.OrdinalIgnoreCase))
		{
			if (MarkdownFileLocator.IsSelfNamedDirectory(currentDirectory))
			{
				var primaryFile = Path.Combine(currentDirectory, $"{Path.GetFileName(currentDirectory)}.md");
				var parsed = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(primaryFile);
				var ancestorId = parsed.Id;
				if (string.IsNullOrWhiteSpace(ancestorId) && File.Exists(primaryFile))
				{
					var frontMatter = markdownSerializer.ParseFrontMatter(await File.ReadAllTextAsync(primaryFile, cancellationToken));
					if (frontMatter.TryGetValue("puck", out var rawPuck) && !string.IsNullOrWhiteSpace(rawPuck))
					{
						ancestorId = rawPuck.Trim().Trim('"');
					}
				}

				if (!string.IsNullOrWhiteSpace(ancestorId)
					&& !string.Equals(ancestorId, currentDirectiveId, StringComparison.OrdinalIgnoreCase)
					&& knownDirectiveIds.Contains(ancestorId))
				{
					return ancestorId;
				}
			}

			currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
		}

		return null;
	}

	/// <summary>
	/// Extracts markdown body content while removing YAML frontmatter delimiters and payload.
	/// </summary>
	/// <param name="markdown">The full markdown document.</param>
	/// <returns>The markdown body section without frontmatter.</returns>
	private static string ExtractBody(string markdown)
	{
		using var reader = new StringReader(markdown);
		var firstLine = reader.ReadLine();
		if (!string.Equals(NormalizeFrontMatterDelimiterLine(firstLine), "---", StringComparison.Ordinal))
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

	/// <summary>
	/// Normalizes a potential frontmatter delimiter line (including BOM trimming).
	/// </summary>
	/// <param name="line">The raw line to normalize.</param>
	/// <returns>The normalized delimiter candidate, or <see langword="null"/>.</returns>
	private static string? NormalizeFrontMatterDelimiterLine(string? line)
	{
		if (line is null)
		{
			return null;
		}

		if (line.Length > 0 && line[0] == '\uFEFF')
		{
			line = line[1..];
		}

		return line.Trim();
	}

	/// <summary>
	/// Computes a stable SHA-256 hash string for body content comparison.
	/// </summary>
	/// <param name="value">The source value to hash.</param>
	/// <returns>An uppercase hexadecimal SHA-256 digest.</returns>
	private static string ComputeHash(string value)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		return Convert.ToHexString(bytes);
	}
}
