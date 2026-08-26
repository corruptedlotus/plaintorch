[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding  = [System.Text.Encoding]::UTF8
$OutputEncoding           = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'

# 1. Handle the _dist junction
$distPath = "./obsidian/_dist"
$item = Get-Item $distPath -ErrorAction SilentlyContinue

$targetDist = $env:DIST_LOCATION
if ($null -eq $targetDist) {
	Write-Host "Distro location not set, no junction will be created." -ForegroundColor Magenta
}

if ($null -eq $env:VAULT_LOCATION) {
	Write-Host "Vault location not set, defaulting to _sandbox." -ForegroundColor Magenta
	$env:VAULT_LOCATION = "_sandbox"
}

if ($null -ne $targetDist){
	if ($null -ne $item -and $item.LinkType -notmatch 'Junction|SymbolicLink') {
		Write-Host "Removing standard directory at $distPath..." -ForegroundColor Yellow
		Remove-Item $distPath -Recurse -Force
	}
	
	if (-not (Test-Path $distPath)) {
		Write-Host "Creating junction to $targetDist..." -ForegroundColor Yellow
		New-Item -ItemType Junction -Path $distPath -Target $targetDist | Out-Null
	}
}

# 2. Define the jobs
# Note: We force color output because native executables often strip ANSI 
# colors when they detect they are running in a background job instead of a real TTY.
$jobs = @(
    Start-ThreadJob -Name "obsidian" -ScriptBlock {
        $env:FORCE_COLOR = 1
        Push-Location ./obsidian
        npm i
        npm run dev
    }
    
    Start-ThreadJob -Name "core" -ScriptBlock {
        $env:DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION = 1
        Push-Location ./core
        dotnet watch run -- serve --vault $env:VAULT_LOCATION
    }
)

$prefixes = @{
    "obsidian" = @{ Color = "Magenta"; Pad = 10 } # Magenta is PS's closest native color to purple
    "core"     = @{ Color = "Cyan";    Pad = 10 } # Cyan matches teal
}

Write-Host "`nStarting tasks. Press Ctrl+C to stop.`n" -ForegroundColor Green


# 3. Stream and prefix the output
try {
    while ($jobs.State -contains 'Running' -or $jobs.State -contains 'NotStarted') {
    foreach ($job in $jobs) {
        $rawOutput = Receive-Job -Job $job 
        
        if ($null -ne $rawOutput) {
            $config = $prefixes[$job.Name]
            $prefix = "$($job.Name.PadRight($config.Pad)) | "
            
            # Join any arrays and split on newlines to guarantee every line is separate
            $lines = ($rawOutput -join "`n") -split '\r?\n'
            
            for ($i = 0; $i -lt $lines.Count; $i++) {
                # If a chunk ended in a newline, -split creates a trailing empty string. 
                # We skip it here to avoid printing empty prefixes.
                if ($i -eq ($lines.Count - 1) -and $lines[$i] -eq '') {
                    continue
                }
                
                Write-Host $prefix -ForegroundColor $config.Color -NoNewline
                Write-Host $lines[$i]
            }
        }
    }
    Start-Sleep -Milliseconds 50
}
}
finally {
    # Ensure background processes are killed if you Ctrl+C the script
    Write-Host "`nCleaning up background jobs..." -ForegroundColor Yellow
    $jobs | Stop-Job -PassThru | Remove-Job
}