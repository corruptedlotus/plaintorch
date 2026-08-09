using Pleiades.Orchestration;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Covers the executive-order effectiveness rule (PEP102.5): each unset bound falls back to the parent
/// onrush's window, so a timeless order is Onrush-bound — active only while the onrush runs.
/// </summary>
public sealed class ExecutiveOrderEffectivenessTests
{
	private static OnrushSprint Sprint(DateOnly start, DateOnly end)
		=> new() { Id = "x0100", Title = "Sprint", StartDate = start, EndDate = end };

	private static ExecutiveOrder Order(OnrushSprint sprint, DateOnly? from = null, DateOnly? until = null)
		=> new() { Id = "x100-o01", Title = "EO", OnrushSprintId = sprint.Id, OnrushSprint = sprint, EffectiveFrom = from, EffectiveUntil = until };

	[Fact]
	public void Timeless_order_is_bound_to_its_onrush_window()
	{
		var sprint = Sprint(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 14));
		var order = Order(sprint);

		Assert.True(order.IsOnrushBound);
		Assert.True(order.IsEffectiveOn(new DateOnly(2026, 8, 1)));
		Assert.True(order.IsEffectiveOn(new DateOnly(2026, 8, 14)));
		Assert.False(order.IsEffectiveOn(new DateOnly(2026, 7, 31)));
		Assert.False(order.IsEffectiveOn(new DateOnly(2026, 8, 15)));
	}

	[Fact]
	public void Dated_order_uses_its_own_window_over_the_onrush()
	{
		var sprint = Sprint(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 14));
		var order = Order(sprint, from: new DateOnly(2026, 8, 20), until: new DateOnly(2026, 8, 25));

		Assert.False(order.IsOnrushBound);
		// Inside the onrush window but outside the order's own — not in effect.
		Assert.False(order.IsEffectiveOn(new DateOnly(2026, 8, 10)));
		Assert.True(order.IsEffectiveOn(new DateOnly(2026, 8, 22)));
	}

	[Fact]
	public void Half_dated_order_inherits_the_missing_bound_from_the_onrush()
	{
		var sprint = Sprint(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 14));
		// Only an end is set; the start falls back to the onrush's start.
		var order = Order(sprint, until: new DateOnly(2026, 8, 5));

		Assert.False(order.IsOnrushBound);
		Assert.True(order.IsEffectiveOn(new DateOnly(2026, 8, 1)));
		Assert.True(order.IsEffectiveOn(new DateOnly(2026, 8, 5)));
		Assert.False(order.IsEffectiveOn(new DateOnly(2026, 8, 6)));
		Assert.False(order.IsEffectiveOn(new DateOnly(2026, 7, 31)));
	}

	[Fact]
	public void Timeless_order_on_a_dateless_onrush_stays_unbounded()
	{
		var sprint = new OnrushSprint { Id = "x0100", Title = "Sprint" };
		var order = Order(sprint);

		Assert.True(order.IsOnrushBound);
		Assert.True(order.IsEffectiveOn(new DateOnly(2026, 8, 10)));
	}
}
