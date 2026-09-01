using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Api.Transport;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the media-facing PLAINTORCH API (PEP105): storing and listing assets, decoupled from the fields that
/// reference them.
/// </summary>
public sealed class MediaModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IMediaApi, MediaApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapEnrichedGroup("/api/media");

		// Vault-level (shared) media at the vault root.
		group.MapGet("/vault", async (IMediaApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListVaultAssetsAsync(cancellationToken)));

		group.MapPost("/vault", async (MediaUpload request, IMediaApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UploadVaultAsync(request, cancellationToken)));

		// Entity-level (self) media in an entity's own asset folder.
		group.MapGet("/entity/{entityType}/{entityId}", async (string entityType, string entityId, IMediaApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListEntityAssetsAsync(entityType, entityId, cancellationToken)));

		group.MapPost("/entity/{entityType}/{entityId}", async (string entityType, string entityId, MediaUpload request, IMediaApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UploadEntityAsync(entityType, entityId, request, cancellationToken)));
	}
}
