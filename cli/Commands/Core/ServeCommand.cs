namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Starts the hosted PLAINTORCH core.
/// </summary>
[CliDescription("Run the hosted PLAINTORCH core. Idle until a vault is activated unless --vault is given.")]
public sealed class ServeCommand(CoreCommandForwarder forwarder)
{
	[CliDefaultCommand]
	public Task<int> ExecuteAsync(ServeCommandOptions options, CancellationToken cancellationToken = default)
	{
		return forwarder.ForwardAsync("serve", options.ToCoreArguments());
	}
}
