using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Scans vault-backed markdown paths, resolves path-based models, and builds validated sync candidates.
/// </summary>
public sealed class VaultMarkdownDiscoveryService(
	VaultLayout layout,
	PlainfraContext context,
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	VaultStoragePolicyEngine policyEngine,
	VaultWatcherPathPolicy pathPolicy,
	MarkdownFrontMatterSerializer markdownSerializer,
	VaultAuditLogService auditLogService,
	VaultImplicitBoundaryService implicitBoundaryService,
	VaultSyncDecisionService decisionService,
	PuckCreationService puckCreationService,
	PuckEntityResolutionService puckEntityResolutionService,
	VaultEntityModelCatalog entityModelCatalog,
	VaultFamilyInstantiationResolver familyInstantiationResolver,
	PuckIdentityGate identityGate,
	VaultStoragePathComposer pathComposer)
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

		var allPaths = policyEngine.EnumerateCandidateMarkdownPaths();

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

		// Orphan pass (offline-deletion parity): a boundary-begun file deleted while the daemon was off produces no
		// filesystem event, so the file scan above never sees it. Reconcile those entities by inspecting each begun
		// boundary's recorded path — when the file is now absent, discovery recovers the identity and the mode policy
		// decides the authoritative action (implicit deletes), so a sweep reaches the same state a live delete would.
		var discoveredPaths = new HashSet<string>(allPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
		foreach (var boundary in await implicitBoundaryService.EnumerateBegunBoundariesAsync(cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var absolutePath = Path.GetFullPath(Path.Combine(layout.VaultRoot, boundary.VaultRelativePath));
			if (File.Exists(absolutePath) || discoveredPaths.Contains(absolutePath))
			{
				continue;
			}

			var candidate = await InspectPathCoreAsync(absolutePath, knownIdsByType, cancellationToken);
			if (candidate is not null)
			{
				candidates.Add(candidate);
			}
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
	/// Inspects an arbitrary markdown path as an init candidate for a specific entity type, even when generic
	/// path-catalog classification does not apply (an explicit API <c>init</c> points at a specific file, so the
	/// entity's model is forced and the watcher-ignore / model-belonging policies are relaxed).
	/// </summary>
	/// <param name="path">The filesystem path to inspect.</param>
	/// <param name="entityType">The entity type whose model is forced for classification.</param>
	/// <param name="origin">The logical source performing inspection.</param>
	/// <param name="cancellationToken">A token used to cancel inspection.</param>
	/// <returns>A resolved sync candidate, or <see langword="null"/> when the path cannot be treated as a candidate for the type.</returns>
	public async Task<VaultSyncCandidate?> InspectInitPathAsync(string path, Type entityType, string origin, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);

		var fullPath = Path.GetFullPath(path);
		if (!File.Exists(fullPath)
			|| !string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var forcedModel = pathSyncModelCatalog
			.GetModels()
			.FirstOrDefault(model => model.EntityType == entityType);
		if (forcedModel is null)
		{
			return null;
		}

		var knownIdsByType = await LoadKnownIdsAsync(cancellationToken);
		var candidate = await InspectPathCoreAsync(
			fullPath,
			knownIdsByType,
			cancellationToken,
			forcedModel: forcedModel,
			enforceWatcherIgnorePolicy: false,
			enforceModelBelongingPolicy: false);

		if (candidate is null)
		{
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
	/// Inspects an arbitrary markdown path as a directive-init candidate. Thin wrapper over
	/// <see cref="InspectInitPathAsync"/> for the directive family anchor.
	/// </summary>
	public Task<VaultSyncCandidate?> InspectDirectiveInitPathAsync(string path, string origin, CancellationToken cancellationToken = default)
		=> InspectInitPathAsync(path, typeof(Directive), origin, cancellationToken);

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
		CancellationToken cancellationToken,
		VaultPathSyncModel? forcedModel = null,
		bool enforceWatcherIgnorePolicy = true,
		bool enforceModelBelongingPolicy = true)
	{
		if (enforceWatcherIgnorePolicy && pathPolicy.ShouldIgnorePath(path))
		{
			return null;
		}

		string? resolvedPath;
		VaultPathSyncModel? model;
		if (forcedModel is null)
		{
			if (!policyEngine.TryResolveWatchPath(path, out resolvedPath, out model)
				|| string.IsNullOrWhiteSpace(resolvedPath)
				|| model is null)
			{
				return null;
			}
		}
		else
		{
			resolvedPath = Path.GetFullPath(path);
			model = forcedModel;
		}

		if (string.IsNullOrWhiteSpace(resolvedPath)
			|| model is null)
		{
			return null;
		}

		var fullPath = Path.GetFullPath(resolvedPath);
		var fileExists = File.Exists(fullPath);
		var modePolicy = policyEngine.PolicyFor(model.Mode);
		var markdown = fileExists
			? await VaultFileAccess.ReadAllTextAsync(fullPath, cancellationToken)
			: string.Empty;
		if (enforceModelBelongingPolicy
			&& !await policyEngine.BelongsToModelAsync(model, fullPath, markdown, cancellationToken))
		{
			return null;
		}

		var (pathId, pathTitle) = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(fullPath);
		var pathDerivedId = pathId;
		var pathDerivedTitle = pathTitle;
		var instantiationType = familyInstantiationResolver.ResolveInstantiationType(
			model,
			ResolveComposedIdentity(markdown, pathId));
		var parsedModel = CreatePathComposedModel(instantiationType, fullPath);
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

		// Notation-gate a filename-derived Index identity: a flat "{prefix} - {title}" filename is an identity only if
		// the prefix tokenizes against the entity's declared PUCK. A user's note whose name merely contains " - " (e.g.
		// "Council Meeting - Q3") composes a non-tokenizing id, so it is treated as identity-less — an enforced root
		// then rejects it as a foreign file instead of materializing an invalid-PUCK entity. Quiet storage (frontmatter
		// identity, trusted) and hierarchical path-composed ids (which carry '/') are left alone.
		if (!string.IsNullOrWhiteSpace(pathId)
			&& !pathId.Contains('/')
			&& entityModelCatalog.TryGet(model.EntityType, out var declaredModel)
			&& declaredModel?.Storage?.PuckStorage == VaultPuckStorage.Index
			&& !identityGate.IsMintable(instantiationType, pathId))
		{
			pathId = null;
			if (parsedModel is IPuckNamedEntity gatedEntity)
			{
				gatedEntity.Id = string.Empty;
			}
		}

		var knownIds = knownIdsByType.TryGetValue(model.EntityType, out var ids)
			? ids
			: new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var isNewEntity = string.IsNullOrWhiteSpace(pathId) || !knownIds.Contains(pathId);
		var preserveDefaultsForMissingFields = isNewEntity
			|| (!modePolicy.IsIdentityDriven && typeof(IPuckNamedEntity).IsAssignableFrom(model.EntityType));
		var issues = DeserializeInto(parsedModel, instantiationType, markdown, preserveDefaultsForMissingFields: preserveDefaultsForMissingFields)
			.Select(issue => issue)
			.ToList();
		ApplyPathAuthorities(parsedModel, fullPath, issues);
		if (parsedModel is IPuckNamedEntity resolvedNamedEntity)
		{
			if (!modePolicy.IsIdentityDriven
				&& !string.IsNullOrWhiteSpace(pathDerivedId)
				&& string.Equals(resolvedNamedEntity.Id, pathDerivedId, StringComparison.OrdinalIgnoreCase))
			{
				resolvedNamedEntity.Title = pathDerivedTitle;
			}

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
			&& modePolicy.IsIdentityDriven)
		{
			var assertionViolation = pathPolicy.TryGetFreeformDirectiveAssertionViolation(fullPath);
			if (assertionViolation is not null)
			{
				issues.Add(new MarkdownValidationIssue(
					"path",
					assertionViolation,
					Path.GetRelativePath(layout.VaultRoot, fullPath)));
			}

			if (!string.IsNullOrWhiteSpace(pathId) && !knownIds.Contains(pathId))
			{
				var resolved = await puckEntityResolutionService.ResolveAsync(pathId, cancellationToken);
				if (resolved.Exists && !entityModelCatalog.IsFamilyMember(typeof(Directive), resolved.EntityType))
				{
					return null;
				}
			}

			freeformDirective.ParentDirectiveId = await ResolveFreeformDirectiveParentIdAsync(fullPath, pathId, knownIds, cancellationToken);
		}

		await ApplyDomainValidationsAsync(parsedModel, issues, cancellationToken);

		if (string.IsNullOrWhiteSpace(pathId) && puckCreationService.RequiresCallerInputFor(instantiationType))
		{
			issues.Add(new MarkdownValidationIssue("id", "Path identity is missing required caller-provided PUCK input."));
		}

		var boundaryBegun = true;
		if (modePolicy.BeginsSyncBoundaryOnFirstFile)
		{
			if (!string.IsNullOrWhiteSpace(pathId))
			{
				boundaryBegun = await implicitBoundaryService.HasBoundaryBegunAsync(model.EntityName, pathId, cancellationToken);
			}
			else if (!fileExists)
			{
				// A quiet (title-only) file carries no filename identity, so a deletion recovers its identity
				// from the boundary entry recorded for that path when the file first began existing.
				var recoveredId = await implicitBoundaryService.TryRecoverEntityIdByLocationAsync(
					model.EntityName,
					Path.GetRelativePath(layout.VaultRoot, fullPath),
					cancellationToken);
				if (!string.IsNullOrWhiteSpace(recoveredId))
				{
					pathId = recoveredId;
					boundaryBegun = true;
				}
			}
		}

		var issueMessages = issues.Select(issue => $"{issue.FieldPath}: {issue.Message}").ToArray();
		var decision = decisionService.Decide(model, pathId, pathTitle, issueMessages, knownIds, fileExists, boundaryBegun);

		return new VaultSyncCandidate(
			fullPath,
			Path.GetRelativePath(layout.VaultRoot, fullPath),
			model,
			pathId,
			pathTitle,
			parsedModel,
			issues,
			ComputeHash(ExtractBody(markdown)),
			fileExists ? File.GetLastWriteTimeUtc(fullPath) : DateTime.UtcNow,
			fileExists,
			decision.Action,
			decision.Reason,
			decision.Concern);
	}

	/// <summary>
	/// Applies domain-specific validations that require cross-entity context not available in generic frontmatter validation.
	/// </summary>
	/// <param name="model">The parsed model being validated.</param>
	/// <param name="issues">The mutable issue collection to append to.</param>
	/// <param name="cancellationToken">A token used to cancel validation.</param>
	private async Task ApplyDomainValidationsAsync(object model, ICollection<MarkdownValidationIssue> issues, CancellationToken cancellationToken)
	{
		if (model is ExecutiveOrder order)
		{
			// The owning sprint relation is required and path-derived, so an unresolvable parent must block reconciliation.
			var sprintExists = !string.IsNullOrWhiteSpace(order.OnrushSprintId)
				&& await context.OnrushSprints
					.AsNoTracking()
					.AnyAsync(item => item.Id == order.OnrushSprintId, cancellationToken);
			if (!sprintExists)
			{
				issues.Add(new MarkdownValidationIssue(
					"onrush",
					"Executive order path does not resolve to a known owning onrush sprint.",
					order.OnrushSprintId));
			}

			return;
		}

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
			?? throw new MarkdownDeserializationException($"Failed to deserialize markdown into '{modelType.Name}'.");

		var issuesProperty = result.GetType().GetProperty(nameof(MarkdownDeserializationResult<object>.Issues))
			?? throw new MarkdownDeserializationException($"Deserialization result for '{modelType.Name}' does not expose issues.");

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

		pathComposer.ApplyCompositionFromPath(model, path);
		return model;
	}

	/// <summary>
	/// Resolves the identity that selects a polymorphic family's concrete member before composition.
	/// </summary>
	/// <param name="markdown">The file's raw markdown, when it exists.</param>
	/// <param name="pathId">The loose identity already parsed from the path.</param>
	/// <returns>The frontmatter PUCK when present; otherwise the path-derived identity.</returns>
	private string? ResolveComposedIdentity(string markdown, string? pathId)
	{
		// A Quiet or freeform file keeps its PUCK in frontmatter, not in a title-named path, so prefer it; fall back
		// to the loose path identity for index-stored files that carry the PUCK in the filename.
		if (!string.IsNullOrWhiteSpace(markdown))
		{
			var frontMatter = markdownSerializer.ParseFrontMatter(markdown);
			if (frontMatter.TryGetValue("puck", out var rawPuck) && !string.IsNullOrWhiteSpace(rawPuck))
			{
				return rawPuck.Trim().Trim('"');
			}
		}

		return pathId;
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
				var pathParentId = pathPolicy.TryResolveContainingDirectiveId(path, skipCurrentIfSelfNamed: true);
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
			case Incentive incentive:
			{
				// Each incentive kind's standalone root carries no directive ancestry authority.
				var storage = incentive.GetType().GetCustomAttribute<VaultStorageAttribute>();
				var standaloneRoot = storage is null ? layout.ObjectivesRoot : layout.GetLocationRoot(storage.LocationKey);
				var normalizedStandaloneRoot = Path.GetFullPath(standaloneRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				if (string.Equals(normalizedPath, normalizedStandaloneRoot, StringComparison.OrdinalIgnoreCase)
					|| normalizedPath.StartsWith(normalizedStandaloneRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					break;
				}

				var pathDirectiveId = pathPolicy.TryResolveContainingDirectiveId(path);
				if (string.IsNullOrWhiteSpace(pathDirectiveId))
				{
					break;
				}

				if (!string.Equals(incentive.DirectiveId, pathDirectiveId, StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrWhiteSpace(incentive.DirectiveId))
					{
						issues.Add(new MarkdownValidationIssue("directive", "Frontmatter directive relation does not match the path-derived directive container.", incentive.DirectiveId));
					}

					incentive.DirectiveId = pathDirectiveId;
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
