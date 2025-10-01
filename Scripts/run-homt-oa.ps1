#Requires -Version 7
param(
  [string]$ReposRoot = 'C:\Users\MerlijnU\source\repos\TimeProviderExtensions',
  [string]$ReportOutputDir = 'C:\Users\MerlijnU\outputs\Marsen.NetCore.Dojo',
  [int]   $RepeatCount = 5,
  [string[]]$RandomRowArgs = @(),   # only applied to OA01 ("random HOM generator")
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$null = New-Item -ItemType Directory -Path $ReportOutputDir -Force | Out-Null

# Heuristic factor mapping
$HMap_old = @{
  H1 = 'CodeLocation'
  H2 = 'EmptyAssessingTests'
  H3 = 'MutatorType'
  H4 = 'MaxSizeLimit'
  H5 = 'OverlappingTests'
  H6 = 'SyntaxNodeConflict'
}

# Taguchi L12(2^11) — OA01 reserved for "random" condition
$OA_L12_wrong = @(
  @{H1 = 1; H2 = 1; H3 = 1; H4 = 1; H5 = 1; H6 = 1 }
  @{H1 = 1; H2 = 1; H3 = 1; H4 = 2; H5 = 2; H6 = 2 }
  @{H1 = 1; H2 = 2; H3 = 2; H4 = 1; H5 = 1; H6 = 2 }
  @{H1 = 1; H2 = 2; H3 = 2; H4 = 2; H5 = 2; H6 = 1 }
  @{H1 = 2; H2 = 1; H3 = 2; H4 = 1; H5 = 2; H6 = 1 }
  @{H1 = 2; H2 = 1; H3 = 2; H4 = 2; H5 = 1; H6 = 2 }
  @{H1 = 2; H2 = 2; H3 = 1; H4 = 1; H5 = 2; H6 = 2 }
  @{H1 = 2; H2 = 2; H3 = 1; H4 = 2; H5 = 1; H6 = 1 }
  @{H1 = 1; H2 = 1; H3 = 2; H4 = 1; H5 = 2; H6 = 2 }
  @{H1 = 1; H2 = 1; H3 = 2; H4 = 2; H5 = 1; H6 = 1 }
  @{H1 = 2; H2 = 2; H3 = 2; H4 = 1; H5 = 1; H6 = 1 }
  @{H1 = 2; H2 = 2; H3 = 2; H4 = 2; H5 = 2; H6 = 2 }
)

$HMapOld = @{
  H1 = 'CodeLocation'
  H2 = 'EmptyAssessingTests'
  H3 = 'MutatorType'
  H4 = 'MaxSizeLimit'
  H5 = 'OverlappingTests'
  H6 = 'SyntaxNodeConflict'
}

$OA_L12_h4h5swap = @(
  @{H1 = 1; H2 = 1; H3 = 1; H4 = 1; H5 = 1; H6 = 1 } # OA01
  @{H1 = 1; H2 = 1; H3 = 1; H4 = 2; H5 = 2; H6 = 2 } 
  @{H1 = 1; H2 = 2; H3 = 2; H4 = 1; H5 = 1; H6 = 2 }
  @{H1 = 1; H2 = 2; H3 = 2; H4 = 2; H5 = 2; H6 = 1 }
  @{H1 = 2; H2 = 1; H3 = 2; H4 = 2; H5 = 1; H6 = 1 } # OA07
  @{H1 = 2; H2 = 1; H3 = 2; H4 = 1; H5 = 2; H6 = 2 } # OA08
  @{H1 = 2; H2 = 2; H3 = 1; H4 = 2; H5 = 1; H6 = 2 } # OA11
  @{H1 = 2; H2 = 2; H3 = 1; H4 = 1; H5 = 2; H6 = 1 } # OA12
  @{H1 = 1; H2 = 1; H3 = 2; H4 = 2; H5 = 1; H6 = 2 } 
  @{H1 = 1; H2 = 1; H3 = 2; H4 = 1; H5 = 2; H6 = 1 }
  @{H1 = 2; H2 = 2; H3 = 2; H4 = 1; H5 = 1; H6 = 1 } # OA10
  @{H1 = 2; H2 = 2; H3 = 2; H4 = 2; H5 = 2; H6 = 2 }
)

$HMap = @{
  H1 = 'CodeLocation'
  H2 = 'EmptyAssessingTests'
  H3 = 'MutatorType'
  H4 = 'OverlappingTests'
  H5 = 'MaxSizeLimit'
  H6 = 'SyntaxNodeConflict'
}

#correct
$OA_L12 = @(
  @{H1 = 1; H2 = 1; H3 = 1; H4 = 1; H5 = 1; H6 = 1 } # OA01
  @{H1 = 1; H2 = 1; H3 = 1; H4 = 1; H5 = 1; H6 = 2 } # OA02
  @{H1 = 1; H2 = 1; H3 = 2; H4 = 2; H5 = 2; H6 = 1 } # OA03
  @{H1 = 1; H2 = 2; H3 = 1; H4 = 2; H5 = 2; H6 = 1 } # OA04
  @{H1 = 1; H2 = 2; H3 = 2; H4 = 1; H5 = 2; H6 = 2 } # OA05
  @{H1 = 1; H2 = 2; H3 = 2; H4 = 2; H5 = 1; H6 = 2 } # OA06
  @{H1 = 2; H2 = 1; H3 = 2; H4 = 2; H5 = 1; H6 = 1 } # OA07
  @{H1 = 2; H2 = 1; H3 = 2; H4 = 1; H5 = 2; H6 = 2 } # OA08
  @{H1 = 2; H2 = 1; H3 = 1; H4 = 2; H5 = 2; H6 = 2 } # OA09
  @{H1 = 2; H2 = 2; H3 = 2; H4 = 1; H5 = 1; H6 = 1 } # OA10
  @{H1 = 2; H2 = 2; H3 = 1; H4 = 2; H5 = 1; H6 = 2 } # OA11
  @{H1 = 2; H2 = 2; H3 = 1; H4 = 1; H5 = 2; H6 = 1 } # OA12
)

$Algorithms = @('genetic', 'local')

# Discover solutions reliably
$solutions = Get-ChildItem -Path $ReposRoot -Recurse -Filter *.sln -File -ErrorAction Ignore
Write-Host "Found $($solutions.Count) .sln file(s) under $ReposRoot"
if (-not $solutions) { exit 0 }

foreach ($sln in $solutions) {
  $slnDir = Split-Path -Parent $sln.FullName
  $slnName = [IO.Path]::GetFileNameWithoutExtension($sln.Name)

  foreach ($alg in $Algorithms) {
    for ($i = 0; $i -lt $OA_L12.Count; $i++) {
      $rowIndex = $i + 1
      $row = $OA_L12[$i]
      $isRandom = ($rowIndex -eq 1)

      # Output dirs
      $baseDir = Join-Path $ReportOutputDir $slnName
      $algDir = Join-Path $baseDir       $alg
      $oaDir = Join-Path $algDir        ("OA{0:00}" -f $rowIndex)

      $hset = @()
      foreach ($k in 'H1', 'H2', 'H3', 'H4', 'H5', 'H6') {
        if ($row[$k] -eq 2) { $hset += $HMap[$k] }
      }
      
      # Determine heuristic key for display
      if ($hset.Count -eq 0) {
        $heurKey = 'none'  # OA01 case: all heuristics OFF
      }
      else {
        $heurKey = $hset -join '-'
      }

      # Handle special random mode for OA01 if RandomRowArgs are provided
      if ($isRandom -and $RandomRowArgs.Count -gt 0) {
        foreach ($rep in 1..$RepeatCount) {
          $runOutDir = Join-Path $oaDir ("rep{0:00}" -f $rep)
          $null = New-Item -ItemType Directory -Path $runOutDir -Force | Out-Null

          $args = @(
            'stryker',
            '--homt-validate',
            '--homt-algorithm', $alg,
            '--reporter', 'HomCsv',
            '--output', $runOutDir
          ) + $RandomRowArgs

          $cmdPretty = 'dotnet ' + ($args -join ' ')
          Write-Host ("[{0}] OA#{1:00} alg={2} heur=random rep={3}" -f $slnName, $rowIndex, $alg, $rep)
          Write-Host "  $cmdPretty"

          if (-not $DryRun) {
            Push-Location $slnDir
            try {
              & dotnet @args *> (Join-Path $runOutDir 'stryker-output.log')
            }
            finally {
              Pop-Location
            }
          }
        }
      }
      else {
        # Standard heuristic processing (including OA01 with no heuristics)
        foreach ($rep in 1..$RepeatCount) {
          $runOutDir = Join-Path $oaDir ("rep{0:00}" -f $rep)
          $null = New-Item -ItemType Directory -Path $runOutDir -Force | Out-Null

          $args = @(
            'stryker',
            '--homt-validate',
            '--homt-algorithm', $alg,
            '--reporter', 'HomCsv',
            '--output', $runOutDir
          )
          foreach ($h in $hset) { $args += @('--homt-heuristics', $h) }

          $cmdPretty = 'dotnet ' + ($args -join ' ')
          Write-Host ("[{0}] OA#{1:00} alg={2} heur={3} rep={4}" -f $slnName, $rowIndex, $alg, $heurKey, $rep)
          Write-Host "  $cmdPretty"

          if (-not $DryRun) {
            Push-Location $slnDir
            try {
              & dotnet @args *> (Join-Path $runOutDir 'stryker-output.log')
            }
            finally {
              Pop-Location
            }
          }
        }
      }
    }
  }
}

Write-Host "`nDone. HomCsv + stryker-output.log are under: $ReportOutputDir"
