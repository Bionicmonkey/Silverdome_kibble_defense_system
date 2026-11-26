# Deploy script for NewDetector
# Copies build output to C:\altcode\AnimalDetector\ for live testing
# Usage: powershell -ExecutionPolicy Bypass -File deploy.ps1

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = "C:\altcode\AnimalDetector"
$publishDir = Join-Path $projectRoot ".publish"

Write-Host "=== NewDetector Deploy ===" -ForegroundColor Cyan
Write-Host "Project root: $projectRoot"
Write-Host "Target: $target"
Write-Host ""

# Step 1: Build Release
Write-Host "[1/4] Building Release..." -ForegroundColor Yellow
dotnet build "$projectRoot\newdetector.csproj" --configuration Release --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Build succeeded." -ForegroundColor Green

# Step 2: Publish to staging directory (single file, framework-dependent)
Write-Host "[2/4] Publishing..." -ForegroundColor Yellow
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "$projectRoot\newdetector.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output "$publishDir" `
    --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Publish failed!" -ForegroundColor Red
    exit 1
}
Write-Host "  Published to $publishDir" -ForegroundColor Green

# Step 3: Create target directory and deploy files
Write-Host "[3/4] Deploying to $target..." -ForegroundColor Yellow
if (-not (Test-Path $target)) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Write-Host "  Created $target" -ForegroundColor Gray
}

# Copy published files
$publishedFiles = Get-ChildItem -Path $publishDir -File
foreach ($file in $publishedFiles) {
    Copy-Item $file.FullName -Destination $target -Force
    Write-Host "  Copied: $($file.Name)" -ForegroundColor Gray
}

# Copy config files from project root if they exist
$configFiles = @("system.json", "animals.json", "config.json")
foreach ($name in $configFiles) {
    $src = Join-Path $projectRoot $name
    if (Test-Path $src) {
        Copy-Item $src -Destination $target -Force
        Write-Host "  Copied: $name" -ForegroundColor Gray
    } else {
        Write-Host "  Skip (missing): $name" -ForegroundColor DarkGray
    }
}

# Step 4: Verify deployment
Write-Host "[4/4] Verifying..." -ForegroundColor Yellow
$exePath = Join-Path $target "newdetector.exe"
if (Test-Path $exePath) {
    Write-Host "" -ForegroundColor White
    Write-Host "=== Deploy succeeded! ===" -ForegroundColor Green
    Write-Host "Binary: $exePath" -ForegroundColor Gray
    Write-Host "Run: cd $target && .\newdetector.exe" -ForegroundColor Gray
    
    $files = Get-ChildItem $target -File | Sort-Object Name
    Write-Host ""
    Write-Host "Deployed files ($($files.Count)):" -ForegroundColor Cyan
    $files | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length/1KB, 1)) KB)" -ForegroundColor Gray }
} else {
    Write-Host "ERROR: newdetector.exe not found in target!" -ForegroundColor Red
    exit 1
}
