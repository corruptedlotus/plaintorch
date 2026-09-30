using Pleiades.Orchestration;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Frontmatter serialize/deserialize invariants: quiet PUCK identity, field mapping, body and unknown-key preservation.
/// </summary>
public sealed class SerializerRoundTripTests : VaultTestBase
{
	[Fact]
	public void Quiet_objective_round_trips_puck_body_and_fields()
	{
		var serializer = Vault.GetSingleton<MarkdownFrontMatterSerializer>();
		var objective = new Objective
		{
			Id = "j12345678",
			Title = "Ship it",
			College = ObjectiveCollege.Lore,
			Status = ObjectiveStatus.Onrush,
			CelestronValue = 7,
		};

		var markdown = serializer.Serialize(objective, "My precious body.");

		Assert.Contains("puck: j12345678", markdown);
		Assert.Contains("college: Lore", markdown);
		Assert.Contains("starfire: 7", markdown);
		Assert.EndsWith("My precious body." + Environment.NewLine, markdown);

		var result = serializer.Deserialize(markdown, new Objective { Id = string.Empty, Title = "Ship it" });
		Assert.Empty(result.Issues);
		Assert.Equal("j12345678", result.Model.Id);
		Assert.Equal(ObjectiveCollege.Lore, result.Model.College);
		Assert.Equal(ObjectiveStatus.Onrush, result.Model.Status);
		Assert.Equal(7, result.Model.CelestronValue);
	}

	[Fact]
	public void Objective_due_round_trips_as_one_compact_field()
	{
		var serializer = Vault.GetSingleton<MarkdownFrontMatterSerializer>();

		// A timed due renders as one minute-precise datetime field and round-trips its moment (floating, no zone).
		var timed = new Objective { Id = "j00000003", Title = "Timed", Due = Due.At(new DateTime(2026, 7, 20, 14, 30, 0)) };
		var timedMarkdown = serializer.Serialize(timed);
		Assert.Contains("due: 2026-07-20T14:30" + Environment.NewLine, timedMarkdown);
		var timedBack = serializer.Deserialize(timedMarkdown, new Objective { Id = string.Empty, Title = "Timed" });
		Assert.Empty(timedBack.Issues);
		Assert.Equal(new DateTime(2026, 7, 20, 14, 30, 0), timedBack.Model.Due!.Moment);
		Assert.Null(timedBack.Model.Due!.TimeZone);

		// A whole-day due renders as a bare date.
		var allDay = new Objective { Id = "j00000004", Title = "AllDay", Due = Due.On(new DateOnly(2026, 7, 20)) };
		Assert.Contains("due: 2026-07-20" + Environment.NewLine, serializer.Serialize(allDay));

		// A zoned due carries a trailing space-separated zone, both directions.
		var zoned = new Objective { Id = "j00000005", Title = "Zoned", Due = Due.At(new DateTime(2026, 7, 20, 9, 0, 0), "America/New_York") };
		var zonedMarkdown = serializer.Serialize(zoned);
		Assert.Contains("due: 2026-07-20T09:00 America/New_York" + Environment.NewLine, zonedMarkdown);
		var zonedBack = serializer.Deserialize(zonedMarkdown, new Objective { Id = string.Empty, Title = "Zoned" });
		Assert.Equal(new DateTime(2026, 7, 20, 9, 0, 0), zonedBack.Model.Due!.Moment);
		Assert.Equal("America/New_York", zonedBack.Model.Due!.TimeZone);
	}

	[Fact]
	public void Unknown_frontmatter_keys_are_preserved()
	{
		var serializer = Vault.GetSingleton<MarkdownFrontMatterSerializer>();
		var objective = new Objective { Id = "j00000001", Title = "Keep" };
		var preserved = new Dictionary<string, string> { ["customKey"] = "customValue" };

		var markdown = serializer.Serialize(objective, string.Empty, preserved);

		Assert.Contains("customKey: customValue", markdown);
	}

	[Fact]
	public void Retired_frontmatter_keys_are_dropped_while_unknown_ones_are_kept(/* PEP101 */)
	{
		// A stellar directive no longer maps startDate/endDate (its dates became backlog schedules). Left as unknown keys,
		// they would linger in every note that ever carried them; retired, they go on the next rewrite. Matching ignores
		// case, as frontmatter parsing does.
		var serializer = Vault.GetSingleton<MarkdownFrontMatterSerializer>();
		var directive = new StellarDirective { Id = "A12345678", Title = "Campaign", Status = DirectiveStatus.Active };
		var preserved = new Dictionary<string, string>
		{
			["startDate"] = "2026-07-01",
			["ENDDATE"] = "2026-09-01",
			["mood"] = "bright",
		};

		var markdown = serializer.Serialize(directive, string.Empty, preserved);

		Assert.DoesNotContain("startDate", markdown, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("endDate", markdown, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("mood: bright", markdown);
		Assert.Contains("status: Active", markdown);
		Assert.Equal(["endDate", "startDate"], MarkdownFrontMatterSerializer.GetRetiredFieldNames(typeof(StellarDirective)).Order(StringComparer.Ordinal));
		Assert.Empty(MarkdownFrontMatterSerializer.GetRetiredFieldNames(typeof(LunarDirective)));
	}

	[Fact]
	public void Missing_required_fields_produce_validation_issues()
	{
		var serializer = Vault.GetSingleton<MarkdownFrontMatterSerializer>();
		var markdown = "---" + Environment.NewLine + "puck: j00000002" + Environment.NewLine + "---" + Environment.NewLine;

		var issues = serializer.Validate(markdown, new Objective { Id = string.Empty, Title = "Untitled" });

		Assert.NotEmpty(issues);
	}
}
