namespace Pleiades.Vault.Watcher;

/// <summary>
/// The structured outcome of a storage-mode reconciliation decision: the action to take, a human-readable reason,
/// and the typed <see cref="VaultSyncConcern"/> classifying the single root concern the decision represents (if any).
/// The concern is what the watcher's status reporter reads to raise exactly one classified issue per candidate,
/// replacing the earlier reason-string heuristics (PEP108 phase D). The <see cref="Reason"/> remains a diagnostic
/// message shown to the operator — it is no longer parsed to classify the outcome.
/// </summary>
/// <param name="Action">The provisional reconciliation action suggested for the candidate.</param>
/// <param name="Reason">The human-readable explanation for the action.</param>
/// <param name="Concern">The structured classification of the decision's root concern, or <see cref="VaultSyncConcern.None"/>.</param>
public readonly record struct VaultSyncDecision(VaultSyncAction Action, string Reason, VaultSyncConcern Concern = VaultSyncConcern.None);
