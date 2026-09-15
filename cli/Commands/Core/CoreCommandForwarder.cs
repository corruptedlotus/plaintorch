namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Forwards CLI command execution into the existing core application entry point.
/// </summary>
public sealed class CoreCommandForwarder
{
	public Task<int> ForwardAsync(string commandName, string? vaultPath = null)
	{
		var arguments = new List<string>();
		if (!string.IsNullOrWhiteSpace(vaultPath))
		{
			arguments.Add("--vault");
			arguments.Add(vaultPath);
		}

		return ForwardAsync(commandName, arguments);
	}

	public Task<int> ForwardAsync(string commandName, IReadOnlyList<string> coreArguments)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
		ArgumentNullException.ThrowIfNull(coreArguments);
		return Pleiades.Plaintorch.Program.Main([commandName, .. coreArguments]);
	}
}
