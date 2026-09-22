using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Plaintorch.State;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Registers shared PLAINTORCH API infrastructure and the aggregate API facade.
/// </summary>
public sealed class PlaintorchApiModule : Module
{
	/// <inheritdoc />
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddScoped<PlaintorchStateService>();
		services.AddScoped<PlaintorchMarkdownStorageService>();
		services.AddScoped<VaultWriteReadiness>();
		services.AddScoped<VaultWriteQueue>();
		services.AddScoped<IPlaintorchApi, PlaintorchApiService>();
	}

	/// <inheritdoc />
	public override void ConfigureApplication(WebApplication application)
	{
	}

	/// <inheritdoc />
	public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
	{
	}
}