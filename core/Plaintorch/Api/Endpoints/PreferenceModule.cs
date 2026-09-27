using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Preferences;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the user-preferences API (PEP116). The listing is registry-driven off <see cref="PreferenceCatalog"/>,
/// so a new preference is exposed automatically once it is added to the catalog — no endpoint change.
/// </summary>
public sealed class PreferenceModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddSingleton<PreferenceCatalog>();
		services.AddScoped<IPreferenceApi, PreferenceApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapEnrichedGroup("/api/preferences");

		group.MapGet("/", async (IPreferenceApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListAsync(cancellationToken)));

		group.MapPut("/{key}", async (string key, PreferenceUpdateRequest request, IPreferenceApi api, CancellationToken cancellationToken) =>
		{
			try
			{
				var view = await api.SetAsync(key, request.Value, cancellationToken);
				return view is null ? Results.NotFound() : Results.Ok(view);
			}
			catch (PreferenceValidationException exception)
			{
				return Results.BadRequest(exception.Message);
			}
		});

		group.MapDelete("/{key}", async (string key, IPreferenceApi api, CancellationToken cancellationToken) =>
		{
			var view = await api.ResetAsync(key, cancellationToken);
			return view is null ? Results.NotFound() : Results.Ok(view);
		});
	}
}
