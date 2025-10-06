################################################################################
# COMPLETE ANALYSIS PIPELINE
# From Raw HOM CSVs to LaTeX Tables
# 
# Prerequisites:
# - Python 3.11+ installed
# - Required packages: pandas, numpy, scipy, statsmodels, matplotlib
# - Raw HOM CSV files in C:\users\merlijnu\outputs\<RepoName>\
################################################################################

Write-Host "=" -NoNewline -ForegroundColor Cyan
Write-Host ("="*99) -ForegroundColor Cyan
Write-Host "STRYKER.NET HOM ANALYSIS PIPELINE" -ForegroundColor Yellow
Write-Host "Complete workflow from raw data to LaTeX tables" -ForegroundColor Yellow
Write-Host ("="*100) -ForegroundColor Cyan
Write-Host ""

# Set working directory
Set-Location "C:\Users\MerlijnU\Repos\stryker-net-homt\Scripts"

################################################################################
# STEP 1: Process Raw HOM CSV Files
# Purpose: Transform raw Stryker output into analysis-ready format
# Input:   C:\users\merlijnu\outputs\<RepoName>\*.csv
# Output:  C:\Users\MerlijnU\analysis\<RepoName>\data.csv
################################################################################

Write-Host "STEP 1: Processing Raw HOM CSV Files" -ForegroundColor Green
Write-Host ("-"*100) -ForegroundColor Gray
Write-Host ""

Write-Host "[1/5] Processing Marsen.NetCore.Dojo..." -ForegroundColor Cyan
python analyze_homcsv_enhanced.py `
    --root "C:\users\merlijnu\outputs\Marsen.NetCore.Dojo" `
    --solution "Marsen.NetCore.Dojo.Integration.Test"

Write-Host "[2/5] Processing MoreLinq..." -ForegroundColor Cyan
python analyze_homcsv_enhanced.py `
    --root "C:\users\merlijnu\outputs\MoreLinq" `
    --solution "MoreLinq"

Write-Host "[3/5] Processing CsvHelper..." -ForegroundColor Cyan
python analyze_homcsv_enhanced.py `
    --root "C:\users\merlijnu\outputs\CsvHelper" `
    --solution "CsvHelper"

Write-Host "[4/5] Processing GuardClauses..." -ForegroundColor Cyan
python analyze_homcsv_enhanced.py `
    --root "C:\users\merlijnu\outputs\GuardClauses" `
    --solution "GuardClauses"

Write-Host "[5/5] Processing TimeProviderExtensions..." -ForegroundColor Cyan
python analyze_homcsv_enhanced.py `
    --root "C:\users\merlijnu\outputs\TimeProviderExtensions" `
    --solution "TimeProviderExtensions"

Write-Host ""
Write-Host "✓ Step 1 Complete" -ForegroundColor Green
Write-Host "  Output: C:\Users\MerlijnU\analysis\<RepoName>\data.csv (5 files)" -ForegroundColor Gray
Write-Host ""

################################################################################
# STEP 2: Run GLM Binomial ANOVA
# Purpose: Fit statistical models and test heuristic effects
# Input:   C:\Users\MerlijnU\analysis\<RepoName>\data.csv
# Output:  Individual GLM results + Aggregated results
#          - C:\Users\MerlijnU\analysis\<RepoName>\anova\*.csv (per repo)
#          - C:\Users\MerlijnU\analysis\anova\*_all_repos.csv (aggregated)
################################################################################

Write-Host "STEP 2: Running GLM Binomial ANOVA" -ForegroundColor Green
Write-Host ("-"*100) -ForegroundColor Gray
Write-Host ""
Write-Host "Fitting GLM models for all 5 repositories..." -ForegroundColor Cyan
Write-Host "This will analyze:" -ForegroundColor Gray
Write-Host "  - Main effects (6 heuristics + 1 algorithm)" -ForegroundColor Gray
Write-Host "  - Two-way interactions (algorithm × heuristics)" -ForegroundColor Gray
Write-Host "  - Statistical significance (LR tests, FDR correction)" -ForegroundColor Gray
Write-Host ""

python glm_binomial_anova.py `
    --solution Marsen.NetCore.Dojo.Integration.Test CsvHelper GuardClauses MoreLinq TimeProviderExtensions `
    --repo Marsen.NetCore.Dojo CsvHelper GuardClauses MoreLinq TimeProviderExtensions

