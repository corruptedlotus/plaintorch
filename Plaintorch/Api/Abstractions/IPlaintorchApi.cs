namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Aggregates the application-level API surfaces exposed by PLAINTORCH.
/// </summary>
public interface IPlaintorchApi
{
	/// <summary>
	/// Gets the system-facing API surface.
	/// </summary>
	ISystemApi System { get; }

	/// <summary>
	/// Gets the directive-facing API surface.
	/// </summary>
	IDirectiveApi Directives { get; }

	/// <summary>
	/// Gets the objective-facing API surface.
	/// </summary>
	IObjectiveApi Objectives { get; }

	/// <summary>
	/// Gets the onrush sprint-facing API surface.
	/// </summary>
	IOnrushSprintApi OnrushSprints { get; }

	/// <summary>
	/// Gets the Polaris cycle-facing API surface.
	/// </summary>
	IPolarisCycleApi PolarisCycles { get; }
}