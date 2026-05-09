using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the directive-facing application actions exposed by PLAINTORCH.
/// </summary>
public interface IDirectiveApi
{
	/// <summary>
	/// Gets a directive by identifier.
	/// </summary>
	Task<Directive?> GetAsync(string directiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists directives.
	/// </summary>
	Task<IReadOnlyList<Directive>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Finds directives using a free-text query.
	/// </summary>
	Task<IReadOnlyList<Directive>> FindAsync(SearchRequest search, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a standalone directive.
	/// </summary>
	Task<Directive> CreateStandaloneAsync(string title, string? codename = null, string? requestedId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a child directive beneath a parent directive.
	/// </summary>
	Task<Directive> CreateFromParentAsync(string parentDirectiveId, string title, string? codename = null, string? requestedId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a generic update to a directive.
	/// </summary>
	Task<Directive> UpdateAsync(string directiveId, DirectiveUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a workflow shift to a directive.
	/// </summary>
	Task<Directive> ShiftWorkflowAsync(string directiveId, DirectiveWorkflowShift shift, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a directive.
	/// </summary>
	Task DeleteAsync(string directiveId, CancellationToken cancellationToken = default);
}