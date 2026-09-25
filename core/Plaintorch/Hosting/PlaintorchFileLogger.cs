using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// A small daily-rolling file logger for the background core, whose console is either invisible (desktop shell) or
/// captured by a service manager. One file per UTC day under the per-user profile's <c>logs</c> folder, with the
/// oldest files pruned past a retention count.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately minimal: a logger only formats its line and enqueues it, and a single drain task writes every line it
/// finds waiting, then flushes once, so logging never blocks the caller and a burst costs one flush. There is no
/// structured output. Filtering is left to the logging framework (see the <c>Logging:PlaintorchFile</c> section in
/// <c>appsettings.json</c>).
/// </para>
/// <para>
/// The sink never takes the host down and never stops on its own. A failed open leaves the queued lines waiting; a
/// failed write or flush loses the lines of that attempt. Either way the file is closed, the failure is reported once on
/// standard error, and the drain retries with a growing backoff. Only disposal ends the drain.
/// </para>
/// <para>
/// The queue is bounded to <see cref="QueueCapacity"/> lines and drops the oldest beyond that, so a stalled disk
/// costs a bounded amount of memory. Lines lost to a full queue or to a failed write are reported as one warning line,
/// written as soon as the file is writable again.
/// </para>
/// <para>
/// Register the provider through a factory (a container-owned singleton), never as an instance: the host then disposes
/// it on shutdown, and <see cref="Dispose"/> completes the queue and waits a bounded time for the tail to reach the disk.
/// </para>
/// </remarks>
[ProviderAlias("PlaintorchFile")]
public sealed class PlaintorchFileLoggerProvider : ILoggerProvider
{
	/// <summary>
	/// The number of lines the queue holds before the oldest waiting lines are dropped.
	/// </summary>
	public const int QueueCapacity = 10_000;

	private const int RetainedFiles = 14;

	private static readonly string ProviderCategory = typeof(PlaintorchFileLoggerProvider).FullName!;
	private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan LastRetryDelay = TimeSpan.FromSeconds(5);
	private static readonly Lock DirectAppendGate = new();

	private readonly string _directory;
	private readonly Channel<string> _lines;
	private readonly ConcurrentDictionary<string, PlaintorchFileLogger> _loggers = new(StringComparer.Ordinal);
	private readonly CancellationTokenSource _stopping = new();
	private readonly Task _drain;
	private StreamWriter? _file;
	private DateTime _fileDay;
	private bool _failing;
	private long _dropped;
	private int _disposed;

	/// <summary>
	/// Initializes the provider over a log directory and starts its drain. The directory is created on the first write
	/// when missing, so an unwritable location never fails the host that builds the provider.
	/// </summary>
	/// <param name="directory">The directory that receives the daily log files.</param>
	public PlaintorchFileLoggerProvider(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		_directory = directory;
		_lines = Channel.CreateBounded<string>(
			new BoundedChannelOptions(QueueCapacity)
			{
				SingleReader = true,
				FullMode = BoundedChannelFullMode.DropOldest,
			},
			_ => Interlocked.Increment(ref _dropped));
		_drain = Task.Run(DrainAsync);
	}

	/// <inheritdoc />
	public ILogger CreateLogger(string categoryName)
	{
		return _loggers.GetOrAdd(categoryName, name => new PlaintorchFileLogger(name, this));
	}

