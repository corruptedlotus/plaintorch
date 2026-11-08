namespace Pleiades.Plaintorch.Api;

/// <summary>
/// Request-wide flags a client can set on any API call through headers (PEP103), read by any service in the request's
/// scope. The API sets them before the endpoint runs (<c>PlaintorchRequestFlagsMiddleware</c>); a scope that serves no API
/// request — a note edit, a background pass — keeps the defaults: no flag, and nobody to ask.
/// </summary>
public sealed class PlaintorchRequestFlags
{
	/// <summary>
	/// The header carrying the request's state enforcement choice. Its one value, <c>auto</c>, accepts the compensations
	/// a change needs (<see cref="ForceCompensation"/>); any other value is refused with 400 Bad Request.
	/// </summary>
	public const string EnforcementHeader = "X-Plaintorch-Enforcement";

	/// <summary>
	/// The one value <see cref="EnforcementHeader"/> takes.
	/// </summary>
	public const string ForceCompensationValue = "auto";

	/// <summary>
	/// Gets or sets whether the scope serves an API request, whose client can be asked to confirm a change.
	/// </summary>
	public bool Interactive { get; set; }

	/// <summary>
	/// Gets or sets whether the client accepts the compensations a change needs. Under
	/// <see cref="Enforcement.BacklogStateEnforcementLevel.Warn"/>, the change then goes ahead with them instead of waiting
	/// for confirmation; it never lifts <see cref="Enforcement.BacklogStateEnforcementLevel.Block"/>, nor a systemic refusal.
	/// </summary>
	public bool ForceCompensation { get; set; }
}
