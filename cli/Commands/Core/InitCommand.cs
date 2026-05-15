namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Initializes a PLAINTORCH vault.
/// </summary>
[CliDescription("Initialize a PLAINTORCH vault in the selected directory.")]
public sealed class InitCommand(CoreCommandForwarder forwarder)
{
	[CliDefaultCommand]
	public Task<int> ExecuteAsync(CoreCommandOptions options, CancellationToken cancellationToken = default)
	{
		return forwarder.ForwardAsync("init", options.VaultPath);
	}
}
