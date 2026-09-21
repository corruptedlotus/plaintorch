using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Harness;

/// <summary>
/// A fully isolated, initialized PLAINTORCH vault for a single test: its own temp directory and SQLite database,
/// built and initialized through the real dependency-injection graph, and torn down afterwards.
/// </summary>
/// <remarks>
/// Each operation runs in its own DI scope, mirroring the production model where each request/watcher event gets a fresh
/// scope — so tests exercise scope/tracking behaviour faithfully rather than sharing one long-lived context.
/// </remarks>
public sealed class TestVault : IAsyncLifetime
{
	private WebApplication _app = null!;

	/// <summary>
	/// Gets the absolute root path of the isolated test vault.
	/// </summary>
	public string VaultRoot { get; private set; } = null!;

	/// <summary>
	/// Writes raw content to a vault file before the vault is initialized (so a startup migration/consistency pass
	/// sees it as a pre-existing file). The vault root directory is created on demand.
	/// </summary>
	public void WritePreExistingVaultFile(string vaultRelativePath, string content)
	{
		var path = Path.Combine(VaultRoot, vaultRelativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
	}

	/// <summary>
	/// Gets the resolved vault layout (a singleton, safe to read outside a scope).
	/// </summary>
	public VaultLayout Layout => _app.Services.GetRequiredService<VaultLayout>();

	/// <summary>
	/// Resolves a singleton service from the root provider. Use only for singletons, not scoped services.
	/// </summary>
	public T GetSingleton<T>() where T : notnull => _app.Services.GetRequiredService<T>();

	/// <inheritdoc />
	public async ValueTask InitializeAsync()
	{
		VaultRoot = Path.Combine(Path.GetTempPath(), "plaintorch-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(VaultRoot);
		if (PreExistingFiles is not null)
		{
			foreach (var (relativePath, content) in PreExistingFiles)
			{
				WritePreExistingVaultFile(relativePath, content);
			}
		}

		_app = PlaintorchTestHost.Build(VaultRoot);
		await WithScopeAsync(services => services.GetRequiredService<PlaintorchEngine>().InitializeVaultAsync());
	}

	/// <summary>
	/// Gets or sets files to write into the vault before initialization runs, so startup migrations and the
	/// consistency pass observe them as pre-existing. Set before <see cref="InitializeAsync"/> runs.
	/// </summary>
	public IReadOnlyList<(string RelativePath, string Content)>? PreExistingFiles { get; set; }

	/// <summary>
	/// Runs an action inside a fresh DI scope and returns its result.
	/// </summary>
	public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		using var scope = _app.Services.CreateScope();
		return await action(scope.ServiceProvider);
	}

	/// <summary>
	/// Runs an action inside a fresh DI scope.
	/// </summary>
	public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		using var scope = _app.Services.CreateScope();
		await action(scope.ServiceProvider);
	}

	/// <summary>
	/// Runs a query against a fresh database context scope.
	/// </summary>
	public Task<T> QueryAsync<T>(Func<PlainfraContext, Task<T>> query)
	{
		ArgumentNullException.ThrowIfNull(query);
		return WithScopeAsync(services => query(services.GetRequiredService<PlainfraContext>()));
	}

	// --- vault file helpers ---

