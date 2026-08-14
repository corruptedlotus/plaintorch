namespace Pleiades.Vault.Media;

/// <summary>
/// Marks a string property as a media key (PEP105), so <see cref="VaultMediaService.EnrichMedia"/> resolves it
/// model-agnostically into a companion <see cref="MediaReference"/>.
/// </summary>
/// <remarks>
/// The companion is a sibling property named <c>{PropertyName}Media</c> — for an <c>Icon</c> key, an
/// <c>IconMedia</c> of type <see cref="MediaReference"/>. Mark it <c>[NotMapped]</c>: it is transient, filled on
/// the way out and never persisted. The attribute is orthogonal to <c>[MarkdownField]</c>; a media key that
/// round-trips through frontmatter carries both.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MediaAttribute : Attribute
{
}
