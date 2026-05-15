using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Pleiades.Plaintorch.Cli;

/// <summary>
/// Provides modular command registration in the CLI package.
/// </summary>
public abstract class CliCommandModule
{
	/// <summary>
	/// Allows a module to register services for its commands.
	/// </summary>
	public virtual void ConfigureServices(IServiceCollection services)
	{
	}

	/// <summary>
	/// Registers the root commands owned by the module.
	/// </summary>
	public abstract void RegisterCommands(CliCommandCollection commands);
}

/// <summary>
/// Represents a resolved command registry for the CLI engine.
/// </summary>
public sealed class CliCommandRegistry
{
	private CliCommandRegistry(CliCommandNode root, IReadOnlyCollection<Type> commandTypes)
	{
		Root = root;
		CommandTypes = commandTypes;
	}

	public CliCommandNode Root { get; }

	public IReadOnlyCollection<Type> CommandTypes { get; }

	public static IReadOnlyList<CliCommandModule> DiscoverModules(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		return assembly
			.GetTypes()
			.Where(type => !type.IsAbstract && typeof(CliCommandModule).IsAssignableFrom(type))
			.OrderBy(type => type.FullName, StringComparer.Ordinal)
			.Select(type => (CliCommandModule)(Activator.CreateInstance(type)
				?? throw new InvalidOperationException($"Could not create command module '{type.FullName}'.")))
			.ToArray();
	}

	public static CliCommandRegistry Create(IEnumerable<CliCommandModule> modules)
	{
		ArgumentNullException.ThrowIfNull(modules);
		var root = new CliCommandNode(string.Empty, null, null);
		var commandTypes = new HashSet<Type>();
		var collection = new CliCommandCollection(root, commandTypes);

		foreach (var module in modules)
		{
			module.RegisterCommands(collection);
		}

		return new CliCommandRegistry(root, commandTypes.ToArray());
	}
}

/// <summary>
/// Provides registration helpers for root and nested commands.
/// </summary>
public sealed class CliCommandCollection
{
	private readonly CliCommandNode parent;
	private readonly ISet<Type> commandTypes;

	internal CliCommandCollection(CliCommandNode parent, ISet<Type> commandTypes)
	{
		this.parent = parent;
		this.commandTypes = commandTypes;
	}

	public CliCommandCollection Add<TCommand>(string name)
		where TCommand : class
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		var normalizedName = name.Trim();
		if (parent.Children.ContainsKey(normalizedName))
		{
			throw new InvalidOperationException($"Command '{normalizedName}' is already registered under '{parent.Path}'.");
		}

		var node = new CliCommandNode(normalizedName, typeof(TCommand), parent);
		parent.Children.Add(normalizedName, node);
		commandTypes.Add(typeof(TCommand));
		RegisterSubcommands(typeof(TCommand), node);
		return new CliCommandCollection(node, commandTypes);
	}

	private void RegisterSubcommands(Type commandType, CliCommandNode node)
	{
		var registrationMethod = commandType.GetMethod(
			"RegisterSubcommands",
			BindingFlags.Public | BindingFlags.Static,
			binder: null,
			types: [typeof(CliCommandCollection)],
			modifiers: null);

		if (registrationMethod is null)
		{
			return;
		}

		registrationMethod.Invoke(null, [new CliCommandCollection(node, commandTypes)]);
	}
}

/// <summary>
/// Represents a node in the command tree.
/// </summary>
public sealed class CliCommandNode
{
	internal CliCommandNode(string name, Type? commandType, CliCommandNode? parent)
	{
		Name = name;
		CommandType = commandType;
		Parent = parent;
	}

	public string Name { get; }

	public Type? CommandType { get; }

	public CliCommandNode? Parent { get; }

	public string Path => Parent is null || string.IsNullOrWhiteSpace(Parent.Path)
		? Name
		: $"{Parent.Path} {Name}";

	public IDictionary<string, CliCommandNode> Children { get; } = new Dictionary<string, CliCommandNode>(StringComparer.OrdinalIgnoreCase);
}
