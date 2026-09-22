using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Database;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// User preferences (PEP116) are a sparse, vault-bound key/value store with code-owned defaults surfaced through
/// .NET Options: an unset preference resolves to its POCO default, a set value overrides and persists across
/// scopes, a reset reverts, and a malformed stored value degrades to the default rather than throwing.
/// </summary>
public sealed class UserPreferencesTests : VaultTestBase
{
	/// <summary>Reads the note-queue timeout through the Options surface in a fresh scope (the hot read path).</summary>
	private Task<int> NoteQueueTimeoutOptionAsync()
		=> Vault.WithScopeAsync(services => Task.FromResult(
			services.GetRequiredService<IOptionsSnapshot<WatcherPreferences>>().Value.NoteQueueTimeout));

	private Task<int> NoteQueueTimeoutDirectAsync()
		=> Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>()
			.GetAsync(PreferenceKeys.NoteQueueTimeout, 2000, TestContext.Current.CancellationToken));

	[Fact]
	public async Task Unset_preference_resolves_to_the_code_default()
	{
		// A fresh vault stores nothing, so both the Options surface and a direct read return the POCO default.
		Assert.Equal(2000, await NoteQueueTimeoutOptionAsync());
		Assert.Equal(2000, await NoteQueueTimeoutDirectAsync());

		var anyRow = await Vault.QueryAsync(context => context.UserPreferences
			.AnyAsync(TestContext.Current.CancellationToken));
		Assert.False(anyRow);
	}

	[Fact]
	public async Task Set_overrides_the_default_persists_and_is_visible_through_options()
	{
		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>()
			.SetAsync(PreferenceKeys.NoteQueueTimeout, 500, TestContext.Current.CancellationToken));

		// The write-through store makes the override visible through Options in a subsequent scope.
		Assert.Equal(500, await NoteQueueTimeoutOptionAsync());
		Assert.Equal(500, await NoteQueueTimeoutDirectAsync());

		// The value is a durable row, not just an in-memory override.
		var stored = await Vault.QueryAsync(context => context.UserPreferences.AsNoTracking()
			.FirstOrDefaultAsync(row => row.Key == PreferenceKeys.NoteQueueTimeout, TestContext.Current.CancellationToken));
		Assert.NotNull(stored);
		Assert.Equal("500", stored!.Value);
	}

	[Fact]
	public async Task Set_twice_is_an_idempotent_upsert()
	{
		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>()
			.SetAsync(PreferenceKeys.NoteQueueTimeout, 500, TestContext.Current.CancellationToken));
		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>()
			.SetAsync(PreferenceKeys.NoteQueueTimeout, 750, TestContext.Current.CancellationToken));

		Assert.Equal(750, await NoteQueueTimeoutOptionAsync());
		var rowCount = await Vault.QueryAsync(context => context.UserPreferences
			.CountAsync(row => row.Key == PreferenceKeys.NoteQueueTimeout, TestContext.Current.CancellationToken));
		Assert.Equal(1, rowCount);
	}

	[Fact]
	public async Task Reset_reverts_to_the_default()
	{
		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>()
			.SetAsync(PreferenceKeys.NoteQueueTimeout, 500, TestContext.Current.CancellationToken));
		Assert.Equal(500, await NoteQueueTimeoutOptionAsync());

		var removed = await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>()
			.ResetAsync(PreferenceKeys.NoteQueueTimeout, TestContext.Current.CancellationToken));
		Assert.True(removed);

		Assert.Equal(2000, await NoteQueueTimeoutOptionAsync());
		var anyRow = await Vault.QueryAsync(context => context.UserPreferences
			.AnyAsync(row => row.Key == PreferenceKeys.NoteQueueTimeout, TestContext.Current.CancellationToken));
		Assert.False(anyRow);
	}

	[Fact]
	public async Task A_malformed_stored_value_falls_back_to_the_default()
	{
		// A hand-edited or type-drifted row whose value is not a valid int for this preference.
		await Vault.WithScopeAsync(async services =>
		{
			var context = services.GetRequiredService<PlainfraContext>();
			context.UserPreferences.Add(new UserPreferenceRecord
			{
				Key = PreferenceKeys.NoteQueueTimeout,
				Value = "not-an-int",
				UpdatedUtc = DateTimeOffset.UtcNow,
			});
			await context.SaveChangesAsync(TestContext.Current.CancellationToken);

			// Refresh the in-memory store from the database, as a vault activation would.
			await services.GetRequiredService<UserPreferenceService>().LoadAsync(TestContext.Current.CancellationToken);
		});

		Assert.Equal(2000, await NoteQueueTimeoutOptionAsync());
		Assert.Equal(2000, await NoteQueueTimeoutDirectAsync());
	}

	[Fact]
	public async Task CalDav_floating_render_defaults_to_all_day_and_an_override_stores_by_name()
	{
		var initial = await Vault.WithScopeAsync(services => Task.FromResult(
			services.GetRequiredService<IOptionsSnapshot<CalDavPreferences>>().Value.FloatingRender));
		Assert.Equal(CalDavFloatingRender.AllDay, initial);

		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>()
			.SetAsync(PreferenceKeys.CalDavFloatingRender, CalDavFloatingRender.PinToStart, TestContext.Current.CancellationToken));

		var overridden = await Vault.WithScopeAsync(services => Task.FromResult(
			services.GetRequiredService<IOptionsSnapshot<CalDavPreferences>>().Value.FloatingRender));
		Assert.Equal(CalDavFloatingRender.PinToStart, overridden);

		// The enum is persisted by name, not ordinal, so reordering the enum can never reinterpret a stored value.
		var stored = await Vault.QueryAsync(context => context.UserPreferences.AsNoTracking()
			.FirstAsync(row => row.Key == PreferenceKeys.CalDavFloatingRender, TestContext.Current.CancellationToken));
		Assert.Equal("\"PinToStart\"", stored.Value);
	}

	[Fact]
	public async Task Auto_materialise_optout_defaults_to_false_and_can_be_enabled()
	{
		var initial = await Vault.WithScopeAsync(services => Task.FromResult(
			services.GetRequiredService<IOptionsSnapshot<AgendaPreferences>>().Value.AutoMaterialiseOptOut));
		Assert.False(initial);

		await Vault.WithScopeAsync(services => services.GetRequiredService<UserPreferenceService>()
			.SetAsync(PreferenceKeys.AutoMaterialiseOptOut, true, TestContext.Current.CancellationToken));

		var enabled = await Vault.WithScopeAsync(services => Task.FromResult(
			services.GetRequiredService<IOptionsSnapshot<AgendaPreferences>>().Value.AutoMaterialiseOptOut));
		Assert.True(enabled);
	}
}
