using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Api.Transport;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the onrush sprint-facing PLAINTORCH API services.
/// </summary>
public sealed class OnrushSprintModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IOnrushSprintApi, OnrushSprintApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/onrush");

		group.MapGet("/", async (IOnrushSprintApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListAsync(cancellationToken)));

		group.MapGet("/current", async (IOnrushSprintApi api, CancellationToken cancellationToken) =>
		{
			var sprint = await api.GetAsync(null, cancellationToken);
			return sprint is null ? Results.NotFound() : Results.Ok(sprint);
		});

		group.MapGet("/planning", async (IOnrushSprintApi api, CancellationToken cancellationToken) =>
		{
			var sprint = await api.GetPlanningAsync(cancellationToken);
			return sprint is null ? Results.NotFound() : Results.Ok(sprint);
		});

		group.MapGet("/available", async (IOnrushSprintApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.GetAvailableAsync(cancellationToken)));

		group.MapGet("/{onrushSprintId}", async (string onrushSprintId, IOnrushSprintApi api, CancellationToken cancellationToken) =>
		{
			var sprint = await api.GetAsync(onrushSprintId, cancellationToken);
			return sprint is null ? Results.NotFound() : Results.Ok(sprint);
		});

		group.MapPut("/{onrushSprintId}", async (string onrushSprintId, OnrushSprintUpdate request, IOnrushSprintApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateAsync(onrushSprintId, request, cancellationToken)));

		group.MapPost("/plan", async (OnrushSprintPlan request, IOnrushSprintApi api, CancellationToken cancellationToken) =>
		{
			var sprint = await api.PlanAsync(request, cancellationToken);
			return Results.Created($"/api/onrush/{sprint.Id}", sprint);
		});

		group.MapPost("/start-new", async (OnrushSprintDateRequest request, IOnrushSprintApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.StartNewAsync(request.Date, cancellationToken)));

		group.MapPost("/{onrushSprintId}/begin", async (string onrushSprintId, OnrushSprintDateRequest request, IOnrushSprintApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.BeginAsync(onrushSprintId, request.Date, cancellationToken)));

		group.MapPost("/{onrushSprintId}/end", async (string onrushSprintId, OnrushSprintDateRequest request, IOnrushSprintApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.EndAsync(onrushSprintId, request.Date, cancellationToken)));

		group.MapPost("/{onrushSprintId}/assign-onrush", async (string onrushSprintId, IOnrushSprintApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.AssignAllOnrushStateObjectivesToSelfAsync(onrushSprintId, cancellationToken)));
	}
}