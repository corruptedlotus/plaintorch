using System.Resources;
using System.Runtime.CompilerServices;

namespace Pleiades.Resources;

/// <summary>
/// Typed access to the frontmatter validation message sheet (<c>MarkdownMessages.resx</c>): the text of every
/// <c>MarkdownValidationIssue</c> the serializer and discovery raise. Each member reads the sheet entry of its own name,
/// resolved at call time against the current UI culture. Classification never reads this text — an issue that code must
/// recognise carries a <c>MarkdownValidationCodes</c> code alongside its message.
/// </summary>
public static class MarkdownMessages
{
	private static readonly ResourceManager Sheet = MessageSheet.For(nameof(MarkdownMessages));

	private static string Get([CallerMemberName] string key = "") => MessageSheet.Resolve(Sheet, string.Empty, key);

	private static string Format(object?[] args, [CallerMemberName] string key = "") => MessageSheet.Format(Sheet, string.Empty, key, args);

	public static string FieldRequired => Get();
	public static string FieldEmpty => Get();
	public static string MissingRequiredPuckInput => Get();
	public static string ExecutiveOrderSprintUnresolved => Get();

	/// <summary>"Value '{value}' is not a defined {type} state."</summary>
	public static string EnumValueUndefined(object? value, string typeName) => Format([value, typeName]);

	/// <summary>"Value '{value}' is not a valid {type} state. Expected one of: {expected}."</summary>
	public static string EnumValueInvalid(string value, string typeName, string expected) => Format([value, typeName, expected]);

	/// <summary>"Value '{value}' is not a valid {type} PUCK: {reason}"</summary>
	public static string RelatedPuckInvalid(string value, string typeName, string reason) => Format([value, typeName, reason]);
}
