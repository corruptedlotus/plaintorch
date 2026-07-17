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

		group.MapGet("/", async (string? q, int? take, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			return q is null
				? Results.Ok(await api.ListAsync(cancellationToken))
				: Results.Ok(await api.FindAsync(new SearchRequest(q, take), cancellationToken));
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

		group.MapPut("/{directiveId}", async (string directiveId, DirectiveUpdate request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateAsync(directiveId, request, cancellationToken)));

		group.MapPost("/{directiveId}/workflow", async (string directiveId, DirectiveWorkflowShift request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ShiftWorkflowAsync(directiveId, request, cancellationToken)));

		group.MapDelete("/{directiveId}", async (string directiveId, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteAsync(directiveId, cancellationToken);
			return Results.NoContent();
		});

		group.MapPost("/lunar", async (CreateLunarDirectiveRequest request, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			var directive = await api.CreateLunarAsync(request.Title, request.Codename, request.ParentDirectiveId, cancellationToken);
			return Results.Created($"/api/directives/{directive.Id}", directive);
		});

		group.MapPost("/{directiveId}/lunar-workflow", async (string directiveId, LunarDirectiveWorkflowShift request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ShiftLunarWorkflowAsync(directiveId, request, cancellationToken)));

		group.MapGet("/{directiveId}/timeframes", async (string directiveId, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ListTimeframesAsync(directiveId, cancellationToken)));

		group.MapPost("/{directiveId}/timeframes", async (string directiveId, TimeframePlan request, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			var timeframe = await api.CreateTimeframeAsync(directiveId, request, cancellationToken);
			return Results.Created($"/api/timeframes/{timeframe.Id}", timeframe);
		});

		var timeframes = endpoints.MapGroup("/api/timeframes");

		timeframes.MapPut("/{timeframeId:long}", async (long timeframeId, TimeframeUpdate request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateTimeframeAsync(timeframeId, request, cancellationToken)));

		timeframes.MapDelete("/{timeframeId:long}", async (long timeframeId, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteTimeframeAsync(timeframeId, cancellationToken);
			return Results.NoContent();
		});
	}
}