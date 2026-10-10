using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.State;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// Finds every rule a save would break (PEP103): the conflicts <see cref="StateDecisionService"/> weighs against the backlog
/// state enforcement level, each with the compensations that would resolve it. The save is judged as it stands once its
/// structural reactions have run (<see cref="StructureReactions"/>), so every pathway — an API call, a note edit, an
/// activation or a background pass — meets the same rules.
/// </summary>
/// <remarks>
/// <para>
/// <b>Systemic rules</b>, which no state change resolves and every level refuses:
/// <list type="bullet">
/// <item><description>at most one planning placeholder sprint, one active onrush sprint and one active Polaris
/// cycle;</description></item>
/// <item><description>a sprint or cycle that has ended never begins again, and its end is never moved
/// (<c>onrush.restart</c>, <c>polaris.restart</c>);</description></item>
/// <item><description>a sprint or cycle ends only once it has begun, and never before its start (<c>onrush.end</c>,
/// <c>polaris.end</c>); the planning placeholder never ends;</description></item>
/// <item><description>a Polaris cycle never begins before its day (<c>polaris.early-start</c>), and a sprint never begins on
/// a day still to come (<c>onrush.early-start</c>);</description></item>
/// <item><description>what a sprint or cycle that has ended holds is history (<c>onrush.ended</c>, <c>polaris.ended</c>):
/// its executives, reflectives, executive orders and checkpoints are neither added, changed, moved nor removed, and nothing
/// joins it;</description></item>
/// <item><description>an executive works at an objective or a started decree (<c>polaris.incentive</c>).</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Lifecycle rules</b>, judged on every lifecycle state a save moves an objective, a stellar directive or a fate into
/// (<see cref="LifecycleTransition.Entered"/>), so a jump meets every gate it passes, and on every move back:
/// <list type="bullet">
/// <item><description><c>dependency.start-gate</c>: entering Started needs every stored dependency constrained to the start
/// (<see cref="DependencyConstraint.ToBegin"/>, the default) satisfied.</description></item>
/// <item><description><c>dependency.finish-gate</c>: entering Finished needs every stored dependency constrained to the
/// finish satisfied.</description></item>
/// <item><description><c>directive.open-children</c>: a stellar directive entering Finished, or closing (becoming
/// <see cref="DirectiveStatus.Over"/>), needs every direct child finished or set aside (its intrinsic edges).</description></item>
/// <item><description><c>dependency.close-gate</c>: closing an item — a status declared
/// <see cref="LifecycleStateAttribute.Closed"/>, an objective's Archived or a directive's Over — needs every stored
/// dependency into it satisfied, whatever it is constrained to (PEP103 patch 5).</description></item>
/// <item><description><c>dependency.reopen</c>: any status may follow any other, as long as no dependency is reopened (user
/// ruling, PEP103 patch 4). An item moving back out of the state one of its dependencies needs — its intrinsic edge to its
/// directive included — conflicts while that dependency's target has already passed the state it gates. Setting an item
/// aside is the exception: a deferral never reopens anything.</description></item>
/// <item><description><c>dependency.closed</c>: what is done is done (user ruling, PEP103 patch 5). A dependency added to a
/// finished item, or one that stops holding without its source moving back, only keeps it from closing; an item already
/// closed steps back to its kind's default finished status. That covers a dependency the save adds, a checkpoint the save
/// locks again (a new dependency into it, its condition turned against it), and open work arriving under a closed directive
/// — created there, moved there, or taken up there again from a deferral.</description></item>
/// <item><description><c>membership.closed</c>: a closed objective joining the active onrush sprint or Polaris cycle steps
/// back to its default finished status (user ruling, PEP103 patch 5). Joining never moves an item back otherwise.</description></item>
/// </list>
/// A move back is otherwise never gated, and a deferral never is.
/// </para>
/// <para>
/// <b>Compensations.</b> Each lifecycle conflict carries the moves that would resolve it: a dependency's source moved to the
/// state its trigger needs, an open child finished, and, the other way, a target that relied on a reopened dependency moved
/// back to the state before the one it gates, or a closed item stepped back to its open finish. Each item moves to the
/// default status of its new state (<see cref="EntityLifecycleResolver.DefaultStatus"/>). A move is judged in
/// turn, as a requested one is, so a source that is gated itself brings the moves its own gates need, and a target moved
/// back takes back what relied on it in turn, until nothing more is needed. A locked checkpoint is passed through to the
/// dependencies holding it, when its toll and condition do not, and a checkpoint that relocks takes back what it held. A
/// conflict nothing could resolve carries no compensation: a source that is set aside (taking it up again is the user's
/// call), an occurrence (time moves it), a fate asked to finish (a fate never does) or to move back before its start (a fate
/// has no open state), a checkpoint held by its toll or its condition or with nothing to wait for
/// (<see cref="DependencyReconciler.Unlocks"/>), or an endpoint with nothing behind it.
/// </para>
/// <para>
/// Messages say what blocks a change, not which items or edges do: "… cannot finish: it is blocked by 2 dependencies." The
/// compensations name the items a confirmation would move.
/// </para>
/// </remarks>
public sealed class StateJudge(EntityLifecycleResolver lifecycleResolver, IEnumerable<IIntrinsicDependencyRule> intrinsicRules)
{
	/// <summary>
	/// The rule stopping a second planning placeholder sprint.
	/// </summary>
	public const string SinglePlanningOnrushRule = "onrush.single-planning";

	/// <summary>
	/// The rule stopping a second active onrush sprint.
	/// </summary>
	public const string SingleActiveOnrushRule = "onrush.single-active";

	/// <summary>
	/// The rule stopping a second active Polaris cycle.
	/// </summary>
	public const string SingleActivePolarisRule = "polaris.single-active";

	/// <summary>
	/// The rule holding a start back until the dependencies constrained to it are satisfied.
	/// </summary>
	public const string StartGateRule = "dependency.start-gate";

	/// <summary>
	/// The rule holding a finish back until the dependencies constrained to it are satisfied.
	/// </summary>
	public const string FinishGateRule = "dependency.finish-gate";

	/// <summary>
	/// The rule holding a directive's finish, or its Over, back until its direct children are finished or set aside.
	/// </summary>
	public const string OpenChildrenRule = "directive.open-children";

	/// <summary>
	/// The rule holding an item's closing back until every dependency into it is satisfied.
	/// </summary>
	public const string CloseGateRule = "dependency.close-gate";

	/// <summary>
	/// The rule stepping a closed item back to its open finish when it gains a dependency that does not hold.
	/// </summary>
	public const string ClosedRule = "dependency.closed";

	/// <summary>
	/// The rule stepping a closed objective back to its open finish when it joins the active onrush sprint or Polaris cycle.
	/// </summary>
	public const string ClosedJoinRule = "membership.closed";

	/// <summary>
	/// The rule keeping an item from moving back out of what a dependency needs while the dependency's target relies on it.
	/// </summary>
	public const string ReopenRule = "dependency.reopen";

	/// <summary>
	/// The rule keeping an ended sprint from beginning again or moving its end.
	/// </summary>
	public const string SprintRestartRule = "onrush.restart";

	/// <summary>
	/// The rule keeping a sprint from ending before it begins, or before its start.
	/// </summary>
	public const string SprintEndRule = "onrush.end";

	/// <summary>
	/// The rule keeping an ended Polaris cycle from beginning again or moving its end.
	/// </summary>
	public const string CycleRestartRule = "polaris.restart";

	/// <summary>
	/// The rule keeping a Polaris cycle from ending before it begins, or before its start.
	/// </summary>
	public const string CycleEndRule = "polaris.end";

	/// <summary>
	/// The rule keeping a Polaris cycle from beginning before its day.
	/// </summary>
	public const string CycleEarlyStartRule = "polaris.early-start";

	/// <summary>
	/// The rule keeping a sprint from beginning on a day still to come.
	/// </summary>
	public const string SprintEarlyStartRule = "onrush.early-start";

	/// <summary>
	/// The rule keeping what an ended sprint holds — its executive orders and checkpoints — as it was, and keeping anything
	/// from joining it.
	/// </summary>
	public const string SprintEndedRule = "onrush.ended";

