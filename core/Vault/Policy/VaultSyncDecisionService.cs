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
	/// <returns>A provisional action and explanatory reason for watcher reconciliation.</returns>
	public (VaultSyncAction Action, string Reason) Decide(VaultPathSyncModel model, string? pathId, string pathTitle, IReadOnlyList<string> issueMessages, ISet<string> knownIds, bool fileExists)
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
			puckCreationService.RequiresCallerInputFor(model.EntityType)));
	}
}
