using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;

namespace Pleiades.Vault.Database;

/// <summary>
/// Represents the EF Core database context for PLAINTORCH state stored inside the vault.
/// </summary>
public class PlainfraContext : DbContext
{
	/// <summary>
	/// Initializes a new database context instance.
	/// </summary>
	/// <param name="options">The EF Core options for the context.</param>
	public PlainfraContext(DbContextOptions<PlainfraContext> options)
		: base(options)
	{
	}

	/// <summary>
	/// Gets the directives tracked in the database.
	/// </summary>
	public DbSet<Directive> Directives => Set<Directive>();

	/// <summary>
	/// Gets the objectives tracked in the database.
	/// </summary>
	public DbSet<Objective> Objectives => Set<Objective>();

	/// <summary>
	/// Gets the onrush sprints tracked in the database.
	/// </summary>
	public DbSet<OnrushSprint> OnrushSprints => Set<OnrushSprint>();

	/// <summary>
	/// Gets the Polaris cycles tracked in the database.
	/// </summary>
	public DbSet<PolarisCycle> PolarisCycles => Set<PolarisCycle>();

	/// <summary>
	/// Gets the predefined tag definitions tracked in the database.
	/// </summary>
	public DbSet<TagDefinition> TagDefinitions => Set<TagDefinition>();

	/// <summary>
	/// Gets the Celestron ledger entries tracked in the database.
	/// </summary>
	public DbSet<CelestronTransaction> CelestronLedger => Set<CelestronTransaction>();

	/// <summary>
	/// Gets the issued PUCK registry entries tracked in the database.
	/// </summary>
	public DbSet<PuckRegistryEntry> PuckRegistryEntries => Set<PuckRegistryEntry>();

	/// <summary>
	/// Gets the sequence state used by incremental PUCK declarations.
	/// </summary>
	public DbSet<PuckSequence> PuckSequences => Set<PuckSequence>();

	/// <summary>
	/// Gets the saga lore pages tracked in the database.
	/// </summary>
	public DbSet<LorePage> LorePages => Set<LorePage>();

	/// <summary>
	/// Gets the deleted database entity snapshots tracked in the graveyard.
	/// </summary>
	public DbSet<DatabaseGraveyardEntry> DatabaseGraveyardEntries => Set<DatabaseGraveyardEntry>();

	/// <summary>
	/// Gets the archived file snapshot metadata tracked in the graveyard.
	/// </summary>
	public DbSet<FileGraveyardEntry> FileGraveyardEntries => Set<FileGraveyardEntry>();

	/// <summary>
	/// Gets the audit trail entries tracked in the database.
	/// </summary>
	public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		var tagsConverter = new ValueConverter<List<string>, string>(
			value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
			value => JsonSerializer.Deserialize<List<string>>(value, JsonSerializerOptions.Default) ?? new List<string>());

		var tagsComparer = new ValueComparer<List<string>>(
			(left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
			value => (value ?? new List<string>()).Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
			value => (value ?? new List<string>()).ToList());

		modelBuilder.Entity<Directive>()
			.Property(x => x.Status)
			.HasConversion<string>();

		modelBuilder.Entity<Directive>()
			.Property(x => x.Tags)
			.HasConversion(tagsConverter)
			.Metadata.SetValueComparer(tagsComparer);

		modelBuilder.Entity<Objective>()
			.Property(x => x.College)
			.HasConversion<string>();

		modelBuilder.Entity<Objective>()
			.Property(x => x.Status)
			.HasConversion<string>();

		modelBuilder.Entity<Objective>()
			.Navigation(x => x.Directive)
			.AutoInclude();

		modelBuilder.Entity<TagDefinition>()
			.Property(x => x.Color)
			.HasConversion<string>();

		modelBuilder.Entity<LorePage>()
			.Property(x => x.Id)
			.HasColumnName("Puck");

		modelBuilder.Entity<Executive>()
			.Navigation(x => x.Objective)
			.AutoInclude();
	}
}