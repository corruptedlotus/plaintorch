using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Pleiades.Orchestration;
using Pleiades.Puck;
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
	MarkdownFrontMatterSerializer markdownSerializer,
	VaultAuditLogService auditLogService,
	VaultSyncDecisionService decisionService,
	PuckCreationService puckCreationService)
{
	/// <summary>
	/// Scans all catalog-backed markdown paths and produces sync candidates.
	/// </summary>
	public async Task<VaultDiscoveryScanResult> ScanAsync(string origin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);
		var candidates = new List<VaultSyncCandidate>();
		var ignored = 0;
		var missing = 0;
		var knownIdsByType = await LoadKnownIdsAsync(cancellationToken);

		var allPaths = pathSyncModelCatalog.GetModels()
			.SelectMany(model => model.ScanRoots)
			.Where(Directory.Exists)
			.SelectMany(root => Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var path in allPaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
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

	private async Task<VaultSyncCandidate?> InspectPathCoreAsync(
		string path,
		IReadOnlyDictionary<Type, HashSet<string>> knownIdsByType,
		CancellationToken cancellationToken)
	{
		var fullPath = Path.GetFullPath(path);
		if (!File.Exists(fullPath))
		{
			return null;
		}

		if (!pathSyncModelCatalog.TryResolve(fullPath, out var model) || model is null)
		{
			return null;
		}

		var markdown = await File.ReadAllTextAsync(fullPath, cancellationToken);
		var (pathId, pathTitle) = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(fullPath);
		var knownIds = knownIdsByType.TryGetValue(model.EntityType, out var ids)
			? ids
			: new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var isNewEntity = string.IsNullOrWhiteSpace(pathId) || !knownIds.Contains(pathId);
		var parsedModel = CreatePathComposedModel(model.EntityType, fullPath);
		var issues = DeserializeInto(parsedModel, model.EntityType, markdown, preserveDefaultsForMissingFields: isNewEntity)
			.Select(issue => issue)
			.ToList();
		ApplyPathAuthorities(parsedModel, fullPath, issues);

		if (string.IsNullOrWhiteSpace(pathId) && puckCreationService.RequiresCallerInputFor(model.EntityType))
		{
			issues.Add(new MarkdownValidationIssue("id", "Path identity is missing required caller-provided PUCK input."));
		}

		var issueMessages = issues.Select(issue => $"{issue.FieldPath}: {issue.Message}").ToArray();
		var (action, reason) = decisionService.Decide(model, pathId, issueMessages, knownIds);

		return new VaultSyncCandidate(
			fullPath,
			Path.GetRelativePath(layout.VaultRoot, fullPath),
			model,
			pathId,
			pathTitle,
			parsedModel,
			issues,
			ComputeHash(ExtractBody(markdown)),
			File.GetLastWriteTimeUtc(fullPath),
			action,
			reason);
	}

	private async Task<IReadOnlyDictionary<Type, HashSet<string>>> LoadKnownIdsAsync(CancellationToken cancellationToken)
	{
		var result = new Dictionary<Type, HashSet<string>>();
		foreach (var model in pathSyncModelCatalog.GetModels())
		{
			result[model.EntityType] = await model.LoadKnownIdsAsync(context, cancellationToken);
		}

		return result;
	}

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
			case IPuckNamedEntity namedEntity:
				MarkdownFileLocator.ApplyLoosePuckIdentityFromPath(namedEntity, path);
				break;
		}

		return model;
	}

	private static void ApplyPathAuthorities(object model, string path, ICollection<MarkdownValidationIssue> issues)
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
		}
	}

	private static string ExtractBody(string markdown)
	{
		if (!markdown.StartsWith("---", StringComparison.Ordinal))
		{
			return markdown;
		}

		using var reader = new StringReader(markdown);
		reader.ReadLine();
		while (reader.ReadLine() is { } line)
		{
			if (line == "---")
			{
				break;
			}
		}

		return reader.ReadToEnd().TrimStart('\r', '\n');
	}

	private static string ComputeHash(string value)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		return Convert.ToHexString(bytes);
	}
}
