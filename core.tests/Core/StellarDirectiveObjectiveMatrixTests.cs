using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The stellar-directive / objective repro matrix. Every case walks the same three steps:
/// <list type="number">
/// <item>create a stellar directive — core-first (kept in place, or its folder moved outside the entity root) or
/// initialised from a self-named note inside or outside the entity root — as the world, a child, or a child-of-child;</item>
/// <item>place one, two or three objectives in a target location inside it — the directive root, its partition, an
/// arbitrary folder, a nested subfolder, or a folder inside the partition — created core-first, initialised in the
/// partition, initialised at the target, or initialised in the world (optionally reparented through the API) and then
/// moved there;</item>
/// <item>modify all of them or one of them, by title or by a non-title field, never by their owning directive.</item>
/// </list>
/// By the end of steps 2 and 3 each objective must exist, have exactly one note in the target folder, be parented by the
/// directive, and resolve from that note to itself; a title change must rename the note in place. Nothing asserted after
/// step 2 may change in step 3, and siblings placed the same way must behave the same.
/// </summary>
/// <remarks>
/// <para>
/// The matrix is the full product of the variations, split into one class per directive-creation variation so the
/// classes run as parallel collections. With a single objective the "which ones" variation collapses to <c>All</c>.
/// </para>
/// <para>
/// A creation variation applies to the whole directive chain, because a child lives in its parent's folder: a moved-out
/// child is a moved-out world carrying its subtree, and an initialised child is initialised beneath its initialised
/// parent. A directive folder move is observed through a sweep, as the subtree moves elsewhere in the suite are. An
/// objective note move is observed as the running watcher reports a cross-directory move — the old path, then the new
/// one, then the vanished-note check — through <see cref="TestVault.ReconcileEventsWithIssuesAsync"/>. A note that must
/// be moved is found by the identity it asserts, wherever the core put it.
/// </para>
/// <para>
/// Failures are collected rather than thrown one at a time, so a failing case reports every unit and integration
/// assertion it broke, labelled by step and objective.
/// </para>
/// </remarks>
public static class StellarDirectiveObjectiveMatrixTests
{
	/// <summary>How the stellar directive (and the chain above it) comes to exist in step 1.</summary>
	public enum DirectiveCreation
	{
		/// <summary>Created through the API and left where the core puts it, under the entity root.</summary>
		CoreFirstDefaultLocation,

		/// <summary>Created through the API, then its world folder moved outside the entity root.</summary>
		CoreFirstMovedOutOfEntityRoot,

		/// <summary>A self-named note-in-folder written inside the entity root, then initialised.</summary>
		InitInsideEntityRoot,

		/// <summary>A self-named note-in-folder written outside the entity root, then initialised.</summary>
		InitOutsideEntityRoot,
	}

	/// <summary>Where the stellar directive under test sits in its hierarchy.</summary>
	public enum DirectiveHierarchy
	{
		/// <summary>A top-level directive.</summary>
		World,

		/// <summary>A child of a world directive.</summary>
		Child,

		/// <summary>A child of a child directive.</summary>
		ChildOfChild,
	}

	/// <summary>Where, inside the directive, the objectives' notes must end up in step 2.</summary>
	public enum ObjectiveTarget
	{
		/// <summary>The directive's own folder, beside its note.</summary>
		DirectiveRoot,

		/// <summary>The default partition folder, <c>Objectives</c>.</summary>
		DefaultPartition,

		/// <summary>An arbitrary folder in the directive's folder.</summary>
		FolderInDirective,

		/// <summary>An arbitrary subfolder nested in an arbitrary folder of the directive.</summary>
		NestedFolderInDirective,

		/// <summary>An arbitrary folder inside the partition.</summary>
		FolderInPartition,
	}

	/// <summary>How each objective comes to exist and reach its target location in step 2.</summary>
	public enum ObjectiveCreation
	{
		/// <summary>Created under the directive through the API, its boundary begun, then its note moved to the target.</summary>
		CoreFirstBeginBoundaryThenMove,

		/// <summary>A note written in the partition and initialised there, then moved to the target.</summary>
		InitInPartitionThenMove,

		/// <summary>A note written at the target and initialised there.</summary>
		InitAtTarget,

