using Pleiades.Plaintorch.Enforcement;

namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// Vault-bound preferences for the backlog's state enforcement (PEP103, PEP116). Resolved through .NET Options — inject
/// <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{T}"/>.
/// </summary>
public sealed class BacklogPreferences
{
	/// <summary>
	/// How firmly the core holds the backlog to its lifecycle rules when a change would break one: ignore, compensate
	/// automatically, warn first, or block. Defaults to <see cref="BacklogStateEnforcementLevel.Warn"/>. Systemic violations
	/// are refused whatever the level.
	/// </summary>
	public BacklogStateEnforcementLevel StateEnforcement { get; set; } = BacklogStateEnforcementLevel.Warn;
}
