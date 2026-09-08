[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PackageId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ProjectPaths,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string]$OldVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string]$Version
)

$paths = $ProjectPaths.Split(
    ';',
    [System.StringSplitOptions]::RemoveEmptyEntries
) | ForEach-Object { $_.Trim() }

if ($paths.Count -eq 0 -or ($paths | Select-Object -Unique).Count -ne $paths.Count) {
    throw 'ProjectPaths は重複のない1件以上のパスを指定してください。'
}

$escapedPackageId = [regex]::Escape($PackageId)
$pattern = [regex]::new(
    '(<PackageReference\s+Include="' + $escapedPackageId + '"\s+Version=")([^"]+)(")'
)
$updates = @()

foreach ($projectPath in $paths) {
    $resolvedProjectPath = (Resolve-Path -LiteralPath $projectPath -ErrorAction Stop).Path
    $content = [System.IO.File]::ReadAllText($resolvedProjectPath)
    $matches = $pattern.Matches($content)

    if ($matches.Count -ne 1) {
        throw "$PackageId の直接参照が1件に定まりません: $projectPath ($($matches.Count)件)"
    }

    $currentVersion = $matches[0].Groups[2].Value
    if ($currentVersion -ne $OldVersion -and $currentVersion -ne $Version) {
        throw "$PackageId の現在版が想定外です: $projectPath ($currentVersion)"
    }

    $updates += [pscustomobject]@{
        Path = $resolvedProjectPath
        Content = $content
        CurrentVersion = $currentVersion
    }
}

foreach ($update in $updates) {
    if ($update.CurrentVersion -eq $Version) {
        continue
    }

    $updatedContent = $pattern.Replace(
        $update.Content,
        {
            param($match)
            $match.Groups[1].Value + $Version + $match.Groups[3].Value
        },
        1
    )
    [System.IO.File]::WriteAllText(
        $update.Path,
        $updatedContent,
        [System.Text.UTF8Encoding]::new($false)
    )
}
