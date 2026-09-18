using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using Pleiades.Resources;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Pins the message sheets (<c>core/Resources/*.resx</c>) to the typed accessors that read them, so the prose stays
/// centralised and every accessor points at a real entry.
/// </summary>
/// <remarks>
/// A missing entry does not throw at runtime — the sheet resolves the bare key so a lookup can never take a watcher run
/// down — which is exactly why it has to fail here instead: an accessor added without its sheet row, a row renamed
/// without its accessor, or a template whose placeholders drift from the accessor's parameters would otherwise reach
/// the operator as a raw key or a <see cref="FormatException"/>. The sheets are also the seam for localisation, so a
/// culture sheet is expected to mirror these keys exactly.
/// </remarks>
public sealed class MessageSheetCatalogTests
{
	private const BindingFlags Accessors = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
	private static readonly Regex Placeholder = new(@"\{(\d+)", RegexOptions.Compiled);

	[Fact]
	public void Every_catalogued_reason_code_resolves_a_sheet_message()
	{
		var keys = SheetKeys(SheetFor(typeof(WatcherMessages)));
		Assert.NotEmpty(WatcherOperations.ReasonCodes);

		foreach (var code in WatcherOperations.ReasonCodes)
		{
			var descriptor = WatcherOperations.Describe(code);
			Assert.Contains("Reasons." + descriptor.MessageKey, keys);
			Assert.False(string.IsNullOrWhiteSpace(descriptor.Message), $"{code} resolves an empty message");
			Assert.NotEqual(descriptor.MessageKey, descriptor.Message);
		}
	}

	[Fact]
	public void An_uncatalogued_reason_code_describes_itself_rather_than_throwing()
	{
		var descriptor = WatcherOperations.Describe("no-such-reason");
		Assert.Equal("no-such-reason", descriptor.Message);
	}

	[Theory]
	[InlineData(typeof(WatcherMessages))]
	[InlineData(typeof(MarkdownMessages))]
	public void Accessors_and_sheet_entries_agree_one_to_one(Type accessor)
	{
		var expected = AccessorKeys(accessor).Keys.Order(StringComparer.Ordinal).ToArray();
		var actual = SheetKeys(SheetFor(accessor)).Order(StringComparer.Ordinal).ToArray();

		Assert.NotEmpty(expected);
		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData(typeof(WatcherMessages))]
	[InlineData(typeof(MarkdownMessages))]
	public void Parameterised_templates_declare_exactly_their_placeholders(Type accessor)
	{
		var sheet = SheetFor(accessor);
		foreach (var (key, member) in AccessorKeys(accessor))
		{
			var template = sheet.GetString(key, CultureInfo.InvariantCulture) ?? string.Empty;
			var indices = Placeholder.Matches(template).Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
			var parameterCount = member is MethodInfo method ? method.GetParameters().Length : 0;

			Assert.True(
				indices.SequenceEqual(Enumerable.Range(0, parameterCount)),
				$"{key}: template uses placeholders [{string.Join(",", indices)}] but the accessor takes {parameterCount} argument(s)");
		}
	}

	[Fact]
	public void Messages_resolve_from_the_embedded_sheets()
	{
		// A smoke test that the manifest name the accessors resolve matches what the build embedded.
		Assert.Equal("Field is required.", MarkdownMessages.FieldRequired);
		Assert.Equal("Asserted by 2 files: a, b", WatcherMessages.Details.DuplicateIdentity(2, "a, b"));
		Assert.Equal(WatcherMessages.Reasons.ForeignFile, WatcherOperations.Describe(WatcherOperations.ForeignFile).Message);
	}

	private static ResourceManager SheetFor(Type accessor)
		=> new($"Pleiades.Resources.{accessor.Name}", accessor.Assembly);

	private static HashSet<string> SheetKeys(ResourceManager sheet)
	{
		var entries = sheet.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)
			?? throw new InvalidOperationException($"Sheet '{sheet.BaseName}' is not embedded.");
		return entries.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet(StringComparer.Ordinal);
	}

	// Every public static property or method on the accessor (flat keys) and on each of its public nested groups
	// (keys prefixed "<Group>."), which is the naming contract the accessors' [CallerMemberName] lookups rely on.
	private static Dictionary<string, MemberInfo> AccessorKeys(Type accessor)
	{
		var keys = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
		Collect(accessor, string.Empty, keys);
		foreach (var group in accessor.GetNestedTypes(BindingFlags.Public))
		{
			Collect(group, group.Name + ".", keys);
		}

		return keys;
	}

	private static void Collect(Type type, string prefix, Dictionary<string, MemberInfo> into)
	{
		foreach (var property in type.GetProperties(Accessors))
		{
			into[prefix + property.Name] = property;
		}

		foreach (var method in type.GetMethods(Accessors).Where(static method => !method.IsSpecialName))
		{
			into[prefix + method.Name] = method;
		}
	}
}
