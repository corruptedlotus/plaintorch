namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// The switches of the forwarded <c>serve</c> command: the optional vault plus the launch-mode selection.
/// </summary>
public sealed class ServeCommandOptions
{
	[CliOption("vault", ShortName = "v", Description = "Serve a specific vault, bypassing the active vault in user settings.")]
	public string? VaultPath { get; set; }

	[CliOption("daemon", Description = "Run the real per-user profile as a headless background daemon (console plus file logs).")]
	public bool Daemon { get; set; }

	[CliOption("spawn", Description = "Run as a desktop shell's child: file logs only, JSON status lines on stdout, shutdown when stdin closes.")]
	public bool Spawn { get; set; }

	[CliOption("ephemeral", Description = "Use a throwaway, user-independent temp profile.")]
	public bool Ephemeral { get; set; }

	[CliOption("profile", Description = "Use an explicit profile root directory instead of the dev/real selection.")]
	public string? Profile { get; set; }

	[CliOption("loopback", Description = "Also bind the fixed loopback HTTP endpoint.")]
	public bool Loopback { get; set; }

	/// <summary>
	/// Renders the switches as the core executable's serve arguments.
	/// </summary>
	public IReadOnlyList<string> ToCoreArguments()
	{
		var arguments = new List<string>();
		if (!string.IsNullOrWhiteSpace(VaultPath))
		{
			arguments.Add("--vault");
			arguments.Add(VaultPath);
		}

		if (Daemon)
		{
			arguments.Add("--daemon");
		}

		if (Spawn)
		{
			arguments.Add("--spawn");
		}

		if (Ephemeral)
		{
			arguments.Add("--ephemeral");
		}

		if (!string.IsNullOrWhiteSpace(Profile))
		{
			arguments.Add("--profile");
			arguments.Add(Profile);
		}

		if (Loopback)
		{
			arguments.Add("--loopback");
		}

		return arguments;
	}
}
