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
}
