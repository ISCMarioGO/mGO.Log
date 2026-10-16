param(
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot '../artifacts/startup-final'),
    [double]$MinimumLineCoverage = 80
)

$ErrorActionPreference = 'Stop'
$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter coverage.cobertura.xml)
if ($reports.Count -eq 0) { throw "No coverage.cobertura.xml found in $ResultsDirectory" }
$modules = @{}
foreach ($report in $reports) {
    [xml]$coverage = Get-Content -LiteralPath $report.FullName -Raw
    foreach ($package in $coverage.coverage.packages.package) {
        if (-not $modules.ContainsKey($package.name)) {
            $modules[$package.name] = @{}
        }
        foreach ($class in $package.classes.class) {
            foreach ($line in $class.lines.line) {
                $key = "$($class.filename):$($line.number)"
                $hit = [int]$line.hits -gt 0
                if ($modules[$package.name].ContainsKey($key)) {
                    $modules[$package.name][$key] = $modules[$package.name][$key] -or $hit
                } else { $modules[$package.name][$key] = $hit }
            }
        }
    }
}
foreach ($requiredModule in @('GO.Log.Abstractions', 'GO.Log', 'GO.Log.Extensions.Hosting')) {
    if (-not $modules.ContainsKey($requiredModule)) { throw "Missing coverage for $requiredModule; run both test suites." }
}
$summary = foreach ($name in ($modules.Keys | Sort-Object)) {
    $lines = $modules[$name]
    $covered = @($lines.Values | Where-Object { $_ }).Count
    [pscustomobject]@{
        Module = $name
        CoveredLines = $covered
        TotalLines = $lines.Count
        LineCoverage = [Math]::Round(100 * $covered / $lines.Count, 2)
    }
}
$summary | Format-Table -AutoSize
if ($summary | Where-Object { $_.LineCoverage -lt $MinimumLineCoverage }) {
    throw "A module is below the $MinimumLineCoverage percent line coverage threshold."
}
