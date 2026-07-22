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
			IsEnduring = true,
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
		Assert.True(result.Model.IsEnduring);
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
	public void Missing_required_fields_produce_validation_issues()
	{
		var serializer = Vault.GetSingleton<MarkdownFrontMatterSerializer>();
		var markdown = "---" + Environment.NewLine + "puck: j00000002" + Environment.NewLine + "---" + Environment.NewLine;

		var issues = serializer.Validate(markdown, new Objective { Id = string.Empty, Title = "Untitled" });

		Assert.NotEmpty(issues);
	}
}
