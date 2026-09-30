using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Api.Transport;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the backlog-schedule PLAINTORCH API services (PEP101).
/// </summary>
public sealed class BacklogScheduleModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IBacklogScheduleApi, BacklogScheduleApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var schedules = endpoints.MapEnrichedGroup("/api/schedules");

		schedules.MapGet("/", async (string? entityId, IBacklogScheduleApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListAsync(entityId, cancellationToken)));

		schedules.MapPost("/", async (AddBacklogScheduleRequest request, IBacklogScheduleApi api, CancellationToken cancellationToken) =>
		{
			var schedule = await api.AddAsync(request.EntityId, request.Type, request.State, request.Time, cancellationToken);
			return Results.Created($"/api/schedules/{schedule.Id}", schedule);
		});

		schedules.MapDelete("/{scheduleId:long}", async (long scheduleId, IBacklogScheduleApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteAsync(scheduleId, cancellationToken);
			return Results.NoContent();
		});
	}
}
