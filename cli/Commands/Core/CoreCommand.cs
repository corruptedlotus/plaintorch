namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Groups the forwarded core lifecycle commands under a shared command root.
/// </summary>
[CliDescription("Access the existing hosted-core lifecycle commands through a grouped command root.")]
public sealed class CoreCommand
{
	public static void RegisterSubcommands(CliCommandCollection commands)
	{
		commands.Add<InitCommand>("init");
		commands.Add<ActivateCommand>("activate");
		commands.Add<DeactivateCommand>("deactivate");
		commands.Add<ServeCommand>("serve");
		commands.Add<BootstrapServiceCommand>("bootstrap-service");
	}
}
