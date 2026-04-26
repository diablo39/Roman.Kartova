[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,

    [Parameter(Mandatory = $true)]
    [string]$TempRoot,

    [string]$OutputPath,

    [string]$GeneratedAtUtc,

    [string]$RunId,

    [string]$ManifestPath,

    [string[]]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$TempRoot = [System.IO.Path]::GetFullPath($TempRoot)
$OutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $ProjectRoot 'mutation-report-surviving.md'
} else {
    [System.IO.Path]::GetFullPath($OutputPath)
}

if ([string]::IsNullOrWhiteSpace($GeneratedAtUtc)) {
    $GeneratedAtUtc = [DateTime]::UtcNow.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
}

New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
$analysisDir = Join-Path $TempRoot 'analysis'
New-Item -ItemType Directory -Path $analysisDir -Force | Out-Null

function Resolve-ReportPaths {
    param(
        [string]$ProjectRoot,
        [string]$RunId,
        [string]$ManifestPath,
        [string[]]$ReportPath
    )

    if ($ReportPath -and $ReportPath.Count -gt 0) {
        return $ReportPath | ForEach-Object { [System.IO.Path]::GetFullPath($_) } | Sort-Object -Unique
    }

    if (-not [string]::IsNullOrWhiteSpace($ManifestPath) -and (Test-Path $ManifestPath)) {
        $manifestReports = Get-Content $ManifestPath |
            Where-Object { $_ -like 'report_path=*' } |
            ForEach-Object { $_.Substring('report_path='.Length) } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

        if ($manifestReports) {
            return $manifestReports | ForEach-Object { [System.IO.Path]::GetFullPath($_) } | Sort-Object -Unique
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($RunId)) {
        $root = Join-Path $ProjectRoot 'StrykerOutput'
        if (-not (Test-Path $root)) {
            throw "Stryker output directory not found: $root"
        }

        return Get-ChildItem -Path $root -Filter 'mutation-report.json' -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*$RunId*\reports\mutation-report.json" } |
            Sort-Object FullName |
            Select-Object -ExpandProperty FullName
    }

    throw 'Provide -ReportPath, -ManifestPath, or -RunId.'
}

function Get-RelativePath {
    param([string]$Path, [string]$ProjectRoot)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($ProjectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($ProjectRoot.Length).TrimStart('\').Replace('\', '/')
    }

    return $fullPath.Replace('\', '/')
}

function Resolve-SourcePath {
    param([string]$Path, [string]$ProjectRoot)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Path))
}

function Get-StatusCount {
    param(
        [hashtable]$Counts,
        [string]$Name
    )

    if ($Counts.ContainsKey($Name)) {
        return [int]$Counts[$Name]
    }

    return 0
}

function Get-OptionalJsonString {
    param(
        [object]$Object,
        [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return ''
    }

    return [string]$property.Value
}

function Get-MethodMarkers {
    param([string[]]$Lines)

    $markers = New-Object 'System.Collections.Generic.List[object]'
    for ($index = 0; $index -lt $Lines.Length; $index++) {
        $line = $Lines[$index].Trim()
        if ($line -match '^(if|for|foreach|while|switch|catch|using|return|lock)\b') {
            continue
        }

        if ($line -match '^(?:public|private|protected|internal|static|async|virtual|override|sealed|partial|file|extern|new|unsafe|readonly|abstract|\s)+.*?([A-Za-z_][A-Za-z0-9_]*)\s*\(') {
            $markers.Add([pscustomobject]@{ Line = $index + 1; Name = $Matches[1] })
        }
    }

    return $markers
}

function Get-ClassMarkers {
    param([string[]]$Lines)

    $markers = New-Object 'System.Collections.Generic.List[object]'
    for ($index = 0; $index -lt $Lines.Length; $index++) {
        $line = $Lines[$index].Trim()
        if ($line -match '^(?:public|internal|private|protected|file|sealed|abstract|partial|static|\s)*(class|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)') {
            $markers.Add([pscustomobject]@{ Line = $index + 1; Name = $Matches[2] })
        }
    }

    return $markers
}

function Get-NearestName {
    param(
        [object[]]$Markers,
        [int]$LineNumber,
        [string]$Fallback
    )

    $current = $Fallback
    foreach ($marker in $Markers) {
        if ($marker.Line -gt $LineNumber) {
            break
        }

        $current = $marker.Name
    }

    return $current
}

function Get-ScenarioHint {
    param(
        [string]$Original,
        [string]$MethodName,
        [string]$MutationType,
        [string]$MutantStatus
    )

    if ($Original -match '==\s*null|is\s+null') { return 'a null input and a non-null control case' }
    if ($Original -match '!=\s*null|is\s+not\s+null') { return 'a non-null input plus a null control case' }
    if ($Original -match '\.Any\s*\(' -or $Original -match 'Count\s*(==|!=|>|<|>=|<=)\s*0' -or $Original -match 'Length\s*(==|!=|>|<|>=|<=)\s*0') { return 'an empty collection and a single-item collection' }
    if ($Original -match 'StringComparison|ToLower|ToUpper|OrdinalIgnoreCase|Contains\s*\(|StartsWith\s*\(|EndsWith\s*\(') { return 'mixed-case values that should and should not match' }
    if ($Original -match '&&|\|\|') { return 'the mixed boolean case where one side is true and the other is false' }
    if ($Original -match '(>=|<=|>|<|==|!=)\s*(-?\d+(?:\.\d+)?)') { return "inputs on both sides of boundary $($Matches[2])" }
    if ($MutationType -match 'Linq|Statement|Collection' -or $Original -match '\.Select\(|\.Where\(|\.OrderBy\(|\.GroupBy\(') { return 'a multi-item data set where the exact returned items and order matter' }
    if ($MutationType -match 'Arithmetic|Math' -or $Original -match '[+\-*/%]') { return 'inputs with a known exact numeric result' }
    if ($MutantStatus -eq 'NoCoverage') { return "an input that reaches $MethodName and executes this branch" }

    return "an input that exercises $MethodName with an observable assertion on the affected result"
}

function Get-WhyText {
    param(
        [string]$MutantStatus,
        [string]$MethodName,
        [string]$Original,
        [string]$MutatedCode,
        [string]$MutationType,
        [string]$Description
    )

    if ($MutantStatus -eq 'NoCoverage') {
        return "No test currently reaches $MethodName where '$Original' is evaluated, so this mutation never executes and no assertion can fail."
    }

    if ($MutationType -match 'Arithmetic|Math' -or $Original -match '[+\-*/%]') {
        return "Tests execute $MethodName, but they do not pin the exact numeric result produced by '$Original', so changing it to '$MutatedCode' leaves the current assertions green."
    }

    if ($MutationType -match 'Logical|Boolean|Conditional|Equality|Linq' -or $Original -match '&&|\|\||==|!=|>=|<=|>|<|\.Any\(|Contains\(') {
        return "Tests cover $MethodName, but they miss the decision case around '$Original', so the mutated behavior '$MutatedCode' is still accepted by current assertions."
    }

    if (-not [string]::IsNullOrWhiteSpace($Description)) {
        return "Tests execute $MethodName, but they do not assert the effect described by the $MutationType mutation on '$Original', so the tool-reported change ($Description) survives."
    }

    return "Tests execute $MethodName, but they do not assert the observable effect of '$Original' tightly enough to detect the $MutationType mutation."
}

function Get-SuggestedFix {
    param(
        [string]$MutantStatus,
        [string]$ClassName,
        [string]$MethodName,
        [string]$Original,
        [string]$MutatedCode,
        [string]$MutationType
    )

    $scenario = Get-ScenarioHint -Original $Original -MethodName $MethodName -MutationType $MutationType -MutantStatus $MutantStatus
    if ($MutantStatus -eq 'NoCoverage') {
        return "Add a test for $ClassName.$MethodName using $scenario so execution reaches '$Original', then assert the exact branch result or returned data from that path."
    }

    if ($MutationType -match 'Arithmetic|Math' -or $Original -match '[+\-*/%]') {
        return "Add a test for $ClassName.$MethodName with $scenario and assert the exact computed value so replacing '$Original' with '$MutatedCode' fails immediately."
    }

    if ($MutationType -match 'Logical|Boolean|Conditional|Equality' -or $Original -match '&&|\|\||==|!=|>=|<=|>|<') {
        return "Add a test for $ClassName.$MethodName with $scenario and assert which branch or boolean outcome is produced when '$Original' is evaluated."
    }

    if ($MutationType -match 'Linq|Statement|Collection' -or $Original -match '\.Select\(|\.Where\(|\.OrderBy\(|\.GroupBy\(') {
        return "Add a test for $ClassName.$MethodName with $scenario and assert the exact collection contents, count, and order so the $MutationType change is observable."
    }

    return "Add a test for $ClassName.$MethodName with $scenario and assert the exact observable output affected by '$Original' so the mutation cannot survive."
}

$reportPaths = @(Resolve-ReportPaths -ProjectRoot $ProjectRoot -RunId $RunId -ManifestPath $ManifestPath -ReportPath $ReportPath)
if (-not $reportPaths -or $reportPaths.Count -eq 0) {
    throw 'No mutation report files were resolved.'
}

$survivorRows = New-Object 'System.Collections.Generic.List[string]'
$statusCounts = @{}
foreach ($report in $reportPaths) {
    if (-not (Test-Path $report)) {
        throw "Mutation report not found: $report"
    }

    $json = Get-Content $report -Raw | ConvertFrom-Json
    foreach ($fileEntry in $json.files.PSObject.Properties) {
        $filePath = $fileEntry.Name
        foreach ($mutant in $fileEntry.Value.mutants) {
            $status = [string]$mutant.status
            if (-not $statusCounts.ContainsKey($status)) {
                $statusCounts[$status] = 0
            }
            $statusCounts[$status]++

            if ($status -eq 'Survived' -or $status -eq 'NoCoverage') {
                $replacement = Get-OptionalJsonString -Object $mutant -Name 'replacement'
                $description = Get-OptionalJsonString -Object $mutant -Name 'description'
                $fields = @(
                    [string]$filePath,
                    [string]$mutant.location.start.line,
                    [string]$mutant.mutatorName,
                    $status,
                    $replacement,
                    $description
                ) | ForEach-Object { ($_ -replace "`t", ' ') -replace "`r|`n", ' ' }
                $survivorRows.Add(($fields -join "`t"))
            }
        }
    }
}

$survivorPath = Join-Path $TempRoot 'ms-survivors.tsv'
$countsPath = Join-Path $TempRoot 'ms-counts.json'

$orderedCounts = [ordered]@{}
foreach ($fixedStatus in 'Ignored', 'CompileError', 'Killed', 'NoCoverage', 'Survived', 'Timeout') {
    if ($statusCounts.ContainsKey($fixedStatus)) {
        $orderedCounts[$fixedStatus] = [int]$statusCounts[$fixedStatus]
    }
}

foreach ($extraStatus in ($statusCounts.Keys | Where-Object { -not $orderedCounts.Contains($_) } | Sort-Object)) {
    $orderedCounts[$extraStatus] = [int]$statusCounts[$extraStatus]
}

$survivorRows | Set-Content -Path $survivorPath -Encoding utf8
($orderedCounts | ConvertTo-Json -Compress) | Set-Content -Path $countsPath -Encoding utf8

$ignored = Get-StatusCount -Counts $statusCounts -Name 'Ignored'
$compileError = Get-StatusCount -Counts $statusCounts -Name 'CompileError'
$survived = Get-StatusCount -Counts $statusCounts -Name 'Survived'
$noCoverage = Get-StatusCount -Counts $statusCounts -Name 'NoCoverage'
$killed = Get-StatusCount -Counts $statusCounts -Name 'Killed'
$timeouts = Get-StatusCount -Counts $statusCounts -Name 'Timeout'
$totalMutants = $ignored + $compileError + $survived + $noCoverage + $killed + $timeouts
$validTotal = $totalMutants - $ignored - $compileError
$effectiveKilled = $killed + $timeouts
$score = if ($validTotal -gt 0) { [math]::Round(($effectiveKilled / $validTotal) * 100, 2) } else { 0 }
$scoreText = $score.ToString('0.00', [System.Globalization.CultureInfo]::InvariantCulture)
$status = if ($score -ge 80) { 'PASS' } else { 'FAIL' }

$rows = foreach ($line in Get-Content $survivorPath) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line -split "`t", 6
    if ($parts.Length -lt 6) { continue }
    [pscustomobject]@{
        FilePath = $parts[0]
        Line = [int]$parts[1]
        MutationType = $parts[2]
        MutantStatus = $parts[3]
        Replacement = $parts[4]
        Description = $parts[5]
    }
}

$sha = [System.Security.Cryptography.SHA256]::Create()
$entries = New-Object 'System.Collections.Generic.List[object]'
foreach ($fileGroup in ($rows | Group-Object FilePath | Sort-Object Name)) {
    $filePath = Resolve-SourcePath -Path $fileGroup.Name -ProjectRoot $ProjectRoot
    $relativeFile = Get-RelativePath -Path $filePath -ProjectRoot $ProjectRoot
    $sourceLines = Get-Content $filePath
    $methodMarkers = Get-MethodMarkers -Lines $sourceLines
    $classMarkers = Get-ClassMarkers -Lines $sourceLines
    $analysisLines = New-Object 'System.Collections.Generic.List[string]'

    foreach ($row in ($fileGroup.Group | Sort-Object Line, MutationType)) {
        $sourceLine = if ($row.Line -gt 0 -and $row.Line -le $sourceLines.Length) { $sourceLines[$row.Line - 1].Trim() } else { '' }
        if ([string]::IsNullOrWhiteSpace($sourceLine)) {
            $sourceLine = '[source line unavailable]'
        }

        $mutatedCode = if ([string]::IsNullOrWhiteSpace($row.Replacement)) {
            if ([string]::IsNullOrWhiteSpace($row.Description)) {
                '[tool did not expose replacement]'
            } else {
                "[tool mutation: $($row.Description)]"
            }
        } else {
            $row.Replacement
        }

        $methodName = Get-NearestName -Markers $methodMarkers -LineNumber $row.Line -Fallback 'current code path'
        $className = Get-NearestName -Markers $classMarkers -LineNumber $row.Line -Fallback 'current type'
        $safeOriginal = $sourceLine -replace '`', "'"
        $safeMutated = $mutatedCode -replace '`', "'"
        $why = Get-WhyText -MutantStatus $row.MutantStatus -MethodName $methodName -Original $safeOriginal -MutatedCode $safeMutated -MutationType $row.MutationType -Description $row.Description
        $fix = Get-SuggestedFix -MutantStatus $row.MutantStatus -ClassName $className -MethodName $methodName -Original $safeOriginal -MutatedCode $safeMutated -MutationType $row.MutationType

        $entry = [pscustomobject]@{
            File = $relativeFile
            Line = $row.Line
            MutationType = $row.MutationType
            MutantStatus = $row.MutantStatus
            OriginalCode = $safeOriginal
            MutatedCode = $safeMutated
            Why = $why
            Fix = $fix
        }
        $entries.Add($entry)

        $analysisLines.Add("#### ${relativeFile}:$($row.Line)")
        $analysisLines.Add("- **Mutation type**: $($row.MutationType)")
        $analysisLines.Add("- **Mutant status**: $($row.MutantStatus)")
        $analysisLines.Add('- **Original code**: `' + $safeOriginal + '`')
        $analysisLines.Add('- **Mutated code**: `' + $safeMutated + '`')
        $analysisLines.Add("- **Why it survived**: $why")
        $analysisLines.Add("- **Suggested fix**: $fix")
        $analysisLines.Add('')
    }

    $hash = ([System.BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($relativeFile)))).Replace('-', '').Substring(0, 16).ToLowerInvariant()
    $analysisPath = Join-Path $analysisDir ("ms-analysis-$hash.md")
    $analysisLines | Set-Content -Path $analysisPath -Encoding utf8
}

