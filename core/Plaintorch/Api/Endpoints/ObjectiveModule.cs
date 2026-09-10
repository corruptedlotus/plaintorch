using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Api.Transport;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the objective-facing PLAINTORCH API services.
/// </summary>
public sealed class ObjectiveModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IObjectiveApi, ObjectiveApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapEnrichedGroup("/api/objectives");

		group.MapGet("/", async (string? q, int? take, IObjectiveApi api, CancellationToken cancellationToken) =>
		{
			return q is null
				? Results.Ok(await api.ListAsync(cancellationToken))
				: Results.Ok(await api.FindAsync(new SearchRequest(q, take), cancellationToken));
		});

		group.MapGet("/{objectiveId}", async (string objectiveId, IObjectiveApi api, CancellationToken cancellationToken) =>
		{
			var objective = await api.GetAsync(objectiveId, cancellationToken);
			return objective is null ? Results.NotFound() : Results.Ok(objective);
		});

		group.MapPost("/", async (CreateObjectiveRequest request, IObjectiveApi api, CancellationToken cancellationToken) =>
		{
			var objective = string.IsNullOrWhiteSpace(request.DirectiveId)
				? await api.CreateStandaloneAsync(request.Title, request.IsEnduring, request.Id, cancellationToken)
				: await api.CreateFromDirectiveAsync(request.DirectiveId, request.Title, request.OnrushSprintId, request.IsEnduring, request.Id, cancellationToken);
			return Results.Created($"/api/objectives/{objective.Id}", objective);
		});

		group.MapPut("/{objectiveId}", async (string objectiveId, ObjectiveUpdate request, IObjectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateAsync(objectiveId, request, cancellationToken)));

		group.MapPost("/{objectiveId}/workflow", async (string objectiveId, ObjectiveWorkflowShift request, IObjectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ShiftWorkflowAsync(objectiveId, request, cancellationToken)));

		group.MapPost("/{objectiveId}/onrush", async (string objectiveId, AddObjectiveToOnrushRequest request, IObjectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.AddToOnrushAsync(objectiveId, request.OnrushSprintId, cancellationToken)));

		group.MapDelete("/{objectiveId}/onrush", async (string objectiveId, IObjectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.RemoveFromOnrushAsync(objectiveId, cancellationToken)));

		group.MapPost("/{objectiveId}/begin", async (string objectiveId, IObjectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.BeginBoundaryAsync(objectiveId, cancellationToken)));

		group.MapPost("/init", async (InitFromFileRequest request, IObjectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.InitializeFromPathAsync(request.Path, cancellationToken)));

		group.MapDelete("/{objectiveId}", async (string objectiveId, IObjectiveApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteAsync(objectiveId, cancellationToken);
			return Results.NoContent();
		});
	}
}