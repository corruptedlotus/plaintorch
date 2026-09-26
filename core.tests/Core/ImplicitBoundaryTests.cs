using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// PEP091 implicit boundary: no file on create, materialize on begin, boundary-authoritative deletion. A boundary is an
/// identity only: a deleted note is found by the identity no note asserts any more, never by a remembered path, and the
/// boundary ends with its entity.
/// </summary>
public sealed class ImplicitBoundaryTests : VaultTestBase
{
	[Fact]
	public async Task Create_does_not_materialize_a_file_but_persists_the_entity()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");

		Assert.Empty(Vault.MarkdownFilesUnder(Vault.Layout.ObjectivesRoot));
		Assert.True(await Vault.QueryAsync(context => context.Objectives.AnyAsync(item => item.Id == objective.Id)));
	}

	[Fact]
	public async Task Begin_materializes_a_quiet_file_with_puck_frontmatter()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");

		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		Assert.True(Vault.VaultFileExists("Objectives/Ship it.md"));
		Assert.Contains($"puck: {objective.Id}", Vault.ReadVaultFile("Objectives/Ship it.md"));
	}

	[Fact]
	public async Task Deleting_a_boundary_begun_file_is_authoritative()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		var path = Vault.AbsolutePath("Objectives/Ship it.md");
		File.Delete(path);

		var candidate = await Vault.ReconcileAsync(path);

		Assert.NotNull(candidate);
		Assert.Equal(VaultSyncAction.DeleteFromDatabase, candidate!.SuggestedAction);
		Assert.False(await Vault.QueryAsync(context => context.Objectives.AnyAsync(item => item.Id == objective.Id)));
	}

	[Fact]
	public async Task Objective_without_a_begun_boundary_survives_a_scan()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");

		await Vault.ScanAsync();

		Assert.True(await Vault.QueryAsync(context => context.Objectives.AnyAsync(item => item.Id == objective.Id)));
	}

	private Task<bool> ObjectiveExistsAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AnyAsync(item => item.Id == id, TestContext.Current.CancellationToken));

	private Task<List<string>> BoundaryTransitionsAsync(string id)
		=> Vault.QueryAsync(context => context.AuditLogEntries
			.Where(entry => entry.SubjectId == id && entry.Category == VaultImplicitBoundaryService.BoundaryCategory)
			.OrderBy(entry => entry.Id)
			.Select(entry => entry.Action)
			.ToListAsync(TestContext.Current.CancellationToken));

	[Fact]
	public async Task A_boundary_records_no_path()
	{
		// An implicit note carries its identity inside the file and may live anywhere, so its boundary is the identity
		// alone. A remembered path went stale as soon as the note or a parent folder moved.
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		var entry = await Vault.QueryAsync(context => context.AuditLogEntries.SingleAsync(
			item => item.SubjectId == objective.Id && item.Action == VaultImplicitBoundaryService.BoundaryBeginAction,
			TestContext.Current.CancellationToken));
		Assert.Null(entry.TemporalLocation);
		Assert.Null(entry.DetailsJson);
	}

	[Fact]
	public async Task Deleting_a_moved_note_deletes_its_objective()
	{
		// The note moves into another folder, then is deleted at runtime. With a recorded path, the delete was read against
		// the old location and the objective lingered; found by identity, it goes.
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		Directory.CreateDirectory(Vault.AbsolutePath("Objectives/Moved"));
		File.Move(Vault.AbsolutePath("Objectives/Ship it.md"), Vault.AbsolutePath("Objectives/Moved/Ship it.md"));
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath("Objectives/Ship it.md"));
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath("Objectives/Moved/Ship it.md"));
		Assert.True(await ObjectiveExistsAsync(objective.Id)); // a move is not a deletion: the identity is still asserted

		File.Delete(Vault.AbsolutePath("Objectives/Moved/Ship it.md"));
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath("Objectives/Moved/Ship it.md"));

		Assert.False(await ObjectiveExistsAsync(objective.Id));
	}

	[Fact]
	public async Task Deleting_a_folder_of_notes_deletes_their_objectives()
	{
		// A folder deletion may arrive as one event for the folder alone; it names no identity, so every begun note that is
		// gone is found by identity.
		var first = await Vault.SeedStandaloneObjectiveAsync("First");
		var second = await Vault.SeedStandaloneObjectiveAsync("Second");
		var kept = await Vault.SeedStandaloneObjectiveAsync("Kept");
		foreach (var objective in new[] { first, second, kept })
		{
			await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		}

		Directory.CreateDirectory(Vault.AbsolutePath("Objectives/Batch"));
		File.Move(Vault.AbsolutePath("Objectives/First.md"), Vault.AbsolutePath("Objectives/Batch/First.md"));
		File.Move(Vault.AbsolutePath("Objectives/Second.md"), Vault.AbsolutePath("Objectives/Batch/Second.md"));
		Directory.Delete(Vault.AbsolutePath("Objectives/Batch"), recursive: true);

		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath("Objectives/Batch"));

		Assert.False(await ObjectiveExistsAsync(first.Id));
		Assert.False(await ObjectiveExistsAsync(second.Id));
		Assert.True(await ObjectiveExistsAsync(kept.Id));
	}

	[Fact]
	public async Task A_note_whose_frontmatter_is_broken_still_asserts_its_identity()
	{
		// A note that exists but no longer parses is not a deleted one: its identity is read line by line, so a typo in its
		// frontmatter never deletes the objective behind it.
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		Vault.WriteVaultFile("Objectives/Ship it.md", $"---{Environment.NewLine}puck: {objective.Id}{Environment.NewLine}status: [unclosed{Environment.NewLine}---{Environment.NewLine}Body.{Environment.NewLine}");

		await Vault.SweepWithIssuesAsync();

		Assert.True(await ObjectiveExistsAsync(objective.Id));
	}

	[Fact]
	public async Task Deleting_an_objective_ends_its_boundary()
	{
		var viaApi = await Vault.SeedStandaloneObjectiveAsync("Through the API");
		var viaNote = await Vault.SeedStandaloneObjectiveAsync("Through its note");
		await Vault.BeginObjectiveBoundaryAsync(viaApi.Id);
		await Vault.BeginObjectiveBoundaryAsync(viaNote.Id);

		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().DeleteAsync(viaApi.Id, TestContext.Current.CancellationToken));
		File.Delete(Vault.AbsolutePath("Objectives/Through its note.md"));
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath("Objectives/Through its note.md"));

		foreach (var id in new[] { viaApi.Id, viaNote.Id })
		{
			Assert.False(await ObjectiveExistsAsync(id));
			Assert.Equal([VaultImplicitBoundaryService.BoundaryBeginAction, VaultImplicitBoundaryService.BoundaryEndAction], await BoundaryTransitionsAsync(id));
		}

		var standing = await Vault.WithScopeAsync(services => services.GetRequiredService<VaultImplicitBoundaryService>().EnumerateBegunBoundariesAsync(TestContext.Current.CancellationToken));
		Assert.Empty(standing);
	}
}