$reportLines = New-Object 'System.Collections.Generic.List[string]'
$reportLines.Add('## Mutation Testing Results')
$reportLines.Add('')
$reportLines.Add("**Generated**: $GeneratedAtUtc")
$reportLines.Add('**Project type**: .NET')
$reportLines.Add('**Tool**: Stryker.NET')
$reportLines.Add("**Score**: $scoreText% ($effectiveKilled killed / $validTotal total)")
$reportLines.Add('**Target**: >= 80%')
$reportLines.Add("**Status**: $status")
$reportLines.Add('')
$reportLines.Add('### Summary')
$reportLines.Add('')
$reportLines.Add("- **Total mutants**: $totalMutants")
$reportLines.Add("- **Killed**: $killed")
$reportLines.Add("- **Survived**: $survived")
$reportLines.Add("- **No coverage**: $noCoverage")
$reportLines.Add('- **Equivalent (excluded)**: 0')
$reportLines.Add("- **Compile errors (excluded)**: $compileError")
$reportLines.Add("- **Timeouts (counted as killed)**: $timeouts")
$reportLines.Add('')
$reportLines.Add('### Surviving Mutants')
$reportLines.Add('')
if ($entries.Count -eq 0) {
    $reportLines.Add('No surviving or no-coverage mutants were reported.')
} else {
    foreach ($entry in ($entries | Sort-Object File, Line, MutationType)) {
        $reportLines.Add("#### $($entry.File):$($entry.Line)")
        $reportLines.Add("- **Mutation type**: $($entry.MutationType)")
        $reportLines.Add("- **Mutant status**: $($entry.MutantStatus)")
        $reportLines.Add('- **Original code**: `' + $entry.OriginalCode + '`')
        $reportLines.Add('- **Mutated code**: `' + $entry.MutatedCode + '`')
        $reportLines.Add("- **Why it survived**: $($entry.Why)")
        $reportLines.Add("- **Suggested fix**: $($entry.Fix)")
        $reportLines.Add('')
    }
}
$reportLines.Add('### Recommendation')
$reportLines.Add('')
if ($status -eq 'FAIL') {
    $reportLines.Add('Feed this report to `/test-generator-gh` to generate targeted tests for the surviving mutants. Expected improvement: 10-25 percentage points per feedback iteration.')
} else {
    $reportLines.Add('Mutation score meets the target threshold. Proceed to `/coverage-auditor` for final verification.')
}
$reportLines | Set-Content -Path $OutputPath -Encoding utf8

Write-Output "report_count=$($reportPaths.Count)"
Write-Output "survivor_rows=$($survivorRows.Count)"
Write-Output "unique_files=$((($rows | Group-Object FilePath).Count))"
Write-Output "score=$scoreText"
Write-Output "status=$status"
Write-Output "temp_root=$TempRoot"
Write-Output "survivors_path=$survivorPath"
Write-Output "counts_path=$countsPath"
Write-Output "analysis_dir=$analysisDir"
Write-Output "report_path=$OutputPath"
