using System.Text.Json.Serialization;

namespace Pleiades.Vault.Media;

/// <summary>
/// How a media key resolves (PEP105).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaKind>))]
public enum MediaKind
{
	/// <summary>A plain glyph or lucide name — a built-in icon, not a stored file. Carries no resolved path.</summary>
	[JsonStringEnumMemberName("icon")]
	Icon,

	/// <summary>Self/level media: a <c>media:</c> file in the owning entity's own asset folder.</summary>
	[JsonStringEnumMemberName("media")]
	Media,

	/// <summary>Vault-level media: a <c>vault:</c> file in the vault root's shared asset folder.</summary>
	[JsonStringEnumMemberName("vault")]
	Vault,
}

/// <summary>
/// The resolved view of a media key (PEP105): the companion an entity carries beside each <see cref="MediaAttribute"/>
/// key. It names how the key resolves and, for custom media, where the file lives — so a client renders it without
/// knowing the asset-folder convention.
/// </summary>
/// <param name="Key">The raw stored key, e.g. <c>media:crest.png</c>, <c>vault:logo.png</c>, or <c>lucide:star</c>.</param>
/// <param name="Type">How the key resolves.</param>
/// <param name="Path">The forward-slashed vault-relative path for custom media; <see langword="null"/> for a glyph.</param>
public sealed record MediaReference(string Key, MediaKind Type, string? Path);
