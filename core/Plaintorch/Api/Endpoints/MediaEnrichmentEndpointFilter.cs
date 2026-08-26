using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Media;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Enriches media companions on an API response immediately before JSON serialization.
/// </summary>
public sealed class MediaEnrichmentEndpointFilter : IEndpointFilter
{
	/// <inheritdoc />
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		var result = await next(context);
		var response = result is IValueHttpResult valueResult ? valueResult.Value : result;
		var enricher = context.HttpContext.RequestServices.GetRequiredService<MediaResponseEnricher>();
		await enricher.EnrichAsync(response, context.HttpContext.RequestAborted);
		return result;
	}
}
