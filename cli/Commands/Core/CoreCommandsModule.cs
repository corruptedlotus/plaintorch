using Microsoft.Extensions.DependencyInjection;

namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Registers the top-level lifecycle commands that currently forward into the existing core entry point.
/// </summary>
public sealed class CoreCommandsModule : CliCommandModule
{
	public override void ConfigureServices(IServiceCollection services)
	{
		services.AddTransient<CoreCommandForwarder>();
	}

	public override void RegisterCommands(CliCommandCollection commands)
	{
		commands.Add<CoreCommand>("core");
	}
}