Write-Host ""
Write-Host "✓ Step 2 Complete" -ForegroundColor Green
Write-Host "  Output (per repository):" -ForegroundColor Gray
Write-Host "    - glm_coef_or.csv          (Odds ratios with CIs)" -ForegroundColor Gray
Write-Host "    - glm_lr_tests.csv         (Likelihood ratio tests)" -ForegroundColor Gray
Write-Host "    - glm_model_summary.txt    (Model diagnostics)" -ForegroundColor Gray
Write-Host "  Output (aggregated):" -ForegroundColor Gray
Write-Host "    - C:\Users\MerlijnU\analysis\anova\glm_coef_or_all_repos.csv" -ForegroundColor Gray
Write-Host "    - C:\Users\MerlijnU\analysis\anova\glm_lr_tests_all_repos.csv" -ForegroundColor Gray
Write-Host ""

################################################################################
# STEP 3: Display GLM Results (Per Repository)
# Purpose: Generate human-readable summaries and visualizations
# Input:   C:\Users\MerlijnU\analysis\<RepoName>\anova\*.csv
# Output:  Console output + interaction plots (optional)
################################################################################

Write-Host "STEP 3: Displaying GLM Results (Per Repository)" -ForegroundColor Green
Write-Host ("-"*100) -ForegroundColor Gray
Write-Host ""

Write-Host "[1/5] Marsen.NetCore.Dojo.Integration.Test" -ForegroundColor Cyan
python display_glm_results.py `
    --glm_dir "C:/Users/MerlijnU/analysis/Marsen.NetCore.Dojo.Integration.Test/anova" `
    --solution "Marsen.NetCore.Dojo.Integration.Test"

Write-Host ""
Write-Host "[2/5] MoreLinq" -ForegroundColor Cyan
python display_glm_results.py `
    --glm_dir "C:/Users/MerlijnU/analysis/MoreLinq/anova" `
    --solution "MoreLinq"

Write-Host ""
Write-Host "[3/5] CsvHelper" -ForegroundColor Cyan
python display_glm_results.py `
    --glm_dir "C:/Users/MerlijnU/analysis/CsvHelper/anova" `
    --solution "CsvHelper"

Write-Host ""
Write-Host "[4/5] GuardClauses" -ForegroundColor Cyan
python display_glm_results.py `
    --glm_dir "C:/Users/MerlijnU/analysis/GuardClauses/anova" `
    --solution "GuardClauses"

Write-Host ""
Write-Host "[5/5] TimeProviderExtensions" -ForegroundColor Cyan
python display_glm_results.py `
    --glm_dir "C:/Users/MerlijnU/analysis/TimeProviderExtensions/anova" `
    --solution "TimeProviderExtensions"

Write-Host ""
Write-Host "✓ Step 3 Complete" -ForegroundColor Green
Write-Host "  Review console output for repository-specific insights" -ForegroundColor Gray
Write-Host ""

################################################################################
# STEP 4: Display Aggregated GLM Results
# Purpose: Show cross-repository patterns and meta-analysis
# Input:   C:\Users\MerlijnU\analysis\anova\*_all_repos.csv
# Output:  Console summary of overall effects
################################################################################

Write-Host "STEP 4: Displaying Aggregated GLM Results" -ForegroundColor Green
Write-Host ("-"*100) -ForegroundColor Gray
Write-Host ""
Write-Host "Analyzing cross-repository patterns..." -ForegroundColor Cyan

python display_glm_results.py `
    --glm_dir "C:/Users/MerlijnU/analysis/anova" `
    --solution "All"

Write-Host ""
Write-Host "✓ Step 4 Complete" -ForegroundColor Green
Write-Host "  Cross-repository effect patterns displayed" -ForegroundColor Gray
Write-Host ""

################################################################################
# STEP 5: Analyze Heuristic Performance by Algorithm
# Purpose: Identify optimal heuristic configurations for each algorithm
# Input:   C:\Users\MerlijnU\analysis\anova\*_all_repos.csv
# Output:  
#   - Console output with 4 parts:
#     Part 1: Main effects
#     Part 2: Interaction effects
#     Part 3: Rankings by algorithm
#     Part 4: Optimal configurations
#   - CSV exports: part4_best_configs_<repo>.csv (5 files)
################################################################################

