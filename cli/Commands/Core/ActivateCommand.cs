namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Marks a vault as the active PLAINTORCH workspace for the current user.
/// </summary>
[CliDescription("Mark a PLAINTORCH vault as the active per-user workspace.")]
public sealed class ActivateCommand(CoreCommandForwarder forwarder)
{
	[CliDefaultCommand]
	public Task<int> ExecuteAsync(CoreCommandOptions options, CancellationToken cancellationToken = default)
	{
		return forwarder.ForwardAsync("activate", options.VaultPath);
	}
}
