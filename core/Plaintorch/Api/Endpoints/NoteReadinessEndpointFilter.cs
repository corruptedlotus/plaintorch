using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Markdown;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Emits the <c>X-Note-Ready</c> response header (PEP110 Refactor BETA). When a mutation's vault write did not land
/// within <see cref="Pleiades.Plaintorch.Preferences.WatcherPreferences.NoteQueueTimeout"/> — it is finishing in the
/// background — the header is set to <c>false</c> so a client about to open the note (a rename-reveal, a
/// create-then-open) waits for it instead of opening a file that is not on disk yet. The header is absent when every
/// write in the request was ready (or none occurred), which the client reads as "open now", so existing flows are
/// unchanged.
/// </summary>
public sealed class NoteReadinessEndpointFilter : IEndpointFilter
{
	/// <summary>The response header carrying note readiness.</summary>
	public const string HeaderName = "X-Note-Ready";

	/// <inheritdoc />
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		var result = await next(context);

		// The endpoint result is not serialized until the filter chain returns, so the response headers are still open.
		var readiness = context.HttpContext.RequestServices.GetRequiredService<VaultWriteReadiness>();
		if (readiness.IsPending)
		{
			context.HttpContext.Response.Headers[HeaderName] = "false";
		}

		return result;
	}
}
