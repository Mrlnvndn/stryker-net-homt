#Requires -Version 7
<#
.SYNOPSIS
    Run Stryker HOMT comparison experiments with and without heuristics on multiple repositories.

.DESCRIPTION
    Runs Stryker 20 times on each specified repository:
    - 5 runs in BASELINE mode (standard FOM mutation testing, no HOMs)
    - 5 runs in ACCELERATE mode WITH heuristics (CodeLocation + MutatorType)
    - 5 runs in ACCELERATE mode WITH heuristics - REDUCED (no redundant HOMs)
    - 5 runs in VALIDATE mode WITH heuristics (CodeLocation + MutatorType)
    
    HOMT configurations use:
    - --homt-algorithm both

.PARAMETER RepeatCount
    Number of times to repeat each configuration (default: 5).

.PARAMETER DryRun
    If specified, only print commands without executing them.

.EXAMPLE
    .\run-homt-comparison.ps1
    
.EXAMPLE
    .\run-homt-comparison.ps1 -RepeatCount 3

.EXAMPLE
    .\run-homt-comparison.ps1 -DryRun
#>

param(
  [int]$RepeatCount = 5,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# ============================================================================
# CONFIGURATION: Add your repositories here
# ============================================================================
$repositories = @(
  @{
    ReposRoot = 'C:\Users\MerlijnU\source\repos\CsvHelper'
    ReportOutputDir = 'C:\Users\MerlijnU\outputs\CsvHelper\comparison'
  },
  @{
    ReposRoot = 'C:\Users\MerlijnU\source\repos\GuardClauses'
    ReportOutputDir = 'C:\Users\MerlijnU\outputs\GuardClauses\comparison'
  },
  @{
    ReposRoot = 'C:\Users\MerlijnU\source\repos\Marsen.NetCore.Dojo'
    ReportOutputDir = 'C:\Users\MerlijnU\outputs\Marsen.NetCore.Dojo\comparison'
  },
  @{
    ReposRoot = 'C:\Users\MerlijnU\source\repos\TimeProviderExtensions'
    ReportOutputDir = 'C:\Users\MerlijnU\outputs\TimeProviderExtensions\comparison'
  },
  @{
    ReposRoot = 'C:\Users\MerlijnU\source\repos\MoreLINQ'
    ReportOutputDir = 'C:\Users\MerlijnU\outputs\MoreLINQ\comparison'
  }
)

Write-Host "======================================================================"
Write-Host "  Stryker HOMT Comparison Experiment (Multiple Repositories)"
Write-Host "======================================================================"
Write-Host "Repositories:     $($repositories.Count)"
Write-Host "Repeat Count:     $RepeatCount per configuration"
Write-Host "Dry Run:          $DryRun"
Write-Host "======================================================================"
Write-Host ""

# Configuration definitions
$configurations = @(
  @{
    Name = 'baseline-fom-only'
    Description = 'BASELINE - Standard FOM mutation testing (no HOMs)'
    Mode = $null
    Heuristics = @()
    IsBaseline = $true
  },
  @{
    Name = 'accelerate-with-heuristics-reduced'
    Description = 'ACCELERATE mode WITH heuristics (CodeLocation + MutatorType) - REDUCED redundant HOMs'
    Mode = 'accelerate'
    Heuristics = @('CodeLocation', 'MutatorType')
  },
  @{
    Name = 'validate-with-heuristics'
    Description = 'VALIDATE mode WITH heuristics (CodeLocation + MutatorType)'
    Mode = 'validate'
    Heuristics = @('CodeLocation', 'MutatorType')
  }
)

# Statistics tracking
$totalRepos = $repositories.Count
$totalRunsPerRepo = $RepeatCount * $configurations.Count
$currentRepoNum = 0

# Process each repository
foreach ($repo in $repositories) {
  $currentRepoNum++
  $ReposRoot = $repo.ReposRoot
  $ReportOutputDir = $repo.ReportOutputDir
  
  Write-Host ""
  Write-Host "***********************************************************************"
  Write-Host "  REPOSITORY $currentRepoNum of $totalRepos"
  Write-Host "***********************************************************************"
  Write-Host "Repository:      $ReposRoot"
  Write-Host "Output Directory: $ReportOutputDir"
  Write-Host "***********************************************************************"
  Write-Host ""
  
  # Create output directory
  if (-not $DryRun) {
    $null = New-Item -ItemType Directory -Path $ReportOutputDir -Force | Out-Null
    Write-Host "Created output directory: $ReportOutputDir"
  }
  
  # Discover solution file
  $solutions = Get-ChildItem -Path $ReposRoot -Recurse -Filter *.sln -File -ErrorAction Ignore
  Write-Host "Found $($solutions.Count) .sln file(s) under $ReposRoot"
  
  if (-not $solutions) {
    Write-Host "ERROR: No solution files found in $ReposRoot! Skipping..." -ForegroundColor Red
    continue
  }
  
  if ($solutions.Count -gt 1) {
    Write-Host "WARNING: Multiple solution files found. Using the first one: $($solutions[0].Name)" -ForegroundColor Yellow
  }
  
  $sln = $solutions[0]
  $slnDir = Split-Path -Parent $sln.FullName
  $slnName = [IO.Path]::GetFileNameWithoutExtension($sln.Name)
  
  Write-Host "Using solution: $($sln.Name)"
  Write-Host "Solution directory: $slnDir"
  Write-Host ""
  
  # Run experiments for this repository
  foreach ($config in $configurations) {
    Write-Host ""
    Write-Host "======================================================================"
    Write-Host "  Configuration: $($config.Description)"
    Write-Host "======================================================================"
    
    $configDir = Join-Path $ReportOutputDir $config.Name
    
    for ($rep = 1; $rep -le $RepeatCount; $rep++) {
      $runOutDir = Join-Path $configDir ("run{0:00}" -f $rep)
      
      # Skip if directory already exists
      if (Test-Path $runOutDir) {
        Write-Host "[$slnName] $($config.Name) run=$rep SKIP (directory already exists: $runOutDir)" -ForegroundColor Yellow
        continue
      }
      
      if (-not $DryRun) {
        $null = New-Item -ItemType Directory -Path $runOutDir -Force | Out-Null
      }
      
      # Build Stryker command arguments
      if ($config.IsBaseline) {
        # Baseline: just standard Stryker with CSV reporter
        $args = @(
          'stryker',
          '--reporter', 'HomCsv',
          '--output', $runOutDir
        )
        $heurStr = 'N/A (FOM only)'
      }
      else {
        # HOMT configurations
        $args = @(
          'stryker',
          "--homt-$($config.Mode)",
          '--homt-algorithm', 'both',
          '--reporter', 'HomCsv',
          '--output', $runOutDir
        )
        
        # Add heuristics if specified
        if ($config.Heuristics.Count -gt 0) {
          foreach ($heuristic in $config.Heuristics) {
            $args += @('--homt-heuristics', $heuristic)
          }
          $heurStr = $config.Heuristics -join ', '
        }
        else {
          $args += @('--homt-heuristics', 'none')
          $heurStr = 'none'
        }
      }
      
      # Display command
      $cmdPretty = 'dotnet ' + ($args -join ' ')
      Write-Host ""
      Write-Host "[$slnName] $($config.Name) run=$rep heuristics=[$heurStr]" -ForegroundColor Cyan
      Write-Host "  Output: $runOutDir" -ForegroundColor Gray
      Write-Host "  Command: $cmdPretty" -ForegroundColor Gray
      
      # Execute command
      if (-not $DryRun) {
        Write-Host "  Executing..." -ForegroundColor Green
        Push-Location $slnDir
        try {
          $logFile = Join-Path $runOutDir 'stryker-output.log'
          & dotnet @args *> $logFile
          
          if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ Completed successfully" -ForegroundColor Green
          }
          else {
            Write-Host "  ✗ Failed with exit code: $LASTEXITCODE" -ForegroundColor Red
          }
        }
        catch {
          Write-Host "  ✗ Error: $_" -ForegroundColor Red
        }
        finally {
          Pop-Location
        }
      }
      else {
        Write-Host "  [DRY RUN - Command not executed]" -ForegroundColor Yellow
      }
    }
  }
  
  Write-Host ""
  Write-Host "======================================================================"
  Write-Host "  Repository '$slnName' Complete!"
  Write-Host "======================================================================"
  Write-Host ""
}

