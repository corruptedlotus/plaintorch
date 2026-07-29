using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Changes;
using Pleiades.Plaintorch.Api.Services;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the system-facing PLAINTORCH API services.
/// </summary>
public sealed class SystemModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<ISystemApi, SystemApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/system");
		group.MapGet("/briefing", async (ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.GetBriefingAsync(cancellationToken));
		});

		// Long-lived: holds the response open and streams entity changes as they are saved, so a client
		// learns about writes it did not make — a markdown edit picked up by the watcher, the CLI, or the
		// scheduler.
		group.MapGet("/changes", async (HttpContext http, PlaintorchChangeBroker broker, CancellationToken cancellationToken) =>
		{
			await PlaintorchChangeFeedWriter.WriteAsync(http, broker, cancellationToken);
		});

		group.MapGet("/resolve-note", async (string path, ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.ResolveVaultNoteAsync(path, cancellationToken));
		});

		group.MapGet("/resolve/{id}", async (string id, ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.ResolveEntityByPuckAsync(id, cancellationToken));
		});

		group.MapGet("/watcher/issues", async (ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.GetWatcherIssuesAsync(cancellationToken));
		});

		group.MapGet("/watcher/issues-for", async (string path, ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.GetWatcherIssuesForPathAsync(path, cancellationToken));
		});

		endpoints.MapGet("/system/resolve/{id}", async (string id, ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.ResolveEntityByPuckAsync(id, cancellationToken));
		});

		endpoints.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
	}
}