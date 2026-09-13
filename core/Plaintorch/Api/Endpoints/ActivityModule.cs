using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the activity-facing PLAINTORCH API: a unified search over objectives and decrees, mirroring the
/// objective search endpoint.
/// </summary>
public sealed class ActivityModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IActivityApi, ActivityApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapEnrichedGroup("/api/activities");

		group.MapGet("/", async (string? q, int? take, IActivityApi api, CancellationToken cancellationToken) =>
		{
			return q is null
				? Results.Ok(await api.ListAsync(cancellationToken))
				: Results.Ok(await api.FindAsync(new SearchRequest(q, take), cancellationToken));
		});
	}
}
