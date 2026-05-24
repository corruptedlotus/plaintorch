using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Saga;

namespace Pleiades.Vault.Database;

/// <summary>
/// Provides application-facing persistence operations over the vault database.
/// </summary>
public sealed class PlaintorchRepository(PlainfraContext context)
{
	/// <summary>
	/// Replaces the current lore page index with a newly rebuilt snapshot.
	/// </summary>
	/// <param name="entries">The lore pages to persist.</param>
	public void ReplaceLoreIndexEntries(IReadOnlyCollection<LorePage> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);

		context.LorePages.RemoveRange(context.LorePages);
		context.LorePages.AddRange(entries);
		context.SaveChanges();
	}

	/// <summary>
	/// Replaces the current lore page index with a newly rebuilt snapshot.
	/// </summary>
	/// <param name="entries">The lore pages to persist.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	public async Task ReplaceLoreIndexEntriesAsync(IReadOnlyCollection<LorePage> entries, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entries);

		context.LorePages.RemoveRange(context.LorePages);
		await context.LorePages.AddRangeAsync(entries, cancellationToken);
		await context.SaveChangesAsync(cancellationToken);
	}

	/// <summary>
	/// Gets the current lore page index ordered by PUCK.
	/// </summary>
	/// <returns>The indexed lore pages.</returns>
	public IReadOnlyList<LorePage> GetLoreIndexEntries()
	{
		return context.LorePages
			.OrderBy(x => x.Id)
			.ToList();
	}

	/// <summary>
	/// Creates or updates a predefined tag definition.
	/// </summary>
	/// <param name="tagDefinition">The tag definition to persist.</param>
	public void UpsertTagDefinition(TagDefinition tagDefinition)
	{
		var existing = context.TagDefinitions.SingleOrDefault(x => x.Id == tagDefinition.Id);
		if (existing is null)
		{
			context.TagDefinitions.Add(tagDefinition);
		}
		else
		{
			existing.Title = tagDefinition.Title;
			existing.Color = tagDefinition.Color;
			existing.Description = tagDefinition.Description;
		}

		context.SaveChanges();
	}

	/// <summary>
	/// Gets all predefined tag definitions ordered by title.
	/// </summary>
	/// <returns>The persisted tag definitions.</returns>
	public IReadOnlyList<TagDefinition> GetTagDefinitions()
	{
		return context.TagDefinitions
			.OrderBy(x => x.Title)
			.ToList();
	}

	/// <summary>
	/// Gets a directive by identifier.
	/// </summary>
	public Directive? GetDirective(string directiveId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directiveId);
		var directive = context.Directives.SingleOrDefault(x => x.Id == directiveId);
		if (directive is not null && !string.IsNullOrWhiteSpace(directive.ParentDirectiveId))
		{
			directive.ParentDirective = GetDirective(directive.ParentDirectiveId);
		}

		return directive;
	}

	/// <summary>
	/// Creates or updates a directive.
	/// </summary>
	/// <param name="directive">The directive to persist.</param>
	public void UpsertDirective(Directive directive)
	{
		var existing = context.Directives.SingleOrDefault(x => x.Id == directive.Id);
		if (existing is null)
		{
			context.Directives.Add(directive);
		}
		else
		{
			existing.Title = directive.Title;
			existing.Codename = directive.Codename;
			existing.ParentDirectiveId = directive.ParentDirectiveId;
			existing.Status = directive.Status;
			existing.Tags = directive.Tags.ToList();
			existing.Due = directive.Due;
			existing.StartDate = directive.StartDate;
			existing.EndDate = directive.EndDate;
		}

		context.SaveChanges();
	}

	/// <summary>
	/// Creates or updates an onrush sprint.
	/// </summary>
	/// <param name="sprint">The sprint to persist.</param>
	public void UpsertOnrushSprint(OnrushSprint sprint)
	{
		var existing = context.OnrushSprints.SingleOrDefault(x => x.Id == sprint.Id);
		if (existing is null)
		{
			context.OnrushSprints.Add(sprint);
		}
		else
		{
			existing.Title = sprint.Title;
			existing.StartDate = sprint.StartDate;
			existing.EndDate = sprint.EndDate;
		}

		context.SaveChanges();
	}

	/// <summary>
	/// Creates or updates an objective.
	/// </summary>
	/// <param name="objective">The objective to persist.</param>
	public void UpsertObjective(Objective objective)
	{
		var existing = context.Objectives.SingleOrDefault(x => x.Id == objective.Id);
		if (existing is null)
		{
			context.Objectives.Add(objective);
		}
		else
		{
			existing.Title = objective.Title;
			existing.DirectiveId = objective.DirectiveId;
			existing.OnrushSprintId = objective.OnrushSprintId;
			existing.College = objective.College;
			existing.Status = objective.Status;
			existing.CelestronValue = objective.CelestronValue;
			existing.IsEnduring = objective.IsEnduring;
		}

		context.SaveChanges();
	}

	/// <summary>
	/// Creates or updates a Polaris cycle.
	/// </summary>
	/// <param name="cycle">The cycle to persist.</param>
	public void UpsertPolarisCycle(PolarisCycle cycle)
	{
		var existing = context.PolarisCycles.SingleOrDefault(x => x.Id == cycle.Id);
		if (existing is null)
		{
			context.PolarisCycles.Add(cycle);
		}
		else
		{
			existing.Title = cycle.Title;
			existing.StartTime = cycle.StartTime;
			existing.EndTime = cycle.EndTime;
			existing.Forecast = cycle.Forecast is null
				? null
				: new PolarisForecast
				{
					ForecastReference = cycle.Forecast.ForecastReference,
					ForecastTarget = cycle.Forecast.ForecastTarget,
				};
		}

		context.SaveChanges();
	}

	/// <summary>
	/// Adds an execution record to an existing Polaris cycle.
	/// </summary>
	/// <param name="executive">The execution record to add.</param>
	/// <returns>The generated primary key value.</returns>
	public long AddExecutive(Executive executive)
	{
		var cycle = context.PolarisCycles
			.Include(x => x.Executives)
			.Single(x => x.Id == executive.PolarisCycleId);

		if (!string.IsNullOrWhiteSpace(executive.ObjectiveId))
		{
			executive.Objective = context.Objectives.SingleOrDefault(x => x.Id == executive.ObjectiveId);
		}

		cycle.Executives.Add(executive);
		context.SaveChanges();
		return executive.Id;
	}

	/// <summary>
	/// Adds a reflective record to an existing Polaris cycle.
	/// </summary>
	/// <param name="reflective">The reflective record to add.</param>
	/// <returns>The generated primary key value.</returns>
	public long AddReflective(Reflective reflective)
	{
		var cycle = context.PolarisCycles
			.Include(x => x.Reflectives)
			.Single(x => x.Id == reflective.PolarisCycleId);

		cycle.Reflectives.Add(reflective);
		context.SaveChanges();
		return reflective.Id;
	}

	/// <summary>
	/// Adds a transaction to the Celestron ledger.
	/// </summary>
	/// <param name="transaction">The transaction to record.</param>
	public void AddCelestronTransaction(CelestronTransaction transaction)
	{
		context.CelestronLedger.Add(transaction);
		context.SaveChanges();
	}

	/// <summary>
	/// Gets aggregate counts for the current stored model set.
	/// </summary>
	/// <returns>A tuple containing the current entity counts.</returns>
	public (int Directives, int Objectives, int Sprints, int Cycles, int Executives, int Reflectives, int Transactions, int Tags, int LoreEntries) GetCounts()
	{
		return (
			context.Directives.Count(),
			context.Objectives.Count(),
			context.OnrushSprints.Count(),
			context.PolarisCycles.Count(),
			context.PolarisCycles.SelectMany(x => x.Executives).Count(),
			context.PolarisCycles.SelectMany(x => x.Reflectives).Count(),
			context.CelestronLedger.Count(),
			context.TagDefinitions.Count(),
			context.LorePages.Count());
	}
}