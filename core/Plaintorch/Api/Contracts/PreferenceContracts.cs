using System.Text.Json;

namespace Pleiades.Plaintorch.Api.Contracts;

/// <summary>
/// One preference as the settings surface sees it (PEP116): its metadata, the current resolved value, and the
/// code-owned default. <see cref="Value"/> and <see cref="Default"/> are the JSON values themselves (a number,
/// boolean, or string), matching <see cref="Kind"/>.
/// </summary>
public sealed record PreferenceView(
	string Key,
	string Label,
	string Group,
	string? Description,
	string Kind,
	JsonElement Value,
	JsonElement Default,
	IReadOnlyList<string>? Options);

/// <summary>A preference write: the new JSON value for the keyed preference.</summary>
public sealed record PreferenceUpdateRequest(JsonElement Value);
