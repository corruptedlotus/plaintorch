using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Changes;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Hosting;

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
		var group = endpoints.MapEnrichedGroup("/api/system");
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

		// PEP108 dismiss feature: suppress or restore a watcher issue. The result reports whether state changed.
		group.MapPost("/watcher/issues/dismiss", async (WatcherIssueDismissalRequest request, ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.DismissWatcherIssueAsync(request.Key, request.Scope, cancellationToken));
		});

		group.MapPost("/watcher/issues/restore", async (WatcherIssueDismissalRequest request, ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.RestoreWatcherIssueAsync(request.Key, request.Scope, cancellationToken));
		});

		var legacySystem = endpoints.MapEnrichedGroup("/system");
		legacySystem.MapGet("/resolve/{id}", async (string id, ISystemApi api, CancellationToken cancellationToken) =>
		{
			return Results.Ok(await api.ResolveEntityByPuckAsync(id, cancellationToken));
		});

		// Answers without touching vault state, so idle versus serving (and the phase behind it) is observable
		// even while a vault is still being activated or has failed to.
		endpoints.MapGet("/healthz", (ActiveVaultSession session, PlaintorchHostState hostState, PlaintorchUserLayout userLayout) =>
		{
			var status = hostState.Current;
			return Results.Ok(new
			{
				status = "ok",
				mode = session.IsActive ? "active" : "idle",
				activeVault = session.ActiveVaultPath,
				phase = status.Phase,
				message = status.Message,
				vault = status.VaultPath,
				sweeping = status.Sweeping,
				endpoint = userLayout.EndpointDisplay,
				since = status.At,
			});
		});
	}
}