	/// <summary>
	/// The rule keeping what an ended Polaris cycle holds — its executives and reflectives — as it was.
	/// </summary>
	public const string CycleEndedRule = "polaris.ended";

	/// <summary>
	/// The rule keeping an executive working at an objective or a started decree.
	/// </summary>
	public const string IncentiveRule = "polaris.incentive";

	private static readonly IReadOnlyDictionary<Type, EndpointShape> Shapes = new Dictionary<Type, EndpointShape>
	{
		[typeof(Objective)] = new(DependencyEndpointKind.Objective, nameof(Objective.DirectiveId)),
		[typeof(StellarDirective)] = new(DependencyEndpointKind.Directive, nameof(StellarDirective.ParentDirectiveId)),
		[typeof(Fate)] = new(DependencyEndpointKind.Fate, null),
	};

	private readonly EntityLifecycleResolver _lifecycle = lifecycleResolver;
	private readonly IReadOnlyList<IIntrinsicDependencyRule> _intrinsicRules = intrinsicRules.ToList();

	/// <summary>
	/// Judges the save pending on <paramref name="context"/>.
	/// </summary>
	public async Task<StateJudgement> JudgeAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		var judgement = new StateJudgement();
		await JudgeOnrushAsync(context, judgement, cancellationToken);
		await JudgePolarisAsync(context, judgement, cancellationToken);
		await JudgeRunsAsync(context, judgement, cancellationToken);
		await JudgeEndedAsync(context, judgement, cancellationToken);
		await JudgeIncentivesAsync(context, judgement, cancellationToken);

