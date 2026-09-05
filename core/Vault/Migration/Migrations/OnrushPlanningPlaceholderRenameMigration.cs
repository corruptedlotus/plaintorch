using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Renames the in-planning onrush placeholder from the bare sentinel <c>0</c> to the gate-passing PUCK <c>x0000</c>.
/// </summary>
/// <remarks>
/// Advances the vault from v2 to v3. The planning placeholder used to carry the literal id <c>0</c>, which does not
/// tokenize against the onrush grammar <c>x{I:4:100}</c>. Under the identity-gated watcher that non-mintable id is a
/// "sudden exception in the system": the gate strips it and the enforced sync boundary purges its self-named directory.
/// The placeholder is now minted as <c>x0000</c> (<see cref="OnrushSprint.PlanningPlaceholderId"/>), a legitimate onrush
/// PUCK. This migration carries any existing placeholder across: the sprint row, the foreign keys of the objectives and
/// checkpoints it tracks (including its milestone), the PUCK registry entry it resolved through, and its self-named
/// directory on disk — reusing the same storage machinery an onrush identity change already relies on. Executive orders
/// cannot be issued against the placeholder, so none reference it.
/// </remarks>
public sealed class OnrushPlanningPlaceholderRenameMigration(
	PlainfraContext context,
	PlaintorchMarkdownStorageService storageService,
	VaultAuditLogService auditLogService,
	ILogger<OnrushPlanningPlaceholderRenameMigration> logger) : IVaultMigration
{
	/// <summary>
	/// The pre-migration identity of the in-planning placeholder sprint: the bare numeral the onrush grammar rejects.
	/// </summary>
	private const string LegacyPlanningId = "0";

	/// <inheritdoc />
	public int FromVersion => 2;

	/// <inheritdoc />
	public int ToVersion => 3;

	/// <inheritdoc />
	public string Id => "20260905_0001_OnrushPlanningPlaceholderRename";

	/// <inheritdoc />
	public string Description => "Rename the in-planning onrush placeholder from the bare sentinel '0' to the gate-passing PUCK 'x0000'.";

	/// <inheritdoc />
	public async Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default)
	{
		var placeholder = await context.OnrushSprints
			.Include(sprint => sprint.Objectives)
			.Include(sprint => sprint.Checkpoints)
			.Include(sprint => sprint.MilestoneCheckpoint)
			.FirstOrDefaultAsync(sprint => sprint.Id == LegacyPlanningId, cancellationToken);
		if (placeholder is null)
		{
			// No legacy placeholder to carry across — a vault that never planned, or one already minting x0000.
			return new VaultMigrationOutcome(0, 0, 0, 0, 0);
		}

		// The file move is driven from a snapshot of the pre-rename identity, so the storage service can resolve the
		// old self-named directory ('0 - {Title}') by composition after the row itself has been renamed.
		var previous = new OnrushSprint
		{
			Id = placeholder.Id,
			Title = placeholder.Title,
			StartDate = placeholder.StartDate,
			EndDate = placeholder.EndDate,
		};

		// Re-mint the placeholder under the gate-passing identity, preserving its planning state (dates stay as-is; it
		// remains the planning sprint, it does not become a begun one). This mirrors the placeholder-transition an
		// onrush begin performs: insert the new identity, re-point the tracked children's foreign keys, hand off the
		// milestone across the reference cycle, then remove the old row — all in one save.
		var renamed = new OnrushSprint
		{
			Id = OnrushSprint.PlanningPlaceholderId,
			Title = placeholder.Title,
			StartDate = placeholder.StartDate,
			EndDate = placeholder.EndDate,
			GraphLayout = placeholder.GraphLayout,
		};
		context.OnrushSprints.Add(renamed);

		foreach (var objective in placeholder.Objectives)
		{
			objective.OnrushSprintId = renamed.Id;
		}

		var trackedCheckpoints = await context.Checkpoints
			.Where(checkpoint => checkpoint.OnrushSprintId == LegacyPlanningId)
			.ToListAsync(cancellationToken);
		foreach (var checkpoint in trackedCheckpoints)
		{
			checkpoint.OnrushSprintId = renamed.Id;
		}

		if (!string.IsNullOrWhiteSpace(placeholder.MilestoneCheckpointId))
		{
			renamed.MilestoneCheckpointId = placeholder.MilestoneCheckpointId;
			// Detach the milestone from the old row first: its row is about to be removed, and the reciprocal foreign
			// key would otherwise still name it and stall EF's command ordering across the cycle.
			placeholder.MilestoneCheckpointId = null;
		}

		context.OnrushSprints.Remove(placeholder);
		await context.SaveChangesAsync(cancellationToken);

		// Carry the PUCK registry entry across if the placeholder was registered. Index storage resolves the
		// non-tokenizable legacy id only through this row, so it must follow the rename; a placeholder that was never
		// registered simply has none.
		var registryEntry = await context.PuckRegistryEntries
			.FirstOrDefaultAsync(entry => entry.Id == LegacyPlanningId, cancellationToken);
		if (registryEntry is not null)
		{
			context.PuckRegistryEntries.Remove(registryEntry);
			context.PuckRegistryEntries.Add(new PuckRegistryEntry
			{
				Id = renamed.Id,
				Declaration = registryEntry.Declaration,
				IssuedUtc = registryEntry.IssuedUtc,
			});
			await context.SaveChangesAsync(cancellationToken);
		}

		// Move the self-named directory '0 - {Title}' -> 'x0000 - {Title}' through the same storage path an onrush
		// identity change uses (Directory.Move under watcher suppression), preserving the note body and front matter.
		await storageService.SaveOnrushSprintAsync(renamed, previous, cancellationToken: cancellationToken);

		logger.LogInformation(
			"Vault migration {Migration} renamed the in-planning onrush placeholder '{LegacyId}' to '{NewId}'.",
			Id,
			LegacyPlanningId,
			renamed.Id);

		await auditLogService.WriteAsync(
			"migration",
			"onrush-planning-rename",
			subjectType: nameof(OnrushSprint),
			subjectId: renamed.Id,
			subjectTitle: renamed.Title,
			details: new { migration = Id, FromVersion, ToVersion, previousId = LegacyPlanningId },
			cancellationToken: cancellationToken);

		return new VaultMigrationOutcome(1, 1, 0, 0, 0);
	}
}
