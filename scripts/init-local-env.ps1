param([string]$EnvFile = "$PSScriptRoot/../.env")

$ErrorActionPreference = 'Stop'
$lines = @()
if (Test-Path -LiteralPath $EnvFile) { $lines = @(Get-Content -LiteralPath $EnvFile) }
$defaults = [ordered]@{
    MYSQL_ROOT_PASSWORD = ''
    BOOKING_DB_PASSWORD = ''
    PRICING_DB_PASSWORD = ''
    JWT_KEY = ''
    ADMIN_EMAIL = 'admin@example.test'
    ADMIN_PASSWORD = ''
    SEPAY_BANK = 'Sacombank'
    SEPAY_ACCOUNT_NUMBER = '0000000000'
    SEPAY_WEBHOOK_KEY = ''
}
foreach ($name in @($defaults.Keys)) {
    if (!$defaults[$name]) {
        $bytes = [byte[]]::new(32)
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $defaults[$name] = [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
    }
    $position = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match ('^' + [regex]::Escape($name) + '=')) { $position = $index; break }
    }
    if ($position -lt 0) { $lines += "$name=$($defaults[$name])" }
    elseif (($lines[$position] -split '=', 2)[1].Trim().Trim('"').Trim("'") -eq '') {
        $lines[$position] = "$name=$($defaults[$name])"
    }
}
Set-Content -LiteralPath $EnvFile -Value $lines -Encoding utf8
Write-Output "Local Docker configuration is ready in $EnvFile."
Write-Output 'New demo configurations use bank account 0000000000. Read the environment file locally for your admin credentials.'
