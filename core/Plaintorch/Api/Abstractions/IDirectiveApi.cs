using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the directive-facing application actions exposed by PLAINTORCH.
/// </summary>
/// <remarks>
/// Stellar and lunar directives are table siblings under the abstract <see cref="Directive"/> base (PEP100).
/// Read actions (get/list/find) operate across the whole family; workflow shifts, updates, and timeframe
/// definitions are kind-specific because the two siblings carry different state and capabilities.
/// </remarks>
public interface IDirectiveApi
{
	/// <summary>
	/// Gets a directive by identifier, regardless of kind.
	/// </summary>
	Task<Directive?> GetAsync(string directiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists directives, optionally filtered to a single kind.
	/// </summary>
	/// <param name="kind">The directive kind to restrict the listing to, or <see langword="null"/> for every kind.</param>
	Task<IReadOnlyList<Directive>> ListAsync(DirectiveKind? kind = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists stellar directives only.
	/// </summary>
	Task<IReadOnlyList<StellarDirective>> ListStellarAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists lunar directives only.
	/// </summary>
	Task<IReadOnlyList<LunarDirective>> ListLunarAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Finds directives using a free-text query across every kind.
	/// </summary>
	Task<IReadOnlyList<Directive>> FindAsync(SearchRequest search, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a standalone stellar directive.
	/// </summary>
	Task<StellarDirective> CreateStandaloneAsync(string title, string? codename = null, string? requestedId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a child stellar directive beneath a parent directive.
	/// </summary>
	Task<StellarDirective> CreateFromParentAsync(string parentDirectiveId, string title, string? codename = null, string? requestedId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a lunar (Moonlight) directive (PEP100).
	/// </summary>
	Task<LunarDirective> CreateLunarAsync(string title, string? codename = null, string? parentDirectiveId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies an update to a stellar directive, including its scheduling dates.
	/// </summary>
	Task<StellarDirective> UpdateStellarAsync(string directiveId, StellarDirectiveUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies an update to a lunar directive.
	/// </summary>
	Task<LunarDirective> UpdateLunarAsync(string directiveId, LunarDirectiveUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a lifecycle workflow shift to a stellar directive.
	/// </summary>
	Task<StellarDirective> ShiftStellarWorkflowAsync(string directiveId, StellarDirectiveWorkflowShift shift, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a moonlight workflow shift to a lunar directive.
	/// </summary>
	Task<LunarDirective> ShiftLunarWorkflowAsync(string directiveId, LunarDirectiveWorkflowShift shift, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a directive of either kind.
	/// </summary>
	Task DeleteAsync(string directiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets or clears a directive's icon (PEP105): a built-in glyph name, an uploaded image, or cleared to the
	/// per-kind default. Applies to either kind.
	/// </summary>
	Task<Directive> SetIconAsync(string directiveId, DirectiveIconRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets or clears a directive's banner image (PEP105). Applies to either kind.
	/// </summary>
	Task<Directive> SetBannerAsync(string directiveId, DirectiveBannerRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Initializes a stellar directive from an existing vault markdown path using watcher creation policy.
	/// </summary>
	Task<Directive> InitializeFromPathAsync(string vaultRelativePath, CancellationToken cancellationToken = default);

	/// <summary>
	/// Defines a timeframe under a lunar directive (PEP100). Only lunar directives may own timeframes.
	/// </summary>
	Task<Timeframe> CreateTimeframeAsync(string lunarDirectiveId, TimeframePlan plan, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists the timeframes defined by a lunar directive.
	/// </summary>
	Task<IReadOnlyList<Timeframe>> ListTimeframesAsync(string lunarDirectiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists every timeframe across all lunar directives, each paired with a summary of its owning directive.
	/// </summary>
	Task<IReadOnlyList<DirectiveTimeframeRecord>> ListAllTimeframesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Updates a timeframe definition.
	/// </summary>
	Task<Timeframe> UpdateTimeframeAsync(long timeframeId, TimeframeUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a timeframe definition, clearing any executive affinity references to it.
	/// </summary>
	Task DeleteTimeframeAsync(long timeframeId, CancellationToken cancellationToken = default);
}