		/// <summary>A note written in the world (the standalone objectives root) and initialised, then moved to the target.</summary>
		InitInWorldThenMove,

		/// <summary>A note initialised in the world, reparented under the directive through the API, then moved to the target.</summary>
		InitInWorldReparentThroughApiThenMove,
	}

	/// <summary>Which of the placed objectives step 3 modifies.</summary>
	public enum ModifiedObjectives
	{
		/// <summary>Every placed objective.</summary>
		All,

		/// <summary>The first placed objective only.</summary>
		One,
	}

	/// <summary>What step 3 changes on each modified objective.</summary>
	public enum ModifiedProperty
	{
		/// <summary>A valid non-title change: the college and the Starfire value.</summary>
		NonTitle,

		/// <summary>The title, which drives the note's file name.</summary>
		Title,
	}

	/// <summary>Step 1 with directives created through the API and left in the entity root.</summary>
	public sealed class CoreFirstDefaultLocation : Scenario
	{
		/// <inheritdoc />
		protected override DirectiveCreation Creation => DirectiveCreation.CoreFirstDefaultLocation;
	}

	/// <summary>Step 1 with directives created through the API and then moved outside the entity root.</summary>
	public sealed class CoreFirstMovedOutOfEntityRoot : Scenario
	{
		/// <inheritdoc />
		protected override DirectiveCreation Creation => DirectiveCreation.CoreFirstMovedOutOfEntityRoot;
	}

	/// <summary>Step 1 with directives initialised from notes inside the entity root.</summary>
	public sealed class InitInsideEntityRoot : Scenario
	{
		/// <inheritdoc />
		protected override DirectiveCreation Creation => DirectiveCreation.InitInsideEntityRoot;
	}

	/// <summary>Step 1 with directives initialised from notes outside the entity root.</summary>
	public sealed class InitOutsideEntityRoot : Scenario
	{
		/// <inheritdoc />
		protected override DirectiveCreation Creation => DirectiveCreation.InitOutsideEntityRoot;
	}

	/// <summary>
	/// The three steps and their assertions, run for every remaining variation under the directive-creation variation a
	/// derived class fixes.
	/// </summary>
	public abstract class Scenario : VaultTestBase
	{
		private const string EntityRoot = "Directives";
		private const string OutOfEntityRoot = "Projects";
		private const string WorldObjectivesRoot = "Objectives";
		private const string Partition = "Objectives";

		private static readonly string[] DirectiveTitles = ["Campaign", "Strike", "Sortie"];
		private static readonly string[] ObjectiveTitles = ["Take the bridge", "Hold the line", "Signal the fleet"];

		private readonly List<string> _failures = [];

		private static CancellationToken Token => TestContext.Current.CancellationToken;

		/// <summary>Gets the step-1 creation variation this class runs the matrix under.</summary>
		protected abstract DirectiveCreation Creation { get; }

		/// <summary>
		/// Every combination of hierarchy, target, objective creation, count, which-ones and property. A single objective
		/// yields only <see cref="ModifiedObjectives.All"/>, since "one" of one is the same case.
		/// </summary>
		public static TheoryData<DirectiveHierarchy, ObjectiveTarget, ObjectiveCreation, int, ModifiedObjectives, ModifiedProperty> Cases()
		{
			var cases = new TheoryData<DirectiveHierarchy, ObjectiveTarget, ObjectiveCreation, int, ModifiedObjectives, ModifiedProperty>();
			foreach (var hierarchy in Enum.GetValues<DirectiveHierarchy>())
			{
				foreach (var target in Enum.GetValues<ObjectiveTarget>())
				{
					foreach (var creation in Enum.GetValues<ObjectiveCreation>())
					{
						for (var count = 1; count <= ObjectiveTitles.Length; count++)
						{
							var selections = count == 1 ? [ModifiedObjectives.All] : Enum.GetValues<ModifiedObjectives>();
							foreach (var which in selections)
							{
								foreach (var property in Enum.GetValues<ModifiedProperty>())
								{
									cases.Add(hierarchy, target, creation, count, which, property);
								}
							}
						}
					}
				}
			}

			return cases;
		}

