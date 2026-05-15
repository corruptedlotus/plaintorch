namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Starts the hosted PLAINTORCH core for the selected vault.
/// </summary>
[CliDescription("Run the hosted PLAINTORCH core against the selected vault.")]
public sealed class ServeCommand(CoreCommandForwarder forwarder)
{
	[CliDefaultCommand]
	public Task<int> ExecuteAsync(CoreCommandOptions options, CancellationToken cancellationToken = default)
	{
		return forwarder.ForwardAsync("serve", options.VaultPath);
	}
}
