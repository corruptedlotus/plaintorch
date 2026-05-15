using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
var modules = CliCommandRegistry.DiscoverModules(Assembly.GetExecutingAssembly());

foreach (var module in modules)
{
	module.ConfigureServices(services);
}

var registry = CliCommandRegistry.Create(modules);
services.AddSingleton(registry);
services.AddSingleton<CliArgumentParser>();
services.AddSingleton<CliOptionsBinder>();
services.AddSingleton<CliHelpTextRenderer>();
services.AddSingleton<CliCommandEngine>();

foreach (var commandType in registry.CommandTypes)
{
	services.AddTransient(commandType);
}

using var serviceProvider = services.BuildServiceProvider();
var engine = serviceProvider.GetRequiredService<CliCommandEngine>();
var exitCode = await engine.ExecuteAsync(args);
return exitCode;
