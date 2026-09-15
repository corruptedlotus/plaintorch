namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// The parsed command line of the core executable: the command, its vault, and the serve-mode switches.
/// </summary>
/// <remarks>
/// Switch semantics for <c>serve</c>:
/// <list type="bullet">
/// <item><c>--daemon</c>: run the real per-user profile as a headless background daemon (console plus file logs).</item>
/// <item><c>--spawn</c>: run the real per-user profile as a desktop shell's child (file logs, stdout status stream, stdin-close shutdown).</item>
/// <item><c>--ephemeral</c>: a throwaway, user-independent temp profile for tests and sandboxes.</item>
/// <item><c>--profile &lt;dir&gt;</c>: an explicit profile root, overriding the dev/real selection (a shell in development points its spawned core at the dev sub-profile with this).</item>
/// <item><c>--loopback</c>: also bind the fixed loopback HTTP endpoint.</item>
/// <item><c>--vault &lt;path&gt;</c>: serve a specific vault, bypassing user settings.</item>
/// </list>
/// Without <c>--daemon</c>, <c>--spawn</c>, <c>--ephemeral</c>, or <c>--profile</c>, a manual <c>serve</c> targets the persistent
/// development sub-profile, so a sandbox never collides with the real installed daemon. Every other command
/// targets the real per-user profile.
/// </remarks>
public sealed class PlaintorchLaunchArguments
{
	private static readonly string[] KnownCommands = ["init", "activate", "deactivate", "serve", "bootstrap-service"];

	private PlaintorchLaunchArguments(string command, string[] raw)
	{
		Command = command;
		Raw = raw;
	}

	/// <summary>Gets the command to run.</summary>
	public string Command { get; }

	/// <summary>Gets the raw arguments, forwarded to the host builder.</summary>
	public string[] Raw { get; }

	/// <summary>Gets the explicit <c>--vault</c> value, when given.</summary>
	public string? VaultArgument => ExtractValue("--vault");

	/// <summary>Gets the explicit <c>--profile</c> root, when given.</summary>
	public string? ProfileArgument => ExtractValue("--profile");

	/// <summary>Gets whether <c>--daemon</c> was passed.</summary>
	public bool Daemon => HasFlag("--daemon");

	/// <summary>Gets whether <c>--spawn</c> was passed.</summary>
	public bool Spawn => HasFlag("--spawn");

	/// <summary>Gets whether <c>--ephemeral</c> was passed.</summary>
	public bool Ephemeral => HasFlag("--ephemeral");

	/// <summary>Gets whether <c>--loopback</c> was passed.</summary>
	public bool Loopback => HasFlag("--loopback");

	/// <summary>Gets whether the command is the long-lived <c>serve</c>.</summary>
	public bool IsServe => Command == "serve";

	/// <summary>
	/// Gets the comma-separated list of supported commands, for error output.
	/// </summary>
	public static string SupportedCommands => string.Join(", ", KnownCommands);

	/// <summary>
	/// Parses the process arguments. The first non-switch token is the command; it defaults to <c>init</c>.
	/// </summary>
	/// <param name="args">The process arguments.</param>
	/// <returns>The parsed arguments, or <see langword="null"/> when the command is unknown.</returns>
	public static PlaintorchLaunchArguments? TryParse(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);
		var command = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant() ?? "init";
		return KnownCommands.Contains(command, StringComparer.Ordinal)
			? new PlaintorchLaunchArguments(command, args)
			: null;
	}

	/// <summary>
	/// Resolves the launch mode the switches describe.
	/// </summary>
	public PlaintorchLaunchMode ResolveLaunchMode()
	{
		if (!IsServe)
		{
			return PlaintorchLaunchMode.Interactive;
		}

		if (Spawn)
		{
			return PlaintorchLaunchMode.Spawn;
		}

		return Daemon || Microsoft.Extensions.Hosting.Systemd.SystemdHelpers.IsSystemdService()
			? PlaintorchLaunchMode.Daemon
			: PlaintorchLaunchMode.Interactive;
	}

	/// <summary>
	/// Resolves the per-user profile the command runs against, honouring the serve-mode switches.
	/// </summary>
	public PlaintorchUserLayout ResolveUserLayout()
	{
		var layout = ResolveUserLayoutRoot();
		layout.LoopbackEnabled = Loopback;
		return layout;
	}

	/// <summary>
	/// Resolves the vault a one-shot command operates on: the explicit argument, else the current directory.
	/// <c>serve</c> yields <see langword="null"/>, since it activates its vault from user settings at runtime.
	/// </summary>
	public string? ResolveCommandVaultPath()
	{
		return IsServe ? null : VaultArgument ?? Directory.GetCurrentDirectory();
	}

	private PlaintorchUserLayout ResolveUserLayoutRoot()
	{
		if (ProfileArgument is { } profile)
		{
			return PlaintorchUserLayout.CreateAt(profile);
		}

		if (!IsServe)
		{
			return PlaintorchUserLayout.CreateDefault();
		}

		if (Ephemeral)
		{
			return PlaintorchUserLayout.CreateEphemeral();
		}

		return ResolveLaunchMode() == PlaintorchLaunchMode.Interactive
			? PlaintorchUserLayout.CreateDevProfile()
			: PlaintorchUserLayout.CreateDefault();
	}

	private bool HasFlag(string flag)
	{
		return Raw.Any(argument => string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase));
	}

	private string? ExtractValue(string option)
	{
		for (var index = 0; index < Raw.Length - 1; index++)
		{
			if (string.Equals(Raw[index], option, StringComparison.OrdinalIgnoreCase))
			{
				return Raw[index + 1];
			}
		}

		return null;
	}
}
