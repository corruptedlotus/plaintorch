using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Changes;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Policy;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Executes watcher discovery decisions by reconciling markdown candidates with database state and canonical markdown metadata.
/// Watcher rewrites may normalize frontmatter, file name, and path, but must preserve markdown body content.
/// </summary>
public sealed class VaultWatcherSyncService(
	PlainfraContext context,
	PuckCreationService puckCreationService,
	PlaintorchMarkdownStorageService markdownStorageService,
	VaultTemporalDataService temporalDataService,
	VaultAuditLogService auditLogService,
	VaultImplicitBoundaryService implicitBoundaryService,
	VaultWatcherWriteBarrier writeBarrier,
	VaultEntityGateway entityGateway,
	VaultStoragePolicyEngine policyEngine,
	ILogger<VaultWatcherSyncService> logger)
{
	/// <summary>
	/// Executes the suggested reconciliation action for a discovered candidate.
	/// </summary>
	/// <param name="candidate">The discovered candidate to reconcile.</param>
	/// <param name="origin">The source initiating the reconciliation.</param>
	/// <param name="cancellationToken">A token used to cancel reconciliation.</param>
	/// <remarks>
	/// A candidate that throws leaves nothing staged on the scope's context, so a failure is contained to its own
	/// candidate even when many are reconciled in one scope. A file-driven delete that other rows still block throws
	/// <see cref="VaultEntityDeleteBlockedException"/> having written nothing.
	/// </remarks>
	public async Task ExecuteAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);

		// Reconciling a file makes the vault the authority on the entity it describes, so a client must
		// apply what comes out of this even over an edit it has in flight.
		using var critical = PlaintorchChangeOrigin.Critical();

		try
		{
			await ExecuteCoreAsync(candidate, origin, cancellationToken);
		}
		catch
		{
			// A candidate that fails leaves nothing staged behind. The startup sweep reconciles every candidate in one
			// scope, so a change the database refused would otherwise stay tracked and be re-flushed — and fail again —
			// by the next candidate's save, failing the rest of the sweep with it.
			context.ChangeTracker.Clear();
			throw;
		}
	}

	private async Task ExecuteCoreAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		switch (candidate.SuggestedAction)
		{
			case VaultSyncAction.CreateFromFile:
				await CreateFromFileAsync(candidate, origin, cancellationToken);
				break;
			case VaultSyncAction.UpdateFromFile:
				await UpdateFromFileAsync(candidate, origin, cancellationToken);
				break;
				case VaultSyncAction.DeleteFromDatabase:
					await DeleteFromDatabaseAsync(candidate, origin, cancellationToken);
					break;
			case VaultSyncAction.RewriteFromDatabase:
				await RewriteFromDatabaseAsync(candidate, origin, cancellationToken);
				break;
			case VaultSyncAction.PurgeFile:
				await PurgeFileAsync(candidate, origin, cancellationToken);
				break;
			case VaultSyncAction.Ignore:
				await auditLogService.WriteAsync(
					"sync",
					"watcher-ignore",
					subjectType: candidate.Model.EntityName,
					subjectId: candidate.PathId,
					subjectTitle: candidate.PathTitle,
					details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason },
					cancellationToken: cancellationToken);
				break;
			case VaultSyncAction.Conflict:
				await auditLogService.WriteAsync(
					"sync",
					"watcher-conflict",
					subjectType: candidate.Model.EntityName,
					subjectId: candidate.PathId,
					subjectTitle: candidate.PathTitle,
					details: new
					{
						origin,
						candidate.VaultRelativePath,
						candidate.SuggestedReason,
						issues = candidate.Issues.Select(issue => new { issue.FieldPath, issue.Message, issue.RawValue }).ToArray(),
					},
					cancellationToken: cancellationToken);
				break;
		}
	}

	/// <summary>
	/// Initializes an entity from a validated file candidate using the same creation logic as watcher create-from-file.
	/// </summary>
	public async Task InitializeFromFileAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(candidate);
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);

		using var critical = PlaintorchChangeOrigin.Critical();

		if (!candidate.IsValid)
		{
			throw new InvalidOperationException($"Initialization rejected because candidate '{candidate.VaultRelativePath}' has validation issues.");
		}

		if (!string.IsNullOrWhiteSpace(candidate.PathId))
		{
			var existing = await LoadExistingAsync(candidate.Model.EntityType, candidate.PathId, cancellationToken);
			if (existing is not null)
			{
				throw new InvalidOperationException($"Initialization rejected because entity '{candidate.PathId}' already exists.");
			}
		}

		await CreateFromFileAsync(candidate, origin, cancellationToken);
	}

	/// <summary>
	/// Creates a new entity from file-derived data and persists canonical markdown.
	/// </summary>
	/// <param name="candidate">The candidate used to create the entity.</param>
	/// <param name="origin">The reconciliation source.</param>
	/// <param name="cancellationToken">A token used to cancel processing.</param>
	private async Task CreateFromFileAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		var model = candidate.ParsedModel;
		if (model is LorePage lorePage && !string.IsNullOrWhiteSpace(lorePage.ParentId))
		{
			var parentExists = await context.LorePages.AnyAsync(item => item.Id == lorePage.ParentId, cancellationToken);
			if (!parentExists)
			{
				lorePage.ParentId = null;
			}
		}

		if (model is Objective objective)
		{
			await NormalizeObjectiveForeignKeysAsync(objective, candidate.VaultRelativePath, cancellationToken);
		}

		if (model is Fate or Decree)
		{
			await NormalizeIncentiveDirectiveAsync((Incentive)model, candidate.VaultRelativePath, cancellationToken);
		}

		if (model is not IPuckNamedEntity namedEntity)
		{
			throw new InvalidOperationException($"Watcher create-from-file requires a PUCK-named model, but '{candidate.Model.EntityName}' is not PUCK-backed.");
		}

		// Identity is declared per concrete type (a polymorphic family's siblings may carry different PUCK
		// declarations), so id creation keys on the actual composed type — a lunar directive discovery materialized
		// mints a LUNA… id, not the family's stellar fallback — rather than the model's anchor or default type.
		var composedType = namedEntity.GetType();
		var ignoredPathId = default(string);
		if (!puckCreationService.RequiresCallerInputFor(composedType))
		{
			ignoredPathId = string.IsNullOrWhiteSpace(candidate.PathId) ? null : candidate.PathId;
			namedEntity.Id = puckCreationService.CreateIdFor(composedType);
		}
		else if (string.IsNullOrWhiteSpace(namedEntity.Id))
		{
			namedEntity.Id = puckCreationService.CreateIdFor(composedType);
		}

		context.Add(model);
		await context.SaveChangesAsync(cancellationToken);
		await TryBeginImplicitBoundaryAsync(candidate, model, cancellationToken);
		var shouldRewriteCanonical = string.IsNullOrWhiteSpace(candidate.PathId)
			|| !candidate.IsValid
			|| RequiresCanonicalScaffold(candidate);
		if (shouldRewriteCanonical)
		{
			await SaveCanonicalMarkdownAsync(model, sourcePath: candidate.AbsolutePath, cancellationToken: cancellationToken);
		}

		if (ignoredPathId is null && shouldRewriteCanonical)
		{
			logger.LogInformation(
				"Watcher created {EntityType} '{EntityId}' from '{Path}' and rewrote canonical markdown.",
				candidate.Model.EntityName,
				namedEntity.Id,
				candidate.VaultRelativePath);
		}
		else if (ignoredPathId is null)
		{
			logger.LogInformation(
				"Watcher created {EntityType} '{EntityId}' from '{Path}' without canonical rewrite because the file already matches authority.",
				candidate.Model.EntityName,
				namedEntity.Id,
				candidate.VaultRelativePath);
		}
		else if (shouldRewriteCanonical)
		{
			logger.LogInformation(
				"Watcher created {EntityType} '{EntityId}' from '{Path}' and rewrote canonical markdown, ignoring supplied path id '{IgnoredPathId}'.",
				candidate.Model.EntityName,
				namedEntity.Id,
				candidate.VaultRelativePath,
				ignoredPathId);
		}
		else
		{
			logger.LogInformation(
				"Watcher created {EntityType} '{EntityId}' from '{Path}' without canonical rewrite, ignoring supplied path id '{IgnoredPathId}'.",
				candidate.Model.EntityName,
				namedEntity.Id,
				candidate.VaultRelativePath,
				ignoredPathId);
		}

		await auditLogService.WriteAsync(
			"sync",
			"create-from-file",
			subject: model,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason, ignoredPathId, canonicalRewriteApplied = shouldRewriteCanonical },
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Updates an existing entity from file-derived data and rewrites canonical markdown when metadata changed.
	/// </summary>
	/// <param name="candidate">The candidate used to update the entity.</param>
	/// <param name="origin">The reconciliation source.</param>
	/// <param name="cancellationToken">A token used to cancel processing.</param>
	private async Task UpdateFromFileAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(candidate.PathId))
		{
			await CreateFromFileAsync(candidate, origin, cancellationToken);
			return;
		}

		var existing = await LoadExistingAsync(candidate.Model.EntityType, candidate.PathId, cancellationToken);
		if (existing is null)
		{
			await CreateFromFileAsync(candidate, origin, cancellationToken);
			return;
		}

		await TryBeginImplicitBoundaryAsync(candidate, existing, cancellationToken);
		var previous = CloneEntity(existing);
		var existingEntry = context.Entry(existing);
		existingEntry.CurrentValues.SetValues(candidate.ParsedModel);
		if (existing is LorePage lorePage && !string.IsNullOrWhiteSpace(lorePage.ParentId))
		{
			var parentExists = await context.LorePages.AnyAsync(item => item.Id == lorePage.ParentId, cancellationToken);
			if (!parentExists)
			{
				lorePage.ParentId = null;
			}
		}

		if (existing is Objective objective)
		{
			// Due is an owned type (PEP111); SetValues copies only scalar properties and never descends into an
			// owned reference, so the parsed frontmatter due would be dropped. Copy it across explicitly.
			objective.Due = (candidate.ParsedModel as Objective)?.Due;
			await NormalizeObjectiveForeignKeysAsync(objective, candidate.VaultRelativePath, cancellationToken);
		}

		if (existing is Incentive existingIncentive)
		{
			// Parenting is not markdown-mapped, so a frontmatter sync must never clear it.
			existingIncentive.ParentIncentiveId = existingEntry.OriginalValues.GetValue<string?>(nameof(Incentive.ParentIncentiveId));
		}

		if (existing is OnrushSprint existingSprint)
		{
			// The milestone binding and the graph layout are database-only state (PEP102), never written to the
			// sprint's frontmatter, so a frontmatter sync must never clear them the way SetValues otherwise does —
			// the same reasoning as parenting above. Losing the milestone id detaches a sprint from its milestone
			// (it then reads as an ordinary checkpoint); losing the layout discards the saved canvas arrangement.
			existingSprint.MilestoneCheckpointId = existingEntry.OriginalValues.GetValue<string?>(nameof(OnrushSprint.MilestoneCheckpointId));
			existingSprint.GraphLayout = existingEntry.OriginalValues.GetValue<string?>(nameof(OnrushSprint.GraphLayout));
		}

		if (existing is Directive existingDirective)
		{
			// A directive's availability timeframe is database-only (PEP100 patch 2) — a timeframe id is a row id, not
			// a PUCK, so it never reaches frontmatter — and a frontmatter sync must not clear it, for the same reason
			// as parenting and the sprint fields above.
			existingDirective.AvailabilityTimeframeId = existingEntry.OriginalValues.GetValue<long?>(nameof(Directive.AvailabilityTimeframeId));
		}

		if (existing is Fate or Decree)
		{
			await NormalizeIncentiveDirectiveAsync((Incentive)existing, candidate.VaultRelativePath, cancellationToken);
			await ResetOrbitStateOnChangeAsync(existingEntry, ((Incentive)existing).Id, cancellationToken);
		}

		if (existing is IPuckNamedEntity namedEntity && !string.IsNullOrWhiteSpace(candidate.PathTitle))
		{
			namedEntity.Title = candidate.PathTitle;
		}

		var requiresScaffold = RequiresCanonicalScaffold(candidate);
		if (!HasSyncChanges(existingEntry))
		{
			if (requiresScaffold)
			{
				await SaveCanonicalMarkdownAsync(existing, previous, candidate.AbsolutePath, cancellationToken);

				logger.LogInformation(
					"Watcher normalized canonical frontmatter for {EntityType} '{EntityId}' at '{Path}' despite no metadata changes.",
					candidate.Model.EntityName,
					candidate.PathId,
					candidate.VaultRelativePath);

				await auditLogService.WriteAsync(
					"sync",
					"update-rewrite-scaffold",
					subjectType: candidate.Model.EntityName,
					subjectId: candidate.PathId,
					subjectTitle: candidate.PathTitle,
					details: new
					{
						origin,
						candidate.VaultRelativePath,
						reason = "No metadata changes detected, but canonical file/frontmatter scaffold was missing.",
					},
					cancellationToken: cancellationToken);
				return;
			}

			logger.LogDebug(
				"Watcher detected no metadata sync changes for {EntityType} '{EntityId}' from '{Path}'. Skipping rewrite.",
				candidate.Model.EntityName,
				candidate.PathId,
				candidate.VaultRelativePath);

			await auditLogService.WriteAsync(
				"sync",
				"update-skipped-noop",
				subjectType: candidate.Model.EntityName,
				subjectId: candidate.PathId,
				subjectTitle: candidate.PathTitle,
				details: new { origin, candidate.VaultRelativePath, reason = "No metadata changes detected." },
				cancellationToken: cancellationToken);

			return;
		}

		await context.SaveChangesAsync(cancellationToken);

		var shouldRewriteCanonical = !candidate.IsValid || requiresScaffold;
		if (shouldRewriteCanonical)
		{
			await SaveCanonicalMarkdownAsync(existing, previous, candidate.AbsolutePath, cancellationToken);
		}

		if (shouldRewriteCanonical)
		{
			logger.LogInformation(
				"Watcher updated {EntityType} '{EntityId}' from '{Path}' and rewrote canonical markdown.",
				candidate.Model.EntityName,
				candidate.PathId,
				candidate.VaultRelativePath);
		}
		else
		{
			logger.LogInformation(
				"Watcher updated {EntityType} '{EntityId}' from '{Path}' without canonical rewrite because the file is authoritative.",
				candidate.Model.EntityName,
				candidate.PathId,
				candidate.VaultRelativePath);
		}

		await auditLogService.WriteAsync(
			"sync",
			"update-from-file",
			subject: existing,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason, canonicalRewriteApplied = shouldRewriteCanonical },
			cancellationToken: cancellationToken);
	}

	private static bool RequiresCanonicalScaffold(VaultSyncCandidate candidate)
	{
		if (!File.Exists(candidate.AbsolutePath))
		{
			return true;
		}

		return !HasFrontMatter(candidate.AbsolutePath);
	}

	private async Task NormalizeObjectiveForeignKeysAsync(Objective objective, string vaultRelativePath, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(objective.DirectiveId))
		{
			var directiveExists = await context.Directives
				.AsNoTracking()
				.AnyAsync(item => item.Id == objective.DirectiveId, cancellationToken);
			if (!directiveExists)
			{
				logger.LogWarning(
					"Watcher normalized missing directive relation '{DirectiveId}' on objective '{ObjectiveId}' from '{Path}'.",
					objective.DirectiveId,
					objective.Id,
					vaultRelativePath);
				objective.DirectiveId = null;
			}
		}

		if (!string.IsNullOrWhiteSpace(objective.OnrushSprintId))
		{
			var onrushExists = await context.OnrushSprints
				.AsNoTracking()
				.AnyAsync(item => item.Id == objective.OnrushSprintId, cancellationToken);
			if (!onrushExists)
			{
				logger.LogWarning(
					"Watcher normalized missing onrush sprint relation '{OnrushSprintId}' on objective '{ObjectiveId}' from '{Path}'.",
					objective.OnrushSprintId,
					objective.Id,
					vaultRelativePath);
				objective.OnrushSprintId = null;
			}
		}
	}

	/// <summary>
	/// Normalizes a declarative's directive relation when the referenced directive no longer exists.
	/// </summary>
	private async Task NormalizeIncentiveDirectiveAsync(Incentive incentive, string vaultRelativePath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(incentive.DirectiveId))
		{
			return;
		}

		var directiveExists = await context.Directives
			.AsNoTracking()
			.AnyAsync(item => item.Id == incentive.DirectiveId, cancellationToken);
		if (!directiveExists)
		{
			logger.LogWarning(
				"Watcher normalized missing directive relation '{DirectiveId}' on incentive '{IncentiveId}' from '{Path}'.",
				incentive.DirectiveId,
				incentive.Id,
				vaultRelativePath);
			incentive.DirectiveId = null;
		}
	}

	/// <summary>
	/// Whether a frontmatter sync actually changed the entity: a modified scalar property, or a changed owned
	/// reference it maps (Objective.Due, PEP111) — SetValues never touches an owned reference, so it is applied
	/// separately and the scalar-only modified check would otherwise miss it.
	/// </summary>
	private static bool HasSyncChanges(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
		=> entry.Properties.Any(property => property.IsModified)
			|| entry.References.Any(reference => reference.TargetEntry is { } owned
				&& owned.Metadata.IsOwned()
				&& owned.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

	/// <summary>
	/// Resets the persisted orbit schedule state when a file sync changed a declarative's orbit notation.
	/// A fresh state (anchored at reset time) is lazily rebuilt on the next seeking resolution.
	/// </summary>
	private async Task ResetOrbitStateOnChangeAsync(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string incentiveId, CancellationToken cancellationToken)
	{
		var originalOrbit = entry.OriginalValues.GetValue<string?>("Orbit");
		var currentOrbit = entry.CurrentValues.GetValue<string?>("Orbit");
		if (string.Equals(originalOrbit, currentOrbit, StringComparison.Ordinal))
		{
			return;
		}

		var states = await context.Set<IncentiveOrbitScheduleState>()
			.Where(state => state.IncentiveId == incentiveId)
			.ToListAsync(cancellationToken);
		if (states.Count > 0)
		{
			context.RemoveRange(states);
			logger.LogInformation("Watcher reset orbit schedule state for '{IncentiveId}' after an orbit change.", incentiveId);
		}
	}

	/// <summary>
	/// Records the synchronization boundary for an implicit entity when the watcher confirms its file exists.
	/// </summary>
	private async Task TryBeginImplicitBoundaryAsync(VaultSyncCandidate candidate, object entity, CancellationToken cancellationToken)
	{
		if (!policyEngine.PolicyFor(candidate.Model.Mode).BeginsSyncBoundaryOnFirstFile
			|| !candidate.FileExists
			|| entity is not IPuckNamedEntity namedEntity
			|| string.IsNullOrWhiteSpace(namedEntity.Id))
		{
			return;
		}

		await implicitBoundaryService.EnsureBoundaryBegunAsync(
			candidate.Model.EntityName,
			namedEntity.Id,
			namedEntity.Title,
			cancellationToken);
	}

	private static bool HasFrontMatter(string path)
	{
		using var reader = new StreamReader(path);
		var firstLine = reader.ReadLine();
		if (string.IsNullOrWhiteSpace(firstLine))
		{
			return false;
		}

		if (firstLine.Length > 0 && firstLine[0] == '\uFEFF')
		{
			firstLine = firstLine[1..];
		}

		return string.Equals(firstLine.Trim(), "---", StringComparison.Ordinal);
	}

	/// <summary>
	/// Rewrites canonical markdown from database state for a candidate that should not be trusted as file-authoritative.
	/// </summary>
	/// <param name="candidate">The candidate requesting rewrite.</param>
	/// <param name="origin">The reconciliation source.</param>
	/// <param name="cancellationToken">A token used to cancel processing.</param>
	private async Task RewriteFromDatabaseAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(candidate.PathId))
		{
			await auditLogService.WriteAsync(
				"sync",
				"rewrite-skipped",
				subjectType: candidate.Model.EntityName,
				subjectTitle: candidate.PathTitle,
				details: new { origin, candidate.VaultRelativePath, reason = "No database identifier was available for canonical rewrite." },
				cancellationToken: cancellationToken);
			return;
		}

		var existing = await LoadExistingAsync(candidate.Model.EntityType, candidate.PathId, cancellationToken);
		if (existing is null)
		{
			await auditLogService.WriteAsync(
				"sync",
				"rewrite-skipped",
				subjectType: candidate.Model.EntityName,
				subjectId: candidate.PathId,
				subjectTitle: candidate.PathTitle,
				details: new { origin, candidate.VaultRelativePath, reason = "Database entity no longer exists for canonical rewrite." },
				cancellationToken: cancellationToken);
			return;
		}

		await SaveCanonicalMarkdownAsync(existing, sourcePath: candidate.AbsolutePath, cancellationToken: cancellationToken);

		logger.LogInformation(
			"Watcher rewrote canonical {EntityType} '{EntityId}' markdown over '{Path}'.",
			candidate.Model.EntityName,
			candidate.PathId,
			candidate.VaultRelativePath);

		await auditLogService.WriteAsync(
			"sync",
			"rewrite-from-database",
			subject: existing,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason },
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Purges a disallowed file candidate by archiving it and emitting audit metadata.
	/// </summary>
	/// <param name="candidate">The candidate to purge.</param>
	/// <param name="origin">The reconciliation source.</param>
	/// <param name="cancellationToken">A token used to cancel processing.</param>
	private async Task PurgeFileAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		writeBarrier.Suppress(candidate.AbsolutePath);
		var archived = await temporalDataService.ArchivePathAsync(
			candidate.AbsolutePath,
			"watcher-purge",
			candidate.Model.EntityName,
			candidate.PathId,
			candidate.PathTitle,
			Environment.UserName,
			cancellationToken);

		logger.LogInformation(
			"Watcher purged '{Path}' for {EntityType}.",
			candidate.VaultRelativePath,
			candidate.Model.EntityName);

		await auditLogService.WriteAsync(
			"sync",
			"purge-file",
			subjectType: candidate.Model.EntityName,
			subjectId: candidate.PathId,
			subjectTitle: candidate.PathTitle,
			temporalKind: archived is null ? null : "file",
			temporalEntryKey: archived?.EntryKey,
			temporalEntityType: archived?.EntityType,
			temporalEntityId: archived?.EntityId,
			temporalEntityTitle: archived?.EntityTitle,
			temporalLocation: archived?.ArchivedRelativePath,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason },
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Deletes an existing database entity when policy marks file deletion as authoritative.
	/// </summary>
	/// <param name="candidate">The candidate requesting database deletion.</param>
	/// <param name="origin">The reconciliation source.</param>
	/// <param name="cancellationToken">A token used to cancel processing.</param>
	private async Task DeleteFromDatabaseAsync(VaultSyncCandidate candidate, string origin, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(candidate.PathId))
		{
			await auditLogService.WriteAsync(
				"sync",
				"delete-skipped",
				subjectType: candidate.Model.EntityName,
				subjectTitle: candidate.PathTitle,
				details: new { origin, candidate.VaultRelativePath, reason = "No database identifier was available for deletion." },
				cancellationToken: cancellationToken);
			return;
		}

		var existing = await LoadExistingAsync(candidate.Model.EntityType, candidate.PathId, cancellationToken);
		if (existing is null)
		{
			await auditLogService.WriteAsync(
				"sync",
				"delete-skipped",
				subjectType: candidate.Model.EntityName,
				subjectId: candidate.PathId,
				subjectTitle: candidate.PathTitle,
				details: new { origin, candidate.VaultRelativePath, reason = "Database entity no longer exists." },
				cancellationToken: cancellationToken);
			return;
		}

		// Rows that still reference the entity through a restricting relationship (a directive's children, an incentive's
		// child incentives) make the database refuse the removal — and the API refuses the same deletes. Attempting it
		// anyway failed on every retry, so the blocked delete is reported as such and nothing is written. An incentive's
		// executives do not block: the save-time state rule releases them in the removing save.
		var blockers = await entityGateway.FindDeleteBlockersAsync(existing, cancellationToken);
		if (blockers.Count > 0)
		{
			logger.LogWarning(
				"Watcher did not delete {EntityType} '{EntityId}' after file '{Path}' was removed: it is still referenced by {Blockers}.",
				candidate.Model.EntityName,
				candidate.PathId,
				candidate.VaultRelativePath,
				string.Join(", ", blockers.Select(blocker => $"{blocker.Count} {blocker.DependentEntity}.{blocker.ForeignKeyProperty}")));

			await auditLogService.WriteAsync(
				"sync",
				"delete-blocked",
				subjectType: candidate.Model.EntityName,
				subjectId: candidate.PathId,
				subjectTitle: candidate.PathTitle,
				details: new { origin, candidate.VaultRelativePath, blockers },
				cancellationToken: cancellationToken);

			throw new VaultEntityDeleteBlockedException(
				candidate.Model.EntityName,
				candidate.PathId,
				(existing as IPuckNamedEntity)?.Title ?? candidate.PathTitle,
				blockers);
		}

		// Staged, not saved: the graveyard entry commits in the same save that removes the entity, so a removal the
		// database still refuses leaves no entry behind.
		var graveyardEntry = temporalDataService.StageEntityArchive(existing, "watcher-file-delete", Environment.UserName);
		context.Remove(existing);
		await context.SaveChangesAsync(cancellationToken);

		logger.LogInformation(
			"Watcher deleted {EntityType} '{EntityId}' because file '{Path}' was removed.",
			candidate.Model.EntityName,
			candidate.PathId,
			candidate.VaultRelativePath);

		await auditLogService.WriteAsync(
			"sync",
			"delete-from-database",
			subjectType: candidate.Model.EntityName,
			subjectId: candidate.PathId,
			subjectTitle: candidate.PathTitle,
			temporalKind: "database",
			temporalEntryKey: graveyardEntry.EntryKey,
			temporalEntityType: graveyardEntry.EntityType,
			temporalEntityId: graveyardEntry.EntityId,
			temporalEntityTitle: graveyardEntry.EntityTitle,
			details: new { origin, candidate.VaultRelativePath, candidate.SuggestedReason },
			cancellationToken: cancellationToken);
	}

	/// <summary>
	/// Loads an existing entity instance by type and identifier.
	/// </summary>
	/// <param name="entityType">The entity CLR type to query.</param>
	/// <param name="id">The identifier of the entity.</param>
	/// <param name="cancellationToken">A token used to cancel lookup.</param>
	/// <returns>The existing entity instance, or <see langword="null"/> when not found.</returns>
	private Task<object?> LoadExistingAsync(Type entityType, string id, CancellationToken cancellationToken)
	{
		return entityGateway.FindByIdAsync(entityType, id, track: true, cancellationToken);
	}

	/// <summary>
	/// Saves canonical markdown for a supported entity type.
	/// </summary>
	/// <param name="entity">The entity whose markdown should be saved.</param>
	/// <param name="previous">An optional previous snapshot used for rename/delete decisions.</param>
	/// <param name="sourcePath">An optional source file path that triggered reconciliation.</param>
	/// <param name="cancellationToken">A token used to cancel save operations.</param>
	private async Task SaveCanonicalMarkdownAsync(object entity, object? previous = null, string? sourcePath = null, CancellationToken cancellationToken = default)
	{
		switch (entity)
		{
			case Directive directive:
				await markdownStorageService.SaveDirectiveAsync(directive, previous as Directive, sourcePath, cancellationToken);
				break;
			case Objective objective:
				await markdownStorageService.SaveObjectiveAsync(objective, previous as Objective, sourcePath, cancellationToken: cancellationToken);
				break;
			case Fate fate:
				await markdownStorageService.SaveFateAsync(fate, previous as Fate, sourcePath, cancellationToken: cancellationToken);
				break;
			case Decree decree:
				await markdownStorageService.SaveDecreeAsync(decree, previous as Decree, sourcePath, cancellationToken: cancellationToken);
				break;
			case OnrushSprint sprint:
				await markdownStorageService.SaveOnrushSprintAsync(sprint, previous as OnrushSprint, sourcePath, cancellationToken);
				break;
			case ExecutiveOrder order:
				await markdownStorageService.SaveExecutiveOrderAsync(order, previous as ExecutiveOrder, sourcePath, cancellationToken);
				break;
			case PolarisCycle cycle:
				await markdownStorageService.SavePolarisCycleAsync(cycle, previous as PolarisCycle, sourcePath: sourcePath, cancellationToken: cancellationToken);
				break;
			case LorePage lorePage:
				await markdownStorageService.SaveLorePageAsync(lorePage, previous as LorePage, sourcePath: sourcePath, cancellationToken: cancellationToken);
				break;
			default:
				throw new InvalidOperationException($"Watcher synchronization does not support markdown save for entity type '{entity.GetType().Name}'.");
		}
	}

	/// <summary>
	/// Creates an isolated clone snapshot used for diff-aware canonical rewrite workflows.
	/// </summary>
	/// <param name="entity">The entity instance to clone.</param>
	/// <returns>A detached clone with relevant persisted fields copied.</returns>
	private object CloneEntity(object entity)
	{
		return entityGateway.CloneScalars(entity);
	}
}
