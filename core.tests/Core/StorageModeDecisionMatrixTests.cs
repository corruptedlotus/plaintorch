using Pleiades.Orchestration;
using Pleiades.Vault;
using Pleiades.Vault.Policy;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The per-mode reconciliation decision matrix. Each storage mode interprets the same candidate facts — identity
/// present / already-known, file exists, validation issues, whether identity needs caller input — into a different
/// sync action. This pins the current behaviour of the path-bound modes that were otherwise untested at the decision
/// level (Enforced / Synced / FileFirst / Optional) as the baseline the REFACTOR Alpha phase-4 policy-object
/// consolidation must preserve. Freeform and Implicit carry constructor dependencies and are exercised through the
/// directive-init and implicit-boundary tests respectively.
/// </summary>
public sealed class StorageModeDecisionMatrixTests
{
	private static readonly VaultPathSyncModel DummyModel = new(
		typeof(Objective),
		["."],
		VaultStorageMode.Synced,
		VaultStorageShape.SingleFile,
		static _ => true,
		static (_, _) => Task.FromResult(new HashSet<string>()),
		null);

	// The decision methods read only these candidate facts (never the model itself), so a single dummy model backs
	// every scenario. `known` seeds the id into KnownIds; `hasIssues` supplies a validation message.
	private static VaultStorageModeDecisionContext Ctx(
		string? pathId = "j00000001",
		bool fileExists = true,
		bool known = false,
		bool requiresCallerInput = false,
		string title = "Ship it",
		bool hasIssues = false)
	{
		var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (known && !string.IsNullOrWhiteSpace(pathId))
		{
			knownIds.Add(pathId);
		}

		return new VaultStorageModeDecisionContext(
			DummyModel,
			pathId,
			title,
			hasIssues ? ["validation issue"] : [],
			knownIds,
			fileExists,
			requiresCallerInput);
	}

