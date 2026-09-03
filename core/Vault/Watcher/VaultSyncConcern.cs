namespace Pleiades.Vault.Watcher;

/// <summary>
/// The structured classification of the single root concern a storage-mode reconciliation decision represents, if any.
/// A policy's <c>Decide</c> emits exactly one of these alongside its action, so the watcher's status reporter can
/// classify the outcome from typed data instead of sniffing the human-readable reason string (PEP108 phase D). One
/// root cause therefore yields one classified reason: a decision that rejects a file for its identity is a
/// <see cref="PuckViolation"/> even when the file also carries validation issues — the more specific concern subsumes
/// the rest.
/// </summary>
public enum VaultSyncConcern
{
	/// <summary>
	/// The decision surfaces no concern: a clean sync, a benign hold, or a self-healing rewrite that needs no
	/// operator attention.
	/// </summary>
	None,

	/// <summary>
	/// The candidate's markdown/frontmatter content fails validation, while its identity and placement are otherwise
	/// acceptable to the mode. Maps to the watcher's <c>markdown-invalid</c> reason code.
	/// </summary>
	MarkdownInvalid,

	/// <summary>
	/// The candidate's PUCK identity is the problem — missing required identity input, or an unknown/unresolvable
	/// frontmatter PUCK assertion the mode rejects. Maps to the watcher's <c>puck-violation</c> reason code.
	/// </summary>
	PuckViolation,

	/// <summary>
	/// The candidate is disallowed by the storage policy for what or where it is — a foreign or unresolved file the
	/// mode will not adopt — independent of whether its content validates. Maps to the watcher's <c>policy-violation</c>
	/// reason code.
	/// </summary>
	PolicyViolation,

	/// <summary>
	/// The candidate asserts an identity the vault does not recognise but sits in a <em>non-exclusive</em> root (a
	/// non-Enforced, identity-driven mode), so it is the user's own file, not the core's to destroy. The mode leaves it
	/// in place and raises a dismissible <em>warning</em> rather than purging it — aggression is confined to Enforced
	/// (granted) territory. Maps to the watcher's <c>foreign-file</c> reason code (Warning severity).
	/// </summary>
	ForeignFile,
}
