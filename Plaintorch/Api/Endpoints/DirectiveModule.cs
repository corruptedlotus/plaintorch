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

		group.MapPut("/{directiveId}", async (string directiveId, DirectiveUpdate request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.UpdateAsync(directiveId, request, cancellationToken)));

		group.MapPost("/{directiveId}/workflow", async (string directiveId, DirectiveWorkflowShift request, IDirectiveApi api, CancellationToken cancellationToken) =>
			Results.Ok(await api.ShiftWorkflowAsync(directiveId, request, cancellationToken)));

		group.MapDelete("/{directiveId}", async (string directiveId, IDirectiveApi api, CancellationToken cancellationToken) =>
		{
			await api.DeleteAsync(directiveId, cancellationToken);
			return Results.NoContent();
		});
	}
}