		var changes = await ReadChangesAsync(context, cancellationToken);
		var joins = await MembershipReactions.JoinsAsync(context, cancellationToken);
		await new LifecycleRuling(this, context, judgement, cancellationToken).RuleAsync(changes, joins);
		return judgement;
	}

	/// <summary>
	/// At most one planning placeholder sprint, and at most one active sprint: started, not ended, not the placeholder.
	/// </summary>
	private static async Task JudgeOnrushAsync(PlainfraContext context, StateJudgement judgement, CancellationToken cancellationToken)
	{
		var tracked = context.ChangeTracker.Entries<OnrushSprint>().ToList();
		var candidates = tracked.Where(entry => entry.State is EntityState.Added or EntityState.Modified).ToList();
		if (candidates.Count == 0)
		{
			return;
		}

		var planning = candidates
			.Where(entry => string.Equals(entry.Entity.Id, OnrushSprint.PlanningPlaceholderId, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (planning.Count > 1)
		{
			judgement.Refuse(SinglePlanningOnrushRule, SubjectOf(planning[0].Entity), "Only one planning onrush sprint may exist at a time.");
		}
		else if (planning is [{ State: EntityState.Added } added]
			&& await context.OnrushSprints.AsNoTracking().AnyAsync(sprint => sprint.Id == OnrushSprint.PlanningPlaceholderId, cancellationToken))
		{
			judgement.Refuse(SinglePlanningOnrushRule, SubjectOf(added.Entity), "A planning onrush sprint already exists.");
		}

		var active = candidates.Where(entry => IsActiveOnrush(entry.Entity)).ToList();
		if (active.Count > 1)
		{
			judgement.Refuse(SingleActiveOnrushRule, SubjectOf(active[1].Entity), "Only one onrush sprint may be active at a time.");
			return;
		}

		if (active.Count == 0)
		{
			return;
		}

		// A tracked sprint is read as the save leaves it, so ending one sprint and starting another in one save is no clash.
		var trackedIds = tracked.Select(entry => entry.Entity.Id).ToList();
		if (await context.OnrushSprints
			.AsNoTracking()
			.AnyAsync(sprint => sprint.Id != OnrushSprint.PlanningPlaceholderId
				&& !trackedIds.Contains(sprint.Id)
				&& sprint.StartDate != null
				&& sprint.EndDate == null,
				cancellationToken))
		{
			judgement.Refuse(SingleActiveOnrushRule, SubjectOf(active[0].Entity), "Another onrush sprint is already active.");
		}
	}

	/// <summary>
	/// At most one active Polaris cycle: started and not ended.
	/// </summary>
	private static async Task JudgePolarisAsync(PlainfraContext context, StateJudgement judgement, CancellationToken cancellationToken)
	{
		var tracked = context.ChangeTracker.Entries<PolarisCycle>().ToList();
		var active = tracked
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified && IsActivePolaris(entry.Entity))
			.ToList();
		if (active.Count > 1)
		{
			judgement.Refuse(SingleActivePolarisRule, SubjectOf(active[1].Entity), "Only one Polaris cycle may be active at a time.");
			return;
		}

		if (active.Count == 0)
		{
			return;
		}

		var trackedIds = tracked.Select(entry => entry.Entity.Id).ToList();
		if (await context.PolarisCycles
			.AsNoTracking()
			.AnyAsync(cycle => !trackedIds.Contains(cycle.Id) && cycle.StartTime != null && cycle.EndTime == null, cancellationToken))
		{
			judgement.Refuse(SingleActivePolarisRule, SubjectOf(active[0].Entity), "Another Polaris cycle is already active.");
		}
	}

	/// <summary>
	/// The run of every sprint and cycle whose start or end the save sets (PEP103 patch 4): one that has ended never begins
	/// again and keeps its end, nothing ends before it begins or before its start, the planning placeholder never ends (its
	/// dates are the plan's, and a plan has not begun), a Polaris cycle never begins before its day, and a sprint never begins
	/// on a day still to come (PEP103 patch 5; a sprint whose window lies ahead has no reading yet, so it is not begun). A
	/// sprint or cycle the save leaves untouched is not read, so a row stored out of shape does not stop an unrelated save.
	/// </summary>
	private static async Task JudgeRunsAsync(PlainfraContext context, StateJudgement judgement, CancellationToken cancellationToken)
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		foreach (var entry in context.ChangeTracker.Entries<OnrushSprint>().ToList())
		{
			var placeholder = string.Equals(entry.Entity.Id, OnrushSprint.PlanningPlaceholderId, StringComparison.OrdinalIgnoreCase);
			var startSet = await JudgeRunAsync(entry, nameof(OnrushSprint.StartDate), nameof(OnrushSprint.EndDate), !placeholder, SprintRestartRule, SprintEndRule, judgement, cancellationToken);
			if (startSet && !placeholder && entry.Entity.StartDate is { } start && start > today)
			{
				var subject = SubjectOf(entry.Entity);
				judgement.Refuse(SprintEarlyStartRule, subject, $"{Name(subject)} cannot begin on a day still to come.");
			}
		}

		foreach (var entry in context.ChangeTracker.Entries<PolarisCycle>().ToList())
		{
			var startSet = await JudgeRunAsync(entry, nameof(PolarisCycle.StartTime), nameof(PolarisCycle.EndTime), true, CycleRestartRule, CycleEndRule, judgement, cancellationToken);
			if (startSet
				&& entry.Entity.StartTime is { } start
				&& entry.Entity.Day is { } day
				&& DateOnly.FromDateTime(start.LocalDateTime) < day)
			{
				var subject = SubjectOf(entry.Entity);
				judgement.Refuse(CycleEarlyStartRule, subject, $"{Name(subject)} cannot begin before its day.");
			}
		}
	}

	/// <summary>
	/// Judges the run of one sprint or cycle, and returns whether the save sets its start.
	/// </summary>
	/// <param name="entry">The sprint or cycle.</param>
	/// <param name="startName">The property holding its start.</param>
	/// <param name="endName">The property holding its end.</param>
	/// <param name="canBegin">Whether it can begin at all; the planning placeholder cannot, so it can never end.</param>
	/// <param name="restartRule">The rule an ended one breaks by beginning again or moving its end.</param>
	/// <param name="endRule">The rule one breaks by ending before it begins or before its start.</param>
	/// <param name="judgement">Where conflicts go.</param>
	/// <param name="cancellationToken">A token to cancel the read of the stored row.</param>
	private static async Task<bool> JudgeRunAsync(
		EntityEntry entry,
		string startName,
		string endName,
		bool canBegin,
		string restartRule,
		string endRule,
		StateJudgement judgement,
		CancellationToken cancellationToken)
	{
		if (entry.State is not (EntityState.Added or EntityState.Modified)
			|| (entry.State == EntityState.Modified && !entry.Property(startName).IsModified && !entry.Property(endName).IsModified))
		{
			return false;
		}

		var stored = entry.State == EntityState.Added ? null : await entry.GetDatabaseValuesAsync(cancellationToken);
		var storedEnd = stored?[endName];
		var start = entry.Property(startName).CurrentValue;
		var end = entry.Property(endName).CurrentValue;
		var startSet = !Equals(stored?[startName], start);
		var endSet = !Equals(storedEnd, end);
		var subject = SubjectOf(entry.Entity);

		// Clearing the end takes an ended sprint or cycle up again; that stays allowed.
		if (storedEnd is not null && end is not null)
		{
			if (startSet)
			{
				judgement.Refuse(restartRule, subject, RestartMessage(subject));
			}

			if (endSet)
			{
				judgement.Refuse(restartRule, subject, EndedMessage(subject));
			}
		}
		else if (end is not null && endSet)
		{
			if (!canBegin || start is null)
			{
				judgement.Refuse(endRule, subject, UnbegunEndMessage(subject));
			}
			else if (Comparer<object>.Default.Compare(end, start) < 0)
			{
				judgement.Refuse(endRule, subject, $"{Name(subject)} cannot end before its start.");
			}
		}

		return startSet;
	}

	/// <summary>
	/// What a sprint or cycle that has ended holds is history (PEP103 patch 5, user ruling): a Polaris cycle's executives and
	/// reflectives, and a sprint's executive orders and checkpoints, are neither added, changed, moved in or out, nor removed
	/// once it has ended, and no objective or directive joins a sprint that has. An objective or a directive leaving an ended
	/// sprint for the next stays allowed: it is backlog work, not the sprint's record.
	/// </summary>
	/// <remarks>
	/// Ended means ended before the save and after it, so the save that ends a sprint or cycle, or takes one up again by
	/// clearing its end, may still change what it holds. A reference cleared because the item it named is deleted in the same
	/// save is no change — a deleted objective's executive keeps its record with its reference cleared — and neither is
	/// anything done to a sprint or cycle the save deletes.
	/// </remarks>
	private static async Task JudgeEndedAsync(PlainfraContext context, StateJudgement judgement, CancellationToken cancellationToken)
	{
		var ended = new EndedRuns(context, cancellationToken);
		var refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		void Refuse(string rule, object run, string what)
		{
			var subject = SubjectOf(run);
			if (refused.Add($"{rule}\u001F{subject.Id}\u001F{what}"))
			{
				judgement.Refuse(rule, subject, $"{Name(subject)} has ended; {what}.");
			}
		}

		foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.Entity is Executive or Reflective).ToList())
		{
			if (await ended.RecordAsync(entry, nameof(Executive.PolarisCycleId), ended.CycleAsync) is { } cycle)
			{
				Refuse(CycleEndedRule, cycle, "the work it holds can no longer change");
			}
		}

		foreach (var entry in context.ChangeTracker.Entries<ExecutiveOrder>().ToList())
		{
			if (await ended.RecordAsync(entry, nameof(ExecutiveOrder.OnrushSprintId), ended.SprintAsync) is { } sprint)
			{
				Refuse(SprintEndedRule, sprint, "its executive orders can no longer change");
			}
		}

		foreach (var entry in context.ChangeTracker.Entries<Checkpoint>().ToList())
		{
			// A checkpoint may leave an ended sprint, and its unlock is the engine's to keep, not a change to the record.
			if (await ended.RecordAsync(entry, nameof(Checkpoint.OnrushSprintId), ended.SprintAsync, leaves: true, nameof(Checkpoint.Unlocked)) is { } sprint)
			{
				Refuse(SprintEndedRule, sprint, "its checkpoints can no longer change");
			}
		}

		foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.Entity is Objective or StellarDirective).ToList())
		{
			if (await ended.JoinedAsync(entry, nameof(Objective.OnrushSprintId)) is { } sprint)
			{
				Refuse(SprintEndedRule, sprint, "nothing can join it");
			}
		}
	}

	/// <summary>
	/// An executive works at an objective or at a started decree (PEP103 patch 5, user ruling), however it gets there —
	/// planned, added or reassigned: a fate is never worked, and a decree that is not started cannot be. An objective may be
	/// worked whatever its state; a finished one is worked as it is, and a closed one is the enforcement level's call
	/// (<see cref="ClosedJoinRule"/>). A one-shot executive works at nothing and is not read.
	/// </summary>
	private async Task JudgeIncentivesAsync(PlainfraContext context, StateJudgement judgement, CancellationToken cancellationToken)
	{
		foreach (var entry in context.ChangeTracker.Entries<Executive>().ToList())
		{
			if (entry.Entity.IncentiveId is not { } incentiveId
				|| entry.State is not (EntityState.Added or EntityState.Modified)
				|| (entry.State == EntityState.Modified
					&& string.Equals(entry.Property(nameof(Executive.IncentiveId)).OriginalValue as string, incentiveId, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			var incentive = context.ChangeTracker.Entries<Incentive>()
				.FirstOrDefault(candidate => string.Equals(candidate.Entity.Id, incentiveId, StringComparison.OrdinalIgnoreCase))?.Entity
				?? await context.Set<Incentive>().AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == incentiveId, cancellationToken);
			switch (incentive)
			{
				case Objective:
					continue;
				case Decree decree when _lifecycle.StateOf(decree) == LifecycleState.Started:
					continue;
				case Decree decree:
					var abandoned = SubjectOf(decree);
					judgement.Refuse(IncentiveRule, abandoned, $"{Name(abandoned)} is {decree.Status}, so it cannot be worked in a Polaris cycle.");
					break;
				case null:
					judgement.Refuse(IncentiveRule, new StateSubject(nameof(Incentive), incentiveId), $"An executive cannot work at '{incentiveId}': nothing has that id.");
					break;
				default:
					var subject = SubjectOf(incentive);
					judgement.Refuse(IncentiveRule, subject, $"{Name(subject)} cannot be worked in a Polaris cycle; only objectives and decrees are.");
					break;
			}
		}
	}

	/// <summary>
	/// Whether the lifecycle rules would let the save move <paramref name="entity"/> to <paramref name="status"/>, judged on the
	/// save as it stands: the move meets no rule the item does not meet already where the save leaves it. A held gate of a
	/// state the move enters blocks it, and so does moving back out of what a dependency's target already relies on —
	/// bringing work back under a finished directive among it; a conflict the save has anyway (open work created under such a
	/// directive) does not, and is weighed as usual. Nothing is proposed to
	/// unblock a move, whatever the level. The engine makes its own moves only where this holds
	/// (<see cref="MembershipReactions"/>).
	/// </summary>
	/// <param name="context">The unit of work being saved.</param>
	/// <param name="entity">A tracked objective, stellar directive or fate.</param>
	/// <param name="status">The status the move would set, read from where the item is stored.</param>
	/// <param name="cancellationToken">A token to cancel the reads.</param>
	public async Task<bool> AllowsAsync(PlainfraContext context, object entity, Enum status, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(entity);
		ArgumentNullException.ThrowIfNull(status);
		if (!Shapes.ContainsKey(entity.GetType()))
		{
			throw new ArgumentException($"A {entity.GetType().Name} has no lifecycle the judge reads.", nameof(entity));
		}

		var entry = context.Entry(entity);
		var stored = entry.State == EntityState.Added ? null : await entry.GetDatabaseValuesAsync(cancellationToken);
		var current = (Enum)entry.Property(_lifecycle.StatusPropertyOf(entity.GetType())!.Name).CurrentValue!;
		var asIs = await RulesMetAsync(context, ChangeOf(entry, stored, current), cancellationToken);
		var moved = await RulesMetAsync(context, ChangeOf(entry, stored, status), cancellationToken);
		return moved.IsSubsetOf(asIs);
	}

	/// <summary>
	/// The rules one change meets on the save alone.
	/// </summary>
	private async Task<HashSet<string>> RulesMetAsync(PlainfraContext context, RequestedChange change, CancellationToken cancellationToken)
	{
		var judgement = new StateJudgement();
		await new LifecycleRuling(this, context, judgement, cancellationToken).JudgeAsync(change);
		return judgement.Conflicts.Select(conflict => conflict.Rule).ToHashSet(StringComparer.Ordinal);
	}

	/// <summary>
	/// Reads what the save asks of each objective, stellar directive and fate whose status or parent it changes, or that it
	/// creates: the status and parent as stored against those it is about to store. The stored row, not the tracked original
	/// values, is the reference, so an entity attached rather than loaded is read correctly too.
	/// </summary>
	private async Task<List<RequestedChange>> ReadChangesAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var changes = new List<RequestedChange>();
		var entries = context.ChangeTracker.Entries()
			.Where(entry => entry.State is EntityState.Added or EntityState.Modified && Shapes.ContainsKey(entry.Entity.GetType()))
			.ToList();
		foreach (var entry in entries)
		{
			var shape = Shapes[entry.Entity.GetType()];
			var statusName = _lifecycle.StatusPropertyOf(entry.Entity.GetType())!.Name;
			if (entry.State == EntityState.Modified
				&& !entry.Property(statusName).IsModified
				&& !(shape.ParentProperty is { } parentName && entry.Property(parentName).IsModified))
			{
				continue;
			}

			var stored = entry.State == EntityState.Added ? null : await entry.GetDatabaseValuesAsync(cancellationToken);
			changes.Add(ChangeOf(entry, stored, (Enum)entry.Property(statusName).CurrentValue!));
		}

		return changes;
	}

	/// <summary>
	/// What a save that leaves the item in <paramref name="status"/> asks of it, against its stored row
	/// (<see langword="null"/> for an item the save creates).
	/// </summary>
	private RequestedChange ChangeOf(EntityEntry entry, PropertyValues? stored, Enum status)
	{
		var shape = Shapes[entry.Entity.GetType()];
		var storedStatus = stored?[_lifecycle.StatusPropertyOf(entry.Entity.GetType())!.Name] as Enum;
		return new RequestedChange(
			entry.Entity,
			new EndpointRef(shape.Kind, ((IPuckNamedEntity)entry.Entity).Id),
			storedStatus,
			status,
			storedStatus is null ? null : _lifecycle.StateOf(storedStatus),
			_lifecycle.StateOf(status),
			shape.ParentProperty is { } storedParent && stored is not null ? stored[storedParent] as string : null,
			shape.ParentProperty is { } parent ? entry.Property(parent).CurrentValue as string : null,
			Created: stored is null,
			Closes: _lifecycle.IsClosed(status) && (storedStatus is null || !_lifecycle.IsClosed(storedStatus)));
	}

	/// <summary>
	/// Whether a sprint is the active one: started, not ended, and not the planning placeholder.
	/// </summary>
	internal static bool IsActiveOnrush(OnrushSprint sprint) => CyclePhases.Of(sprint) == CyclePhase.Active;

	/// <summary>
	/// Whether a Polaris cycle is the active one: started and not ended.
	/// </summary>
	internal static bool IsActivePolaris(PolarisCycle cycle) => CyclePhases.Of(cycle) == CyclePhase.Active;

	/// <summary>
	/// Names an entity for a conflict or a compensation: its kind, its id and its title.
	/// </summary>
	internal static StateSubject SubjectOf(object entity)
		=> entity is IPuckNamedEntity named
			? new StateSubject(entity.GetType().Name, named.Id, string.IsNullOrWhiteSpace(named.Title) ? null : named.Title)
			: new StateSubject(entity.GetType().Name, entity.ToString() ?? string.Empty);

	/// <summary>
	/// Words a subject the way a message reads it: <c>Directive 'Campaign'</c>.
	/// </summary>
	internal static string Name(StateSubject subject)
	{
		var kind = subject.Kind switch
		{
			nameof(StellarDirective) => "Directive",
			nameof(OnrushSprint) => "Onrush sprint",
			nameof(PolarisCycle) => "Polaris cycle",
			_ => subject.Kind,
		};
		return $"{kind} '{subject.Title ?? subject.Id}'";
	}

	private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

	/// <summary>
	/// Words the refusal of a sprint or cycle that has ended being begun again.
	/// </summary>
	internal static string RestartMessage(StateSubject subject) => $"{Name(subject)} has ended; it cannot begin again.";

	/// <summary>
	/// Words the refusal of a sprint or cycle that has ended being ended again.
	/// </summary>
	internal static string EndedMessage(StateSubject subject) => $"{Name(subject)} has already ended.";

	/// <summary>
	/// Words the refusal of a sprint or cycle being ended before it has begun.
	/// </summary>
	internal static string UnbegunEndMessage(StateSubject subject) => $"{Name(subject)} cannot end before it begins.";

	/// <summary>
	/// How an endpoint kind with a status of its own sits in the structure: its dependency endpoint kind, and the property
	/// naming its parent directive, if it has one.
	/// </summary>
	private sealed record EndpointShape(DependencyEndpointKind Kind, string? ParentProperty);

	/// <summary>
	/// Reads which sprints and cycles had ended before the save and stay ended after it, each once, and what the save does to
	/// the records they hold (<see cref="JudgeEndedAsync"/>). A sprint or cycle the save creates or deletes is never read as
	/// ended.
	/// </summary>
	private sealed class EndedRuns(PlainfraContext context, CancellationToken cancellationToken)
	{
		private readonly Dictionary<string, object?> _cycles = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, object?> _sprints = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// The Polaris cycle with this id when it had ended and stays ended, else <see langword="null"/>.
		/// </summary>
		public Task<object?> CycleAsync(string? id)
			=> EndedAsync(id, _cycles, nameof(PolarisCycle.EndTime), async () => await context.PolarisCycles.AsNoTracking().IgnoreAutoIncludes()
				.FirstOrDefaultAsync(cycle => cycle.Id == id && cycle.EndTime != null, cancellationToken));

		/// <summary>
		/// The onrush sprint with this id when it had ended and stays ended, else <see langword="null"/>. The planning
		/// placeholder never ends.
		/// </summary>
		public Task<object?> SprintAsync(string? id)
			=> EndedAsync(id, _sprints, nameof(OnrushSprint.EndDate), async () => await context.OnrushSprints.AsNoTracking().IgnoreAutoIncludes()
				.FirstOrDefaultAsync(sprint => sprint.Id == id && sprint.Id != OnrushSprint.PlanningPlaceholderId && sprint.EndDate != null, cancellationToken));

		/// <summary>
		/// The ended sprint or cycle whose record the save adds, changes, moves or removes, else <see langword="null"/>.
		/// </summary>
		/// <param name="entry">The record: an executive, a reflective, an executive order or a checkpoint.</param>
		/// <param name="runProperty">The property naming the sprint or cycle that holds it.</param>
		/// <param name="run">Reads that sprint or cycle as ended.</param>
		/// <param name="leaves">Whether the record may leave an ended sprint or cycle, as a checkpoint may.</param>
		/// <param name="ignored">Properties that are not the record's to change: what the engine keeps.</param>
		public async Task<object?> RecordAsync(EntityEntry entry, string runProperty, Func<string?, Task<object?>> run, bool leaves = false, params string[] ignored)
		{
			var held = entry.Property(runProperty);
			switch (entry.State)
			{
				case EntityState.Added:
					return await run(held.CurrentValue as string);
				case EntityState.Deleted:
					return await run(held.OriginalValue as string);
				case EntityState.Modified:
					break;
				default:
					return null;
			}

			var changed = entry.Properties
				.Where(property => property.IsModified
					&& !ignored.Contains(property.Metadata.Name)
					&& !Equals(property.OriginalValue, property.CurrentValue)
					&& !ClearedByDelete(entry, property))
				.Select(property => property.Metadata.Name)
				.ToList();
			if (changed.Count == 0)
			{
				return null;
			}

			if (changed.Contains(runProperty))
			{
				var moved = (leaves ? null : await run(held.OriginalValue as string)) ?? await run(held.CurrentValue as string);
				if (moved is not null || changed.Count == 1)
				{
					return moved;
				}
			}

			return await run(held.CurrentValue as string);
		}

		/// <summary>
		/// The ended sprint an objective or a directive joins in this save, else <see langword="null"/>.
		/// </summary>
		public async Task<object?> JoinedAsync(EntityEntry entry, string sprintProperty)
		{
			var sprint = entry.Property(sprintProperty);
			return entry.State switch
			{
				EntityState.Added => await SprintAsync(sprint.CurrentValue as string),
				EntityState.Modified when sprint.IsModified
					&& !string.Equals(sprint.OriginalValue as string, sprint.CurrentValue as string, StringComparison.OrdinalIgnoreCase)
					=> await SprintAsync(sprint.CurrentValue as string),
				_ => null,
			};
		}

		private async Task<object?> EndedAsync(string? id, Dictionary<string, object?> known, string endProperty, Func<Task<object?>> stored)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				return null;
			}

			if (known.TryGetValue(id, out var cached))
			{
				return cached;
			}

			var tracked = context.ChangeTracker.Entries()
				.FirstOrDefault(entry => entry.Entity is PolarisCycle or OnrushSprint
					&& entry.Metadata.FindProperty(endProperty) is not null
					&& string.Equals(((IPuckNamedEntity)entry.Entity).Id, id, StringComparison.OrdinalIgnoreCase));
			object? ended;
			if (tracked is null)
			{
				ended = await stored();
			}
			else if (tracked.State is EntityState.Added or EntityState.Deleted or EntityState.Detached
				|| tracked.Property(endProperty).CurrentValue is null
				|| (tracked.Entity is OnrushSprint sprint && string.Equals(sprint.Id, OnrushSprint.PlanningPlaceholderId, StringComparison.OrdinalIgnoreCase)))
			{
				ended = null;
			}
			else if (tracked.State == EntityState.Unchanged || !tracked.Property(endProperty).IsModified)
			{
				ended = tracked.Entity;
			}
			else
			{
				var values = await tracked.GetDatabaseValuesAsync(cancellationToken);
				ended = values?[endProperty] is not null ? tracked.Entity : null;
			}

			known[id] = ended;
			return ended;
		}

		/// <summary>
		/// Whether a reference was cleared because the item it named is deleted in the same save.
		/// </summary>
		private bool ClearedByDelete(EntityEntry entry, PropertyEntry property)
		{
			if (property.CurrentValue is not null || property.OriginalValue is not { } original)
			{
				return false;
			}

			foreach (var foreignKey in entry.Metadata.FindForeignKeys(property.Metadata))
			{
				var principal = foreignKey.PrincipalEntityType;
				var key = foreignKey.PrincipalKey.Properties[0].Name;
				if (context.ChangeTracker.Entries().Any(candidate => candidate.State == EntityState.Deleted
					&& principal.IsAssignableFrom(candidate.Metadata)
					&& Equals(candidate.Property(key).CurrentValue, original)))
				{
					return true;
				}
			}

			return false;
		}
	}

	/// <summary>
	/// What a save asks of one objective, stellar directive or fate.
	/// </summary>
	private sealed record RequestedChange(
		object Entity,
		EndpointRef Endpoint,
		Enum? StoredStatus,
		Enum Status,
		LifecycleState? StoredState,
		LifecycleState State,
		string? StoredParentId,
		string? ParentId,
		bool Created,
		bool Closes)
	{
		/// <summary>
		/// Gets the state the item moves from: as stored, or Open for an item the save creates.
		/// </summary>
		public LifecycleState From => StoredState ?? LifecycleState.Open;

		/// <summary>
		/// Gets whether the save moves the item under another parent.
		/// </summary>
		public bool ParentChanged => !string.Equals(StoredParentId, ParentId, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// One move of an item to a state: requested, or proposed to compensate.
	/// </summary>
	private sealed record Move(object Entity, EndpointRef Endpoint, LifecycleState From, LifecycleState To, Enum Status);

	/// <summary>
	/// The lifecycle rules over one save: the graph as the save leaves it, and the moves compensation would add on top of it.
	/// A conflict found on what the save asks is judged on the save alone; the moves its compensations add are judged in the
	/// rounds that follow, each on everything moved before it, until no move is left to judge.
	/// </summary>
	private sealed class LifecycleRuling(StateJudge judge, PlainfraContext context, StateJudgement judgement, CancellationToken cancellationToken)
	{
		// Each item moves up the main line at most twice (to Started, then Finished), so the rounds end long before this.
		private const int MaxRounds = 1024;

		private readonly Dictionary<EndpointRef, Resolved?> _resolved = new(EndpointComparer.Instance);
		private readonly Dictionary<EndpointRef, LifecycleState> _moved = new(EndpointComparer.Instance);
		private readonly Dictionary<string, bool> _unlocked = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _unlocking = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<Move> _proposed = [];
		private List<Dependency>? _explicit;
		private List<Dependency>? _intrinsic;
		private Dictionary<string, Checkpoint>? _checkpoints;

		private EntityLifecycleResolver Lifecycle => judge._lifecycle;

		public async Task RuleAsync(IReadOnlyList<RequestedChange> changes, IReadOnlyList<MembershipJoin> joins)
		{
			foreach (var change in changes)
			{
				await JudgeAsync(change);
			}

			JudgeJoins(joins);
			await JudgeGainedAsync();

			for (var round = 0; _proposed.Count > 0 && round < MaxRounds; round++)
			{
				// A move forward is judged on the gates it enters; a move back on what relied on the item.
				var next = new List<(Move Move, LifecycleState From)>();
				foreach (var move in _proposed)
				{
					var before = await StateAsync(move.Endpoint) ?? move.From;
					if (move.To != before && (move.To > before) == (move.To > move.From))
					{
						_moved[move.Endpoint] = move.To;
						next.Add((move, before));
					}
				}

				_proposed.Clear();
				_unlocked.Clear();
				foreach (var (move, from) in next)
				{
					if (move.To > from)
					{
						await JudgeMoveAsync(SubjectOf(move.Entity), move.Endpoint, move.Entity.GetType(), from, move.To, closing: null);
					}
					else
					{
						await ReopenAsync(SubjectOf(move.Entity), move.Endpoint, from, move.To);
					}
				}
			}
		}

		/// <summary>
		/// Judges what the save asks of one item on the save alone: the move, what relied on it if it moves back, and where it
		/// arrives. The moves its conflicts propose are left for the rounds <see cref="RuleAsync"/> runs.
		/// </summary>
		public async Task JudgeAsync(RequestedChange change)
		{
			var subject = SubjectOf(change.Entity);
			if (change.State != change.From || change.Closes)
			{
				await JudgeMoveAsync(subject, change.Endpoint, change.Entity.GetType(), change.From, change.State, change.Closes ? change.Status : null);
			}

			if (!change.Created && change.State < change.From)
			{
				// The item is read where the save leaves it, also when the change is only being weighed (AllowsAsync).
				_moved[change.Endpoint] = change.State;
				await ReopenAsync(subject, change.Endpoint, change.From, change.State);
			}

			await JudgeArrivalAsync(subject, change);
		}

		/// <summary>
		/// An item moving back out of the state one of its dependencies needs, while that dependency's target has already
		/// passed the state it gates (PEP103 patch 4): the targets move back to the state before it, each in turn taking back
		/// what relied on it. A checkpoint the move relocks takes back what it held. Setting an item aside never reopens
		/// anything (user ruling), and an occurrence is not judged.
		/// </summary>
		private async Task ReopenAsync(StateSubject subject, EndpointRef endpoint, LifecycleState from, LifecycleState to)
		{
			if (to >= from || to == LifecycleState.Deferred)
			{
				return;
			}

			_unlocked.Clear();
			var moves = new List<Move>();
			var stuck = new HashSet<EndpointRef>(EndpointComparer.Instance);
			var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var edge in (await ExplicitAsync()).Concat(await IntrinsicAsync()))
			{
				var needed = Needed(edge);
				if (Sources(edge, endpoint) && from >= needed && to < needed)
				{
					await RelyingAsync(edge, moves, stuck, visiting);
				}
			}

			if (moves.Count == 0 && stuck.Count == 0)
			{
				return;
			}

			// One move per item: the furthest back any reopened dependency needs.
			var merged = moves
				.GroupBy(move => move.Endpoint, EndpointComparer.Instance)
				.Select(group => group.MinBy(move => move.To)!)
				.ToList();
			var relying = merged.Select(move => move.Endpoint).Concat(stuck).Distinct(EndpointComparer.Instance).Count();
			var message = $"{Name(subject)} cannot move back: {Count(relying, "item already relies", "items already rely")} on it.";
			if (stuck.Count > 0)
			{
				judgement.Conflict(new StateConflict(StateConflictKind.Lifecycle, ReopenRule, subject, message, []), []);
				return;
			}

			judgement.Conflict(
				new StateConflict(StateConflictKind.Lifecycle, ReopenRule, subject, message, merged.Select(move => Compensation(move, "It relied on an item that moved back.")).ToList()),
				merged.Select(move => move.Entity));
			_proposed.AddRange(merged);
		}

		/// <summary>
		/// Gathers the moves back of what relied on an edge that no longer holds: its target, when the target has passed the
		/// state the edge gates, or what a checkpoint the edge kept unlocked held in turn. A target relying on it that cannot
		/// move back (a fate has no open state) goes to <paramref name="stuck"/>.
		/// </summary>
		private async Task RelyingAsync(Dependency edge, List<Move> moves, HashSet<EndpointRef> stuck, HashSet<string> visiting)
		{
			if (edge.TargetKind == DependencyEndpointKind.Checkpoint)
			{
				// A checkpoint that stays unlocked by other means, or was never unlocked, held nothing on this edge.
				var checkpoint = await CheckpointAsync(edge.TargetId);
				if (checkpoint is null || !checkpoint.Unlocked || await UnlockedAsync(edge.TargetId) || !visiting.Add(edge.TargetId))
				{
					return;
				}

				var held = new EndpointRef(DependencyEndpointKind.Checkpoint, edge.TargetId);
				foreach (var outgoing in await ExplicitAsync())
				{
					if (Sources(outgoing, held))
					{
						await RelyingAsync(outgoing, moves, stuck, visiting);
					}
				}

				return;
			}

			// An occurrence moves with time, and is not judged.
			var target = new EndpointRef(edge.TargetKind, edge.TargetId, edge.TargetRecurrenceId);
			if (edge.TargetKind == DependencyEndpointKind.Eventive || await ResolveAsync(target) is not { } resolved)
			{
				return;
			}

			var gated = Gated(edge);
			var state = _moved.TryGetValue(target, out var moved) ? moved : resolved.State;
			if (state < gated)
			{
				return;
			}

			var back = gated == LifecycleState.Finished ? LifecycleState.Started : LifecycleState.Open;
			var kind = resolved.Entity.GetType();
			if (!Lifecycle.StatesOf(kind).Contains(back) || Lifecycle.DefaultStatus(kind, back) is not { } status)
			{
				stuck.Add(target);
				return;
			}

			moves.Add(new Move(resolved.Entity, target, state, back, status));
		}

		/// <summary>
		/// Judges one item's move: the gates of every state it enters, the open children of a directive that finishes, and,
		/// when the move closes the item (<paramref name="closing"/>), every dependency into it the entered gates did not read
		/// and, for a directive, its open children.
		/// </summary>
		private async Task JudgeMoveAsync(StateSubject subject, EndpointRef endpoint, Type kind, LifecycleState from, LifecycleState to, Enum? closing)
		{
			var transition = Lifecycle.Transition(kind, from, to);
			foreach (var state in transition.Entered)
			{
				if (state == LifecycleState.Started)
				{
					await GateAsync(subject, endpoint, DependencyConstraint.ToBegin, StartGateRule, "start");
				}
				else if (state == LifecycleState.Finished)
				{
					await GateAsync(subject, endpoint, DependencyConstraint.ToFinish, FinishGateRule, "finish");
					if (kind == typeof(StellarDirective))
					{
						await OpenChildrenAsync(subject, endpoint, "finish");
					}
				}
			}

			if (closing is null)
			{
				return;
			}

			await CloseGateAsync(subject, endpoint, transition, closing);
			if (kind == typeof(StellarDirective) && !transition.Entered.Contains(LifecycleState.Finished))
			{
				await OpenChildrenAsync(subject, endpoint, $"be {closing}");
			}
		}

		/// <summary>
		/// Closing an item needs every dependency into it satisfied, whatever it is constrained to (PEP103 patch 5): a
		/// dependency added after the item began or finished only keeps it from closing. The gates of the states the move
		/// enters have already read the dependencies constrained to them.
		/// </summary>
		private async Task CloseGateAsync(StateSubject subject, EndpointRef target, LifecycleTransition transition, Enum closing)
		{
			var blocking = new List<Dependency>();
			foreach (var edge in await ExplicitAsync())
			{
				if (Targets(edge, target) && !transition.Entered.Contains(Gated(edge)) && !await SatisfiedAsync(edge))
				{
					blocking.Add(edge);
				}
			}

			if (blocking.Count > 0)
			{
				await ConflictAsync(CloseGateRule, subject, $"{Name(subject)} cannot be {closing}: it is blocked by {Count(blocking.Count, "dependency", "dependencies")}.", blocking);
			}
		}

		private async Task GateAsync(StateSubject subject, EndpointRef target, DependencyConstraint gate, string rule, string verb)
		{
			var blocking = new List<Dependency>();
			foreach (var edge in await ExplicitAsync())
			{
				// The default constraint (null) is ToBegin.
				if (Targets(edge, target) && (edge.Constraint ?? DependencyConstraint.ToBegin) == gate && !await SatisfiedAsync(edge))
				{
					blocking.Add(edge);
				}
			}

			if (blocking.Count > 0)
			{
				await ConflictAsync(rule, subject, $"{Name(subject)} cannot {verb}: it is blocked by {Count(blocking.Count, "dependency", "dependencies")}.", blocking);
			}
		}

		private async Task OpenChildrenAsync(StateSubject subject, EndpointRef target, string verb)
		{
			var open = new List<Dependency>();
			foreach (var edge in await IntrinsicAsync())
			{
				if (Targets(edge, target) && !await SatisfiedAsync(edge))
				{
					open.Add(edge);
				}
			}

			if (open.Count > 0)
			{
				await ConflictAsync(OpenChildrenRule, subject, $"{Name(subject)} cannot {verb}: it still has {Count(open.Count, "open child", "open children")}.", open);
			}
		}

		/// <summary>
		/// Open work arriving under a directive that stood closed before the save and stays so — created there, moved there, or
		/// taken up there again from a deferral (PEP103 patch 5): what is done is done, so the directive keeps its finish, but
		/// it steps back to its default finished status, since a closed directive relies on every child having finished (its
		/// intrinsic edges). Under a directive that has finished without closing, open work is accepted and only keeps it from
		/// closing. Work brought back from a finish is judged by the reopen rule instead, and a directive this save closes by
		/// its own move by the close gate, whose compensation finishes the work.
		/// </summary>
		private async Task JudgeArrivalAsync(StateSubject subject, RequestedChange change)
		{
			if (change.ParentId is null || change.State is not (LifecycleState.Open or LifecycleState.Started))
			{
				return;
			}

			var arrived = change.Created || change.ParentChanged || change.StoredState is LifecycleState.Deferred;
			var endpoint = new EndpointRef(DependencyEndpointKind.Directive, change.ParentId);
			if (!arrived
				|| _moved.ContainsKey(endpoint)
				|| (await ResolveAsync(endpoint))?.Entity is not StellarDirective parent
				|| !Lifecycle.IsClosed(parent.Status)
				|| await StoredStatusAsync(parent) is not { } stored
				|| !Lifecycle.IsClosed(stored))
			{
				return;
			}

			var move = Unclose(parent, endpoint);
			judgement.Conflict(
				new StateConflict(
					StateConflictKind.Lifecycle,
					ClosedRule,
					subject,
					$"{Name(subject)} is open work, and its directive is {parent.Status}.",
					[Compensation(move, "It receives open work.")]),
				[move.Entity]);
		}

		/// <summary>
		/// A closed objective joining the active onrush sprint or Polaris cycle (PEP103 patch 5, user ruling): it steps back to
		/// its default finished status, or the join waits or is refused, as the level decides. Joining moves an item forward
		/// only, so a finished one stays as it is.
		/// </summary>
		private void JudgeJoins(IReadOnlyList<MembershipJoin> joins)
		{
			foreach (var join in joins)
			{
				var endpoint = new EndpointRef(DependencyEndpointKind.Objective, join.Objective.Id);
				if (!Lifecycle.IsClosed(join.Objective.Status) || _moved.ContainsKey(endpoint))
				{
					continue;
				}

				var subject = SubjectOf(join.Objective);
				var move = Unclose(join.Objective, endpoint);
				judgement.Conflict(
					new StateConflict(
						StateConflictKind.Lifecycle,
						ClosedJoinRule,
						subject,
						$"{Name(subject)} is {join.Objective.Status}, so it cannot join the active {join.Membership} as it stands.",
						[Compensation(move, $"It joins the active {join.Membership}.")]),
					[move.Entity]);
			}
		}

		/// <summary>
		/// A closed item gaining a dependency that does not hold, without its source moving back (PEP103 patch 5): one the save
		/// adds into it, or one into a checkpoint ahead of it that the save locks again — a new dependency into the checkpoint,
		/// or its external condition turned against it. What is done is done: a finished item keeps its finish, and only a
		/// closed one steps back to its default finished status. An item another rule already moves is left to it.
		/// </summary>
		private async Task JudgeGainedAsync()
		{
			var added = context.ChangeTracker.Entries<Dependency>()
				.Where(entry => entry.State == EntityState.Added)
				.Select(entry => entry.Entity)
				.ToList();
			var relocking = context.ChangeTracker.Entries<Checkpoint>()
				.Where(entry => entry.State == EntityState.Modified
					&& entry.Property(nameof(Checkpoint.ExternalCondition)).IsModified
					&& entry.Entity.ExternalCondition == false)
				.Select(entry => entry.Entity.Id)
				.ToList();
			if (added.Count == 0 && relocking.Count == 0)
			{
				return;
			}

			var gaining = new Dictionary<EndpointRef, (Resolved Item, int Count)>(EndpointComparer.Instance);
			foreach (var edge in added)
			{
				if (await SatisfiedAsync(edge))
				{
					continue;
				}

				if (edge.TargetKind == DependencyEndpointKind.Checkpoint)
				{
					relocking.Add(edge.TargetId);
				}
				else
				{
					await GainAsync(new EndpointRef(edge.TargetKind, edge.TargetId, edge.TargetRecurrenceId), gaining);
				}
			}

			var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var checkpointId in relocking)
			{
				await RelockedAsync(checkpointId, gaining, visiting);
			}

			foreach (var (endpoint, (item, count)) in gaining)
			{
				var subject = SubjectOf(item.Entity);
				var status = Lifecycle.StatusPropertyOf(item.Entity.GetType())!.GetValue(item.Entity);
				var move = Unclose(item.Entity, endpoint);
				judgement.Conflict(
					new StateConflict(
						StateConflictKind.Lifecycle,
						ClosedRule,
						subject,
						$"{Name(subject)} cannot stay {status}: it is blocked by {Count(count, "dependency", "dependencies")}.",
						[Compensation(move, "A dependency it has does not hold.")]),
					[move.Entity]);
			}
		}

		/// <summary>
		/// What a checkpoint the save locks again held: each closed item behind it gains a dependency that does not hold, and a
		/// checkpoint behind it that locks again in turn passes it on.
		/// </summary>
		private async Task RelockedAsync(string checkpointId, Dictionary<EndpointRef, (Resolved Item, int Count)> gaining, HashSet<string> visiting)
		{
			if (!visiting.Add(checkpointId) || await CheckpointAsync(checkpointId) is not { Unlocked: true } || await UnlockedAsync(checkpointId))
			{
				return;
			}

			var held = new EndpointRef(DependencyEndpointKind.Checkpoint, checkpointId);
			foreach (var edge in await ExplicitAsync())
			{
				if (!Sources(edge, held))
				{
					continue;
				}

				if (edge.TargetKind == DependencyEndpointKind.Checkpoint)
				{
					await RelockedAsync(edge.TargetId, gaining, visiting);
				}
				else
				{
					await GainAsync(new EndpointRef(edge.TargetKind, edge.TargetId, edge.TargetRecurrenceId), gaining);
				}
			}
		}

		/// <summary>
		/// Counts a dependency that does not hold against its target, when the target stands closed and nothing else moves it.
		/// </summary>
		private async Task GainAsync(EndpointRef target, Dictionary<EndpointRef, (Resolved Item, int Count)> gaining)
		{
			if (target.Kind == DependencyEndpointKind.Eventive
				|| _moved.ContainsKey(target)
				|| _proposed.Any(move => EndpointComparer.Instance.Equals(move.Endpoint, target))
				|| await ResolveAsync(target) is not { } resolved
				|| !Lifecycle.IsClosed(resolved.Entity))
			{
				return;
			}

			gaining[target] = gaining.TryGetValue(target, out var known) ? (known.Item, known.Count + 1) : (resolved, 1);
		}

		/// <summary>
		/// The step back of a closed item to its kind's default finished status: still finished, no longer closed.
		/// </summary>
		private Move Unclose(object entity, EndpointRef endpoint)
			=> new(entity, endpoint, LifecycleState.Finished, LifecycleState.Finished, Lifecycle.DefaultStatus(entity.GetType(), LifecycleState.Finished)!);

		/// <summary>
		/// Records a lifecycle conflict over the edges that block it, with the moves that would satisfy them all — or none,
		/// when any of them could not be satisfied — and proposes those moves for judging in the next round.
		/// </summary>
		private async Task ConflictAsync(string rule, StateSubject subject, string message, IReadOnlyList<Dependency> blocking)
		{
			var moves = new List<Move>();
			foreach (var edge in blocking)
			{
				var satisfying = await CompensateAsync(edge.Source, Needed(edge), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
				if (satisfying is null)
				{
					judgement.Conflict(new StateConflict(StateConflictKind.Lifecycle, rule, subject, message, []), []);
					return;
				}

				moves.AddRange(satisfying);
			}

			// One move per item: the furthest any edge needs, since a finish passes through the start.
			var merged = moves
				.GroupBy(move => move.Endpoint, EndpointComparer.Instance)
				.Select(group => group.MaxBy(move => move.To)!)
				.ToList();
			var reason = rule == OpenChildrenRule ? "Its directive is finishing." : "A dependency needs it.";
			judgement.Conflict(
				new StateConflict(StateConflictKind.Lifecycle, rule, subject, message, merged.Select(move => Compensation(move, reason)).ToList()),
				merged.Select(move => move.Entity));
			_proposed.AddRange(merged);
		}

		/// <summary>
		/// The moves that would bring an edge's source to <paramref name="needed"/>, or <see langword="null"/> when nothing the
		/// core could move would.
		/// </summary>
		private async Task<List<Move>?> CompensateAsync(EndpointRef source, LifecycleState needed, HashSet<string> visiting)
		{
			if (source.Kind == DependencyEndpointKind.Checkpoint)
			{
				// A checkpoint unlocks of itself once what holds it is met; only the dependencies holding it can be moved. A locked
				// one with nothing to wait for, or held by its toll, never unlocks by a move.
				var checkpoint = await CheckpointAsync(source.Id);
				var now = DateTime.Now;
				var incoming = (await ExplicitAsync()).Where(edge => Targets(edge, source)).ToList();
				if (checkpoint is null
					|| !DependencyReconciler.ConditionMet(checkpoint)
					|| (!checkpoint.Unlocked && (!DependencyReconciler.TollMet(checkpoint, now) || !DependencyReconciler.Holds(checkpoint, incoming.Count, now)))
					|| !visiting.Add(source.Id))
				{
					return null;
				}

				var moves = new List<Move>();
				foreach (var edge in incoming)
				{
					if (await SatisfiedAsync(edge))
					{
						continue;
					}

					var inner = await CompensateAsync(edge.Source, Needed(edge), visiting);
					if (inner is null)
					{
						return null;
					}

					moves.AddRange(inner);
				}

				return moves;
			}

			// An occurrence moves with time, not by a status the core could set.
			if (source.Kind == DependencyEndpointKind.Eventive || await ResolveAsync(source) is not { } resolved)
			{
				return null;
			}

			var state = _moved.TryGetValue(source, out var moved) ? moved : resolved.State;
			if (state >= needed)
			{
				return [];
			}

			var kind = resolved.Entity.GetType();
			if (state == LifecycleState.Deferred
				|| !Lifecycle.StatesOf(kind).Contains(needed)
				|| Lifecycle.DefaultStatus(kind, needed) is not { } status)
			{
				return null;
			}

			return [new Move(resolved.Entity, source, state, needed, status)];
		}

		/// <summary>
		/// Whether an edge holds, read on the save as it stands and the moves proposed so far: its source reached the state
		/// its trigger needs, or, for a checkpoint source, the checkpoint would unlock.
		/// </summary>
		private async Task<bool> SatisfiedAsync(Dependency edge)
		{
			if (edge.SourceKind == DependencyEndpointKind.Checkpoint)
			{
				return await UnlockedAsync(edge.SourceId);
			}

			return await StateAsync(edge.Source) is { } state && state >= Needed(edge);
		}

		/// <summary>
		/// Whether a checkpoint would be unlocked once the save is through, as the reconciler reads it
		/// (<see cref="DependencyReconciler.Unlocks"/>): every dependency into it holds and its condition stands aside, and it
		/// was unlocked already or has something to wait for, its toll met. A loop of checkpoints holds itself locked.
		/// </summary>
		private async Task<bool> UnlockedAsync(string checkpointId)
		{
			if (_unlocked.TryGetValue(checkpointId, out var known))
			{
				return known;
			}

			if (!_unlocking.Add(checkpointId))
			{
				return false;
			}

			var unlocked = false;
			if (await CheckpointAsync(checkpointId) is { } checkpoint)
			{
				var target = new EndpointRef(DependencyEndpointKind.Checkpoint, checkpointId);
				var incoming = 0;
				var met = true;
				foreach (var edge in await ExplicitAsync())
				{
					if (!Targets(edge, target))
					{
						continue;
					}

					incoming++;
					met = met && await SatisfiedAsync(edge);
				}

				unlocked = DependencyReconciler.Unlocks(checkpoint, incoming, met, DateTime.Now);
			}

			_unlocking.Remove(checkpointId);
			_unlocked[checkpointId] = unlocked;
			return unlocked;
		}

		/// <summary>
		/// An endpoint's state on the save as it stands and the moves proposed so far, or <see langword="null"/> when no entity
		/// backs it (an occurrence still projected, an id naming nothing).
		/// </summary>
		private async Task<LifecycleState?> StateAsync(EndpointRef endpoint)
			=> _moved.TryGetValue(endpoint, out var moved) ? moved : (await ResolveAsync(endpoint))?.State;

		private async Task<Resolved?> ResolveAsync(EndpointRef endpoint)
		{
			if (_resolved.TryGetValue(endpoint, out var known))
			{
				return known;
			}

			var entity = await DependencyEndpoints.ResolveEntityAsync(context, endpoint, cancellationToken);
			var resolved = entity is null || context.Entry(entity).State == EntityState.Deleted
				? null
				: new Resolved(entity, Lifecycle.StateOf(entity));
			_resolved[endpoint] = resolved;
			return resolved;
		}

		private async Task<Enum?> StoredStatusAsync(StellarDirective directive)
		{
			var entry = context.Entry(directive);
			return entry.State switch
			{
				EntityState.Added => null,
				EntityState.Unchanged => directive.Status,
				_ => (await entry.GetDatabaseValuesAsync(cancellationToken))?[nameof(StellarDirective.Status)] as Enum,
			};
		}

		/// <summary>
		/// The stored edges as the save leaves them: those it deletes gone, those it adds counted.
		/// </summary>
		private async Task<List<Dependency>> ExplicitAsync()
		{
			if (_explicit is not null)
			{
				return _explicit;
			}

			var stored = await context.Dependencies.ToListAsync(cancellationToken);
			_explicit = stored
				.Where(dependency => context.Entry(dependency).State != EntityState.Deleted)
				.Concat(context.ChangeTracker.Entries<Dependency>()
					.Where(entry => entry.State == EntityState.Added)
					.Select(entry => entry.Entity))
				.Distinct()
				.ToList();
			return _explicit;
		}

		/// <summary>
		/// The edges the structure implies as the save leaves it (PEP102 patch 9).
		/// </summary>
		private async Task<List<Dependency>> IntrinsicAsync()
		{
			if (_intrinsic is not null)
			{
				return _intrinsic;
			}

			_intrinsic = [];
			foreach (var rule in judge._intrinsicRules)
			{
				_intrinsic.AddRange(await rule.DeriveAsync(context, cancellationToken));
			}

			return _intrinsic;
		}

		private async Task<Checkpoint?> CheckpointAsync(string checkpointId)
		{
			if (_checkpoints is null)
			{
				var stored = await context.Checkpoints.ToListAsync(cancellationToken);
				_checkpoints = stored
					.Where(checkpoint => context.Entry(checkpoint).State != EntityState.Deleted)
					.Concat(context.ChangeTracker.Entries<Checkpoint>()
						.Where(entry => entry.State == EntityState.Added)
						.Select(entry => entry.Entity))
					.DistinctBy(checkpoint => checkpoint.Id, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(checkpoint => checkpoint.Id, StringComparer.OrdinalIgnoreCase);
			}

			return _checkpoints.GetValueOrDefault(checkpointId);
		}

		private StateCompensation Compensation(Move move, string reason)
			=> new(SubjectOf(move.Entity), move.From, move.To, move.Status.ToString(), reason);

		/// <summary>
		/// The state an edge needs of its source: the finish for a finish trigger (the default), the start for a begin trigger.
		/// </summary>
		private static LifecycleState Needed(Dependency edge)
			=> (edge.Trigger ?? DependencyTrigger.OnFinish) == DependencyTrigger.OnFinish ? LifecycleState.Finished : LifecycleState.Started;

		private static bool Targets(Dependency edge, EndpointRef target)
			=> edge.TargetKind == target.Kind && string.Equals(edge.TargetId, target.Id, StringComparison.OrdinalIgnoreCase);

		private static bool Sources(Dependency edge, EndpointRef source)
			=> edge.SourceKind == source.Kind && string.Equals(edge.SourceId, source.Id, StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// The state an edge gates on its target: the finish for a finish constraint, the start otherwise (the default).
		/// </summary>
		private static LifecycleState Gated(Dependency edge)
			=> (edge.Constraint ?? DependencyConstraint.ToBegin) == DependencyConstraint.ToFinish ? LifecycleState.Finished : LifecycleState.Started;

		private sealed record Resolved(object Entity, LifecycleState State);
	}

	/// <summary>
	/// Compares endpoints as the core's ids compare: kind and slot exactly, id regardless of case.
	/// </summary>
	private sealed class EndpointComparer : IEqualityComparer<EndpointRef>
	{
		public static EndpointComparer Instance { get; } = new();

		public bool Equals(EndpointRef x, EndpointRef y)
			=> x.Kind == y.Kind && x.RecurrenceId == y.RecurrenceId && string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase);

		public int GetHashCode(EndpointRef endpoint)
			=> HashCode.Combine(endpoint.Kind, endpoint.RecurrenceId, StringComparer.OrdinalIgnoreCase.GetHashCode(endpoint.Id));
	}
}

/// <summary>
/// What <see cref="StateJudge"/> found in one save (PEP103): the conflicts, and the entities their subjects and compensations
/// name, so a compensation is applied to the very instance the save tracks.
/// </summary>
public sealed class StateJudgement
{
	private readonly List<StateConflict> _conflicts = [];
	private readonly Dictionary<string, object> _entities = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Gets every conflict found, in the order found.
	/// </summary>
	public IReadOnlyList<StateConflict> Conflicts => _conflicts;

	/// <summary>
	/// Gets the tracked entity a subject names, or <see langword="null"/> when the judge did not resolve it.
	/// </summary>
	public object? EntityOf(StateSubject subject)
	{
		ArgumentNullException.ThrowIfNull(subject);
		return _entities.GetValueOrDefault(Key(subject.Kind, subject.Id));
	}

	internal void Conflict(StateConflict conflict, IEnumerable<object> entities)
	{
		_conflicts.Add(conflict);
		foreach (var entity in entities)
		{
			var subject = StateJudge.SubjectOf(entity);
			_entities[Key(subject.Kind, subject.Id)] = entity;
		}
	}

	internal void Refuse(string rule, StateSubject subject, string message)
		=> _conflicts.Add(new StateConflict(StateConflictKind.Systemic, rule, subject, message, []));

	private static string Key(string kind, string id) => $"{kind}\u001F{id}";
}