# Overall Summary
Write-Host ""
Write-Host "***********************************************************************"
Write-Host "  ALL EXPERIMENTS COMPLETE!"
Write-Host "***********************************************************************"
Write-Host "Total repositories processed: $totalRepos"
Write-Host "Total runs per repository:    $totalRunsPerRepo"
Write-Host "Total runs across all repos:  $($totalRepos * $totalRunsPerRepo)"
Write-Host ""
Write-Host "Repositories:"
foreach ($repo in $repositories) {
  Write-Host "  - $($repo.ReposRoot)"
  Write-Host "    Output: $($repo.ReportOutputDir)"
}
Write-Host ""
Write-Host "Directory structure (per repository):"
Write-Host "  <ReportOutputDir>/"
Write-Host "    ├── baseline-fom-only/"
Write-Host "    │   ├── run01/"
Write-Host "    │   ├── run02/"
Write-Host "    │   ├── ..."
Write-Host "    │   └── run0$RepeatCount/"
Write-Host "    ├── accelerate-with-heuristics/"
Write-Host "    │   ├── run01/"
Write-Host "    │   ├── run02/"
Write-Host "    │   ├── ..."
Write-Host "    │   └── run0$RepeatCount/"
Write-Host "    ├── accelerate-with-heuristics-reduced/"
Write-Host "    │   ├── run01/"
Write-Host "    │   ├── run02/"
Write-Host "    │   ├── ..."
Write-Host "    │   └── run0$RepeatCount/"
Write-Host "    └── validate-with-heuristics/"
Write-Host "        ├── run01/"
Write-Host "        ├── run02/"
Write-Host "        ├── ..."
Write-Host "        └── run0$RepeatCount/"
Write-Host ""
Write-Host "Each run directory contains:"
Write-Host "  - mutation-report-hom.csv (HOM analysis data) OR mutation-report.csv (baseline)"
Write-Host "  - stryker-output.log (full execution log)"
Write-Host "***********************************************************************"
