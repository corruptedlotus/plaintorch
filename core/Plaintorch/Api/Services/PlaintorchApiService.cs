using Pleiades.Plaintorch.Api.Abstractions;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Aggregates the concrete PLAINTORCH API surfaces.
/// </summary>
public sealed class PlaintorchApiService(
	ISystemApi systemApi,
	IDirectiveApi directiveApi,
	IObjectiveApi objectiveApi,
	IDeclarativeApi declarativeApi,
	IOnrushSprintApi onrushSprintApi,
	IPolarisCycleApi polarisCycleApi,
	ILorePageApi lorePageApi) : IPlaintorchApi
{
	/// <inheritdoc />
	public ISystemApi System { get; } = systemApi;

	/// <inheritdoc />
	public IDirectiveApi Directives { get; } = directiveApi;

	/// <inheritdoc />
	public IObjectiveApi Objectives { get; } = objectiveApi;

	/// <inheritdoc />
	public IDeclarativeApi Declaratives { get; } = declarativeApi;

	/// <inheritdoc />
	public IOnrushSprintApi OnrushSprints { get; } = onrushSprintApi;

	/// <inheritdoc />
	public IPolarisCycleApi PolarisCycles { get; } = polarisCycleApi;

	/// <inheritdoc />
	public ILorePageApi LorePages { get; } = lorePageApi;
}