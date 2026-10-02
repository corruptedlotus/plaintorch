using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pleiades.Vault.Database;

/// <summary>
/// Registers the <see cref="SearchFolding.FunctionName"/> SQL function on every opened SQLite connection, so a query
/// that folds its search through <see cref="SearchFolding.Fold"/> runs the folding inside the database.
/// </summary>
/// <remarks>
/// A SQLite function lives on its connection, not in the database file, so every connection a context opens needs it
/// anew. <see cref="PlainfraContext"/> adds this interceptor itself rather than leaving it to whoever configures the
/// context, so the function travels with the model mapping that calls it however the context was made.
/// </remarks>
public sealed class SearchFoldingConnectionInterceptor : DbConnectionInterceptor
{
	/// <summary>
	/// The shared instance; the interceptor holds no state.
	/// </summary>
	public static SearchFoldingConnectionInterceptor Instance { get; } = new();

	private SearchFoldingConnectionInterceptor()
	{
	}

	/// <inheritdoc />
	public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
	{
		Register(connection);
		base.ConnectionOpened(connection, eventData);
	}

	/// <inheritdoc />
	public override Task ConnectionOpenedAsync(
		DbConnection connection,
		ConnectionEndEventData eventData,
		CancellationToken cancellationToken = default)
	{
		Register(connection);
		return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
	}

	private static void Register(DbConnection connection)
	{
		if (connection is SqliteConnection sqlite)
		{
			sqlite.CreateFunction<string?, string?>(SearchFolding.FunctionName, SearchFolding.Fold, isDeterministic: true);
		}
	}
}
