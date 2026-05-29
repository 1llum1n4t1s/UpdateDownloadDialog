# VelopackUpdateDialog NuGet パブリッシュスクリプト
# $apiKey を事前に設定するか、環境変数 NUGET_API_KEY を使用
Write-Host $PSScriptRoot

if (-not $apiKey)
{
    $apiKey = $env:NUGET_API_KEY
}

if (-not $apiKey)
{
    throw "NuGet API keyが設定されていません。`$apiKey または環境変数 NUGET_API_KEY を設定してください。"
}

$folder = "$PSScriptRoot\artifacts"
$packages = Get-ChildItem -Path $folder -Filter "*.nupkg" -Recurse | Sort-Object LastWriteTime

if (-not $packages)
{
    Write-Error "パッケージが見つかりません: $folder"
    exit 1
}

# 異なるバージョンの nupkg が混在していたら警告 (未公開バージョンの意図しない公開を防ぐ)
$versions = $packages | ForEach-Object { if ($_.Name -match '(\d+\.\d+\.\d+(?:-[\w.]+)?)\.nupkg$') { $Matches[1] } } | Select-Object -Unique
if ($versions.Count -gt 1)
{
    Write-Warning "artifacts に複数バージョンの nupkg が混在しています: $($versions -join ', ')。意図しない公開を避けるため不要な nupkg を削除してください。"
}

$failed = 0
foreach ($pkg in $packages)
{
    Write-Host "Publishing: $($pkg.Name)"
    $result = dotnet nuget push "$($pkg.FullName)" --api-key $apiKey --source https://api.nuget.org/v3/index.json --skip-duplicate 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0)
    {
        Write-Host "Error: $result"
        Write-Error "Failed to publish $($pkg.Name) (exit code: $exitCode)"
        $failed++
    }
    else
    {
        Write-Host "Successfully published $($pkg.Name)"
    }
}

if ($failed -gt 0)
{
    Write-Error "$failed 個のパッケージの公開に失敗しました"
    exit 1
}
else
{
    Write-Host "全パッケージの公開が完了しました！"
}
