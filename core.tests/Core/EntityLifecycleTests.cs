using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Services;
using Pleiades.Saga;
using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The policy-derived entity lifecycle actions (REFACTOR Alpha phase 5a): the storage-mode-mechanical
/// <c>begin</c>/<c>init</c> actions are served by one generic implementation dispatched through the phase-4 mode
/// policy objects, replacing the per-entity copies. The per-type <c>Begin*BoundaryAsync</c> methods delegate here, so
/// their existing tests are the begin-parity gate; these lock the generic action's own guards.
/// </summary>
public sealed class EntityLifecycleTests : VaultTestBase
{
	[Fact]
	public async Task Begin_boundary_is_rejected_for_a_mode_that_does_not_gate_on_a_boundary()
	{
		// Boundary-begin is only meaningful where the storage mode withholds the file until a boundary is begun
		// (Implicit). A Synced entity (LorePage) materializes on create, so it has no boundary to begin — the generic
		// action refuses it through the mode policy rather than a type check.
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Vault.WithScopeAsync(services => services
			.GetRequiredService<VaultEntityLifecycleService>()
			.BeginBoundaryAsync(typeof(LorePage), "l00000001", TestContext.Current.CancellationToken)));

		Assert.Contains("synchronization boundary", exception.Message, StringComparison.OrdinalIgnoreCase);
	}
}
