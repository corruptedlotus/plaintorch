namespace Pleiades.Orchestration;

/// <summary>
/// Converts semantic tag colors into stable UI tokens.
/// </summary>
public static class TagPalette
{
	/// <summary>
	/// Gets the semantic color token for a tag color.
	/// </summary>
	public static string GetColorToken(TagColor color)
		=> $"plaintorch.tag.{color.ToString().ToLowerInvariant()}";

	/// <summary>
	/// Gets the CSS variable name for a tag color.
	/// </summary>
	public static string GetCssVariable(TagColor color)
		=> $"--plaintorch-tag-{color.ToString().ToLowerInvariant()}";
}