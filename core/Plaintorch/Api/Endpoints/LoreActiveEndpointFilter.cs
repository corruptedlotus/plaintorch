using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Saga;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Stamps <see cref="LorePage.IsActive"/> on every lore page reachable from an API response, on the fly, just
/// before serialization — the one place the active spine is applied, so no endpoint hand-stamps it. Mirrors
/// <see cref="MediaEnrichmentEndpointFilter"/>, resolving the spine once from <see cref="LoreActiveCache"/>.
/// </summary>
public sealed class LoreActiveEndpointFilter : IEndpointFilter
{
	private static readonly ConcurrentDictionary<Type, IReadOnlyList<PropertyInfo>> SerializablePropertyCache = new();

	/// <inheritdoc />
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		var result = await next(context);
		var response = result is IValueHttpResult valueResult ? valueResult.Value : result;

		var services = context.HttpContext.RequestServices;
		var cache = services.GetRequiredService<LoreActiveCache>();
		var dbContext = services.GetRequiredService<PlainfraContext>();
		var session = services.GetRequiredService<ActiveVaultSession>();

		var activeIds = await cache.GetActiveIdsAsync(dbContext.LorePages, session.Generation, context.HttpContext.RequestAborted);
		Stamp(response, activeIds, new HashSet<object>(ReferenceEqualityComparer.Instance));
		return result;
	}

	private static void Stamp(object? value, IReadOnlySet<string> activeIds, ISet<object> visited)
	{
		if (value is null || value is string || value.GetType().IsValueType)
		{
			return;
		}

		if (!visited.Add(value))
		{
			return;
		}

		if (value is LorePage page)
		{
			page.IsActive = activeIds.Contains(page.Id);
		}

		if (value is IDictionary dictionary)
		{
			foreach (DictionaryEntry entry in dictionary)
			{
				Stamp(entry.Value, activeIds, visited);
			}

			return;
		}

		if (value is IEnumerable values)
		{
			foreach (var item in values)
			{
				Stamp(item, activeIds, visited);
			}

			return;
		}

		foreach (var property in GetSerializableProperties(value.GetType()))
		{
			Stamp(property.GetValue(value), activeIds, visited);
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
