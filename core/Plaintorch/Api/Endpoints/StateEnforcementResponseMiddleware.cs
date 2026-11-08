using Microsoft.AspNetCore.Http;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Enforcement;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Answers a change that state enforcement stops (PEP103) with 409 Conflict and the decision as its body
/// (<see cref="StateEnforcementProblem"/>), instead of the bodiless 500 an unhandled refusal gets. Only
/// <see cref="StateEnforcementException"/> is mapped; every other exception passes through untouched.
/// </summary>
public sealed class StateEnforcementResponseMiddleware(RequestDelegate next)
{
	/// <summary>
	/// Runs the rest of the pipeline and maps a stopped change to its answer.
	/// </summary>
	public async Task InvokeAsync(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		try
		{
			await next(context);
		}
		catch (StateEnforcementException exception) when (!context.Response.HasStarted)
		{
			context.Response.Clear();
			context.Response.StatusCode = StatusCodes.Status409Conflict;
			await context.Response.WriteAsJsonAsync(StateEnforcementProblem.From(exception.Decision), context.RequestAborted);
		}
	}
}
