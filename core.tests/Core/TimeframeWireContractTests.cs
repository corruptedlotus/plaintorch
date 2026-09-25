using System.Text.Json;
using System.Text.Json.Nodes;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Hosting;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Pins the PEP100 patch 2 wire contract the SDK and SIPA rely on, serialized with exactly the host's HTTP options (the
/// web defaults plus <see cref="PlaintorchHostFactory.ConfigureSerializer"/>). Responses: timeframes and timeframe
/// records carry a camelCase <c>exclusive</c> and a numeric <c>autoInclusion</c> (Availability is <c>2</c>), and both
/// directive kinds serialized through the polymorphic <see cref="Directive"/> base carry <c>availabilityTimeframeId</c>
/// with the navigation present as <see langword="null"/> — the client's FK-coherence rule needs both keys. Requests: the
/// creation affinity is tri-state (an omitted key is Auto, <see langword="null"/> is an explicit none, an id is
/// explicit), the availability request takes an id or <see langword="null"/>, and a timeframe update keeps
/// <c>exclusive</c> when it is omitted.
/// </summary>
public sealed class TimeframeWireContractTests
{
	private static readonly JsonSerializerOptions HostOptions = CreateHostOptions();

	private static JsonSerializerOptions CreateHostOptions()
	{
		// ASP.NET Core's HTTP JSON options start from the web defaults; the host then applies its own conventions.
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		PlaintorchHostFactory.ConfigureSerializer(options);
		return options;
	}

	private static JsonObject Serialize<T>(T value)
		=> Assert.IsType<JsonObject>(JsonNode.Parse(JsonSerializer.Serialize(value, HostOptions)));

	private static T Deserialize<T>(string json)
		=> Assert.IsType<T>(JsonSerializer.Deserialize<T>(json, HostOptions));

	private static void AssertNumber(JsonNode? node, int expected)
	{
		Assert.NotNull(node);
		Assert.Equal(JsonValueKind.Number, node.GetValueKind());
		Assert.Equal(expected, node.GetValue<int>());
	}

	[Fact]
	public void The_availability_inclusion_is_the_number_two()
	{
		Assert.Equal(2, (int)TimeframeInclusion.Availability);
	}

	[Fact]
	public void A_timeframe_carries_exclusive_and_a_numeric_auto_inclusion()
	{
		var json = Serialize(new Timeframe
		{
			Id = 3,
			DirectiveId = "M1",
			Title = "Office Hours",
			StartTime = new TimeOnly(9, 0),
			EndTime = new TimeOnly(17, 0),
			AutoInclusion = TimeframeInclusion.Availability,
			Exclusive = true,
		});

		Assert.True(json["exclusive"]!.GetValue<bool>());
		Assert.False(json.ContainsKey("Exclusive"));
		AssertNumber(json["autoInclusion"], 2);
	}

	[Fact]
	public void A_timeframe_record_carries_exclusive_and_a_numeric_auto_inclusion()
	{
		var json = Serialize(new DirectiveTimeframeRecord(
			3, "M1", "Moon Law", null, LunarDirectiveStatus.Active, "Office Hours",
			new TimeOnly(9, 0), new TimeOnly(17, 0), null, null,
			TimeframeInclusion.Availability, [], Exclusive: true));

		Assert.True(json["exclusive"]!.GetValue<bool>());
		AssertNumber(json["autoInclusion"], 2);

		var plain = Serialize(new DirectiveTimeframeRecord(
			4, "M1", "Moon Law", null, LunarDirectiveStatus.Active, "Dawn",
			new TimeOnly(6, 0), new TimeOnly(8, 0), null, null,
			TimeframeInclusion.None, []));
		Assert.False(plain["exclusive"]!.GetValue<bool>());
		AssertNumber(plain["autoInclusion"], 0);
	}