	/// <summary>Resolves a vault-relative path to an absolute path.</summary>
	public string AbsolutePath(string vaultRelativePath)
	{
		ArgumentNullException.ThrowIfNull(vaultRelativePath);
		return Path.Combine(VaultRoot, vaultRelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	/// <summary>Writes raw content to a vault file (used to place legacy/edge-case files directly).</summary>
	public void WriteVaultFile(string vaultRelativePath, string content)
	{
		var path = AbsolutePath(vaultRelativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
	}

	/// <summary>Reads a vault file's content.</summary>
	public string ReadVaultFile(string vaultRelativePath) => File.ReadAllText(AbsolutePath(vaultRelativePath));

	/// <summary>Determines whether a vault file exists.</summary>
	public bool VaultFileExists(string vaultRelativePath) => File.Exists(AbsolutePath(vaultRelativePath));

	/// <summary>Enumerates markdown files under an absolute directory.</summary>
	public IReadOnlyList<string> MarkdownFilesUnder(string absoluteDirectory)
	{
		return Directory.Exists(absoluteDirectory)
			? Directory.GetFiles(absoluteDirectory, "*.md", SearchOption.AllDirectories)
			: [];
	}

	// --- seeding (through real services) ---

	/// <summary>Creates a standalone objective through the real application API.</summary>
	public Task<Objective> SeedStandaloneObjectiveAsync(string title, string? requestedId = null)
		=> WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().CreateStandaloneAsync(title, requestedId: requestedId));

	/// <summary>Materializes an implicit objective's file and begins its synchronization boundary.</summary>
	public Task<Objective> BeginObjectiveBoundaryAsync(string objectiveId)
		=> WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>().BeginBoundaryAsync(objectiveId));

	// --- pipeline drivers (deterministic, no live watcher) ---

	/// <summary>Runs a full discovery scan and returns the produced candidates.</summary>
	public Task<VaultDiscoveryScanResult> ScanAsync()
		=> WithScopeAsync(services => services.GetRequiredService<VaultMarkdownDiscoveryService>().ScanAsync("test"));

	/// <summary>Inspects a single path and returns the discovery candidate (no reconciliation applied).</summary>
	public Task<VaultSyncCandidate?> InspectAsync(string absolutePath)
		=> WithScopeAsync(services => services.GetRequiredService<VaultMarkdownDiscoveryService>().InspectPathAsync(absolutePath, "test"));

	/// <summary>Inspects a path and executes its suggested reconciliation action, in one scope (as a watcher event would).</summary>
	public Task<VaultSyncCandidate?> ReconcileAsync(string absolutePath)
	{
		return WithScopeAsync(async services =>
		{
			var discovery = services.GetRequiredService<VaultMarkdownDiscoveryService>();
			var sync = services.GetRequiredService<VaultWatcherSyncService>();
			var candidate = await discovery.InspectPathAsync(absolutePath, "test");
			if (candidate is not null)
			{
				await sync.ExecuteAsync(candidate, "test");
			}

			return candidate;
		});
	}

	/// <summary>
	/// Simulates a full daemon startup over files that already exist on disk: first the startup
	/// auto-generated-PUCK consistency pass (as <c>PlaintorchEngine.InitializeVaultAsync</c> runs it), then the
	/// discovery sweep the watcher's startup scan performs. Use this to test how pre-existing files are treated
	/// when the core boots over them, as opposed to <see cref="ReconcileAsync"/> which models a live watcher event.
	/// </summary>
	public async Task StartupSweepAsync()
	{
		await WithScopeAsync(services => services
			.GetRequiredService<VaultAutoGeneratedPuckConsistencyService>()
			.ReconcileAsync());
		await SweepAsync();
	}

	/// <summary>
	/// Runs a startup sweep through the real <see cref="VaultWatcherReconciler"/>, emitting operation-status issues
	/// exactly as the hosted watcher does on boot. Use this (not <see cref="SweepAsync"/>) to assert issue emission.
	/// </summary>
	public Task SweepWithIssuesAsync()
		=> WithScopeAsync(services => services.GetRequiredService<VaultWatcherReconciler>().ReconcileSweepAsync("test-sweep"));

	/// <summary>
	/// Reconciles a single path through the real <see cref="VaultWatcherReconciler"/>, emitting operation-status issues
	/// exactly as a live filesystem event does. Use this (not <see cref="ReconcileAsync"/>) to assert issue emission.
	/// </summary>
	public Task ReconcileWithIssuesAsync(string absolutePath)
		=> WithScopeAsync(services => services.GetRequiredService<VaultWatcherReconciler>().ReconcilePathAsync(absolutePath, "test-runtime"));

	/// <summary>
	/// Runs the startup sweep: a full discovery scan, then executes every candidate's action in the same priority
	/// order <c>VaultWatcherService</c> uses at startup. This mirrors the live pipeline, so a one-shot sweep and a
	/// sequence of incremental <see cref="ReconcileAsync"/> events can be compared for the same final state.
	/// </summary>
	public Task<VaultDiscoveryScanResult> SweepAsync()
	{
		return WithScopeAsync(async services =>
		{
			var discovery = services.GetRequiredService<VaultMarkdownDiscoveryService>();
			var sync = services.GetRequiredService<VaultWatcherSyncService>();
			var result = await discovery.ScanAsync("test");
			var ordered = result.Candidates
				.OrderBy(candidate => StartupActionPriority(candidate.SuggestedAction))
				.ThenBy(candidate => candidate.VaultRelativePath, StringComparer.OrdinalIgnoreCase)
				.ToList();
			foreach (var candidate in ordered)
			{
				await sync.ExecuteAsync(candidate, "test");
			}

			return result;
		});
	}

	private static int StartupActionPriority(VaultSyncAction action) => action switch
	{
		VaultSyncAction.UpdateFromFile => 0,
		VaultSyncAction.RewriteFromDatabase => 0,
		VaultSyncAction.CreateFromFile => 1,
		VaultSyncAction.PurgeFile => 2,
		VaultSyncAction.Conflict => 3,
		VaultSyncAction.Ignore => 4,
		_ => 5,
	};

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		string? connectionString = null;
		if (_app is not null)
		{
			_app.Services.GetRequiredService<PlaintorchUserLayout>().Cleanup();
			using (var scope = _app.Services.CreateScope())
			{
				connectionString = scope.ServiceProvider.GetRequiredService<PlainfraContext>().Database.GetConnectionString();
			}

			await _app.DisposeAsync();
		}

		// Release only THIS vault's SQLite handles before deleting its temp directory (SQLite/Windows). Clearing the whole
		// process-wide pool (ClearAllPools) races other test collections that are opening their own connections in parallel
		// — surfacing as a spurious SqliteConnection.Open() failure elsewhere — so clear just this connection's pool.
		if (connectionString is not null)
		{
			using var connection = new SqliteConnection(connectionString);
			SqliteConnection.ClearPool(connection);
		}

		TryDeleteDirectory(VaultRoot);
	}

	private static void TryDeleteDirectory(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
		{
			return;
		}

		for (var attempt = 0; attempt < 3; attempt++)
		{
			try
			{
				Directory.Delete(path, recursive: true);
				return;
			}
			catch (IOException)
			{
				Thread.Sleep(50);
			}
			catch (UnauthorizedAccessException)
			{
				Thread.Sleep(50);
			}
		}
	}
}
