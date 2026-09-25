using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pleiades.Plaintorch;
using Pleiades.Plaintorch.Hosting;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The core's daily file sink (<see cref="PlaintorchFileLoggerProvider"/>). It keeps writing after its first flush (it
/// used to die right there, leaving one line per process start), drains its tail on disposal, is owned and disposed by a
/// host built through <see cref="PlaintorchHostFactory"/>, honours the <c>Logging:PlaintorchFile</c> filters from the
/// <c>appsettings.json</c> beside the binaries, survives a day file it cannot open, and bounds its queue.
/// </summary>
/// <remarks>
/// Every test logs into its own temp profile. Files are read with <see cref="FileShare.ReadWrite"/>, as the sink keeps
/// its day file open for appending while it lives.
/// </remarks>
public sealed class PlaintorchFileLoggerTests : IDisposable
{
	// Generous against a loaded test run; a healthy sink writes within milliseconds.
	private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "plaintorch-tests", Guid.NewGuid().ToString("N"));

	private string LogsDirectory => Path.Combine(_root, "logs");

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	[Fact]
	public async Task Keeps_writing_after_its_first_flush()
	{
		using var provider = new PlaintorchFileLoggerProvider(LogsDirectory);

		provider.CreateLogger("Tests.Alpha").LogInformation("first line");
		Assert.Contains("[INF] Tests.Alpha: first line", await WaitForLogsAsync("first line"));

		// Before the fix the drain died after its first line, so this one was queued forever and never written.
		provider.CreateLogger("Tests.Beta").LogWarning("second line");
		Assert.Contains("[WRN] Tests.Beta: second line", await WaitForLogsAsync("second line"));
	}

	[Fact]
	public void Disposal_drains_every_queued_line()
	{
		const int categories = 4;
		const int linesPerCategory = 1_000;
		var provider = new PlaintorchFileLoggerProvider(LogsDirectory);

		Parallel.For(0, categories, category =>
		{
			var logger = provider.CreateLogger($"Tests.Category{category}");
			for (var index = 0; index < linesPerCategory; index++)
			{
				logger.LogInformation("burst {Category}/{Index};", category, index);
			}
		});
		provider.Dispose();

		var written = ReadLogs()
			.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(line => line.Contains("[INF] Tests.Category", StringComparison.Ordinal))
			.ToList();
		Assert.Equal(categories * linesPerCategory, written.Count);
		for (var category = 0; category < categories; category++)
		{
			var prefix = $"Tests.Category{category}: burst {category}/";
			var indexes = written
				.Where(line => line.Contains(prefix, StringComparison.Ordinal))
				.Select(line => int.Parse(line[(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length)..].TrimEnd(';'), CultureInfo.InvariantCulture))
				.Order()
				.ToList();
			Assert.Equal(Enumerable.Range(0, linesPerCategory), indexes);
		}
	}

	[Fact]
	public async Task A_spawned_host_owns_its_file_sink_and_flushes_it_when_disposed()
	{
		var dayBefore = UtcDay();
		// Built but never started, so nothing binds: Kestrel and the hosted services stay dormant.
		var app = PlaintorchHostFactory.Create(new PlaintorchHostOptions
		{
			UserLayout = PlaintorchUserLayout.CreateAt(_root),
			LaunchMode = PlaintorchLaunchMode.Spawn,
		});

		app.Services.GetRequiredService<ILogger<PlaintorchCoreService>>().LogInformation("core line");
		app.Services.GetRequiredService<ILoggerFactory>()
			.CreateLogger("Pleiades.Vault.Watcher.VaultWatcherService")
			.LogError(CaptureFailure(), "watcher line");
		await app.DisposeAsync();

		Assert.True(
			File.Exists(DayFile(dayBefore)) || File.Exists(DayFile(UtcDay())),
			$"Expected the UTC day file under {LogsDirectory}.");
		var content = ReadLogs();
		Assert.Contains("[INF] Pleiades.Plaintorch.PlaintorchCoreService: core line", content);
		Assert.Contains("[ERR] Pleiades.Vault.Watcher.VaultWatcherService: watcher line", content);
		Assert.Contains("System.InvalidOperationException: watcher failure", content);
		Assert.Contains(nameof(ThrowWatcherFailure), content);
	}

	[Fact]
	public async Task The_PlaintorchFile_filters_apply_to_the_container_owned_sink()
	{
		var app = PlaintorchHostFactory.Create(new PlaintorchHostOptions
		{
			UserLayout = PlaintorchUserLayout.CreateAt(_root),
			LaunchMode = PlaintorchLaunchMode.Spawn,
			Arguments = ["--Logging:PlaintorchFile:LogLevel:Microsoft=Warning"],
		});

		var entityFramework = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
		entityFramework.LogInformation("filtered framework information");
		entityFramework.LogError("kept framework error");
		await app.DisposeAsync();

		var content = ReadLogs();
		Assert.DoesNotContain("filtered framework information", content);
		Assert.Contains("[ERR] Microsoft.EntityFrameworkCore.Database.Command: kept framework error", content);
	}

	[Fact]
	public async Task The_default_filters_come_from_the_appsettings_beside_the_binaries()
	{
		var app = PlaintorchHostFactory.Create(new PlaintorchHostOptions
		{
			UserLayout = PlaintorchUserLayout.CreateAt(_root),
			LaunchMode = PlaintorchLaunchMode.Spawn,
		});

		// The content root no longer follows the working directory, so the shipped filters apply wherever the core runs.
		Assert.Equal(
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(app.Environment.ContentRootPath)),
			ignoreCase: OperatingSystem.IsWindows());
		var factory = app.Services.GetRequiredService<ILoggerFactory>();
		factory.CreateLogger("Microsoft.EntityFrameworkCore.Infrastructure").LogInformation("filtered by appsettings");
		factory.CreateLogger("Microsoft.Hosting.Lifetime").LogInformation("lifetime information");
		await app.DisposeAsync();

		var content = ReadLogs();
		Assert.DoesNotContain("filtered by appsettings", content);
		Assert.Contains("[INF] Microsoft.Hosting.Lifetime: lifetime information", content);
	}

	[Fact]
	public async Task A_day_file_it_cannot_open_holds_lines_in_a_bounded_queue_until_it_can()
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(), "Holding a file exclusively to fail the sink's open relies on Windows share modes.");
		const int overflow = 25;
		const int total = PlaintorchFileLoggerProvider.QueueCapacity + overflow;
		Directory.CreateDirectory(LogsDirectory);
		using var provider = new PlaintorchFileLoggerProvider(LogsDirectory);

		using (new FileStream(DayFile(UtcDay()), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
		{
			var logger = provider.CreateLogger("Tests.Locked");
			for (var index = 0; index < total; index++)
			{
				logger.LogInformation("locked #{Index:D5};", index);
			}

			// Long enough for the drain to fail its open at least once and back off; a failed open reads nothing.
			await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);
		}

		var content = await WaitForLogsAsync($"locked #{total - 1:D5};");
		Assert.Contains($"[WRN] {typeof(PlaintorchFileLoggerProvider).FullName}: {overflow} log lines dropped", content);
		Assert.DoesNotContain($"locked #{overflow - 1:D5};", content);
		Assert.Contains($"locked #{overflow:D5};", content);
		Assert.Contains($"locked #{total - 1:D5};", content);

		provider.CreateLogger("Tests.Unlocked").LogInformation("after unlock");
		Assert.Contains("[INF] Tests.Unlocked: after unlock", await WaitForLogsAsync("after unlock"));
	}

	[Fact]
	public void An_entry_appended_after_disposal_goes_straight_to_the_day_file()
	{
		var provider = new PlaintorchFileLoggerProvider(LogsDirectory);
		provider.CreateLogger("Tests.Before").LogInformation("before disposal");
		provider.Dispose();

		provider.Append(LogLevel.Critical, "Tests.Crash", "after disposal", CaptureFailure());

		var content = ReadLogs();
		Assert.Contains("[INF] Tests.Before: before disposal", content);
		Assert.Contains("[CRT] Tests.Crash: after disposal", content);
		Assert.Contains(nameof(ThrowWatcherFailure), content);
	}

	private static Exception CaptureFailure()
	{
		try
		{
			ThrowWatcherFailure();
			throw new UnreachableException();
		}
		catch (InvalidOperationException exception)
		{
			return exception;
		}
	}

	private static void ThrowWatcherFailure() => throw new InvalidOperationException("watcher failure");

	private static string UtcDay() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private string DayFile(string day) => Path.Combine(LogsDirectory, $"plaintorch-{day}.log");

	private string ReadLogs()
	{
		if (!Directory.Exists(LogsDirectory))
		{
			return string.Empty;
		}

		var content = new StringBuilder();
		foreach (var path in Directory.EnumerateFiles(LogsDirectory, "plaintorch-*.log").Order(StringComparer.Ordinal))
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);
			content.Append(reader.ReadToEnd());
		}

		return content.ToString();
	}

	private async Task<string> WaitForLogsAsync(string expected)
	{
		var deadline = DateTime.UtcNow + PollTimeout;
		while (true)
		{
			var content = ReadLogs();
			if (content.Contains(expected, StringComparison.Ordinal) || DateTime.UtcNow >= deadline)
			{
				return content;
			}

			await Task.Delay(25, TestContext.Current.CancellationToken);
		}
	}
}
