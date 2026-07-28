using A11d.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pleiades.Plaintorch;
using Pleiades.Vault;

namespace Pleiades.Tests.Harness;

/// <summary>
/// Builds the real PLAINTORCH dependency-injection graph for tests, over a temp vault and an ephemeral user layout.
/// </summary>
/// <remarks>
/// The host is built exactly as the app builds it (<c>Install&lt;PLAINTORCH&gt;()</c>) but is never started, so Kestrel and
/// the hosted watcher/core services stay dormant. Behaviour is driven explicitly through resolved services instead of
/// live filesystem events, which keeps tests deterministic.
/// </remarks>
public static class PlaintorchTestHost
{
	/// <summary>
	/// Builds a non-started host rooted at the given vault directory with an isolated ephemeral user environment.
	/// </summary>
	/// <param name="vaultRoot">The temp vault directory the host should operate on.</param>
	/// <returns>The built (but not started) application host.</returns>
	public static WebApplication Build(string vaultRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);

		var builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.Services.AddSingleton(new VaultOptions { VaultPath = vaultRoot });
		builder.Services.AddSingleton(PlaintorchUserLayout.CreateEphemeral());

		return builder.Install<PLAINTORCH>().Build();
	}
}
