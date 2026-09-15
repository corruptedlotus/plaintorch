namespace Pleiades.Vault.Markdown;

/// <summary>
/// Describes a markdown validation or deserialization problem for a specific field path.
/// </summary>
/// <param name="FieldPath">The logical field path, such as <c>status</c> or <c>forecast.forecastTarget</c>.</param>
/// <param name="Message">The validation or conversion message (operator-facing prose from <c>MarkdownMessages</c>; never parsed).</param>
/// <param name="RawValue">The raw frontmatter value when available.</param>
/// <param name="Code">
/// A stable <see cref="MarkdownValidationCodes"/> code for the issue kinds code needs to recognise structurally, so
/// classification never sniffs the (localisable) message text. <see langword="null"/> for issues nothing classifies.
/// </param>
public sealed record MarkdownValidationIssue(string FieldPath, string Message, string? RawValue = null, string? Code = null);

/// <summary>
/// The stable codes a <see cref="MarkdownValidationIssue"/> can carry for the issue kinds code recognises structurally.
/// </summary>
public static class MarkdownValidationCodes
{
	/// <summary>The path resolves no identity and the entity's PUCK needs caller input (cannot be auto-generated).</summary>
	public const string MissingRequiredPuckInput = "missing-required-puck-input";
}

/// <summary>
/// Captures the result of mapping markdown frontmatter into a CLR model.
/// </summary>
/// <typeparam name="T">The model type.</typeparam>
/// <param name="Model">The hydrated model instance.</param>
/// <param name="Issues">Any validation or conversion issues that were found.</param>
public sealed record MarkdownDeserializationResult<T>(T Model, IReadOnlyList<MarkdownValidationIssue> Issues)
{
	/// <summary>
	/// Gets a value indicating whether the markdown mapped without validation issues.
	/// </summary>
	public bool IsValid => Issues.Count == 0;
}
