using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The vault write-intent outbox (PEP110 Refactor BETA). A directive write records a durable intent that the drainer
/// clears once the file is written, so the table is normally empty; and an intent a crash leaves behind — the database
/// committed but the file not yet written — is recovered by the startup drain, which re-derives the file from the
/// entity's current database state.
/// </summary>
public sealed class VaultWriteQueueTests : VaultTestBase
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: Token));

	[Fact]
	public async Task A_directive_write_leaves_no_pending_intent()
	{
		await CreateDirectiveAsync("Campaign");

		Assert.True(Vault.VaultFileExists("Directives/Campaign/Campaign.md"));
		Assert.Equal(0, await Vault.QueryAsync(context => context.VaultWriteIntents.CountAsync(Token)));
	}

	[Fact]
	public async Task A_crashed_write_intent_is_recovered_by_the_startup_drain()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Campaign.md"));

		// Simulate a crash after the database commit + intent record but before the file write: the file never landed,
		// and an undrained intent remains.
		Directory.Delete(Vault.AbsolutePath("Directives/Campaign"), recursive: true);
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.VaultWriteIntents.Add(new VaultWriteIntent
			{
				EntityType = typeof(StellarDirective).FullName!,
				EntityId = directive.Id,
				Kind = VaultWriteIntentKind.Reconcile,
				Identity = directive.Id,
				EnqueuedUtc = DateTimeOffset.UtcNow,
			});
			await context.SaveChangesAsync(Token);
		});

		// The startup drain reconciles the entity's file from its database state and clears the row.
		await Vault.WithScopeAsync(services => services.GetRequiredService<VaultWriteQueue>().DrainPendingAsync(Token));

		Assert.True(Vault.VaultFileExists("Directives/Campaign/Campaign.md"), "the file must be recovered");
		Assert.Equal(0, await Vault.QueryAsync(context => context.VaultWriteIntents.CountAsync(Token)));
	}

	[Fact]
	public async Task A_directive_delete_archives_the_file_and_leaves_no_pending_intent()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Campaign.md"));

		await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().DeleteAsync(directive.Id, Token));

		Assert.False(Directory.Exists(Vault.AbsolutePath("Directives/Campaign")), "the file is archived out of the vault");
		Assert.Equal(0, await Vault.QueryAsync(context => context.VaultWriteIntents.CountAsync(Token)));
	}

	[Fact]
	public async Task A_crashed_remove_intent_archives_the_file_by_the_startup_drain()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		const string notePath = "Directives/Campaign/Campaign.md";
		Assert.True(Vault.VaultFileExists(notePath));

		// Simulate a crash after the database removal + remove-intent record but before the file was archived: the row
		// remains and the file is still on disk.
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.VaultWriteIntents.Add(new VaultWriteIntent
			{
				EntityType = typeof(StellarDirective).FullName!,
				EntityId = directive.Id,
				Kind = VaultWriteIntentKind.Remove,
				Identity = directive.Id,
				LastKnownPath = notePath,
				EnqueuedUtc = DateTimeOffset.UtcNow,
			});
			await context.SaveChangesAsync(Token);
		});

		await Vault.WithScopeAsync(services => services.GetRequiredService<VaultWriteQueue>().DrainPendingAsync(Token));

		Assert.False(Vault.VaultFileExists(notePath), "the file must be archived by the recovery");
		Assert.Equal(0, await Vault.QueryAsync(context => context.VaultWriteIntents.CountAsync(Token)));
	}
}
