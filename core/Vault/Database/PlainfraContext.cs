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
	/// Gets the directives tracked in the database, including lunar directives.
	/// </summary>
	public DbSet<Directive> Directives => Set<Directive>();

	/// <summary>
	/// Gets the lunar (Moonlight) directives tracked in the database.
	/// </summary>
	public DbSet<LunarDirective> LunarDirectives => Set<LunarDirective>();

	/// <summary>
	/// Gets all incentives (objectives and declaratives) tracked in the database.
	/// </summary>
	public DbSet<Incentive> Incentives => Set<Incentive>();

	/// <summary>
	/// Gets the objectives tracked in the database.
	/// </summary>
	public DbSet<Objective> Objectives => Set<Objective>();

	/// <summary>
	/// Gets the fate declaratives tracked in the database.
	/// </summary>
	public DbSet<Fate> Fates => Set<Fate>();

	/// <summary>
	/// Gets the decree declaratives tracked in the database.
	/// </summary>
	public DbSet<Decree> Decrees => Set<Decree>();

	/// <summary>
	/// Gets the attentive backlog records tracked in the database.
	/// </summary>
	public DbSet<Attentive> Attentives => Set<Attentive>();

	/// <summary>
	/// Gets the eventive backlog records tracked in the database.
	/// </summary>
	public DbSet<Eventive> Eventives => Set<Eventive>();

	/// <summary>
	/// Gets the directive-level timeframe definitions tracked in the database.
	/// </summary>
	public DbSet<Timeframe> Timeframes => Set<Timeframe>();

	/// <summary>
	/// Gets the persisted orbit engine states for orbit-bearing declaratives (PEP100).
	/// </summary>
	public DbSet<OrbitScheduleState> OrbitScheduleStates => Set<OrbitScheduleState>();

	/// <summary>
	/// Gets the onrush sprints tracked in the database.
	/// </summary>
	public DbSet<OnrushSprint> OnrushSprints => Set<OnrushSprint>();

	/// <summary>
	/// Gets the executive orders tracked in the database.
	/// </summary>
	public DbSet<ExecutiveOrder> ExecutiveOrders => Set<ExecutiveOrder>();

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

	/// <summary>
	/// Gets the applied vault migration history tracked in the database.
	/// </summary>
	public DbSet<VaultMigrationHistory> VaultMigrationHistory => Set<VaultMigrationHistory>();

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

		// Stellar and lunar directives share the Directives table (TPH). Their same-named Status properties carry
		// different enums, so each maps to an explicit column to keep the shared table readable and unambiguous.
		modelBuilder.Entity<StellarDirective>()
			.Property(x => x.Status)
			.HasConversion<string>()
			.HasColumnName("Status");

		modelBuilder.Entity<Directive>()
			.Property(x => x.Tags)
			.HasConversion(tagsConverter)
			.Metadata.SetValueComparer(tagsComparer);

		// PEP100: the incentive family (objectives + declaratives) shares one table through a discriminator.
		modelBuilder.Entity<Incentive>()
			.ToTable("Incentives");

		modelBuilder.Entity<Objective>()
			.Property(x => x.College)
			.HasConversion<string>();

		modelBuilder.Entity<Objective>()
			.Property(x => x.Status)
			.HasConversion<string>();

		modelBuilder.Entity<Incentive>()
			.Navigation(x => x.Directive)
			.AutoInclude();

		// Sibling declarative fields get explicit column names so the shared table stays readable and
		// same-named properties across siblings map deliberately.
		modelBuilder.Entity<Fate>()
			.Property(x => x.Status)
			.HasConversion<string>()
			.HasColumnName("FateStatus");

		modelBuilder.Entity<Decree>()
			.Property(x => x.Status)
			.HasConversion<string>()
			.HasColumnName("DecreeStatus");

		modelBuilder.Entity<Fate>()
			.Property(x => x.Orbit)
			.HasColumnName("Orbit");

		modelBuilder.Entity<Decree>()
			.Property(x => x.Orbit)
			.HasColumnName("Orbit");

		modelBuilder.Entity<LunarDirective>()
			.Property(x => x.Status)
			.HasConversion<string>()
			.HasColumnName("LunarStatus");

		modelBuilder.Entity<Attentive>()
			.Property(x => x.Resolution)
			.HasConversion<string>();

		modelBuilder.Entity<Eventive>()
			.Property(x => x.Resolution)
			.HasConversion<string>();

		// Eventive owners cascade: deleting a fate or an objective removes its materialized occurrences.
		modelBuilder.Entity<Eventive>()
			.HasOne(x => x.Fate)
			.WithMany(x => x.Eventives)
			.OnDelete(DeleteBehavior.Cascade);

		modelBuilder.Entity<Eventive>()
			.HasOne(x => x.Objective)
			.WithMany(x => x.Eventives)
			.OnDelete(DeleteBehavior.Cascade);

		// Removing a timeframe definition only clears executive affinity references.
		modelBuilder.Entity<Executive>()
			.HasOne(x => x.AffinityTimeframe)
			.WithMany()
			.OnDelete(DeleteBehavior.SetNull);

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