		[Theory]
		[MemberData(nameof(Cases), MemberType = typeof(Scenario))]
		public async Task Objectives_keep_their_place_parent_and_identity_through_creation_and_modification(
			DirectiveHierarchy hierarchy,
			ObjectiveTarget target,
			ObjectiveCreation creation,
			int count,
			ModifiedObjectives which,
			ModifiedProperty property)
		{
			var directive = await CreateDirectiveAsync(hierarchy);
			var targetFolder = TargetFolder(directive.Folder, target);

			var objectives = new List<PlacedObjective>();
			foreach (var title in ObjectiveTitles.Take(count))
			{
				var id = await PlaceObjectiveAsync(directive, targetFolder, creation, title);
				objectives.Add(new PlacedObjective(id, title));
			}

			var placed = new Dictionary<string, Observation>();
			foreach (var objective in objectives)
			{
				var observation = await ObserveAsync(objective.Id);
				placed[objective.Id] = observation;
				ExpectUnits("step 2", objective, observation, directive, targetFolder);
			}

			ExpectSiblingsBehaveAlike(objectives, placed);

			var missing = objectives.Where(objective => !placed[objective.Id].Exists).ToList();
			if (missing.Count > 0)
			{
				Report($"step 3 was not run: {string.Join(", ", missing.Select(Describe))} did not survive step 2");
			}

			var modified = which == ModifiedObjectives.All ? objectives : objectives.Take(1).ToList();
			foreach (var objective in modified)
			{
				await ModifyAsync(objective, property);
			}

			foreach (var objective in objectives)
			{
				var observation = await ObserveAsync(objective.Id);
				ExpectUnits("step 3", objective, observation, directive, targetFolder);
				ExpectModificationTook(objective, observation, modified.Contains(objective) ? property : null);
				ExpectFileNameFollowsTitle(objective, observation);
				ExpectUnchangedSinceStep2(objective, placed[objective.Id], observation);
			}

			Report();
		}

		// --- step 1 ---

		private async Task<PlacedDirective> CreateDirectiveAsync(DirectiveHierarchy hierarchy)
		{
			var chain = DirectiveTitles.Take((int)hierarchy + 1).ToArray();
			var container = Creation is DirectiveCreation.InitOutsideEntityRoot ? OutOfEntityRoot : EntityRoot;
			var ids = new List<string>();
			var folder = container;
			foreach (var title in chain)
			{
				folder = $"{folder}/{title}";
				var created = Creation switch
				{
					DirectiveCreation.CoreFirstDefaultLocation or DirectiveCreation.CoreFirstMovedOutOfEntityRoot => ids.Count == 0
						? await Directives(api => api.CreateStandaloneAsync(title, cancellationToken: Token))
						: await Directives(api => api.CreateFromParentAsync(ids[^1], title, cancellationToken: Token)),
					_ => await InitDirectiveAtAsync($"{folder}/{title}.md", title),
				};
				ids.Add(created.Id);
			}

			if (Creation is DirectiveCreation.CoreFirstMovedOutOfEntityRoot)
			{
				MoveDirectory($"{EntityRoot}/{chain[0]}", $"{OutOfEntityRoot}/{chain[0]}");
				await Vault.SweepAsync();
				folder = $"{OutOfEntityRoot}{folder[EntityRoot.Length..]}";
			}

			await RequireDirectiveChainAsync(chain, ids, folder);
			return new PlacedDirective(ids[^1], folder);
		}

		private Task<Directive> InitDirectiveAtAsync(string noteRelativePath, string title)
		{
			Vault.WriteVaultFile(noteRelativePath, $"# {title}{Environment.NewLine}Directive body.{Environment.NewLine}");
			return Directives(api => api.InitializeFromPathAsync(noteRelativePath, Token));
		}

