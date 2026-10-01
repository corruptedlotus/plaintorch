using Microsoft.EntityFrameworkCore.Diagnostics;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// The one place backlog records are inferred and schedule slots settled (PEP101). Every save of a
/// <see cref="PlainfraContext"/> passes through here — asynchronous or synchronous, whoever issues it — so no write
/// pathway has to remember to record a lifecycle transition. The work itself is <see cref="BacklogScheduleRecorder"/>'s.
/// </summary>
/// <remarks>
/// <para>
/// Registered after <see cref="PlaintorchStatePolicyInterceptor"/>, so it reads the statuses the state policy leaves
/// behind (a checkpoint the dependency reconciler has just unlocked included), and before the change feed, so the
/// records it adds and withdraws are announced.
/// </para>
/// <para>
/// The synchronous hook runs the same recorder, blocking on it; the SQLite provider completes its asynchronous calls
/// synchronously, so this ties up no extra thread, as the engine's own startup already does. PUCK issuance used to be the
/// synchronous saver that mattered — it saved on the caller's context, storing whatever the caller had pending — and now
/// registers its ids on a bare context of its own (<c>PlainfraStoreContextFactory</c>); the hook stays so that no
/// synchronous save of domain state can slip past the records.
/// </para>
/// <para>
/// What no save interceptor sees, this one included: bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> and raw SQL, which
/// write past the change tracker.
/// </para>
/// </remarks>
public sealed class BacklogScheduleInterceptor(BacklogScheduleRecorder recorder) : SaveChangesInterceptor
{
	/// <inheritdoc />
	public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
	{
		if (eventData.Context is PlainfraContext context)
		{
			recorder.ApplyAsync(context, CancellationToken.None).GetAwaiter().GetResult();
		}

		return base.SavingChanges(eventData, result);
	}

	/// <inheritdoc />
	public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context)
		{
			await recorder.ApplyAsync(context, cancellationToken);
		}

		return await base.SavingChangesAsync(eventData, result, cancellationToken);
	}
}
