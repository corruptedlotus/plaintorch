using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the declarative-facing PLAINTORCH API services (PEP100).
/// </summary>
public sealed class DeclarativeModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IDeclarativeApi, DeclarativeApiService>();
		services.AddScoped<PlaintorchOrbitService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var fates = endpoints.MapEnrichedGroup("/api/fates");

		fates.MapGet("/", async (IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListFatesAsync(cancellationToken)));

		fates.MapGet("/{fateId}", async (string fateId, IDeclarativeApi api, CancellationToken cancellationToken) =>
		{
			var fate = await api.GetFateAsync(fateId, cancellationToken);
			return fate is null ? Results.NotFound() : Results.Ok(fate);
		});

		fates.MapPost("/", async (FatePlan request, IDeclarativeApi api, CancellationToken cancellationToken) =>
		{
			var fate = await api.CreateFateAsync(request, cancellationToken);
			return Results.Created($"/api/fates/{fate.Id}", fate);
		});

		fates.MapPut("/{fateId}", async (string fateId, FateUpdate request, IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateFateAsync(fateId, request, cancellationToken)));

		fates.MapDelete("/{fateId}", async (string fateId, IDeclarativeApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteFateAsync(fateId, cancellationToken);
			return Results.NoContent();
		});

		fates.MapPost("/{fateId}/begin", async (string fateId, IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.BeginFateBoundaryAsync(fateId, cancellationToken)));

		var decrees = endpoints.MapEnrichedGroup("/api/decrees");

		decrees.MapGet("/", async (IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListDecreesAsync(cancellationToken)));

		decrees.MapGet("/{decreeId}", async (string decreeId, IDeclarativeApi api, CancellationToken cancellationToken) =>
		{
			var decree = await api.GetDecreeAsync(decreeId, cancellationToken);
			return decree is null ? Results.NotFound() : Results.Ok(decree);
		});

		decrees.MapPost("/", async (DecreePlan request, IDeclarativeApi api, CancellationToken cancellationToken) =>
		{
			var decree = await api.CreateDecreeAsync(request, cancellationToken);
			return Results.Created($"/api/decrees/{decree.Id}", decree);
		});

		decrees.MapPut("/{decreeId}", async (string decreeId, DecreeUpdate request, IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateDecreeAsync(decreeId, request, cancellationToken)));

		decrees.MapDelete("/{decreeId}", async (string decreeId, IDeclarativeApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteDecreeAsync(decreeId, cancellationToken);
			return Results.NoContent();
		});

		decrees.MapPost("/{decreeId}/begin", async (string decreeId, IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.BeginDecreeBoundaryAsync(decreeId, cancellationToken)));

		var eventives = endpoints.MapEnrichedGroup("/api/eventives");

		eventives.MapGet("/", async (string? fateId, string? objectiveId, IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListEventivesAsync(fateId, objectiveId, cancellationToken)));

		eventives.MapPut("/", async (EventiveUpdateRequest request, IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateEventiveAsync(request.Occurrence, request.Update, cancellationToken)));

		var attentives = endpoints.MapEnrichedGroup("/api/attentives");

		attentives.MapGet("/", async (string? decreeId, IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListAttentivesAsync(decreeId, cancellationToken)));

		attentives.MapPut("/", async (AttentiveUpdateRequest request, IDeclarativeApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateAttentiveAsync(request.Occurrence, request.Update, cancellationToken)));
	}
}