	/// <summary>
	/// Stops accepting lines and waits up to two seconds for the queued tail to be written and flushed. A drain backing
	/// off after a failure retries at once; if that retry fails too, the tail is abandoned. Idempotent.
	/// </summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_lines.Writer.TryComplete();
		_stopping.Cancel();
		WaitForDrain();
	}

	/// <summary>
	/// Writes one entry that bypasses the logging filters, for process-level failures that may be reported while the
	/// host owning this provider is running or after it was torn down. While the provider is live the entry is queued
	/// like any logged line (and flushed with its batch); once the provider is disposed and its drain has stopped, it is
	/// appended straight to the day file through <see cref="AppendDirect"/>.
	/// </summary>
	/// <param name="level">The entry's level.</param>
	/// <param name="category">The category printed on the line.</param>
	/// <param name="message">The message.</param>
	/// <param name="exception">An exception whose full text (stack trace included) follows the message, if any.</param>
	public void Append(LogLevel level, string category, string message, Exception? exception = null)
	{
		var line = Format(level, category, message, exception);
		if (_lines.Writer.TryWrite(line))
		{
			return;
		}

		if (WaitForDrain())
		{
			AppendLine(_directory, line);
		}
	}

	/// <summary>
	/// Appends one entry, in the sink's line format, synchronously to the current day file of a log directory, without a
	/// provider. Meant for failures reported while no provider drains that directory (before a host is built, or after
	/// it was disposed): two writers appending to one file through separate handles overwrite each other's lines. Never
	/// throws for an I/O failure, which is reported on standard error instead.
	/// </summary>
	/// <param name="directory">The directory that receives the daily log files.</param>
	/// <param name="level">The entry's level.</param>
	/// <param name="category">The category printed on the line.</param>
	/// <param name="message">The message.</param>
	/// <param name="exception">An exception whose full text (stack trace included) follows the message, if any.</param>
	public static void AppendDirect(string directory, LogLevel level, string category, string message, Exception? exception = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		AppendLine(directory, Format(level, category, message, exception));
	}

	internal void Enqueue(string line)
	{
		_lines.Writer.TryWrite(line);
	}

	/// <summary>
	/// Formats one entry as the sink writes it: an ISO-8601 UTC timestamp, the abbreviated level, the category, the
	/// message, and the exception's full text on the following lines.
	/// </summary>
	private static string Format(LogLevel level, string category, string message, Exception? exception)
	{
		var builder = new StringBuilder(160)
			.Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
			.Append(" [").Append(Abbreviate(level)).Append("] ")
			.Append(category).Append(": ")
			.Append(message);
		if (exception is not null)
		{
			builder.AppendLine().Append(exception);
		}

		return builder.ToString();
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

	/// <summary>
	/// Drains the queue until it is completed and empty: one write attempt per wake-up, a growing backoff after a failed
	/// attempt, and, once disposal was requested, no further retries after a failed one.
	/// </summary>
	private async Task DrainAsync()
	{
		var reader = _lines.Reader;
		var retryDelay = FirstRetryDelay;
		try
		{
			while (await reader.WaitToReadAsync().ConfigureAwait(false))
			{
				if (await TryWriteWaitingAsync(reader).ConfigureAwait(false))
				{
					retryDelay = FirstRetryDelay;
					continue;
				}

				if (_stopping.IsCancellationRequested)
				{
					break;
				}

				await Task.Delay(retryDelay, _stopping.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
				retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, LastRetryDelay.Ticks));
			}

			ReportUnwritten(reader);
		}
		catch (Exception exception)
		{
			ReportOnConsole($"PLAINTORCH file log stopped writing to {_directory}: {exception}");
		}
		finally
		{
			CloseFile();
		}
	}

	/// <summary>
	/// Writes every line waiting in the queue, preceded by a warning for lines lost since the last successful attempt,
	/// then flushes. A failure to open the file leaves the waiting lines queued; a failure after that counts the lines of
	/// this attempt as lost. Either way the file is closed so the next attempt reopens it.
	/// </summary>
	/// <returns><see langword="true"/> when the attempt was flushed to disk.</returns>
	private async Task<bool> TryWriteWaitingAsync(ChannelReader<string> reader)
	{
		var attempted = 0L;
		try
		{
			var file = EnsureFile();
			attempted = Interlocked.Exchange(ref _dropped, 0);
			if (attempted > 0)
			{
				var warning = Format(LogLevel.Warning, ProviderCategory, $"{attempted} log lines dropped (the log queue was full or the log file was not writable).", null);
				await file.WriteLineAsync(warning).ConfigureAwait(false);
			}

			while (reader.TryRead(out var line))
			{
				attempted++;
				file = EnsureFile();
				await file.WriteLineAsync(line).ConfigureAwait(false);
			}

			await file.FlushAsync().ConfigureAwait(false);
			_failing = false;
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Interlocked.Add(ref _dropped, attempted);
			CloseFile();
			if (!_failing)
			{
				_failing = true;
				ReportOnConsole($"PLAINTORCH file log cannot write to {_directory} ({exception.GetType().Name}: {exception.Message}); retrying.");
			}

			return false;
		}
	}

	/// <summary>
	/// Returns the open file for the current UTC day, closing the previous day's file (which flushes its tail) and opening
	/// and pruning on a change of day or after a failure closed the file.
	/// </summary>
	private StreamWriter EnsureFile()
	{
		var today = DateTime.UtcNow.Date;
		if (_file is not null && today == _fileDay)
		{
			return _file;
		}

		if (_file is not null)
		{
			var previous = _file;
			_file = null;
			previous.Dispose();
		}

		_file = OpenDayFile(_directory, today);
		_fileDay = today;
		Prune();
		return _file;
	}

	/// <summary>
	/// Closes the file after a failure or at the end of the drain, discarding whatever fails to flush.
	/// </summary>
	private void CloseFile()
	{
		var file = _file;
		_file = null;
		try
		{
			file?.Dispose();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	/// <summary>
	/// Reports on standard error the lines the drain ends without writing: those lost to a failure it never recovered
	/// from, and a tail it gave up on because disposal was requested and the prompt retry failed too.
	/// </summary>
	private void ReportUnwritten(ChannelReader<string> reader)
	{
		var lost = Interlocked.Exchange(ref _dropped, 0);
		while (reader.TryRead(out _))
		{
			lost++;
		}

		if (lost > 0)
		{
			ReportOnConsole($"PLAINTORCH file log stopped with {lost} log lines not written to {_directory}.");
		}
	}

	/// <summary>
	/// Waits a bounded time for the drain to finish.
	/// </summary>
	/// <returns><see langword="true"/> when the drain has finished.</returns>
	private bool WaitForDrain()
	{
		try
		{
			return _drain.Wait(DrainTimeout);
		}
		catch (AggregateException)
		{
			return true;
		}
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

	private static StreamWriter OpenDayFile(string directory, DateTime day)
	{
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, $"plaintorch-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
		var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
		return new StreamWriter(stream, new UTF8Encoding(false));
	}

	private static void AppendLine(string directory, string line)
	{
		lock (DirectAppendGate)
		{
			try
			{
				using var file = OpenDayFile(directory, DateTime.UtcNow.Date);
				file.WriteLine(line);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				ReportOnConsole($"PLAINTORCH file log could not append to {directory} ({exception.GetType().Name}: {exception.Message}).");
			}
		}
	}

	/// <summary>
	/// Reports a problem of the sink itself on standard error, which a desktop shell collects and a service manager
	/// captures; standard output is never used, since in spawn mode it carries the status stream.
	/// </summary>
	private static void ReportOnConsole(string message)
	{
		try
		{
			Console.Error.WriteLine(message);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
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

			provider.Enqueue(Format(logLevel, category, formatter(state, exception), exception));
		}
	}
}
