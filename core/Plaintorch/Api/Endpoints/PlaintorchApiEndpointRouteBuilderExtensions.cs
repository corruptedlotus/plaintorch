namespace Pleiades.Plaintorch.Api.Endpoints;


/// <summary>
/// Maps a PLAINTORCH API route group with response-level media enrichment enabled.
/// </summary>
public static class PlaintorchApiEndpointRouteBuilderExtensions
{
	/// <summary>
	/// Maps an API route group that enriches every <see cref="Pleiades.Vault.Media.MediaAttribute"/> field in its
	/// response object graph before JSON serialization.
	/// </summary>
	public static RouteGroupBuilder MapEnrichedGroup(this IEndpointRouteBuilder endpoints, string pattern)
	{
		ArgumentNullException.ThrowIfNull(endpoints);
		ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
		return endpoints.MapGroup(pattern)
			.AddEndpointFilter<MediaEnrichmentEndpointFilter>()
			.AddEndpointFilter<NoteReadinessEndpointFilter>();
	}
}