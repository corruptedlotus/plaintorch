namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Clears the active PLAINTORCH workspace for the current user, returning any running core to idle.
/// </summary>
[CliDescription("Clear the active per-user PLAINTORCH vault so the hosted core returns to idle.")]
public sealed class DeactivateCommand(CoreCommandForwarder forwarder)
{
	[CliDefaultCommand]
	public Task<int> ExecuteAsync(CoreCommandOptions options, CancellationToken cancellationToken = default)
	{
		return forwarder.ForwardAsync("deactivate");
	}
}