	[Fact]
	public void Enforced_mode_reconciliation_matrix()
	{
		var mode = new EnforcedVaultStorageModePolicyService();

		// A title-only placeholder is held until the user names it; enforced storage otherwise purges unresolved
		// files, keeps the database canonical when a known file vanishes, and rewrites over local issues.
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(pathId: null, title: "Untitled")).Action);
		Assert.Equal(VaultSyncAction.PurgeFile, mode.Decide(Ctx(pathId: null, requiresCallerInput: true)).Action);
		Assert.Equal(VaultSyncAction.PurgeFile, mode.Decide(Ctx(pathId: null)).Action);
		Assert.Equal(VaultSyncAction.RewriteFromDatabase, mode.Decide(Ctx(fileExists: false, known: true)).Action);
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(fileExists: false)).Action);
		Assert.Equal(VaultSyncAction.PurgeFile, mode.Decide(Ctx(hasIssues: true)).Action);
		Assert.Equal(VaultSyncAction.RewriteFromDatabase, mode.Decide(Ctx(known: true, hasIssues: true)).Action);
		Assert.Equal(VaultSyncAction.UpdateFromFile, mode.Decide(Ctx(known: true)).Action);
		Assert.Equal(VaultSyncAction.PurgeFile, mode.Decide(Ctx()).Action);
	}

	[Fact]
	public void Synced_mode_reconciliation_matrix()
	{
		var mode = new SyncedVaultStorageModePolicyService();

		// Synced storage is bidirectional: a valid unknown file originates a new entity, a deleted known file deletes
		// the entity, and validation issues conflict (unknown) or rewrite from canonical (known).
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(pathId: null, title: "Untitled")).Action);
		Assert.Equal(VaultSyncAction.Conflict, mode.Decide(Ctx(pathId: null, requiresCallerInput: true)).Action);
		Assert.Equal(VaultSyncAction.CreateFromFile, mode.Decide(Ctx(pathId: null)).Action);
		Assert.Equal(VaultSyncAction.DeleteFromDatabase, mode.Decide(Ctx(fileExists: false, known: true)).Action);
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(fileExists: false)).Action);
		Assert.Equal(VaultSyncAction.Conflict, mode.Decide(Ctx(hasIssues: true)).Action);
		Assert.Equal(VaultSyncAction.RewriteFromDatabase, mode.Decide(Ctx(known: true, hasIssues: true)).Action);
		Assert.Equal(VaultSyncAction.UpdateFromFile, mode.Decide(Ctx(known: true)).Action);
		Assert.Equal(VaultSyncAction.CreateFromFile, mode.Decide(Ctx()).Action);
	}

	[Fact]
	public void FileFirst_mode_reconciliation_matrix()
	{
		var mode = new FileFirstVaultStorageModePolicyService();

		// File-first storage always originates from files, so any validation issue is a conflict to reconcile (it
		// never silently rewrites over a known file the way synced/optional do).
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(pathId: null, title: "Untitled")).Action);
		Assert.Equal(VaultSyncAction.Conflict, mode.Decide(Ctx(pathId: null, requiresCallerInput: true)).Action);
		Assert.Equal(VaultSyncAction.CreateFromFile, mode.Decide(Ctx(pathId: null)).Action);
		Assert.Equal(VaultSyncAction.DeleteFromDatabase, mode.Decide(Ctx(fileExists: false, known: true)).Action);
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(fileExists: false)).Action);
		Assert.Equal(VaultSyncAction.Conflict, mode.Decide(Ctx(hasIssues: true)).Action);
		Assert.Equal(VaultSyncAction.Conflict, mode.Decide(Ctx(known: true, hasIssues: true)).Action);
		Assert.Equal(VaultSyncAction.UpdateFromFile, mode.Decide(Ctx(known: true)).Action);
		Assert.Equal(VaultSyncAction.CreateFromFile, mode.Decide(Ctx()).Action);
	}

	[Fact]
	public void Optional_mode_reconciliation_matrix()
	{
		var mode = new OptionalVaultStorageModePolicyService();

		// Optional storage never creates entities from standalone files (unknown valid files are ignored, not
		// purged), but still tracks and updates files for entities it already owns.
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(pathId: null, title: "Untitled")).Action);
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(pathId: null, requiresCallerInput: true)).Action);
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(pathId: null)).Action);
		Assert.Equal(VaultSyncAction.DeleteFromDatabase, mode.Decide(Ctx(fileExists: false, known: true)).Action);
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(fileExists: false)).Action);
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx(hasIssues: true)).Action);
		Assert.Equal(VaultSyncAction.RewriteFromDatabase, mode.Decide(Ctx(known: true, hasIssues: true)).Action);
		Assert.Equal(VaultSyncAction.UpdateFromFile, mode.Decide(Ctx(known: true)).Action);
		Assert.Equal(VaultSyncAction.Ignore, mode.Decide(Ctx()).Action);
	}

	[Fact]
	public void A_missing_file_without_an_identity_is_ignored_by_every_path_bound_mode()
	{
		// A note deleted before it was ever synced — or the "<dir>/<dir>.md" primary a folder event synthesizes for a
		// self-named-directory model — reaches the policy as a missing, identity-less path. There is nothing on disk to
		// create from, purge or hold in conflict, so every path-bound mode ignores it with no concern (the sandbox's
		// deleted "Evanesca" order otherwise re-raised a PUCK violation for a file that no longer existed).
		IVaultStorageModePolicyService[] modes =
		[
			new EnforcedVaultStorageModePolicyService(),
			new SyncedVaultStorageModePolicyService(),
			new FileFirstVaultStorageModePolicyService(),
			new OptionalVaultStorageModePolicyService(),
		];
		foreach (var mode in modes)
		{
			foreach (var requiresCallerInput in new[] { false, true })
			{
				var decision = mode.Decide(Ctx(pathId: null, fileExists: false, requiresCallerInput: requiresCallerInput, hasIssues: true));
				Assert.Equal(VaultSyncAction.Ignore, decision.Action);
				Assert.Equal(VaultSyncConcern.None, decision.Concern);
			}
		}
	}

	[Fact]
	public void Decision_concerns_classify_the_single_root_cause_structurally()
	{
		// Phase D: each Decide emits exactly one structured concern so the watcher raises one classified reason per
		// candidate. A rejection driven by identity is a puck concern; one driven by placement/ownership is a policy
		// concern (and subsumes any incidental validation issues on an unknown file); a rewrite/conflict driven purely
		// by a known file's content is a markdown concern; clean and benign decisions carry no concern.
		var enforced = new EnforcedVaultStorageModePolicyService();
		Assert.Equal(VaultSyncConcern.PuckViolation, enforced.Decide(Ctx(pathId: null, requiresCallerInput: true)).Concern);
		Assert.Equal(VaultSyncConcern.PolicyViolation, enforced.Decide(Ctx(pathId: null)).Concern);
		Assert.Equal(VaultSyncConcern.PolicyViolation, enforced.Decide(Ctx()).Concern);
		Assert.Equal(VaultSyncConcern.PolicyViolation, enforced.Decide(Ctx(hasIssues: true)).Concern);
		Assert.Equal(VaultSyncConcern.MarkdownInvalid, enforced.Decide(Ctx(known: true, hasIssues: true)).Concern);
		Assert.Equal(VaultSyncConcern.None, enforced.Decide(Ctx(known: true)).Concern);

		var synced = new SyncedVaultStorageModePolicyService();
		Assert.Equal(VaultSyncConcern.PuckViolation, synced.Decide(Ctx(pathId: null, requiresCallerInput: true)).Concern);
		Assert.Equal(VaultSyncConcern.MarkdownInvalid, synced.Decide(Ctx(hasIssues: true)).Concern);
		Assert.Equal(VaultSyncConcern.MarkdownInvalid, synced.Decide(Ctx(known: true, hasIssues: true)).Concern);
		Assert.Equal(VaultSyncConcern.None, synced.Decide(Ctx()).Concern);

		var fileFirst = new FileFirstVaultStorageModePolicyService();
		Assert.Equal(VaultSyncConcern.PuckViolation, fileFirst.Decide(Ctx(pathId: null, requiresCallerInput: true)).Concern);
		Assert.Equal(VaultSyncConcern.MarkdownInvalid, fileFirst.Decide(Ctx(hasIssues: true)).Concern);
		Assert.Equal(VaultSyncConcern.None, fileFirst.Decide(Ctx(known: true)).Concern);

		// Optional passively ignores unknown/invalid standalone files, asserting no concern of its own; only the rewrite
		// of a known file's local issues is a markdown concern.
		var optional = new OptionalVaultStorageModePolicyService();
		Assert.Equal(VaultSyncConcern.None, optional.Decide(Ctx(hasIssues: true)).Concern);
		Assert.Equal(VaultSyncConcern.MarkdownInvalid, optional.Decide(Ctx(known: true, hasIssues: true)).Concern);
		Assert.Equal(VaultSyncConcern.None, optional.Decide(Ctx()).Concern);
	}

	[Fact]
	public void The_identity_driven_modes_leave_an_unrecognised_puck_in_place_as_a_foreign_file()
	{
		// Freeform and Implicit are non-exclusive, identity-driven roots: an asserted PUCK that resolves to no entity is
		// the user's own file, so the mode leaves it in place (Ignore) with a dismissible foreign-file Warning rather
		// than purging it — aggression stays confined to Enforced (granted) territory. Their Decide reads only the
		// candidate facts (never the injected services), so a bare instance suffices to pin the decision.
		var freeform = new FreeformVaultStorageModePolicyService(null!, null!, null!, null!);
		Assert.Equal(VaultSyncAction.Ignore, freeform.Decide(Ctx(pathId: "d00000001")).Action);
		Assert.Equal(VaultSyncConcern.ForeignFile, freeform.Decide(Ctx(pathId: "d00000001")).Concern);
		Assert.Equal(VaultSyncConcern.ForeignFile, freeform.Decide(Ctx(pathId: "d00000001", hasIssues: true)).Concern);

		var implicitMode = new ImplicitVaultStorageModePolicyService(null!, null!);
		Assert.Equal(VaultSyncAction.Ignore, implicitMode.Decide(Ctx(pathId: "d00000001")).Action);
		Assert.Equal(VaultSyncConcern.ForeignFile, implicitMode.Decide(Ctx(pathId: "d00000001")).Concern);
		Assert.Equal(VaultSyncConcern.ForeignFile, implicitMode.Decide(Ctx(pathId: "d00000001", hasIssues: true)).Concern);
	}
}
