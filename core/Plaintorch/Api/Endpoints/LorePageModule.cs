using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Markdown;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the lore page-facing PLAINTORCH API services.
/// </summary>
public sealed class LorePageModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<ILorePageApi, LorePageApiService>();
		// Lore's per-type save behavior (re-homing a page under a reassigned parent) is an opt-in save hook rather than
		// a special case in the central markdown storage service.
		services.AddScoped<IEntitySaveHook, LorePageSaveHook>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapEnrichedGroup("/api/lorepages");

		group.MapGet("/", async (ILorePageApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListAsync(cancellationToken)));

		group.MapPost("/", async (LorePageCreateRequest request, ILorePageApi api, CancellationToken cancellationToken) =>
		{
			var created = await api.CreateAsync(request, cancellationToken);
			return Results.Created($"/api/lorepages/{created.Puck}", created);
		});

		group.MapPost("/renumber", async (LorePageRenumberRequest request, ILorePageApi api, CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(request.Puck))
			{
				return Results.BadRequest("A lore PUCK is required.");
			}

			var updated = await api.SetIndexAsync(request.Puck, request.Index, cancellationToken);
			return updated is null ? Results.NotFound() : Results.Ok(updated);
		});

		group.MapGet("/{*puck:minlength(1)}", async (string puck, ILorePageApi api, CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(puck))
			{
				return Results.BadRequest("A lore PUCK is required.");
			}

			var lorePage = await api.GetAsync(puck, cancellationToken);
			return lorePage is null ? Results.NotFound() : Results.Ok(lorePage);
		});

		group.MapPut("/{*puck:minlength(1)}", async (string puck, LorePageUpdate request, ILorePageApi api, CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(puck))
			{
				return Results.BadRequest("A lore PUCK is required.");
			}

			var updated = await api.UpdateAsync(puck, request, cancellationToken);
			return updated is null ? Results.NotFound() : Results.Ok(updated);
		});

		group.MapDelete("/{*puck:minlength(1)}", async (string puck, ILorePageApi api, CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(puck))
			{
				return Results.BadRequest("A lore PUCK is required.");
			}

			var deleted = await api.DeleteAsync(puck, cancellationToken);
			return deleted ? Results.NoContent() : Results.NotFound();
		});
	}
}
