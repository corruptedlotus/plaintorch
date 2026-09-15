using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// A small daily-rolling file logger for the background core, whose console is either invisible (desktop shell) or
/// captured by a service manager. One file per UTC day under the per-user profile's <c>logs</c> folder, with the
/// oldest files pruned past a retention count.
/// </summary>
/// <remarks>
/// Deliberately minimal: a single writer task drains a channel so logging never blocks the caller, and there is no
/// structured output. Filtering is left to the logging framework (see the <c>Logging:PlaintorchFile</c> section in
/// <c>appsettings.json</c>).
/// </remarks>
[ProviderAlias("PlaintorchFile")]
public sealed class PlaintorchFileLoggerProvider : ILoggerProvider
{
	private const int RetainedFiles = 14;

	private readonly string _directory;
	private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
	private readonly ConcurrentDictionary<string, PlaintorchFileLogger> _loggers = new(StringComparer.Ordinal);
	private readonly Task _writer;

	/// <summary>
	/// Initializes the provider over a log directory, creating it when missing.
	/// </summary>
	/// <param name="directory">The directory that receives the daily log files.</param>
	public PlaintorchFileLoggerProvider(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		_directory = directory;
		Directory.CreateDirectory(_directory);
		_writer = Task.Run(DrainAsync);
	}

	/// <inheritdoc />
	public ILogger CreateLogger(string categoryName)
	{
		return _loggers.GetOrAdd(categoryName, name => new PlaintorchFileLogger(name, this));
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_lines.Writer.TryComplete();
		try
		{
			_writer.Wait(TimeSpan.FromSeconds(3));
		}
		catch (AggregateException)
		{
		}
	}

	internal void Enqueue(string line)
	{
		_lines.Writer.TryWrite(line);
	}

	private async Task DrainAsync()
	{
		StreamWriter? writer = null;
		string? openDay = null;

		try
		{
			await foreach (var line in _lines.Reader.ReadAllAsync())
			{
				var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
				if (writer is null || !string.Equals(day, openDay, StringComparison.Ordinal))
				{
					writer?.Dispose();
					writer = OpenDailyFile(day);
					openDay = day;
					Prune();
				}

				await writer.WriteLineAsync(line);
				if (_lines.Reader.Count == 0)
				{
					await writer.FlushAsync();
				}
			}
		}
		catch (Exception)
		{
			// The log sink must never take the host down; a failing disk simply loses log lines.
		}
		finally
		{
			writer?.Dispose();
		}
	}

	private StreamWriter OpenDailyFile(string day)
	{
		var path = Path.Combine(_directory, $"plaintorch-{day}.log");
		var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
		return new StreamWriter(stream, new UTF8Encoding(false));
	}

	private void Prune()
	{
		try
		{
			var stale = Directory.EnumerateFiles(_directory, "plaintorch-*.log")
				.OrderByDescending(path => path, StringComparer.Ordinal)
				.Skip(RetainedFiles);
			foreach (var path in stale)
			{
				File.Delete(path);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private sealed class PlaintorchFileLogger(string category, PlaintorchFileLoggerProvider provider) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (!IsEnabled(logLevel))
			{
				return;
			}

			var builder = new StringBuilder(160)
				.Append(DateTimeOffset.UtcNow.ToString("O"))
				.Append(" [").Append(Abbreviate(logLevel)).Append("] ")
				.Append(category).Append(": ")
				.Append(formatter(state, exception));
			if (exception is not null)
			{
				builder.AppendLine().Append(exception);
			}

			provider.Enqueue(builder.ToString());
		}

		private static string Abbreviate(LogLevel level) => level switch
		{
			LogLevel.Trace => "TRC",
			LogLevel.Debug => "DBG",
			LogLevel.Information => "INF",
			LogLevel.Warning => "WRN",
			LogLevel.Error => "ERR",
			LogLevel.Critical => "CRT",
			_ => "???",
		};
	}
}