		/// <summary>
		/// Guards step 1 before anything is placed in it: every directive of the chain exists under its expected parent and
		/// its self-named note is where the variation put it.
		/// </summary>
		private async Task RequireDirectiveChainAsync(string[] chain, List<string> ids, string folder)
		{
			var notePath = folder;
			for (var depth = chain.Length - 1; depth >= 0; depth--)
			{
				var id = ids[depth];
				var stored = await Vault.QueryAsync(context => context.Directives.AsNoTracking()
					.SingleOrDefaultAsync(item => item.Id == id, Token));
				Assert.True(stored is StellarDirective, $"step 1 precondition: '{chain[depth]}' must exist as a stellar directive");
				Assert.True(stored!.ParentDirectiveId == (depth == 0 ? null : ids[depth - 1]),
					$"step 1 precondition: '{chain[depth]}' must be parented by '{(depth == 0 ? "nothing" : chain[depth - 1])}', but is parented by '{stored.ParentDirectiveId ?? "nothing"}'");
				Assert.True(Vault.VaultFileExists($"{notePath}/{chain[depth]}.md"),
					$"step 1 precondition: '{chain[depth]}' must have its note at '{notePath}/{chain[depth]}.md'");
				notePath = notePath[..notePath.LastIndexOf('/')];
			}
		}

		// --- step 2 ---

		private static string TargetFolder(string directiveFolder, ObjectiveTarget target) => target switch
		{
			ObjectiveTarget.DirectiveRoot => directiveFolder,
			ObjectiveTarget.DefaultPartition => $"{directiveFolder}/{Partition}",
			ObjectiveTarget.FolderInDirective => $"{directiveFolder}/Notebook",
			ObjectiveTarget.NestedFolderInDirective => $"{directiveFolder}/Notebook/Drafts",
			ObjectiveTarget.FolderInPartition => $"{directiveFolder}/{Partition}/Backlog",
			_ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
		};

		private async Task<string> PlaceObjectiveAsync(PlacedDirective directive, string targetFolder, ObjectiveCreation creation, string title)
		{
			var target = $"{targetFolder}/{title}.md";
			switch (creation)
			{
				case ObjectiveCreation.CoreFirstBeginBoundaryThenMove:
				{
					var created = await Objectives(api => api.CreateFromDirectiveAsync(directive.Id, title, cancellationToken: Token));
					await Vault.BeginObjectiveBoundaryAsync(created.Id);
					await MoveNoteAsync(created.Id, target);
					return created.Id;
				}

				case ObjectiveCreation.InitInPartitionThenMove:
				{
					var id = await InitObjectiveAtAsync($"{directive.Folder}/{Partition}/{title}.md", title);
					await MoveNoteAsync(id, target);
					return id;
				}

				case ObjectiveCreation.InitAtTarget:
					return await InitObjectiveAtAsync(target, title);

				case ObjectiveCreation.InitInWorldThenMove:
				{
					var id = await InitObjectiveAtAsync($"{WorldObjectivesRoot}/{title}.md", title);
					await MoveNoteAsync(id, target);
					return id;
				}

				case ObjectiveCreation.InitInWorldReparentThroughApiThenMove:
				{
					var id = await InitObjectiveAtAsync($"{WorldObjectivesRoot}/{title}.md", title);
					await Objectives(api => api.UpdateAsync(id, new ObjectiveUpdate(DirectiveId: directive.Id), Token));
					await MoveNoteAsync(id, target);
					return id;
				}

				default:
					throw new ArgumentOutOfRangeException(nameof(creation), creation, null);
			}
		}

		private async Task<string> InitObjectiveAtAsync(string noteRelativePath, string title)
		{
			Vault.WriteVaultFile(noteRelativePath, $"# {title}{Environment.NewLine}Objective body.{Environment.NewLine}");
			var objective = await Objectives(api => api.InitializeFromPathAsync(noteRelativePath, Token));
			return objective.Id;
		}

		/// <summary>
		/// Moves the one note asserting an objective's identity — wherever the core left it — to the target path, and lets
		/// the watcher observe the move. A note already at the target is left alone.
		/// </summary>
		private async Task MoveNoteAsync(string objectiveId, string targetRelativePath)
		{
			var notes = NotesAssertingPuck(objectiveId);
			Assert.True(notes.Length == 1,
				$"step 2 precondition: before its move, '{objectiveId}' must have exactly one note, but has [{string.Join(", ", notes)}]");
			if (notes[0] == targetRelativePath)
			{
				return;
			}

			var from = Vault.AbsolutePath(notes[0]);
			var to = Vault.AbsolutePath(targetRelativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(to)!);
			File.Move(from, to);
			await Vault.ReconcileEventsWithIssuesAsync([from, to]);
		}

