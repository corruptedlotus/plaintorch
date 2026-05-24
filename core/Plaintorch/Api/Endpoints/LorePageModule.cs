using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Services;

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
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/lorepages");

		group.MapGet("/", async (ILorePageApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListAsync(cancellationToken)));

		group.MapGet("/{*puck}", async (string puck, ILorePageApi api, CancellationToken cancellationToken) =>
		{
			if (string.IsNullOrWhiteSpace(puck))
			{
				return Results.BadRequest("A lore PUCK is required.");
			}

			var lorePage = await api.GetAsync(puck, cancellationToken);
			return lorePage is null ? Results.NotFound() : Results.Ok(lorePage);
		});
	}
}
