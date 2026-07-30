using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pleiades.Vault.Database;

/// <summary>
/// Applies, on every opened connection, the SQLite pragmas the core needs to serve concurrent requests off a
/// single database file.
/// </summary>
/// <remarks>
/// The core answers many requests at once — a write and the reads a client fires to resync around it — and
/// each gets its own <see cref="PlainfraContext"/>, so its own connection to the one file. Left at SQLite's
/// defaults that is fragile: the rollback journal takes a whole-file lock for a write, and a busy timeout of
/// zero means a reader that meets that lock fails immediately rather than waiting. Write-ahead logging lets
/// readers proceed alongside the single writer, and a non-zero busy timeout absorbs the brief contention that
/// remains. WAL persists in the file, so re-asserting it per connection is a cheap no-op; the busy timeout is
/// connection-scoped and must be set each time.
/// </remarks>
public sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
	// A reader that meets the writer's lock waits up to this long for it to clear before giving up.
	private const string Pragmas = "PRAGMA busy_timeout = 5000; PRAGMA journal_mode = WAL;";

	/// <inheritdoc />
	public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
	{
		using var command = connection.CreateCommand();
		command.CommandText = Pragmas;
		command.ExecuteNonQuery();
		base.ConnectionOpened(connection, eventData);
	}

	/// <inheritdoc />
	public override async Task ConnectionOpenedAsync(
		DbConnection connection,
		ConnectionEndEventData eventData,
		CancellationToken cancellationToken = default)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = Pragmas;
		await command.ExecuteNonQueryAsync(cancellationToken);
		await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
	}
}
