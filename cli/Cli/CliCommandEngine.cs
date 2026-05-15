using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Pleiades.Plaintorch.Cli;

/// <summary>
/// Executes modular CLI commands, binds options objects, and invokes command methods through DI-created command instances.
/// </summary>
public sealed class CliCommandEngine(
	CliCommandRegistry registry,
	CliOptionsBinder optionsBinder,
	CliHelpTextRenderer helpTextRenderer,
	IServiceProvider serviceProvider)
{
	public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(args);
		if (args.Count == 0)
		{
			Console.WriteLine(helpTextRenderer.RenderRootHelp());
			return 0;
		}

		var resolution = ResolveNode(args);
		if (resolution.Node == registry.Root)
		{
			Console.WriteLine(helpTextRenderer.RenderRootHelp());
			return args.All(IsHelpSwitch) ? 0 : 1;
		}

		if (resolution.UnknownSegment is not null)
		{
			Console.Error.WriteLine($"Unknown command segment '{resolution.UnknownSegment}' under '{resolution.Node.Path}'.");
			Console.WriteLine(helpTextRenderer.RenderCommandHelp(resolution.Node));
			return 1;
		}

		var commandType = resolution.Node.CommandType;
		if (commandType is null)
		{
			Console.WriteLine(helpTextRenderer.RenderCommandHelp(resolution.Node));
			return 0;
		}

		var helpRequested = resolution.RemainingArguments.Any(IsHelpSwitch);
		var method = helpRequested
			? ResolveHelpMethod(commandType)
			: ResolveDefaultMethod(commandType);

		if (helpRequested && method is null)
		{
			Console.WriteLine(helpTextRenderer.RenderCommandHelp(resolution.Node));
			return 0;
		}

		if (method is null)
		{
			Console.WriteLine(helpTextRenderer.RenderCommandHelp(resolution.Node));
			return 0;
		}

		var invocationArguments = resolution.RemainingArguments.Where(argument => !IsHelpSwitch(argument)).ToArray();
		var invocationContext = new CliInvocationContext(resolution.Node, resolution.CommandSegments, invocationArguments);
		var command = serviceProvider.GetRequiredService(commandType);
		var parameters = BuildParameters(method, invocationContext, cancellationToken);

		return await InvokeAsync(command, method, parameters);
	}

	private object?[] BuildParameters(MethodInfo method, CliInvocationContext invocationContext, CancellationToken cancellationToken)
	{
		var parameters = new List<object?>();
		var optionsParameter = method.GetParameters()
			.FirstOrDefault(parameter => parameter.ParameterType != typeof(CliInvocationContext) && parameter.ParameterType != typeof(CancellationToken));
		var optionsObject = optionsParameter is null
			? null
			: optionsBinder.Bind(optionsParameter.ParameterType, invocationContext.Arguments);

		foreach (var parameter in method.GetParameters())
		{
			if (parameter.ParameterType == typeof(CliInvocationContext))
			{
				parameters.Add(invocationContext);
				continue;
			}

			if (parameter.ParameterType == typeof(CancellationToken))
			{
				parameters.Add(cancellationToken);
				continue;
			}

			parameters.Add(optionsObject);
		}

		return parameters.ToArray();
	}

	private static async Task<int> InvokeAsync(object command, MethodInfo method, object?[] parameters)
	{
		var rawResult = method.Invoke(command, parameters);
		return rawResult switch
		{
			Task<int> intTask => await intTask,
			Task task => await AwaitAndReturnSuccessAsync(task),
			int exitCode => exitCode,
			null => 0,
			_ => throw new InvalidOperationException($"Command method '{method.DeclaringType?.Name}.{method.Name}' returned unsupported type '{rawResult.GetType().Name}'."),
		};
	}

	private static async Task<int> AwaitAndReturnSuccessAsync(Task task)
	{
		await task;
		return 0;
	}

	private static MethodInfo? ResolveDefaultMethod(Type commandType)
	{
		return commandType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.SingleOrDefault(method => method.GetCustomAttribute<CliDefaultCommandAttribute>() is not null);
	}

	private static MethodInfo? ResolveHelpMethod(Type commandType)
	{
		return commandType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.SingleOrDefault(method => method.GetCustomAttribute<CliHelpSwitchAttribute>() is not null);
	}

	private static bool IsHelpSwitch(string value)
	{
		return value is "--help" or "-h" or "/?";
	}

	private Resolution ResolveNode(IReadOnlyList<string> args)
	{
		var current = registry.Root;
		var commandSegments = new List<string>();
		var index = 0;

		while (index < args.Count)
		{
			var token = args[index];
			if (string.IsNullOrWhiteSpace(token) || token.StartsWith("-", StringComparison.Ordinal))
			{
				break;
			}

			if (!current.Children.TryGetValue(token, out var child))
			{
				return new Resolution(current, commandSegments, args.Skip(index).ToArray(), token);
			}

			current = child;
			commandSegments.Add(token);
			index++;
		}

		return new Resolution(current, commandSegments, args.Skip(index).ToArray(), null);
	}

	private sealed record Resolution(
		CliCommandNode Node,
		IReadOnlyList<string> CommandSegments,
		IReadOnlyList<string> RemainingArguments,
		string? UnknownSegment);
}

/// <summary>
/// Represents invocation context for the selected command path.
/// </summary>
public sealed record CliInvocationContext(
	CliCommandNode Node,
	IReadOnlyList<string> CommandSegments,
	IReadOnlyList<string> Arguments);