	[Fact]
	public void Both_directive_kinds_carry_their_availability_through_the_polymorphic_base()
	{
		Directive stellar = new StellarDirective { Id = "A1", Title = "Raptor", AvailabilityTimeframeId = 7 };
		Directive lunar = new LunarDirective { Id = "M1", Title = "Moon Law" };

		var stellarJson = Serialize(stellar);
		Assert.Equal("stellar", stellarJson["$type"]!.GetValue<string>());
		Assert.Equal(nameof(StellarDirective), stellarJson["@type"]!.GetValue<string>());
		AssertNumber(stellarJson["availabilityTimeframeId"], 7);
		// The navigation is written as null rather than omitted, so a client can tell an unloaded nav from a clear.
		Assert.True(stellarJson.ContainsKey("availabilityTimeframe"));
		Assert.Null(stellarJson["availabilityTimeframe"]);

		var lunarJson = Serialize(lunar);
		Assert.Equal("lunar", lunarJson["$type"]!.GetValue<string>());
		Assert.Equal(nameof(LunarDirective), lunarJson["@type"]!.GetValue<string>());
		Assert.True(lunarJson.ContainsKey("availabilityTimeframeId"));
		Assert.Null(lunarJson["availabilityTimeframeId"]);
		Assert.True(lunarJson.ContainsKey("availabilityTimeframe"));
		Assert.Null(lunarJson["availabilityTimeframe"]);
	}

	[Fact]
	public void A_decree_add_reads_the_creation_affinity_as_tri_state()
	{
		var omitted = Deserialize<PolarisDecreeAdd>("""{"decreeId":"D1"}""");
		Assert.False(omitted.AffinityTimeframeId.IsSet);

		var none = Deserialize<PolarisDecreeAdd>("""{"decreeId":"D1","affinityTimeframeId":null}""");
		Assert.True(none.AffinityTimeframeId.IsSet);
		Assert.Null(none.AffinityTimeframeId.Value);

		var explicitId = Deserialize<PolarisDecreeAdd>("""{"decreeId":"D1","affinityTimeframeId":7}""");
		Assert.True(explicitId.AffinityTimeframeId.IsSet);
		Assert.Equal(7L, explicitId.AffinityTimeframeId.Value);
	}

	[Fact]
	public void An_executive_plan_reads_the_creation_affinity_as_tri_state()
	{
		var mode = (int)PolarisExecutivePlanningMode.Standalone;

		var omitted = Deserialize<PolarisExecutivePlan>($$"""{"mode":{{mode}},"title":"Draft"}""");
		Assert.Equal(PolarisExecutivePlanningMode.Standalone, omitted.Mode);
		Assert.False(omitted.AffinityTimeframeId.IsSet);

		var none = Deserialize<PolarisExecutivePlan>($$"""{"mode":{{mode}},"title":"Draft","affinityTimeframeId":null}""");
		Assert.True(none.AffinityTimeframeId.IsSet);
		Assert.Null(none.AffinityTimeframeId.Value);

		var explicitId = Deserialize<PolarisExecutivePlan>($$"""{"mode":{{mode}},"title":"Draft","affinityTimeframeId":7}""");
		Assert.True(explicitId.AffinityTimeframeId.IsSet);
		Assert.Equal(7L, explicitId.AffinityTimeframeId.Value);
	}

	[Fact]
	public void An_availability_request_takes_an_id_or_null()
	{
		Assert.Equal(7L, Deserialize<DirectiveAvailabilityRequest>("""{"timeframeId":7}""").TimeframeId);
		Assert.Null(Deserialize<DirectiveAvailabilityRequest>("""{"timeframeId":null}""").TimeframeId);
	}

	[Fact]
	public void A_timeframe_update_keeps_exclusive_when_omitted_and_sets_it_otherwise()
	{
		Assert.Null(Deserialize<TimeframeUpdate>("""{"title":"Dawn"}""").Exclusive);
		Assert.True(Deserialize<TimeframeUpdate>("""{"exclusive":true}""").Exclusive);
		Assert.False(Deserialize<TimeframeUpdate>("""{"exclusive":false}""").Exclusive);
	}
}
