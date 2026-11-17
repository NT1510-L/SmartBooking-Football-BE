param(
    [string]$FrontendPath = "$PSScriptRoot/../../SmartBooking-Football-FE",
    [string]$ChromePath = 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath "$PSScriptRoot/..").Path
$frontend = (Resolve-Path -LiteralPath $FrontendPath).Path
if (!(Test-Path -LiteralPath $ChromePath)) { throw 'Pass -ChromePath with an installed Chrome/Chromium executable.' }
if (!(Test-Path -LiteralPath "$frontend/node_modules/playwright-core")) {
    throw 'Run npm ci in the frontend repository before the browser tests.'
}
$project = 'football-rehearsal-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$run = Join-Path $repo ".artifacts/qa/$project"
New-Item -ItemType Directory -Path $run -Force | Out-Null
$envFile = Join-Path $run '.env'
& "$PSScriptRoot/init-local-env.ps1" -EnvFile $envFile
$config = Get-Content -LiteralPath $envFile | ConvertFrom-StringData
$override = Join-Path $run 'compose.yaml'
$migrationPlaceholder = Join-Path $run '07-placeholder.sql'
Set-Content -LiteralPath $migrationPlaceholder -Value '-- QA applies migration 07 after creating legacy fixtures.' -Encoding utf8
$placeholderMount = $migrationPlaceholder.Replace('\', '/')
@"
services:
  mysql:
    volumes:
      - '$($placeholderMount):/docker-entrypoint-initdb.d/07-deposit-check-in.sql:ro'
  api:
    ports: !override
      - '127.0.0.1::8080'
  web:
    build: '$($frontend.Replace('\', '/'))'
    ports: !override
      - '127.0.0.1::80'
"@ | Set-Content -LiteralPath $override -Encoding utf8

$compose = @('compose', '--env-file', $envFile, '--project-name', $project,
    '--file', (Join-Path $repo 'compose.yaml'), '--file', $override)
function Invoke-Compose([string[]]$ComposeArguments) {
    & docker @compose @ComposeArguments
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed while running $($ComposeArguments[0])." }
}
function Invoke-QaSql([string]$Statement) {
    $result = $Statement | & docker @compose exec -T mysql sh -c 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysql -uroot booking_db --batch --skip-column-names'
    if ($LASTEXITCODE -ne 0) { throw 'The QA SQL operation failed.' }
    return $result
}

$previousEnvironment = @{}
$testEnvironment = @{
    SMARTBOOKING_FE_PATH = $frontend
    SMARTBOOKING_DEMO_ENV = $envFile
    SMARTBOOKING_DEMO_PROJECT = $project
    SMARTBOOKING_DEMO_OVERRIDE = $override
    SMARTBOOKING_TEST_ADMIN = $config.ADMIN_EMAIL
    SMARTBOOKING_TEST_ADMIN_PASSWORD = $config.ADMIN_PASSWORD
    SMARTBOOKING_TEST_WEBHOOK = $config.SEPAY_WEBHOOK_KEY
    CHROME_PATH = $ChromePath
}
Push-Location $repo
try {
    Invoke-Compose -ComposeArguments @('config', '--quiet')
    Invoke-Compose -ComposeArguments @('build')
    Invoke-Compose -ComposeArguments @('up', '-d', '--wait', 'mysql', 'redis', 'pricing')
    $legacySql = @'
SET @branchId = (SELECT id FROM branches ORDER BY id LIMIT 1);
SET @pitchId = (SELECT id FROM pitches WHERE branchId = @branchId ORDER BY id LIMIT 1);
SET @userId = (SELECT id FROM users WHERE role = 'User' ORDER BY id LIMIT 1);
SET @policyId = (SELECT id FROM pricing_db.pricingpolicies WHERE branchId = @branchId ORDER BY id LIMIT 1);
INSERT INTO pitches (branchId,pitchNumber,name,isActive) VALUES (@branchId,4,'Legacy fourth pitch',TRUE);
INSERT INTO bookings (pitchId,userId,startTime,status,priceVnd,pricingPolicyId)
VALUES (@pitchId,@userId,DATE_FORMAT(UTC_TIMESTAMP()+INTERVAL 10 DAY,'%Y-%m-%d 17:00:00'),'Confirmed',220000,@policyId);
SET @legacyId = LAST_INSERT_ID();
INSERT INTO payments (bookingId,code,amountVnd,status,paidAt)
VALUES (@legacyId,'SBABCDEF1234567890',220000,'Paid',UTC_TIMESTAMP());
INSERT INTO bookings (pitchId,userId,startTime,status,priceVnd,pricingPolicyId)
VALUES (@pitchId,@userId,DATE_FORMAT(UTC_TIMESTAMP()-INTERVAL 2 DAY,'%Y-%m-%d 17:00:00'),'Confirmed',220000,@policyId);
SET @pastId = LAST_INSERT_ID();
INSERT INTO payments (bookingId,code,amountVnd,status,paidAt)
VALUES (@pastId,'SBABCDEF1234567891',220000,'Paid',UTC_TIMESTAMP()-INTERVAL 2 DAY);
'@
    $null = Invoke-QaSql $legacySql
    $null = Invoke-QaSql (Get-Content -LiteralPath (Join-Path $repo 'database/07-deposit-check-in.sql') -Raw)
    $migrationCheck = Invoke-QaSql @'
SELECT COUNT(*) FROM payments WHERE code IN ('SBABCDEF1234567890','SBABCDEF1234567891') AND amountVnd=220000 AND balanceAmountVnd=0;
SELECT COUNT(*) FROM bookings b JOIN payments p ON p.bookingId=b.id WHERE p.code='SBABCDEF1234567891' AND b.status='Completed';
SELECT COUNT(*) FROM pitches WHERE pitchNumber=4 AND isActive=FALSE;
'@
    if (($migrationCheck -join ',') -ne '2,1,1') { throw 'Migration 07 did not preserve the legacy fixtures.' }
    Write-Output 'PASS migration 07 preserves full payments and old matches, and archives the fourth pitch.'
    Invoke-Compose -ComposeArguments @('up', '-d', 'api', 'worker', 'web')
    $apiAddress = (& docker @compose port api 8080 | Select-Object -First 1).Trim()
    $webAddress = (& docker @compose port web 80 | Select-Object -First 1).Trim()
    $testEnvironment.SMARTBOOKING_DEMO_API = "http://$apiAddress"
    $testEnvironment.SMARTBOOKING_DEMO_WEB = "http://$webAddress"
    $ready = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri "$($testEnvironment.SMARTBOOKING_DEMO_API)/health" -TimeoutSec 3
            if ($health.status -eq 'ok') { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'The QA API did not start.' }
    foreach ($name in $testEnvironment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $testEnvironment[$name], 'Process')
    }
    & node "$PSScriptRoot/regression.cjs"
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
    Write-Output "PASS full regression. QA frontend is $($testEnvironment.SMARTBOOKING_DEMO_WEB)."
} finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    if ($KeepRunning) {
        Write-Output "QA project $project is retained. Its configuration is in $run."
    } else {
        & docker @compose down --volumes
    }
    Pop-Location
}
