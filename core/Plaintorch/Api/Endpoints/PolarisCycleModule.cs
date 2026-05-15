using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Api.Transport;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the Polaris cycle-facing PLAINTORCH API services.
/// </summary>
public sealed class PolarisCycleModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IPolarisCycleApi, PolarisCycleApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var cycles = endpoints.MapGroup("/api/polaris");

		cycles.MapGet("/current", async (IPolarisCycleApi api, CancellationToken cancellationToken) =>
		{
			var cycle = await api.GetAsync(null, cancellationToken);
			return cycle is null ? Results.NotFound() : Results.Ok(cycle);
		});

		cycles.MapGet("/forecasts", async (IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListForecastsAsync(cancellationToken)));

		cycles.MapGet("/{polarisCycleId}", async (string polarisCycleId, IPolarisCycleApi api, CancellationToken cancellationToken) =>
		{
			var cycle = await api.GetAsync(polarisCycleId, cancellationToken);
			return cycle is null ? Results.NotFound() : Results.Ok(cycle);
		});

		cycles.MapPost("/plan", async (PolarisCyclePlanRequest request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
		{
			var cycle = await api.PlanAsync(request.ForecastReference, request.DaysAhead, request.Body, cancellationToken);
			return Results.Created($"/api/polaris/{cycle.Id}", cycle);
		});

		cycles.MapPost("/start-new", async (PolarisCycleTimeRequest request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.StartNewAsync(request.Time, null, cancellationToken)));

		cycles.MapPost("/{polarisCycleId}/begin", async (string polarisCycleId, PolarisCycleTimeRequest request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.BeginAsync(polarisCycleId, request.Time, cancellationToken)));

		cycles.MapPost("/current/begin", async (PolarisCycleTimeRequest request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.BeginAsync(null, request.Time, cancellationToken)));

		cycles.MapPost("/{polarisCycleId}/end", async (string polarisCycleId, PolarisCycleTimeRequest request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.EndAsync(polarisCycleId, request.Time, cancellationToken)));

		cycles.MapPost("/current/end", async (PolarisCycleTimeRequest request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.EndAsync(null, request.Time, cancellationToken)));

		cycles.MapPost("/{polarisCycleId}/executives/plan", async (string polarisCycleId, PolarisExecutivePlan request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.PlanExecutiveAsync(request, polarisCycleId, cancellationToken)));

		cycles.MapPost("/current/executives/plan", async (PolarisExecutivePlan request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.PlanExecutiveAsync(request, null, cancellationToken)));

		cycles.MapPost("/{polarisCycleId}/reflectives/draw", async (string polarisCycleId, ReflectiveDrawRequest request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.DrawReflectivesAsync(request with { PolarisCycleId = polarisCycleId }, cancellationToken)));

		cycles.MapPost("/current/reflectives/draw", async (ReflectiveDrawRequest request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.DrawReflectivesAsync(request with { PolarisCycleId = null }, cancellationToken)));

		var executives = endpoints.MapGroup("/api/executives");
		executives.MapPut("/{executiveId:long}", async (long executiveId, ExecutiveUpdate request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateExecutiveAsync(executiveId, request, cancellationToken)));

		var reflectives = endpoints.MapGroup("/api/reflectives");
		reflectives.MapPut("/{reflectiveId:long}", async (long reflectiveId, ReflectiveUpdate request, IPolarisCycleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateReflectiveAsync(reflectiveId, request, cancellationToken)));
	}
}