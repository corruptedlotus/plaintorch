using Xunit;

namespace Pleiades.Tests.Harness;

/// <summary>
/// Base class for tests that need a fresh, isolated, initialized vault per test.
/// </summary>
public abstract class VaultTestBase : IAsyncLifetime
{
	/// <summary>
	/// Gets the isolated vault for the current test.
	/// </summary>
	protected TestVault Vault { get; } = new();

	/// <inheritdoc />
	public ValueTask InitializeAsync() => Vault.InitializeAsync();

	/// <inheritdoc />
	public ValueTask DisposeAsync() => Vault.DisposeAsync();
}
