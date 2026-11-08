using Pleiades.Orchestration;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// One run of the <see cref="StateEngine"/> over one save (PEP103): the unit of work being saved, and the note work the
/// save leaves for once it commits.
/// </summary>
/// <param name="context">The unit of work being saved.</param>
public sealed class StatePass(PlainfraContext context)
{
	private readonly List<PolarisCycle> _supersededForecasts = [];
	private readonly List<object> _moved = [];

	/// <summary>
	/// Gets the unit of work being saved.
	/// </summary>
	public PlainfraContext Context { get; } = context;

	/// <summary>
	/// Gets the forecasts the save supersedes, whose notes are deleted once it commits.
	/// </summary>
	public IReadOnlyList<PolarisCycle> SupersededForecasts => _supersededForecasts;

	/// <summary>
	/// Gets the items the engine changed of its own accord in a field their notes carry: the compensations it made, the
	/// objectives it moved on joining the active sprint or cycle. Each note is rewritten once the save commits, unless the
	/// caller already writes it.
	/// </summary>
	public IReadOnlyList<object> Moved => _moved;

	/// <summary>
	/// Records forecasts the save supersedes.
	/// </summary>
	public void Supersede(IEnumerable<PolarisCycle> forecasts) => _supersededForecasts.AddRange(forecasts);

	/// <summary>
	/// Records an item the engine changed of its own accord in a field its note carries.
	/// </summary>
	public void Move(object entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		if (!_moved.Any(moved => ReferenceEquals(moved, entity)))
		{
			_moved.Add(entity);
		}
	}
}

/// <summary>
/// The note work a save leaves for once it commits (PEP103): the work that touches the vault's files, which a save that
/// fails must never have done.
/// </summary>
/// <param name="SupersededForecasts">The forecasts the save superseded, whose notes are deleted.</param>
/// <param name="Reconciles">The items the engine changed of its own accord, whose notes are rewritten from the database.</param>
public sealed record StateNoteWork(IReadOnlyList<PolarisCycle> SupersededForecasts, IReadOnlyList<object> Reconciles)
{
	/// <summary>
	/// Gets the work of a save that leaves none.
	/// </summary>
	public static StateNoteWork None { get; } = new([], []);

	/// <summary>
	/// Gets whether the save left no note work.
	/// </summary>
	public bool IsEmpty => SupersededForecasts.Count == 0 && Reconciles.Count == 0;
}
