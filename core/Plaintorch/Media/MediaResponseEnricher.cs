using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using Pleiades.Vault.Media;

namespace Pleiades.Plaintorch.Media;

/// <summary>
/// Enriches every media-bearing object in an API response graph before it is serialized.
/// </summary>
/// <remarks>
/// An object participates by declaring one or more <see cref="MediaAttribute"/> properties. Self
/// (<c>media:</c>) references use an entity-owned folder when one is available. Vault references and glyphs do
/// not require a self folder.
/// </remarks>
public sealed class MediaResponseEnricher(
	VaultMediaService mediaService,
	MediaAssetFolderResolver assetFolderResolver)
{
	private static readonly ConcurrentDictionary<Type, IReadOnlyList<PropertyInfo>> SerializablePropertyCache = new();

	/// <summary>
	/// Enriches every object with a <see cref="MediaAttribute"/> field reachable from <paramref name="response"/>.
	/// </summary>
	public Task EnrichAsync(object? response, CancellationToken cancellationToken = default)
	{
		return EnrichValueAsync(response, new HashSet<object>(ReferenceEqualityComparer.Instance), cancellationToken);
	}

	private async Task EnrichValueAsync(object? value, ISet<object> visited, CancellationToken cancellationToken)
	{
		if (value is null || value is string || value.GetType().IsValueType)
		{
			return;
		}

		if (!visited.Add(value))
		{
			return;
		}

		if (mediaService.HasMedia(value))
		{
			var selfAssetFolder = mediaService.HasSelfMedia(value)
				? await assetFolderResolver.TryResolveAsync(value, cancellationToken)
				: null;
			mediaService.EnrichMedia(value, selfAssetFolder);
		}

		if (value is IDictionary dictionary)
		{
			foreach (DictionaryEntry entry in dictionary)
			{
				await EnrichValueAsync(entry.Value, visited, cancellationToken);
			}

			return;
		}

		if (value is IEnumerable values)
		{
			foreach (var item in values)
			{
				await EnrichValueAsync(item, visited, cancellationToken);
			}

			return;
		}

		foreach (var property in GetSerializableProperties(value.GetType()))
		{
			await EnrichValueAsync(property.GetValue(value), visited, cancellationToken);
		}
	}

	private static IReadOnlyList<PropertyInfo> GetSerializableProperties(Type type)
	{
		return SerializablePropertyCache.GetOrAdd(type, static valueType =>
			valueType
				.GetProperties(BindingFlags.Instance | BindingFlags.Public)
				.Where(property => property.CanRead
					&& property.GetIndexParameters().Length == 0
					&& property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
				.ToArray());
	}
}
