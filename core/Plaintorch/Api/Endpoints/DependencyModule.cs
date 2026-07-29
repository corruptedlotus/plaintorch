using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Api.Transport;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the dependency-facing PLAINTORCH API services (PEP101).
/// </summary>
public sealed class DependencyModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IDependencyApi, DependencyApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var dependencies = endpoints.MapGroup("/api/dependencies");

		dependencies.MapGet("/", async (string? entityId, IDependencyApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListAsync(entityId, cancellationToken)));

		dependencies.MapPost("/", async (CreateDependencyRequest request, IDependencyApi api, CancellationToken cancellationToken) =>
		{
			var dependency = await api.CreateAsync(ToRef(request.Source), ToRef(request.Target), request.Trigger, request.Constraint, cancellationToken);
			return Results.Created($"/api/dependencies/{dependency.Id}", dependency);
		});

		dependencies.MapGet("/lock/{entityId}", async (string entityId, IDependencyApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.GetLockAsync(entityId, cancellationToken)));

		dependencies.MapDelete("/{dependencyId:long}", async (long dependencyId, IDependencyApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteAsync(dependencyId, cancellationToken);
			return Results.NoContent();
		});

		var checkpoints = endpoints.MapGroup("/api/checkpoints");

		checkpoints.MapGet("/", async (IDependencyApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListCheckpointsAsync(cancellationToken)));

		checkpoints.MapPost("/", async (CreateCheckpointRequest request, IDependencyApi api, CancellationToken cancellationToken) =>
		{
			var checkpoint = await api.CreateCheckpointAsync(request.Title, request.Id, request.CelestronToll, request.ExternalCondition, cancellationToken);
			return Results.Created($"/api/checkpoints/{checkpoint.Id}", checkpoint);
		});

		checkpoints.MapGet("/{checkpointId}", async (string checkpointId, IDependencyApi api, CancellationToken cancellationToken) =>
		{
			var checkpoint = await api.GetCheckpointAsync(checkpointId, cancellationToken);
			return checkpoint is null ? Results.NotFound() : Results.Ok(checkpoint);
		});

		checkpoints.MapDelete("/{checkpointId}", async (string checkpointId, IDependencyApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteCheckpointAsync(checkpointId, cancellationToken);
			return Results.NoContent();
		});

		checkpoints.MapPost("/{checkpointId}/toll", async (string checkpointId, IDependencyApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.PayTollAsync(checkpointId, cancellationToken)));

		checkpoints.MapPost("/{checkpointId}/condition", async (string checkpointId, SetCheckpointConditionRequest request, IDependencyApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.SetExternalConditionAsync(checkpointId, request.Met, cancellationToken)));
	}

	private static EndpointRef ToRef(DependencyEndpointRequest request)
	{
		return new EndpointRef(request.Kind, request.Id, request.RecurrenceDate, request.RecurrenceTime);
	}
}
