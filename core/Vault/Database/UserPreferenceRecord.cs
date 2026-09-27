using System.ComponentModel.DataAnnotations;

namespace Pleiades.Vault.Database;

/// <summary>
/// A single vault-bound user preference override, stored as one key/value row (PEP116). Preferences are
/// sparse: only values the user has explicitly changed are persisted, and everything else resolves to a
/// code-owned default, so introducing a new preference needs no migration or seed. The preference key is the
/// natural primary key, which makes a set an idempotent upsert and a reset a single delete.
/// </summary>
public sealed class UserPreferenceRecord
{
	/// <summary>Gets or sets the preference's stable storage key (see <c>PreferenceKeys</c>).</summary>
	[Key]
	public required string Key { get; set; }

	/// <summary>Gets or sets the preference's value, serialized as compact JSON.</summary>
	public required string Value { get; set; }

	/// <summary>Gets or sets when the preference was last written.</summary>
	public DateTimeOffset UpdatedUtc { get; set; }
}