Write-Host "STEP 5: Analyzing Heuristic Performance by Algorithm" -ForegroundColor Green
Write-Host ("-"*100) -ForegroundColor Gray
Write-Host ""
Write-Host "Finding optimal heuristic combinations..." -ForegroundColor Cyan
Write-Host "This performs exhaustive search of 2^6 = 64 configurations per algorithm×repo" -ForegroundColor Gray
Write-Host ""

python analyze_heuristic_performance.py `
    --glm_dir "C:/Users/MerlijnU/analysis/anova" `
    --save_csv

Write-Host ""
Write-Host "✓ Step 5 Complete" -ForegroundColor Green
Write-Host "  Output (console): 4-part analysis" -ForegroundColor Gray
Write-Host "  Output (CSV): C:/Users/MerlijnU/analysis/part4_best_configs_*.csv (5 files)" -ForegroundColor Gray
Write-Host ""

################################################################################
# STEP 6: Analyze Universal Configuration
# Purpose: Determine single best configuration across algorithms/repos
# Input:   C:\Users\MerlijnU\analysis\part4_best_configs_*.csv
# Output:  Console output with frequency analysis and recommendations
################################################################################

Write-Host "STEP 6: Analyzing Universal Configuration" -ForegroundColor Green
Write-Host ("-"*100) -ForegroundColor Gray
Write-Host ""
Write-Host "Determining universal heuristic recommendations..." -ForegroundColor Cyan

python analyze_universal_config.py

Write-Host ""
Write-Host "✓ Step 6 Complete" -ForegroundColor Green
Write-Host "  Universal configuration recommendations displayed" -ForegroundColor Gray
Write-Host ""

################################################################################
# STEP 7: Verify LaTeX Table Data
# Purpose: Ensure all LaTeX tables match Python outputs exactly
# Input:   
#   - OPTIMAL_CONFIGS_LATEX_TABLES.tex
#   - C:\Users\MerlijnU\analysis\part4_best_configs_*.csv
# Output:  Verification report (console + markdown)
################################################################################

Write-Host "STEP 7: Verifying LaTeX Table Data" -ForegroundColor Green
Write-Host ("-"*100) -ForegroundColor Gray
Write-Host ""
Write-Host "Cross-checking all tables..." -ForegroundColor Cyan

python verify_latex_data.py

Write-Host ""
Write-Host "✓ Step 7 Complete" -ForegroundColor Green
Write-Host "  LaTeX data verification complete" -ForegroundColor Gray
Write-Host ""

################################################################################
# PIPELINE COMPLETE
################################################################################

Write-Host ""
Write-Host "=" -NoNewline -ForegroundColor Cyan
Write-Host ("="*99) -ForegroundColor Cyan
Write-Host "✓✓✓ PIPELINE COMPLETE ✓✓✓" -ForegroundColor Yellow
Write-Host ("="*100) -ForegroundColor Cyan
Write-Host ""
Write-Host "All analysis steps completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "KEY OUTPUT FILES:" -ForegroundColor Yellow
Write-Host "  1. OPTIMAL_CONFIGS_LATEX_TABLES.tex" -ForegroundColor White
Write-Host "     → Ready-to-compile LaTeX tables (10 tables)" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. C:\Users\MerlijnU\analysis\part4_best_configs_*.csv" -ForegroundColor White
Write-Host "     → Optimal configurations for each repository (5 files)" -ForegroundColor Gray
Write-Host ""
Write-Host "  3. C:\Users\MerlijnU\analysis\anova\*_all_repos.csv" -ForegroundColor White
Write-Host "     → Aggregated GLM results across all repositories" -ForegroundColor Gray
Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Yellow
Write-Host "  • Compile OPTIMAL_CONFIGS_LATEX_TABLES.tex with pdflatex" -ForegroundColor White
Write-Host "  • Review console output from Steps 3-6 for insights" -ForegroundColor White
Write-Host "  • Use verify_latex_data.py anytime to check data integrity" -ForegroundColor White
Write-Host ""
Write-Host ("="*100) -ForegroundColor Cyan