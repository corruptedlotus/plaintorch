using Pleiades.Vault;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Vault schema version persistence: round-trips through the settings file and defaults to baseline when absent.
/// </summary>
public sealed class VersionStoreTests
{
	[Fact]
	public void Schema_version_round_trips_through_settings_file()
	{
		RunInTempDirectory(directory =>
		{
			var path = Path.Combine(directory, ".plaintorch");
			new VaultSettings { SchemaVersion = 5 }.Save(path);

			Assert.Equal(5, VaultSettings.Load(path).SchemaVersion);
		});
	}

	[Fact]
	public void Missing_schema_version_defaults_to_baseline()
	{
		RunInTempDirectory(directory =>
		{
			var path = Path.Combine(directory, ".plaintorch");
			File.WriteAllText(path, "{ \"LocationKeys\": { \"Directives\": \"Directives\" } }");

			Assert.Equal(VaultSchema.BaselineVersion, VaultSettings.Load(path).SchemaVersion);
		});
	}

	private static void RunInTempDirectory(Action<string> action)
	{
		var directory = Path.Combine(Path.GetTempPath(), "plaintorch-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			action(directory);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}
