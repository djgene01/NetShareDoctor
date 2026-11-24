# Builds the release: runs the tests, publishes the self-contained exe, then makes the zip and the MSI in dist/.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content NetShareDoctor\NetShareDoctor.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$publish = Join-Path $PSScriptRoot 'dist\NetShareDoctor'
$name = "NetShareDoctor-$version-win-x64"
Write-Host "Network Share Doctor $version" -ForegroundColor Cyan

function Invoke-Step([string]$title, [scriptblock]$step) {
    Write-Host "`n== $title" -ForegroundColor Cyan
    & $step
    if ($LASTEXITCODE -ne 0) { throw "$title failed (exit $LASTEXITCODE)." }
}

Invoke-Step 'Tests' { dotnet test NetShareDoctor.sln -c Release }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
Invoke-Step 'Publish' {
    dotnet publish NetShareDoctor -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -o $publish
}
Remove-Item "$publish\*.pdb" -ErrorAction SilentlyContinue
Copy-Item LICENSE, README.md $publish

Write-Host "`n== Zip" -ForegroundColor Cyan
Compress-Archive "$publish\*" "dist\$name.zip" -Force

Invoke-Step 'MSI' { dotnet build installer -c Release "-p:Version=$version" "-p:PublishDir=$publish" }
Copy-Item "installer\bin\Release\$name.msi" dist\ -Force

Write-Host "`nDone:" -ForegroundColor Green
Get-Item "dist\$name.zip", "dist\$name.msi" | ForEach-Object { '  {0}  ({1:N1} MB)' -f $_.FullName, ($_.Length / 1MB) }
