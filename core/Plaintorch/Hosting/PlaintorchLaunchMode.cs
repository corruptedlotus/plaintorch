namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// How the hosted core was launched, which decides where it logs and how it reports its phase.
/// </summary>
public enum PlaintorchLaunchMode
{
	/// <summary>A one-shot CLI command (init, activate, ...) or a developer's manual <c>serve</c>. Logs to the console.</summary>
	Interactive,

	/// <summary>A headless background daemon (systemd user unit, dedicated server). Logs to the console and to files.</summary>
	Daemon,

	/// <summary>
	/// A child process of a desktop shell. Logs to files only; stdout carries a JSON-lines status stream and closing
	/// stdin requests a graceful shutdown.
	/// </summary>
	Spawn,
}
