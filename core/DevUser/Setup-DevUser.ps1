#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates the static PLAINTORCHDEV local account used by interactive/manual `serve` during development and testing.

.DESCRIPTION
    Interactive `serve` relaunches itself to run as PLAINTORCHDEV so dev/test sessions never touch the real user's
    ~/.pleiades environment. End users run PLAINTORCH through the installed service runner and never need this account.

    Run this script once, elevated. It is idempotent. The password is a documented, dev-only value for an unprivileged
    local account; override it by setting PLAINTORCHDEV_PASSWORD before running (and set the same variable for `serve`).
#>
$ErrorActionPreference = 'Stop'

$UserName = 'PLAINTORCHDEV'
$Password = if ($env:PLAINTORCHDEV_PASSWORD) { $env:PLAINTORCHDEV_PASSWORD } else { 'Plaintorch-Dev-Local-1' }
$Secure = ConvertTo-SecureString $Password -AsPlainText -Force

if (Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue) {
    Set-LocalUser -Name $UserName -Password $Secure
    Write-Host "Updated existing account '$UserName'."
}
else {
    New-LocalUser -Name $UserName -Password $Secure `
        -FullName 'PLAINTORCH Dev' `
        -Description 'PLAINTORCH development/testing account' `
        -PasswordNeverExpires `
        -UserMayNotChangePassword | Out-Null
    Write-Host "Created account '$UserName'."
}

# Grant "Log on as a batch job" (SeBatchLogonRight) so `serve` can relaunch as this account via credentials.
$sid = (Get-LocalUser -Name $UserName).SID.Value
$export = New-TemporaryFile
$import = New-TemporaryFile
try {
    secedit /export /cfg $export.FullName /areas USER_RIGHTS | Out-Null
    $line = (Get-Content $export.FullName | Where-Object { $_ -match '^SeBatchLogonRight' })
    if ($line -and ($line -notmatch [regex]::Escape($sid))) {
        $newLine = "$line,*$sid"
    }
    elseif (-not $line) {
        $newLine = "SeBatchLogonRight = *$sid"
    }
    if ($newLine) {
        @(
            '[Unicode]'
            'Unicode=yes'
            '[Version]'
            'signature="$CHICAGO$"'
            'Revision=1'
            '[Privilege Rights]'
            $newLine
        ) | Set-Content -Path $import.FullName -Encoding Unicode
        secedit /configure /db "$env:TEMP\plaintorchdev.sdb" /cfg $import.FullName /areas USER_RIGHTS | Out-Null
        Write-Host "Granted SeBatchLogonRight to '$UserName'."
    }
    else {
        Write-Host "'$UserName' already has SeBatchLogonRight."
    }
}
finally {
    Remove-Item $export.FullName, $import.FullName -ErrorAction SilentlyContinue
}

Write-Host "Done. Interactive 'serve' will now relaunch as '$UserName'. Use 'serve --ephemeral' for throwaway/test environments."
