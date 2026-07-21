using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pleiades.Orchestration;

/// <summary>
/// Represents a directive-level definition of a portion of time within a Polaris cycle — or within
/// multiple Polaris cycles depending on its Orbit definition (PEP100, originally PEP095).
/// </summary>
/// <remarks>
/// Timeframes do nothing on their own; they simply flag a portion of the day that can be used to clarify
/// affinity. Affinity is purely semantic: executives may name a timeframe as their preferred execution
/// window, and nothing enforces it.
/// </remarks>
public sealed class Timeframe
{
	[Key]
	/// <summary>
	/// Gets or sets the database identity for the timeframe definition.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Gets or sets the defining directive identifier.
	/// </summary>
	public required string DirectiveId { get; set; }

	[ForeignKey(nameof(DirectiveId))]
	[InverseProperty(nameof(Orchestration.LunarDirective.Timeframes))]
	/// <summary>
	/// Gets or sets the defining directive.
	/// </summary>
	public LunarDirective? Directive { get; set; }

	/// <summary>
	/// Gets or sets the human-readable timeframe title.
	/// </summary>
	public required string Title { get; set; }

	/// <summary>
	/// Gets or sets the start of the flagged portion of the day.
	/// </summary>
	public TimeOnly StartTime { get; set; }

	/// <summary>
	/// Gets or sets the end of the flagged portion of the day.
	/// </summary>
	public TimeOnly EndTime { get; set; }

	/// <summary>
	/// Gets or sets the optional Orbit notation deciding which Polaris cycles the timeframe applies to.
	/// When absent, the timeframe applies to every cycle.
	/// </summary>
	public string? Orbit { get; set; }
}
