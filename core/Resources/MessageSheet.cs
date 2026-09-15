using System.Globalization;
using System.Resources;

namespace Pleiades.Resources;

/// <summary>
/// The one way an embedded message sheet (a <c>Resources/*.resx</c>) is read: a key resolves against the current UI
/// culture with the neutral (English) sheet as fallback, and a parameterised template formats with the current culture.
/// A key missing from every sheet resolves to the bare accessor name rather than throwing — a message lookup must never
/// take a watcher run down — and the catalog tests pin that no accessor points at a missing key.
/// </summary>
internal static class MessageSheet
{
	/// <summary>Creates the manager for a sheet embedded under <c>Pleiades.Resources</c> (see the csproj's <c>LogicalName</c>).</summary>
	public static ResourceManager For(string sheetName)
		=> new($"Pleiades.Resources.{sheetName}", typeof(MessageSheet).Assembly);

	/// <summary>
	/// Resolves <c>&lt;group&gt;&lt;key&gt;</c> on a sheet for the current UI culture, or the bare key when the sheet
	/// has no such entry.
	/// </summary>
	public static string Resolve(ResourceManager sheet, string group, string key)
		=> sheet.GetString(group + key, CultureInfo.CurrentUICulture) ?? key;

	/// <summary>Resolves a template and formats it with the current culture.</summary>
	public static string Format(ResourceManager sheet, string group, string key, params object?[] args)
		=> string.Format(CultureInfo.CurrentCulture, Resolve(sheet, group, key), args);
}
