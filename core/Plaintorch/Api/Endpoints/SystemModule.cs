using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Services;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the system-facing PLAINTORCH API services.
/// </summary>
public sealed class SystemModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<ISystemApi, SystemApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/system");
		group.MapGet("/brief", async (ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.BriefAsync(cancellationToken));
		});

		endpoints.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
	}
}