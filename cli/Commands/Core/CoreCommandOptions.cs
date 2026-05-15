namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Provides the common optional vault switch used by the forwarded core commands.
/// </summary>
public sealed class CoreCommandOptions
{
	[CliOption("vault", ShortName = "v", Description = "Vault path to operate against. When omitted, the core falls back to its built-in default resolution for the selected command.")]
	public string? VaultPath { get; set; }
}
