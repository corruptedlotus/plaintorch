namespace Pleiades.Plaintorch.Cli.Commands.Core;

/// <summary>
/// Forwards CLI command execution into the existing core application entry point.
/// </summary>
public sealed class CoreCommandForwarder
{
	public Task<int> ForwardAsync(string commandName, string? vaultPath = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
		var arguments = new List<string> { commandName };
		if (!string.IsNullOrWhiteSpace(vaultPath))
		{
			arguments.Add("--vault");
			arguments.Add(vaultPath);
		}

		return Pleiades.Plaintorch.Program.Main(arguments.ToArray());
	}
}
