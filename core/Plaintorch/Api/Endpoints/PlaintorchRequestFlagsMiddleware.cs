using Microsoft.AspNetCore.Http;

namespace Pleiades.Plaintorch.Api.Endpoints;

/// <summary>
/// Reads the request-wide API flags (PEP103) into the request scope's <see cref="PlaintorchRequestFlags"/> before any
/// endpoint runs, so every service the request reaches sees them. Every request it serves is interactive: its client can be
/// asked to confirm a change.
/// </summary>
public sealed class PlaintorchRequestFlagsMiddleware(RequestDelegate next)
{
	/// <summary>
	/// Sets the flags and runs the rest of the pipeline, or answers 400 Bad Request for a flag value the API does not take.
	/// </summary>
	public async Task InvokeAsync(HttpContext context, PlaintorchRequestFlags flags)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(flags);
		flags.Interactive = true;
		if (context.Request.Headers.TryGetValue(PlaintorchRequestFlags.EnforcementHeader, out var values) && values.Count > 0)
		{
			var value = values.ToString().Trim();
			if (!string.Equals(value, PlaintorchRequestFlags.ForceCompensationValue, StringComparison.OrdinalIgnoreCase))
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				await context.Response.WriteAsync(
					$"'{value}' is not a value of {PlaintorchRequestFlags.EnforcementHeader}; the one value it takes is '{PlaintorchRequestFlags.ForceCompensationValue}'.",
					context.RequestAborted);
				return;
			}

			flags.ForceCompensation = true;
		}

		await next(context);
	}
}
