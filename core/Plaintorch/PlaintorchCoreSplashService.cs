using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Pleiades.Plaintorch;

/// <summary>
/// Manages the optional interactive splash popup shown while the PLAINTORCH core starts.
/// </summary>
public sealed class PlaintorchCoreSplashService(
	PlaintorchUserLayout userLayout,
	ILogger<PlaintorchCoreSplashService> logger) : IAsyncDisposable
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
	};

	private readonly SemaphoreSlim gate = new(1, 1);
	private int disposalState;
	private Process? splashProcess;

	/// <summary>
	/// Shows or updates the loading popup with a new status message.
	/// </summary>
	public Task ShowLoadingAsync(string message, CancellationToken cancellationToken = default)
	{
		return UpdateStateAsync(CoreSplashMode.Loading, message, cancellationToken);
	}

	/// <summary>
	/// Shows the error popup state with the configured error artwork when available.
	/// </summary>
	public Task ShowErrorAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default)
	{
		var fullMessage = exception is null
			? message
			: $"{message}{Environment.NewLine}{Environment.NewLine}{exception.Message}";
		return UpdateStateAsync(CoreSplashMode.Error, fullMessage, cancellationToken);
	}

	/// <summary>
	/// Closes the popup when it is active.
	/// </summary>
	public async Task CloseAsync(CancellationToken cancellationToken = default)
	{
		if (!IsSupported() || Volatile.Read(ref disposalState) != 0)
		{
			return;
		}

		await CloseCoreAsync(cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref disposalState, 1) != 0)
		{
			return;
		}

		await CloseCoreAsync(CancellationToken.None);
		gate.Dispose();
		Volatile.Write(ref disposalState, 2);
	}

	private async Task CloseCoreAsync(CancellationToken cancellationToken)
	{
		var enteredGate = false;
		try
		{
			await gate.WaitAsync(cancellationToken);
			enteredGate = true;

			if (splashProcess is null)
			{
				return;
			}

			await WriteStateAsync(new CoreSplashState
			{
				CloseRequested = true,
			}, cancellationToken);

			if (!splashProcess.HasExited)
			{
				await splashProcess.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
			}
		}
		catch (TimeoutException)
		{
			TryKillSplashProcess();
		}
		catch (ObjectDisposedException)
		{
			return;
		}
		catch (InvalidOperationException)
		{
		}
		finally
		{
			splashProcess?.Dispose();
			splashProcess = null;
			TryDeleteStateArtifacts();
			if (enteredGate)
			{
				gate.Release();
			}
		}
	}

	private async Task UpdateStateAsync(CoreSplashMode mode, string message, CancellationToken cancellationToken)
	{
		if (!IsSupported() || Volatile.Read(ref disposalState) != 0)
		{
			return;
		}

		var enteredGate = false;
		try
		{
			await gate.WaitAsync(cancellationToken);
			enteredGate = true;

			userLayout.EnsureExists();
			Directory.CreateDirectory(userLayout.SplashRootPath);
			await EnsureScriptAsync(cancellationToken);
			EnsureSplashProcessStarted();

			await WriteStateAsync(new CoreSplashState
			{
				Mode = mode.ToString(),
				Message = message,
				ImagePath = ResolveImagePath(mode),
				Theme = CreateTheme(mode),
			}, cancellationToken);
		}
		catch (ObjectDisposedException)
		{
			return;
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Unable to update the PLAINTORCH core splash popup.");
		}
		finally
		{
			if (enteredGate)
			{
				gate.Release();
			}
		}
	}

	private static bool IsSupported()
	{
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			&& Environment.UserInteractive
			&& !WindowsServiceHelpers.IsWindowsService();
	}

	private async Task EnsureScriptAsync(CancellationToken cancellationToken)
	{
		await File.WriteAllTextAsync(userLayout.SplashScriptPath, SplashScript, new UTF8Encoding(false), cancellationToken);
	}

	private void EnsureSplashProcessStarted()
	{
		if (splashProcess is not null && !splashProcess.HasExited)
		{
			return;
		}

		var processStartInfo = new ProcessStartInfo
		{
			FileName = ResolvePowerShellExecutablePath(),
			Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Sta -File \"{userLayout.SplashScriptPath}\" -StatePath \"{userLayout.SplashStatePath}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		splashProcess = Process.Start(processStartInfo)
			?? throw new InvalidOperationException("Unable to start the PLAINTORCH splash popup process.");
	}

	private async Task WriteStateAsync(CoreSplashState state, CancellationToken cancellationToken)
	{
		var json = JsonSerializer.Serialize(state, SerializerOptions);
		await File.WriteAllTextAsync(userLayout.SplashStatePath, json + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
	}

	private string? ResolveImagePath(CoreSplashMode mode)
	{
		var assetPath = mode == CoreSplashMode.Error
			? GetErrorImageAssetPath()
			: GetLoadingImageAssetPath();

		return File.Exists(assetPath) ? assetPath : null;
	}

	private void TryKillSplashProcess()
	{
		try
		{
			if (splashProcess is { HasExited: false })
			{
				splashProcess.Kill(entireProcessTree: true);
			}
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "Unable to force-close the PLAINTORCH splash popup process.");
		}
	}

	private void TryDeleteStateArtifacts()
	{
		try
		{
			if (File.Exists(userLayout.SplashStatePath))
			{
				File.Delete(userLayout.SplashStatePath);
			}
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "Unable to remove the PLAINTORCH splash popup state file.");
		}
	}

	private static string ResolvePowerShellExecutablePath()
	{
		var windowsPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
		return File.Exists(windowsPowerShell) ? windowsPowerShell : "powershell.exe";
	}

	private static string GetLoadingImageAssetPath()
	{
		return Path.Combine(AppContext.BaseDirectory, "Assets", "CoreSplash", "loading.png");
	}

	private static string GetErrorImageAssetPath()
	{
		return Path.Combine(AppContext.BaseDirectory, "Assets", "CoreSplash", "error.png");
	}

	private static CoreSplashTheme CreateTheme(CoreSplashMode mode)
	{
		return mode switch
		{
			CoreSplashMode.Error => new CoreSplashTheme
			{
				WindowWidth = 943,
				WindowHeight = 405,
				Topmost = true,
				Image = new CoreSplashImageStyle
				{
					Stretch = "UniformToFill",
					HorizontalAlignment = "Stretch",
					VerticalAlignment = "Stretch",
				},
				MessageContainer = new CoreSplashContainerStyle
				{
					HorizontalAlignment = "Left",
					VerticalAlignment = "Top",
					Margin = "32,24,32,24",
					Padding = "0,0,0,0",
					BackgroundColor = null,
					BorderBrush = null,
					BorderThickness = "0",
					CornerRadius = "0",
				},
				MessageText = new CoreSplashTextStyle
				{
					ForegroundColor = "#FFF7DEE1",
					FontFamily = "Space Grotesk",
					FontSize = 16,
					FontWeight = "Light",
					TextAlignment = "Left",
					TextWrapping = true,
					MaxWidth = 520,
				},
			},

			_ => new CoreSplashTheme
			{
				WindowWidth = 943,
				WindowHeight = 405,
				Topmost = true,
				Image = new CoreSplashImageStyle
				{
					Stretch = "UniformToFill",
					HorizontalAlignment = "Stretch",
					VerticalAlignment = "Stretch",
				},
				MessageContainer = new CoreSplashContainerStyle
				{
					HorizontalAlignment = "Left",
					VerticalAlignment = "Top",
					Margin = "32,24,32,24",
					Padding = "0,0,0,0",
					BackgroundColor = null,
					BorderBrush = null,
					BorderThickness = "0",
					CornerRadius = "0",
				},
				MessageText = new CoreSplashTextStyle
				{
					ForegroundColor = "#FFF6F1EA",
					FontFamily = "Space Grotesk",
					FontSize = 14,
					FontWeight = "Normal",
					TextAlignment = "Left",
					TextWrapping = true,
					MaxWidth = 520,
				},
			},
		};
	}

	private sealed class CoreSplashState
	{
		public string Mode { get; init; } = nameof(CoreSplashMode.Loading);
		public string? Message { get; init; }
		public string? ImagePath { get; init; }
		public bool CloseRequested { get; init; }
		public CoreSplashTheme Theme { get; init; } = new();
	}

	private sealed class CoreSplashTheme
	{
		public int WindowWidth { get; init; } = 943;
		public int WindowHeight { get; init; } = 405;
		public bool Topmost { get; init; } = true;
		public CoreSplashImageStyle Image { get; init; } = new();
		public CoreSplashContainerStyle MessageContainer { get; init; } = new();
		public CoreSplashTextStyle MessageText { get; init; } = new();
	}

	private sealed class CoreSplashImageStyle
	{
		public string Stretch { get; init; } = "Uniform";
		public string HorizontalAlignment { get; init; } = "Stretch";
		public string VerticalAlignment { get; init; } = "Stretch";
		public string Margin { get; init; } = "0";
	}

	private sealed class CoreSplashContainerStyle
	{
		public string HorizontalAlignment { get; init; } = "Stretch";
		public string VerticalAlignment { get; init; } = "Top";
		public string Margin { get; init; } = "0";
		public string Padding { get; init; } = "0";
		public string? BackgroundColor { get; init; }
		public string? BorderBrush { get; init; }
		public string BorderThickness { get; init; } = "0";
		public string CornerRadius { get; init; } = "0";
	}

	private sealed class CoreSplashTextStyle
	{
		public string? ForegroundColor { get; init; } = "#FFFFFFFF";
		public string FontFamily { get; init; } = "Segoe UI";
		public double FontSize { get; init; } = 16;
		public string FontWeight { get; init; } = "Normal";
		public string TextAlignment { get; init; } = "Left";
		public bool TextWrapping { get; init; } = true;
		public double? MaxWidth { get; init; }
	}

	private enum CoreSplashMode
	{
		Loading,
		Error,
	}

	private const string SplashScript = """
param(
    [Parameter(Mandatory = $true)]
    [string]$StatePath
)

	$ErrorActionPreference = 'Stop'
	$SplashLogPath = "$StatePath.error.log"

	trap {
		try {
			$_ | Out-String | Set-Content -LiteralPath $SplashLogPath -Encoding UTF8
		}
		catch {
		}

		break
	}

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

function Get-SplashState {
    if (-not (Test-Path -LiteralPath $StatePath)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Set-SplashImage {
    param(
        [System.Windows.Controls.Image]$Image,
        [string]$ImagePath
    )

    if ([string]::IsNullOrWhiteSpace($ImagePath) -or -not (Test-Path -LiteralPath $ImagePath)) {
        $Image.Source = $null
        return
    }

    $bitmap = [System.Windows.Media.Imaging.BitmapImage]::new()
    $bitmap.BeginInit()
    $bitmap.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $bitmap.UriSource = [Uri]::new((Resolve-Path -LiteralPath $ImagePath))
    $bitmap.EndInit()
    $bitmap.Freeze()
    $Image.Source = $bitmap
}

function ConvertTo-Thickness {
	param([string]$Value)

	if ([string]::IsNullOrWhiteSpace($Value)) {
		return [System.Windows.Thickness]::new(0)
	}

	$parts = @(
		$Value.Split(',') |
			ForEach-Object { $_.Trim() } |
			Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
	)
	switch ($parts.Length) {
		1 { return [System.Windows.Thickness]::new([double]$parts[0]) }
		2 { return [System.Windows.Thickness]::new([double]$parts[0], [double]$parts[1], [double]$parts[0], [double]$parts[1]) }
		4 { return [System.Windows.Thickness]::new([double]$parts[0], [double]$parts[1], [double]$parts[2], [double]$parts[3]) }
		default { return [System.Windows.Thickness]::new(0) }
	}
}

function ConvertTo-CornerRadius {
	param([string]$Value)

	if ([string]::IsNullOrWhiteSpace($Value)) {
		return [System.Windows.CornerRadius]::new(0)
	}

	$parts = @(
		$Value.Split(',') |
			ForEach-Object { $_.Trim() } |
			Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
	)
	switch ($parts.Length) {
		1 { return [System.Windows.CornerRadius]::new([double]$parts[0]) }
		4 { return [System.Windows.CornerRadius]::new([double]$parts[0], [double]$parts[1], [double]$parts[2], [double]$parts[3]) }
		default { return [System.Windows.CornerRadius]::new(0) }
	}
}

function Resolve-Brush {
	param([string]$Color)

	if ([string]::IsNullOrWhiteSpace($Color)) {
		return $null
	}

	return [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString($Color))
}

function Resolve-HorizontalAlignment {
	param([string]$Value)

	return [System.Windows.HorizontalAlignment]::$Value
}

function Resolve-VerticalAlignment {
	param([string]$Value)

	return [System.Windows.VerticalAlignment]::$Value
}

function Resolve-TextAlignment {
	param([string]$Value)

	return [System.Windows.TextAlignment]::$Value
}

function Resolve-FontWeight {
	param([string]$Value)

	return [System.Windows.FontWeights]::$Value
}

function Resolve-Stretch {
	param([string]$Value)

	return [System.Windows.Media.Stretch]::$Value
}

function Apply-ImageStyle {
	param(
		[System.Windows.Controls.Image]$Image,
		[object]$Style
	)

	if ($null -eq $Style) {
		return
	}

	$Image.Stretch = Resolve-Stretch $Style.Stretch
	$Image.HorizontalAlignment = Resolve-HorizontalAlignment $Style.HorizontalAlignment
	$Image.VerticalAlignment = Resolve-VerticalAlignment $Style.VerticalAlignment
	$Image.Margin = ConvertTo-Thickness $Style.Margin
}

function Apply-ContainerStyle {
	param(
		[System.Windows.Controls.Border]$Container,
		[object]$Style
	)

	if ($null -eq $Style) {
		return
	}

	$Container.HorizontalAlignment = Resolve-HorizontalAlignment $Style.HorizontalAlignment
	$Container.VerticalAlignment = Resolve-VerticalAlignment $Style.VerticalAlignment
	$Container.Margin = ConvertTo-Thickness $Style.Margin
	$Container.Padding = ConvertTo-Thickness $Style.Padding
	$Container.BorderThickness = ConvertTo-Thickness $Style.BorderThickness
	$Container.CornerRadius = ConvertTo-CornerRadius $Style.CornerRadius
	$Container.Background = Resolve-Brush $Style.BackgroundColor
	$Container.BorderBrush = Resolve-Brush $Style.BorderBrush
}

function Apply-TextStyle {
	param(
		[System.Windows.Controls.TextBlock]$Text,
		[object]$Style
	)

	if ($null -eq $Style) {
		return
	}

	$Text.Foreground = Resolve-Brush $Style.ForegroundColor
	$Text.FontFamily = [System.Windows.Media.FontFamily]::new($Style.FontFamily)
	$Text.FontSize = [double]$Style.FontSize
	$Text.FontWeight = Resolve-FontWeight $Style.FontWeight
	$Text.TextAlignment = Resolve-TextAlignment $Style.TextAlignment
	$Text.TextWrapping = if ([bool]$Style.TextWrapping) { [System.Windows.TextWrapping]::Wrap } else { [System.Windows.TextWrapping]::NoWrap }

	if ($null -ne $Style.MaxWidth) {
		$Text.MaxWidth = [double]$Style.MaxWidth
	}
	else {
		$Text.ClearValue([System.Windows.FrameworkElement]::MaxWidthProperty)
	}
}

$initialState = Get-SplashState
if ($null -eq $initialState) {
    $initialState = [pscustomobject]@{
        Mode = 'Loading'
        Message = 'Starting PLAINTORCH core...'
        ImagePath = $null
        CloseRequested = $false
		Theme = [pscustomobject]@{
			WindowWidth = 943
			WindowHeight = 405
			Topmost = $true
			Image = [pscustomobject]@{
				Stretch = 'UniformToFill'
				HorizontalAlignment = 'Stretch'
				VerticalAlignment = 'Stretch'
				Margin = '0'
			}
			MessageContainer = [pscustomobject]@{
				HorizontalAlignment = 'Left'
				VerticalAlignment = 'Top'
				Margin = '32,24,32,24'
				Padding = '0'
				BackgroundColor = $null
				BorderBrush = $null
				BorderThickness = '0'
				CornerRadius = '0'
			}
			MessageText = [pscustomobject]@{
				ForegroundColor = '#FFF6F1EA'
				FontFamily = 'Aptos, Segoe UI, sans-serif'
				FontSize = 22
				FontWeight = 'SemiBold'
				TextAlignment = 'Left'
				TextWrapping = $true
				MaxWidth = 520
			}
		}
    }
}

$window = [System.Windows.Window]::new()
$window.Title = 'PLAINTORCH Core'
$window.Width = [Math]::Max([int]$initialState.Theme.WindowWidth, 320)
$window.Height = [Math]::Max([int]$initialState.Theme.WindowHeight, 180)
$window.WindowStartupLocation = [System.Windows.WindowStartupLocation]::CenterScreen
$window.WindowStyle = [System.Windows.WindowStyle]::None
$window.ResizeMode = [System.Windows.ResizeMode]::NoResize
$window.Topmost = [bool]$initialState.Theme.Topmost
$window.ShowInTaskbar = $false
$window.AllowsTransparency = $true
$window.Background = [System.Windows.Media.Brushes]::Transparent

$grid = [System.Windows.Controls.Grid]::new()
$grid.SnapsToDevicePixels = $true

$image = [System.Windows.Controls.Image]::new()
$grid.Children.Add($image) | Out-Null

$overlay = [System.Windows.Controls.Border]::new()

$text = [System.Windows.Controls.TextBlock]::new()
$overlay.Child = $text
$grid.Children.Add($overlay) | Out-Null

$window.Content = $grid

$window.Add_SourceInitialized({
    $window.Left = [Math]::Round(([System.Windows.SystemParameters]::PrimaryScreenWidth - $window.Width) / 2)
    $window.Top = [Math]::Round(([System.Windows.SystemParameters]::PrimaryScreenHeight - $window.Height) / 2)
})

function Apply-SplashState {
    param([object]$State)

    if ($null -eq $State) {
        return
    }

	if ($null -eq $State.Theme) {
		return
	}

	$window.Width = [Math]::Max([int]$State.Theme.WindowWidth, 320)
	$window.Height = [Math]::Max([int]$State.Theme.WindowHeight, 180)
	$window.Topmost = [bool]$State.Theme.Topmost
    $text.Text = if ([string]::IsNullOrWhiteSpace($State.Message)) { 'Starting PLAINTORCH core...' } else { [string]$State.Message }
    Set-SplashImage -Image $image -ImagePath $State.ImagePath
	Apply-ImageStyle -Image $image -Style $State.Theme.Image
	Apply-ContainerStyle -Container $overlay -Style $State.Theme.MessageContainer
	Apply-TextStyle -Text $text -Style $State.Theme.MessageText
}

Apply-SplashState $initialState

$timer = [System.Windows.Threading.DispatcherTimer]::new()
$timer.Interval = [TimeSpan]::FromMilliseconds(250)
$timer.add_Tick({
    $state = Get-SplashState
    if ($null -eq $state) {
        return
    }

    if ([bool]$state.CloseRequested) {
        $timer.Stop()
        $window.Close()
        return
    }

    Apply-SplashState $state
})

$timer.Start()
$window.ShowDialog() | Out-Null
""";
}