		// --- step 3 ---

		private Task ModifyAsync(PlacedObjective objective, ModifiedProperty property)
		{
			var update = property switch
			{
				ModifiedProperty.Title => new ObjectiveUpdate(Title: RenamedTitle(objective.Title)),
				ModifiedProperty.NonTitle => new ObjectiveUpdate(College: ObjectiveCollege.Lore, CelestronValue: 3),
				_ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
			};
			objective.CurrentTitle = property is ModifiedProperty.Title ? RenamedTitle(objective.Title) : objective.Title;
			return Objectives(api => api.UpdateAsync(objective.Id, update, Token));
		}

		private static string RenamedTitle(string title) => $"{title} revised";

		// --- observation ---

		private async Task<Observation> ObserveAsync(string objectiveId)
		{
			var stored = await Vault.QueryAsync(context => context.Objectives.AsNoTracking()
				.SingleOrDefaultAsync(item => item.Id == objectiveId, Token));
			var notes = NotesAssertingPuck(objectiveId);
			var resolution = notes.Length == 1
				? await Vault.WithScopeAsync(services => services.GetRequiredService<ISystemApi>().ResolveVaultNoteAsync(notes[0], Token))
				: null;
			return new Observation(objectiveId, stored, notes, resolution);
		}

		/// <summary>Every vault-relative note (outside the metadata root) that asserts the given PUCK.</summary>
		private string[] NotesAssertingPuck(string puck)
		{
			return Vault.MarkdownFilesUnder(Vault.VaultRoot)
				.Where(path => !path.StartsWith(Vault.Layout.MetadataRoot, StringComparison.OrdinalIgnoreCase))
				.Where(path => File.ReadAllText(path).Contains($"puck: {puck}", StringComparison.Ordinal))
				.Select(path => Path.GetRelativePath(Vault.VaultRoot, path).Replace('\\', '/'))
				.Order(StringComparer.Ordinal)
				.ToArray();
		}

		// --- assertions ---

		/// <summary>Assert units 1–4: the objective exists, its one note is in the target folder, it is parented by the directive, and its note resolves to it.</summary>
		private void ExpectUnits(string step, PlacedObjective objective, Observation observation, PlacedDirective directive, string targetFolder)
		{
			Expect(observation.Exists, $"{step}, {Describe(objective)}: should exist");
			Expect(observation.NoteFolder == targetFolder,
				$"{step}, {Describe(objective)}: should have one note in '{targetFolder}', but the notes asserting it are {Show(observation.Notes)}");
			Expect(observation.DirectiveId == directive.Id,
				$"{step}, {Describe(objective)}: should be parented by directive '{directive.Id}', but is parented by '{observation.DirectiveId ?? "nothing"}'");
			Expect(observation.ResolvesToItself,
				$"{step}, {Describe(objective)}: resolving its note {Show(observation.Notes)} should yield it, but yields {observation.ResolvedAs ?? "no resolution"}");
		}

		/// <summary>Assert integration 2: after step 2, siblings placed the same way behave the same in units 1–4.</summary>
		private void ExpectSiblingsBehaveAlike(List<PlacedObjective> objectives, Dictionary<string, Observation> placed)
		{
			var first = objectives[0];
			foreach (var sibling in objectives.Skip(1))
			{
				Expect(placed[sibling.Id].Behaviour == placed[first.Id].Behaviour,
					$"step 2, {Describe(sibling)}: should behave as {Describe(first)} does ({placed[first.Id].Behaviour}), but {placed[sibling.Id].Behaviour}");
			}
		}

		/// <summary>Guards that step 3 happened at all, so an unchanged objective is not mistaken for a stable one.</summary>
		private void ExpectModificationTook(PlacedObjective objective, Observation observation, ModifiedProperty? property)
		{
			if (observation.Stored is not { } stored || property is null)
			{
				return;
			}

			Expect(property is ModifiedProperty.Title
					? stored.Title == objective.CurrentTitle
					: stored.College == ObjectiveCollege.Lore && stored.CelestronValue == 3,
				$"step 3, {Describe(objective)}: the {property} modification should have been stored");
		}

