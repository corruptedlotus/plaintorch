using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Resources;
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
	VaultStoragePathComposer pathComposer,
	VaultEntityGateway entityGateway,
	ILogger<VaultMarkdownDiscoveryService> logger)
{
	// A frontmatter line asserting an identity. Read leniently (no YAML parse), so a note whose frontmatter is otherwise
	// broken still counts as asserting its identity and is never mistaken for a deleted one.
	private static readonly Regex PuckLine = new(@"^\s*puck\s*:\s*[""']?(?<id>[^""'\s#]+)", RegexOptions.CultureInvariant);

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

			VaultSyncCandidate? candidate;
			try
			{
				candidate = await InspectPathCoreAsync(path, knownIdsByType, cancellationToken);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				// One unreadable candidate (a file another process holds open, a permission error) must not abort
				// discovery of every other file. Skip it — the live watcher re-inspects it on its next filesystem
				// event, and transient locks are retried at the read boundary — and continue the sweep.
				missing++;
				logger.LogWarning(
					exception,
					"Vault discovery could not inspect '{Path}' during the scan; skipping it and continuing.",
					path);
				continue;
			}

			if (candidate is null)
			{
				ignored++;
				continue;
			}

			candidates.Add(candidate);
		}

		// Vanished-note pass: a note deleted, moved into an ignored folder or stripped of its identity while the daemon was
		// off produces no event, so the file scan above never sees it. It is found by identity — nothing records a note's
		// path — exactly as the running watcher finds it after a change, so a sweep reaches the same state.
		candidates.AddRange(await FindVanishedNoteCandidatesAsync(knownIdsByType, cancellationToken));

		await auditLogService.WriteAsync(
			"discovery",
			"startup-scan",
			details: new { origin, candidateCount = candidates.Count, invalidCount = candidates.Count(candidate => !candidate.IsValid), ignored, missing },
			cancellationToken: cancellationToken);

		return new VaultDiscoveryScanResult(candidates, ignored, missing);
	}

	/// <summary>
	/// Finds the identity-driven entities whose note is gone: the entity still exists, it is expected to have a note (an
	/// implicit entity whose boundary stands, or any freeform entity, which is materialized on create), no write of its
	/// note is pending, and no note in the vault asserts its identity any more. Each comes back as a deletion candidate
	/// (<see cref="VaultSyncCandidate.Vanished"/>) the mode policy decides, ordered so dependents go first: implicit
	/// entities before the directives that own them, and subdirectives before their parents.
	/// </summary>
	/// <remarks>
	/// Nothing records where a note is — an identity-driven note carries its identity inside the file and may live
	/// anywhere — so this is how the sweep and the running watcher alike see a note deleted, moved into an ignored folder,
	/// or stripped of its identity, whatever event (if any) announced it. When any part of the vault cannot be read,
	/// nothing is reported: a note that could not be read is not a deleted one.
	/// </remarks>
	public async Task<IReadOnlyList<VaultSyncCandidate>> FindVanishedNoteCandidatesAsync(CancellationToken cancellationToken = default)
		=> await FindVanishedNoteCandidatesAsync(await LoadKnownIdsAsync(cancellationToken), cancellationToken);

	private async Task<IReadOnlyList<VaultSyncCandidate>> FindVanishedNoteCandidatesAsync(
		IReadOnlyDictionary<Type, HashSet<string>> knownIdsByType,
		CancellationToken cancellationToken)
	{
		var expected = new List<(VaultPathSyncModel Model, string EntityId)>();
		var models = pathSyncModelCatalog.GetModels().Where(model => policyEngine.PolicyFor(model.Mode).IsIdentityDriven).ToList();

		// Implicit: only an entity whose boundary stands has (had) a note. Ids are unique across types, and a boundary of an
		// entity that no longer exists has nothing left to reconcile.
		var implicitModels = models.Where(model => policyEngine.PolicyFor(model.Mode).BeginsSyncBoundaryOnFirstFile).ToList();
		foreach (var boundary in await implicitBoundaryService.EnumerateBegunBoundariesAsync(cancellationToken))
		{
			if (implicitModels.FirstOrDefault(model => knownIdsByType.TryGetValue(model.EntityType, out var ids) && ids.Contains(boundary.EntityId)) is { } model)
			{
				expected.Add((model, boundary.EntityId));
			}
		}

		// Freeform: every entity is materialized on create, so every one is expected to have a note.
		foreach (var model in models.Where(model => policyEngine.PolicyFor(model.Mode) is { BeginsSyncBoundaryOnFirstFile: false, MaterializesOnCreate: true }))
		{
			if (knownIdsByType.TryGetValue(model.EntityType, out var ids))
			{
				expected.AddRange(ids.Select(id => (model, id)));
			}
		}

		if (expected.Count == 0)
		{
			return [];
		}

		// A note whose write is still in flight (its intent committed with the entity, the file not yet on disk) is not a
		// deleted one.
		var pendingWrites = await context.VaultWriteIntents
			.AsNoTracking()
			.Select(intent => intent.EntityId)
			.ToListAsync(cancellationToken);
		var asserted = await CollectAssertedIdentitiesAsync(cancellationToken);
		if (asserted is null)
		{
			return [];
		}

		var pending = new HashSet<string>(pendingWrites, StringComparer.OrdinalIgnoreCase);
		var vanished = new List<(VaultSyncCandidate Candidate, int Order)>();
		foreach (var (model, entityId) in expected.Where(entry => !asserted.Contains(entry.EntityId) && !pending.Contains(entry.EntityId)))
		{
			if (await CreateVanishedCandidateAsync(model, entityId, knownIdsByType[model.EntityType], cancellationToken) is { } candidate)
			{
				vanished.Add((candidate, await DeletionOrderAsync(model, candidate.ParsedModel, cancellationToken)));
			}
		}

		return vanished
			.OrderBy(static entry => entry.Order)
			.ThenBy(static entry => entry.Candidate.VaultRelativePath, StringComparer.OrdinalIgnoreCase)
			.Select(static entry => entry.Candidate)
			.ToList();
	}

	/// <summary>
	/// Orders vanished entities so each goes before what it depends on: implicit entities (which a directive owns) first,
	/// then directives from the deepest subdirective up, since a directive's delete is refused while children remain.
	/// </summary>
	private async Task<int> DeletionOrderAsync(VaultPathSyncModel model, object entity, CancellationToken cancellationToken)
	{
		if (policyEngine.PolicyFor(model.Mode).BeginsSyncBoundaryOnFirstFile || entity is not Directive directive)
		{
			return 0;
		}

		var depth = 0;
		for (var parentId = directive.ParentDirectiveId; !string.IsNullOrWhiteSpace(parentId) && depth < 64; depth++)
		{
			var current = parentId;
			parentId = await context.Directives.AsNoTracking()
				.Where(item => item.Id == current)
				.Select(item => item.ParentDirectiveId)
				.FirstOrDefaultAsync(cancellationToken);
		}

		return 1000 - depth;
	}

	/// <summary>
	/// Reads the identity every note in the vault asserts (its frontmatter PUCK), wherever the note lives; ignored folders
	/// (<c>.trash</c>, <c>_assets</c>, the data folder, …) hold no notes. A note deleted while it is read asserts nothing.
	/// Returns <see langword="null"/> when a folder or a note cannot be read, since an unread note might still assert an
	/// identity.
	/// </summary>
	private async Task<HashSet<string>?> CollectAssertedIdentitiesAsync(CancellationToken cancellationToken)
	{
		var asserted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var pending = new Stack<string>();
		pending.Push(layout.VaultRoot);
		try
		{
			while (pending.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var directory = pending.Pop();
				string[] children;
				string[] notes;
				try
				{
					children = Directory.GetDirectories(directory);
					notes = Directory.GetFiles(directory, "*.md");
				}
				catch (DirectoryNotFoundException)
				{
					continue;
				}

				foreach (var child in children)
				{
					if (!pathPolicy.ShouldIgnorePath(child))
					{
						pending.Push(child);
					}
				}

				foreach (var note in notes)
				{
					if (!pathPolicy.ShouldIgnorePath(note) && await ReadAssertedIdentityAsync(note, cancellationToken) is { } id)
					{
						asserted.Add(id);
					}
				}
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(exception, "Could not read every note in the vault; notes that are gone are not reconciled on this pass.");
			return null;
		}

		return asserted;
	}

	/// <summary>
	/// Reads the identity a note's frontmatter asserts, leniently (a line-level match, no YAML parse) and only as far as
	/// the frontmatter goes. A note deleted meanwhile asserts nothing.
	/// </summary>
	private static async Task<string?> ReadAssertedIdentityAsync(string path, CancellationToken cancellationToken)
	{
		FileStream stream;
		try
		{
			stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
		}
		catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			return null;
		}

		await using (stream)
		{
			using var reader = new StreamReader(stream);
			if ((await reader.ReadLineAsync(cancellationToken))?.TrimStart('﻿').Trim() != "---")
			{
				return null;
			}

			while (await reader.ReadLineAsync(cancellationToken) is { } line)
			{
				if (line.Trim() == "---")
				{
					return null;
				}

				var match = PuckLine.Match(line);
				if (match.Success)
				{
					return match.Groups["id"].Value;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Builds the deletion candidate for an entity whose note is gone. With no path on record, it names the entity's
	/// canonical location for display; its issues are keyed on the identity, not on that path.
	/// </summary>
	private async Task<VaultSyncCandidate?> CreateVanishedCandidateAsync(
		VaultPathSyncModel model,
		string entityId,
		ISet<string> knownIds,
		CancellationToken cancellationToken)
	{
		var entity = await entityGateway.FindByIdAsync(model.EntityType, entityId, track: false, cancellationToken: cancellationToken);
		if (entity is null)
		{
			return null;
		}

		var title = (entity as IPuckNamedEntity)?.Title is { Length: > 0 } named ? named : entityId;
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(pathComposer.GetFilePath(entity, immediateParent: null));
		}
		catch (InvalidOperationException)
		{
			fullPath = Path.GetFullPath(Path.Combine(layout.VaultRoot, $"{entityId}.md"));
		}

		var decision = decisionService.Decide(model, entityId, title, [], knownIds, fileExists: false, boundaryBegun: true);
		return new VaultSyncCandidate(
			fullPath,
			Path.GetRelativePath(layout.VaultRoot, fullPath),
			model,
			entityId,
			title,
			entity,
			[],
			ComputeHash(ExtractBody(string.Empty)),
			DateTime.UtcNow,
			FileExists: false,
			decision.Action,
			decision.Reason,
			decision.Concern,
			Vanished: true);
	}

	/// <summary>
	/// Groups the candidates of an identity-driven scan by the identity they assert and returns those asserted by more
	/// than one file — an ambiguous duplicate the core will not silently resolve. Only identity-driven entities that
	/// exist on disk with a resolved id participate; path-bound modes cannot have two files for one entity, and an
	/// identity-less file is not an assertion. Used by the sweep, which already has every candidate in hand.
	/// </summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> FindDuplicateIdentities(IEnumerable<VaultSyncCandidate> candidates)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		return candidates
			.Where(IsIdentityAssertion)
			.GroupBy(candidate => candidate.PathId!, StringComparer.OrdinalIgnoreCase)
			.Select(group => (Id: group.Key, Files: DistinctPaths(group.Select(candidate => candidate.AbsolutePath))))
			.Where(entry => entry.Files.Count > 1)
			.ToDictionary(entry => entry.Id, entry => entry.Files, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Finds every file across a model's full territory that asserts a given identity, resolving each the same way the
	/// scan does. Used by a live single-path reconcile to detect (and, once resolved, clear) a duplicate identity
	/// without a whole-vault sweep.
	/// </summary>
	public async Task<IReadOnlyList<string>> FindFilesAssertingIdentityAsync(VaultPathSyncModel model, string id, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		var matches = new List<string>();
		foreach (var path in policyEngine.EnumerateCandidateMarkdownPaths(model))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (await AssertsIdentityAsync(path, id, cancellationToken))
			{
				matches.Add(Path.GetFullPath(path));
			}
		}

		return DistinctPaths(matches);
	}

	/// <summary>Whether a candidate is a live identity assertion: an identity-driven entity that exists with a resolved id.</summary>
	private bool IsIdentityAssertion(VaultSyncCandidate candidate)
		=> candidate.FileExists
			&& !string.IsNullOrWhiteSpace(candidate.PathId)
			&& policyEngine.PolicyFor(candidate.Model.Mode).IsIdentityDriven;

	private async Task<bool> AssertsIdentityAsync(string markdownPath, string id, CancellationToken cancellationToken)
	{
		if (!File.Exists(markdownPath))
		{
			return false;
		}

		var looseId = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(markdownPath).Id;
		if (!string.IsNullOrWhiteSpace(looseId) && string.Equals(looseId, id, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		var markdown = await VaultFileAccess.ReadAllTextAsync(markdownPath, cancellationToken);
		var frontMatter = markdownSerializer.ParseFrontMatter(markdown);
		return frontMatter.TryGetValue("puck", out var rawPuck)
			&& string.Equals(rawPuck?.Trim().Trim('"'), id, StringComparison.OrdinalIgnoreCase);
	}

	private static IReadOnlyList<string> DistinctPaths(IEnumerable<string> paths)
		=> paths
			.Select(Path.GetFullPath)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToList();

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
	/// Reads a note exactly as the watcher reads it — the same kind selection, the same identity source for that kind, the
	/// same belonging and path authorities — without recording anything. Whoever needs to know what a note is (note
	/// resolution) asks here, so it can never read a note differently than the watcher syncs it.
	/// </summary>
	/// <param name="path">The note's path.</param>
	/// <param name="cancellationToken">A token used to cancel the read.</param>
	/// <returns>The note as the watcher sees it, or <see langword="null"/> when the watcher does not manage it.</returns>
	/// <exception cref="VaultFileAccessException">The note is held open by another process.</exception>
	public async Task<VaultSyncCandidate?> ReadNoteAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		return await InspectPathCoreAsync(path, await LoadKnownIdsAsync(cancellationToken), cancellationToken);
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
		var markdown = fileExists
			? await VaultFileAccess.ReadAllTextAsync(fullPath, cancellationToken)
			: string.Empty;
		if (forcedModel is null && fileExists)
		{
			model = await SelectModelAsync(model, markdown, cancellationToken);
		}

		var modePolicy = policyEngine.PolicyFor(model.Mode);
		if (enforceModelBelongingPolicy
			&& !await policyEngine.BelongsToModelAsync(model, fullPath, markdown, cancellationToken))
		{
			return null;
		}

		// Storage-aware, symmetric with how the file is written: only Index storage carries a filename identity token;
		// a Quiet filename is a whole title, so a legitimately dashed title ("Q1 - Ship it") is not mis-read as a phantom
		// "{prefix} - {title}" identity that the mode would then reject as an unrecognised PUCK assertion.
		var (pathId, pathTitle) = pathComposer.ReadFilenameIdentity(model.EntityType, fullPath);
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
		ApplyPathAuthorities(parsedModel, fullPath, knownIdsByType);
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

		if (modePolicy.IsIdentityDriven)
		{
			// An identity-driven note may assert its identity anywhere within the territory its kind's declarations allow;
			// outside it, the assertion is flagged rather than adopted.
			var assertionViolation = pathPolicy.TryGetAssertionViolation(model.EntityType, fullPath);
			if (assertionViolation is not null)
			{
				issues.Add(new MarkdownValidationIssue(
					"path",
					assertionViolation,
					Path.GetRelativePath(layout.VaultRoot, fullPath)));
			}

			// A note asserting another kind's identity is that kind's note, never this model's.
			if (!string.IsNullOrWhiteSpace(pathId) && !knownIds.Contains(pathId))
			{
				var resolved = await puckEntityResolutionService.ResolveAsync(pathId, cancellationToken);
				if (resolved.Exists && !entityModelCatalog.IsFamilyMember(model.EntityType, resolved.EntityType))
				{
					return null;
				}
			}
		}

		await ApplyDomainValidationsAsync(parsedModel, issues, cancellationToken);

		if (string.IsNullOrWhiteSpace(pathId) && puckCreationService.RequiresCallerInputFor(instantiationType))
		{
			issues.Add(new MarkdownValidationIssue("id", MarkdownMessages.MissingRequiredPuckInput, Code: MarkdownValidationCodes.MissingRequiredPuckInput));
		}

		// A missing quiet (title-only) note names no entity, and a boundary records no path to recover one from: its
		// deletion is found by identity instead (FindVanishedNoteCandidatesAsync), not by where it used to be.
		var boundaryBegun = true;
		if (modePolicy.BeginsSyncBoundaryOnFirstFile && !string.IsNullOrWhiteSpace(pathId))
		{
			boundaryBegun = await implicitBoundaryService.HasBoundaryBegunAsync(model.EntityName, pathId, cancellationToken);
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
					MarkdownMessages.ExecutiveOrderSprintUnresolved,
					order.OnrushSprintId));
			}

			return;
		}

		// Lore is not a special case: its identity, parenting and shape come from the declared telescopic PUCK
		// (Era{?}/Cha{?}/Act{?}/p{?}) + SelfNamedDirectory storage policy, and its beginning is an ordinary
		// [MarkdownField] the standard sync owns. The hierarchical beginning invariants are a *domain* rule enforced by
		// LorePageApiService (D17) on the write path — the passive watcher must not re-enforce them here by cross-reading
		// database state and rewriting user files, which produced an unresolvable rewrite-churn.
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
		// A Quiet or freeform file keeps its PUCK in frontmatter, not in a title-named path, so prefer it; fall back
		// to the loose path identity for index-stored files that carry the PUCK in the filename.
		=> ReadFrontMatterIdentity(markdown) ?? pathId;

	/// <summary>
	/// Reads the identity a note's frontmatter asserts (its <c>puck</c>), or <see langword="null"/> when it asserts none.
	/// </summary>
	/// <param name="markdown">The note's raw markdown.</param>
	private string? ReadFrontMatterIdentity(string markdown)
	{
		if (string.IsNullOrWhiteSpace(markdown)
			|| !markdownSerializer.ParseFrontMatter(markdown).TryGetValue("puck", out var rawPuck)
			|| string.IsNullOrWhiteSpace(rawPuck))
		{
			return null;
		}

		var puck = rawPuck.Trim().Trim('"');
		return string.IsNullOrWhiteSpace(puck) ? null : puck;
	}

	/// <summary>
	/// Settles which kind an existing note is. Its path proposes a kind (<see cref="VaultStoragePolicyEngine.TryResolveWatchPath"/>);
	/// where the proposed kind is identity-driven, what the path shape suggests is only a proposal and the identity the
	/// note asserts decides between the identity-driven kinds: the one whose declared notation could mint it
	/// (<see cref="VaultFamilyInstantiationResolver.Mints(VaultPathSyncModel, string?)"/>), or — when more than one could —
	/// the one the vault stores it as. A path-bound proposal stands, since there the path is the identity; so does the
	/// proposal for a note asserting nothing, or an identity no identity-driven kind could mint.
	/// </summary>
	/// <remarks>
	/// Without this, a note was handed to whichever kind's path shape claimed it first and dropped when its identity said
	/// otherwise: a directive's main note that does not bear its folder's name, inside another directive, reads as an
	/// objective by shape, so its edits never reached the directive; and an objective's note outside the shape the
	/// objective model predicts was offered only to the directive model, and ignored.
	/// </remarks>
	/// <param name="proposed">The kind the note's path proposes.</param>
	/// <param name="markdown">The note's raw markdown.</param>
	/// <param name="cancellationToken">A token used to cancel the lookup.</param>
	/// <returns>The kind the note is.</returns>
	private async Task<VaultPathSyncModel> SelectModelAsync(VaultPathSyncModel proposed, string markdown, CancellationToken cancellationToken)
	{
		if (!policyEngine.PolicyFor(proposed.Mode).IsIdentityDriven
			|| ReadFrontMatterIdentity(markdown) is not { } asserted)
		{
			return proposed;
		}

		var claimants = pathSyncModelCatalog.GetModels()
			.Where(model => policyEngine.PolicyFor(model.Mode).IsIdentityDriven && familyInstantiationResolver.Mints(model, asserted))
			.ToList();
		if (claimants.Count <= 1)
		{
			return claimants.SingleOrDefault() ?? proposed;
		}

		var stored = await puckEntityResolutionService.ResolveAsync(asserted, cancellationToken);
		return stored.Exists
			? claimants.FirstOrDefault(model => entityModelCatalog.IsFamilyMember(model.EntityType, stored.EntityType)) ?? proposed
			: proposed;
	}

	/// <summary>
	/// Applies authoritative path-derived relationships and identity fields over frontmatter values when conflicts occur.
	/// </summary>
	/// <remarks>
	/// Where an entity's storage declares containment, the path is the authority for its parent: the nearest containing
	/// folder of the declared parent kind whose identity the vault holds (<see cref="VaultWatcherPathPolicy.EnumerateContainingParentIds"/>),
	/// or none — which, for a note outside every such folder, makes the entity top-level or standalone. This corrects the
	/// composer's naive reading, which takes the nearest folder whether or not its identity exists. The storage service's
	/// reparent check reads a note's parent the same way, so the watcher and the write path always agree on it.
	/// </remarks>
	/// <param name="model">The hydrated model to normalize.</param>
	/// <param name="path">The candidate source path.</param>
	/// <param name="knownIdsByType">The identities the vault holds, per path-sync model type.</param>
	private void ApplyPathAuthorities(object model, string path, IReadOnlyDictionary<Type, HashSet<string>> knownIdsByType)
	{
		if (model is LorePage lorePage)
		{
			lorePage.RelativePath = Path.GetRelativePath(layout.VaultRoot, path);
			return;
		}

		if (!entityModelCatalog.TryGet(model.GetType(), out var declared)
			|| declared?.Storage is not { ParentIdProperty: { } parentIdProperty, ParentEntityType: { } parentType })
		{
			return;
		}

		var property = model.GetType().GetProperty(parentIdProperty, BindingFlags.Public | BindingFlags.Instance);
		if (property is null || !property.CanWrite)
		{
			return;
		}

		var knownParentIds = knownIdsByType.TryGetValue(parentType, out var ids) ? ids : null;
		var ownId = (model as IPuckNamedEntity)?.Id;
		property.SetValue(model, pathPolicy.EnumerateContainingParentIds(model.GetType(), path)
			.FirstOrDefault(id => (knownParentIds is null || knownParentIds.Contains(id))
				&& !string.Equals(id, ownId, StringComparison.OrdinalIgnoreCase)));
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
