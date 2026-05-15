namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Generates platform-specific background-service bootstrap assets.
/// </summary>
[CliDescription("Generate service bootstrap assets for the PLAINTORCH core.")]
public sealed class BootstrapServiceCommand(CoreCommandForwarder forwarder)
{
	[CliDefaultCommand]
	public Task<int> ExecuteAsync(CoreCommandOptions options, CancellationToken cancellationToken = default)
	{
		return forwarder.ForwardAsync("bootstrap-service", options.VaultPath);
	}
}