		/// <summary>Assert unit 5: the note's file name follows the title — renamed when the title was modified, untouched otherwise.</summary>
		private void ExpectFileNameFollowsTitle(PlacedObjective objective, Observation observation)
		{
			var expected = $"{objective.CurrentTitle}.md";
			Expect(observation.NoteFileName == expected,
				$"step 3, {Describe(objective)}: its note should be named '{expected}', but the notes asserting it are {Show(observation.Notes)}");
		}

		/// <summary>Assert integration 1: nothing units 1–4 asserted after step 2 changed in step 3.</summary>
		private void ExpectUnchangedSinceStep2(PlacedObjective objective, Observation afterStep2, Observation afterStep3)
		{
			Expect(afterStep3.Parameters == afterStep2.Parameters,
				$"step 3, {Describe(objective)}: should be as it was after step 2 ({afterStep2.Parameters}), but {afterStep3.Parameters}");
		}

		private void Expect(bool condition, string failure)
		{
			if (!condition)
			{
				_failures.Add(failure);
			}
		}

		private void Report(string? abort = null)
		{
			if (abort is not null)
			{
				_failures.Add(abort);
			}

			if (_failures.Count > 0)
			{
				Assert.Fail(string.Join(Environment.NewLine, _failures));
			}
		}

		private static string Describe(PlacedObjective objective) => $"'{objective.Title}' ({objective.Id})";

		private static string Show(IReadOnlyList<string> notes) => notes.Count == 0 ? "none" : $"[{string.Join(", ", notes)}]";

		// --- plumbing ---

		private Task<T> Directives<T>(Func<IDirectiveApi, Task<T>> call)
			=> Vault.WithScopeAsync(services => call(services.GetRequiredService<IDirectiveApi>()));

		private Task<T> Objectives<T>(Func<IObjectiveApi, Task<T>> call)
			=> Vault.WithScopeAsync(services => call(services.GetRequiredService<IObjectiveApi>()));

		private void MoveDirectory(string fromRelative, string toRelative)
		{
			var to = Vault.AbsolutePath(toRelative);
			Directory.CreateDirectory(Path.GetDirectoryName(to)!);
			Directory.Move(Vault.AbsolutePath(fromRelative), to);
		}

		/// <summary>The directive under test and its vault-relative folder.</summary>
		private sealed record PlacedDirective(string Id, string Folder);

		/// <summary>A placed objective, its original title, and the title step 3 left it with.</summary>
		private sealed class PlacedObjective(string id, string title)
		{
			public string Id { get; } = id;

			public string Title { get; } = title;

			public string CurrentTitle { get; set; } = title;
		}

		/// <summary>What units 1–4 assert about an objective, compared as a whole before and after step 3.</summary>
		private sealed record UnitParameters(bool Exists, string? NoteFolder, string? DirectiveId, string? ResolvedAs);

		/// <summary>How an objective fared in units 1–4, free of its own identity so siblings can be compared.</summary>
		private sealed record UnitBehaviour(bool Exists, string? NoteFolder, string? DirectiveId, bool ResolvesToItself);

		/// <summary>An objective's stored row, the notes asserting its identity, and what its one note resolves to.</summary>
		private sealed record Observation(string ObjectiveId, Objective? Stored, string[] Notes, EntityExistence? Resolution)
		{
			public bool Exists => Stored is not null;

			public string? DirectiveId => Stored?.DirectiveId;

			public string? NoteFolder => Notes.Length == 1 ? Notes[0][..Math.Max(Notes[0].LastIndexOf('/'), 0)] : null;

			public string? NoteFileName => Notes.Length == 1 ? Notes[0][(Notes[0].LastIndexOf('/') + 1)..] : null;

			public string? ResolvedAs => Resolution is null
				? null
				: Resolution.Exists ? $"{Resolution.EntityType} '{Resolution.Puck}'" : "nothing";

			public bool ResolvesToItself => Resolution is { Exists: true } resolution
				&& resolution.EntityType == nameof(Objective)
				&& resolution.Puck == ObjectiveId;

			public UnitParameters Parameters => new(Exists, NoteFolder, DirectiveId, ResolvedAs);

			public UnitBehaviour Behaviour => new(Exists, NoteFolder, DirectiveId, ResolvesToItself);
		}
	}
}
