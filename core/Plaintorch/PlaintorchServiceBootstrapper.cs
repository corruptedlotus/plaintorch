using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Pleiades.Plaintorch;

/// <summary>
/// Generates headless service bootstrap assets for dedicated-server deployments of the PLAINTORCH core.
/// </summary>
/// <remarks>
/// Only a Linux systemd <em>user</em> unit is generated. A Windows machine service was dropped on purpose: it ran as
/// LocalSystem, so the "per-user" profile resolved to the system account and no user's client could reach the pipe.
/// On a desktop the standalone shell owns per-user autostart instead; on a Windows server, run <c>serve --daemon</c>
/// from the user's own scheduled task or session.
/// </remarks>
public sealed class PlaintorchServiceBootstrapper(PlaintorchUserLayout userLayout)
{
	/// <summary>
	/// Creates bootstrap assets for the current platform.
	/// </summary>
	/// <returns>A summary of generated files.</returns>
	public PlaintorchServiceBootstrapResult Bootstrap()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			throw new PlatformNotSupportedException(
				"PLAINTORCH service bootstrap assets are generated for Linux systemd user services only. "
				+ "On a desktop, per-user autostart is managed by the PLAINTORCH standalone app; on Windows servers run 'serve --daemon' under the user's own session or scheduled task.");
		}

		userLayout.EnsureExists();
		var serviceRoot = Path.Combine(userLayout.RootPath, "service");
		Directory.CreateDirectory(serviceRoot);

		var launchCommand = BuildLaunchCommand();
		var generatedFiles = new List<string>
		{
			WriteSystemdUnit(serviceRoot, launchCommand),
			WriteSystemdInstallScript(serviceRoot),
		};

		return new PlaintorchServiceBootstrapResult(serviceRoot, generatedFiles);
	}

	private static string BuildLaunchCommand()
	{
		var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to determine the current process path.");
		var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;

		// The installed unit is the real per-user daemon, so it must bind the real ~/.pleiades/plaintorch profile.
		// Systemd detection already routes it there, and `--daemon` makes that explicit and detection-independent.
		if (Path.GetFileName(processPath).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(entryAssemblyPath))
		{
			return $"\"{processPath}\" \"{entryAssemblyPath}\" serve --daemon";
		}

		return $"\"{processPath}\" serve --daemon";
	}

	private static string WriteSystemdUnit(string serviceRoot, string launchCommand)
	{
		var path = Path.Combine(serviceRoot, "plaintorch.service");
		var workingDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var unit = $$"""
[Unit]
Description=PLAINTORCH Core
After=network.target

[Service]
Type=simple
WorkingDirectory={{workingDirectory}}
ExecStart={{launchCommand}}
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
""";

		File.WriteAllText(path, unit.Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
		return path;
	}

	private static string WriteSystemdInstallScript(string serviceRoot)
	{
		var path = Path.Combine(serviceRoot, "install-systemd-service.sh");
		var script = $$"""
#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_SOURCE="$SCRIPT_DIR/plaintorch.service"
UNIT_TARGET="$HOME/.config/systemd/user/plaintorch.service"

mkdir -p "$(dirname "$UNIT_TARGET")"
cp "$UNIT_SOURCE" "$UNIT_TARGET"
systemctl --user daemon-reload
systemctl --user enable --now plaintorch.service
echo "Installed and started plaintorch.service"
""";

		File.WriteAllText(path, script.Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
		return path;
	}
}

/// <summary>
/// Summarizes the generated service bootstrap assets.
/// </summary>
/// <param name="RootPath">The directory containing the generated assets.</param>
/// <param name="GeneratedFiles">The generated asset paths.</param>
public sealed record PlaintorchServiceBootstrapResult(string RootPath, IReadOnlyList<string> GeneratedFiles);
