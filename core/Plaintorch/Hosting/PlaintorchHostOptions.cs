using Pleiades.Vault;

namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// Everything the host factory needs to compose a PLAINTORCH host, independent of how the arguments were parsed.
/// </summary>
public sealed class PlaintorchHostOptions
{
	/// <summary>
	/// Gets the per-user profile the host binds its socket, configuration, and logs to.
	/// </summary>
	public required PlaintorchUserLayout UserLayout { get; init; }

	/// <summary>
	/// Gets how the host was launched.
	/// </summary>
	public PlaintorchLaunchMode LaunchMode { get; init; } = PlaintorchLaunchMode.Interactive;

	/// <summary>
	/// Gets the vault a one-shot command operates on. <see langword="null"/> for a serving host, which starts idle and
	/// activates its vault from user settings at runtime.
	/// </summary>
	public string? VaultPath { get; init; }

	/// <summary>
	/// Gets the raw command-line arguments forwarded to the web host builder (configuration providers, environment).
	/// </summary>
	public string[] Arguments { get; init; } = [];

	/// <summary>
	/// Gets the vault options a one-shot command registers, or <see langword="null"/> when the host starts unbound.
	/// </summary>
	public VaultOptions? ResolveVaultOptions()
	{
		return VaultPath is null ? null : new VaultOptions { VaultPath = VaultPath };
	}
}
