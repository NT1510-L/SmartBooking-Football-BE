param(
    [string]$FrontendPath = "$PSScriptRoot/../../SmartBooking-Football-FE",
    [string]$OutputFile = "$PSScriptRoot/../.artifacts/SmartBooking-Submission.zip",
    [string]$PresentationPath
)

$ErrorActionPreference = 'Stop'
$backend = (Resolve-Path -LiteralPath "$PSScriptRoot/..").Path
$frontend = (Resolve-Path -LiteralPath $FrontendPath).Path
$output = [IO.Path]::GetFullPath($OutputFile)
if (Test-Path -LiteralPath $output) { throw 'Choose a new OutputFile; the destination already exists.' }
if ($PresentationPath -and [IO.Path]::GetExtension($PresentationPath) -ne '.pptx') {
    throw 'PresentationPath must point to a PowerPoint .pptx file.'
}
if ($PresentationPath) { $PresentationPath = (Resolve-Path -LiteralPath $PresentationPath).Path }

$sources = @(
    @{ Name = 'SmartBooking-Football-BE'; Path = $backend },
    @{ Name = 'SmartBooking-Football-FE'; Path = $frontend }
)
foreach ($source in $sources) {
    $status = & git -C $source.Path status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the source repository.' }
    if ($status) { throw "Commit the changes in $($source.Name) before packaging." }
    $source.Commit = & git -C $source.Path rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the source revision.' }
}

$parent = [IO.Path]::GetDirectoryName($output)
New-Item -ItemType Directory -Path $parent -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryArchives = @()
$complete = $false
$bundle = $null
try {
    $bundle = [IO.Compression.ZipFile]::Open($output, [IO.Compression.ZipArchiveMode]::Create)
    foreach ($source in $sources) {
        $temporary = Join-Path $parent ([guid]::NewGuid().ToString('N') + '.zip')
        $temporaryArchives += $temporary
        & git -C $source.Path archive --format=zip "--output=$temporary" HEAD
        if ($LASTEXITCODE -ne 0) { throw 'git archive failed.' }
        $archive = [IO.Compression.ZipFile]::OpenRead($temporary)
        try {
            foreach ($entry in $archive.Entries) {
                if ($entry.FullName.EndsWith('/')) { continue }
                $segments = $entry.FullName -split '/'
                foreach ($segment in $segments) {
                    if ($segment -match '^(\.git|\.vs|node_modules|bin|obj|dist|\.artifacts)$' -or
                        ($segment -match '^\.env($|\.)' -and $segment -ne '.env.example')) {
                        throw "Excluded content is tracked by Git: $($entry.FullName)"
                    }
                }
                $target = $bundle.CreateEntry("$($source.Name)/$($entry.FullName)")
                $inputStream = $entry.Open()
                $outputStream = $target.Open()
                try { $inputStream.CopyTo($outputStream) }
                finally { $inputStream.Dispose(); $outputStream.Dispose() }
            }
        } finally { $archive.Dispose() }
    }
    if ($PresentationPath) {
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $bundle, $PresentationPath, ('Presentation/' + [IO.Path]::GetFileName($PresentationPath)))
    }
    $manifest = $bundle.CreateEntry('source-revisions.json')
    $writer = [IO.StreamWriter]::new($manifest.Open())
    try {
        $writer.Write((@($sources | ForEach-Object {
            @{ repository = $_.Name; commit = $_.Commit }
        }) | ConvertTo-Json))
    } finally { $writer.Dispose() }
    $complete = $true
} finally {
    if ($bundle) { $bundle.Dispose() }
    foreach ($temporary in $temporaryArchives) {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
    if (!$complete -and (Test-Path -LiteralPath $output)) { Remove-Item -LiteralPath $output }
}
Write-Output "Submission package created: $output"
