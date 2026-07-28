using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pleiades.Orchestration;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The <c>LunarStellarDirectiveSiblings</c> migration converts legacy concrete-base directive rows
/// (<c>Discriminator = 'Directive'</c>) into stellar directives so pre-split vault databases stay loadable.
/// </summary>
public sealed class DirectiveMigrationTests
{
	[Fact]
	public async Task Legacy_concrete_directive_rows_migrate_to_stellar()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var dbPath = Path.Combine(Path.GetTempPath(), "plaintorch-tests", $"legacy-{Guid.NewGuid():N}.sqlite3");
		Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
		var options = new DbContextOptionsBuilder<PlainfraContext>()
			.UseSqlite($"Data Source={dbPath}")
			.Options;

		try
		{
			await using (var context = new PlainfraContext(options))
			{
				var migrator = context.GetService<IMigrator>();

				// Bring the schema to the state just before the sibling split, then seed a legacy row exactly
				// as the old concrete-base model wrote it.
				await migrator.MigrateAsync("AddDecreeCollege", cancellationToken);
				await context.Database.ExecuteSqlRawAsync(
					"INSERT INTO \"Directives\" (\"Id\", \"Title\", \"Discriminator\", \"Status\", \"Tags\") VALUES ('A123456', 'Legacy', 'Directive', 'Active', '[]');",
					cancellationToken);

				await migrator.MigrateAsync(cancellationToken: cancellationToken);
			}

			await using (var context = new PlainfraContext(options))
			{
				var directive = await context.Directives.AsNoTracking().SingleAsync(cancellationToken);
				var stellar = Assert.IsType<StellarDirective>(directive);
				Assert.Equal("A123456", stellar.Id);
				Assert.Equal("Legacy", stellar.Title);
				Assert.Equal(DirectiveStatus.Active, stellar.Status);
			}
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(dbPath))
			{
				File.Delete(dbPath);
			}
		}
	}
}
