using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Api.Transport;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers the directive-facing PLAINTORCH API services.
/// </summary>
public sealed class DirectiveModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<IDirectiveApi, DirectiveApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/directives");

		// General directive surface spanning both kinds.
		group.MapGet("/", async (string? q, string? kind, int? take, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			if (q is not null)
			{
				return Results.Ok(await api.FindAsync(new SearchRequest(q, take), cancellationToken));
			}

			if (!TryParseKind(kind, out var parsedKind))
			{
				return Results.BadRequest($"Unknown directive kind '{kind}'. Expected 'stellar' or 'lunar'.");
			}

			return Results.Ok(await api.ListAsync(parsedKind, cancellationToken));
		});

		group.MapGet("/{directiveId}", async (string directiveId, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			var directive = await api.GetAsync(directiveId, cancellationToken);
			return directive is null ? Results.NotFound() : Results.Ok(directive);
		});

		group.MapPost("/", async (CreateDirectiveRequest request, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			var directive = string.IsNullOrWhiteSpace(request.ParentDirectiveId)
				? await api.CreateStandaloneAsync(request.Title, request.Codename, request.Id, cancellationToken)
				: await api.CreateFromParentAsync(request.ParentDirectiveId, request.Title, request.Codename, request.Id, cancellationToken);
			return Results.Created($"/api/directives/{directive.Id}", directive);
		});

		group.MapPost("/init", async (InitDirectiveRequest request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.InitializeFromPathAsync(request.Path, cancellationToken)));

		group.MapDelete("/{directiveId}", async (string directiveId, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteAsync(directiveId, cancellationToken);
			return Results.NoContent();
		});

		// Media (PEP105) spans both kinds: a directive icon or banner is set through its shared base fields.
		group.MapPut("/{directiveId}/icon", async (string directiveId, DirectiveIconRequest request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.SetIconAsync(directiveId, request, cancellationToken)));

		group.MapPut("/{directiveId}/banner", async (string directiveId, DirectiveBannerRequest request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.SetBannerAsync(directiveId, request, cancellationToken)));

		// Stellar-only surface.
		var stellar = group.MapGroup("/stellar");

		stellar.MapGet("/", async (IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListStellarAsync(cancellationToken)));

		stellar.MapPut("/{directiveId}", async (string directiveId, StellarDirectiveUpdate request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateStellarAsync(directiveId, request, cancellationToken)));

		stellar.MapPost("/{directiveId}/workflow", async (string directiveId, StellarDirectiveWorkflowShift request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ShiftStellarWorkflowAsync(directiveId, request, cancellationToken)));

		// Lunar-only surface (PEP100).
		var lunar = group.MapGroup("/lunar");

		lunar.MapGet("/", async (IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListLunarAsync(cancellationToken)));

		lunar.MapPost("/", async (CreateLunarDirectiveRequest request, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			var directive = await api.CreateLunarAsync(request.Title, request.Codename, request.ParentDirectiveId, cancellationToken);
			return Results.Created($"/api/directives/{directive.Id}", directive);
		});

		lunar.MapPut("/{directiveId}", async (string directiveId, LunarDirectiveUpdate request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateLunarAsync(directiveId, request, cancellationToken)));

		lunar.MapPost("/{directiveId}/workflow", async (string directiveId, LunarDirectiveWorkflowShift request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ShiftLunarWorkflowAsync(directiveId, request, cancellationToken)));

		lunar.MapGet("/{directiveId}/timeframes", async (string directiveId, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListTimeframesAsync(directiveId, cancellationToken)));

		lunar.MapPost("/{directiveId}/timeframes", async (string directiveId, TimeframePlan request, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			var timeframe = await api.CreateTimeframeAsync(directiveId, request, cancellationToken);
			return Results.Created($"/api/timeframes/{timeframe.Id}", timeframe);
		});

		// Timeframes span every lunar directive.
		var timeframes = endpoints.MapGroup("/api/timeframes");

		timeframes.MapGet("/", async (IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListAllTimeframesAsync(cancellationToken)));

		timeframes.MapPut("/{timeframeId:long}", async (long timeframeId, TimeframeUpdate request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateTimeframeAsync(timeframeId, request, cancellationToken)));

		timeframes.MapDelete("/{timeframeId:long}", async (long timeframeId, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteTimeframeAsync(timeframeId, cancellationToken);
			return Results.NoContent();
		});
	}

	private static bool TryParseKind(string? kind, out DirectiveKind? parsedKind)
	{
		parsedKind = null;
		if (string.IsNullOrWhiteSpace(kind))
		{
			return true;
		}

		if (Enum.TryParse<DirectiveKind>(kind, ignoreCase: true, out var value))
		{
			parsedKind = value;
			return true;
		}

		return false;
	}
}
