using Pleiades.Puck;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Produces provisional watcher reconciliation decisions from path-resolved markdown candidates.
/// </summary>
public sealed class VaultSyncDecisionService(
	PuckCreationService puckCreationService,
	VaultStorageModePolicyRouter policyRouter)
{
	/// <summary>
	/// Evaluates a discovered candidate against known entity identifiers and storage policy.
	/// </summary>
	/// <param name="model">The path sync model classifying the candidate.</param>
	/// <param name="pathId">The resolved candidate identifier, when available.</param>
	/// <param name="pathTitle">The resolved candidate title parsed from path/frontmatter.</param>
	/// <param name="issueMessages">Validation issue messages produced while hydrating the candidate.</param>
	/// <param name="knownIds">The known identifiers currently present in storage for the model type.</param>
	/// <param name="fileExists">Whether the candidate file currently exists on disk.</param>
	/// <param name="boundaryBegun">Whether an implicit entity's synchronization boundary has begun.</param>
	/// <returns>A provisional action, explanatory reason, and structured concern for watcher reconciliation.</returns>
	public VaultSyncDecision Decide(VaultPathSyncModel model, string? pathId, string pathTitle, IReadOnlyList<string> issueMessages, ISet<string> knownIds, bool fileExists, bool boundaryBegun = true)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentException.ThrowIfNullOrWhiteSpace(pathTitle);
		ArgumentNullException.ThrowIfNull(issueMessages);
		ArgumentNullException.ThrowIfNull(knownIds);

		var policy = policyRouter.Resolve(model.Mode);
		return policy.Decide(new VaultStorageModeDecisionContext(
			model,
			pathId,
			pathTitle,
			issueMessages,
			knownIds,
			fileExists,
			puckCreationService.RequiresCallerInputFor(model.EntityType),
			boundaryBegun));
	}
}
