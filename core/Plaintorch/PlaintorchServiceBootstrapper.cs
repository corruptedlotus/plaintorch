using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Pleiades.Plaintorch;

/// <summary>
/// Generates host-service bootstrap assets for Windows and Linux deployments of PLAINTORCH.
/// </summary>
public sealed class PlaintorchServiceBootstrapper(PlaintorchUserLayout userLayout)
{
	private const string ServiceName = "PlaintorchCore";

	/// <summary>
	/// Creates bootstrap assets for the current platform.
	/// </summary>
	/// <returns>A summary of generated files.</returns>
	public PlaintorchServiceBootstrapResult Bootstrap()
	{
		userLayout.EnsureExists();
		var serviceRoot = Path.Combine(userLayout.RootPath, "service");
		Directory.CreateDirectory(serviceRoot);

		var launchCommand = BuildLaunchCommand();
		var generatedFiles = new List<string>();

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			generatedFiles.Add(WriteWindowsInstallScript(serviceRoot, launchCommand));
			generatedFiles.Add(WriteWindowsUninstallScript(serviceRoot));
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			generatedFiles.Add(WriteSystemdUnit(serviceRoot, launchCommand));
			generatedFiles.Add(WriteSystemdInstallScript(serviceRoot));
		}
		else
		{
			throw new PlatformNotSupportedException("PLAINTORCH service bootstrap assets are currently prepared only for Windows and Linux.");
		}

		return new PlaintorchServiceBootstrapResult(serviceRoot, generatedFiles);
	}

	private static string EscapeDoubleQuotes(string value) => value.Replace("\"", "\"\"");

	private static string EscapeSingleQuotes(string value) => value.Replace("'", "'\"'\"'");

	private static string BuildLaunchCommand()
	{
		var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to determine the current process path.");
		var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;

		// The installed service is the real per-user daemon, so it must bind the real ~/.pleiades/plaintorch profile.
		// Service-context detection already routes it there, and `--daemon` makes that explicit and detection-independent.
		if (Path.GetFileName(processPath).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(entryAssemblyPath))
		{
			return $"\"{processPath}\" \"{entryAssemblyPath}\" serve --daemon";
		}

		return $"\"{processPath}\" serve --daemon";
	}

	private static string WriteWindowsInstallScript(string serviceRoot, string launchCommand)
	{
		var path = Path.Combine(serviceRoot, "install-windows-service.ps1");
		var script = $$"""
$ErrorActionPreference = 'Stop'

$serviceName = '{{ServiceName}}'
$binaryPath = '"{{EscapeSingleQuotes(launchCommand)}}"'

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Write-Host "Service '$serviceName' already exists."
    exit 0
}

New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName 'PLAINTORCH Core' -Description 'PLAINTORCH background core service' -StartupType Automatic
Start-Service -Name $serviceName
Write-Host "Installed and started $serviceName."
""";

		File.WriteAllText(path, script + Environment.NewLine, Encoding.UTF8);
		return path;
	}

	private static string WriteWindowsUninstallScript(string serviceRoot)
	{
		var path = Path.Combine(serviceRoot, "uninstall-windows-service.ps1");
		var script = $$"""
$ErrorActionPreference = 'Stop'

$serviceName = '{{ServiceName}}'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$serviceName' does not exist."
    exit 0
}

if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
}

sc.exe delete $serviceName | Out-Null
Write-Host "Removed $serviceName."
""";

		File.WriteAllText(path, script + Environment.NewLine, Encoding.UTF8);
		return path;
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