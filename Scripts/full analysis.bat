@echo off
REM ============================================================================
REM STRYKER.NET HOM COMPLETE ANALYSIS PIPELINE
REM From Raw CSV to LaTeX Tables
REM ============================================================================

echo ================================================================================
echo STRYKER.NET HOM ANALYSIS PIPELINE
echo ================================================================================
echo.

cd /d "C:\Users\MerlijnU\Repos\stryker-net-homt\Scripts"

REM ============================================================================
REM STEP 1: Process Raw HOM CSV Files
REM ============================================================================
echo [STEP 1/7] Processing Raw HOM CSV Files...
echo --------------------------------------------------------------------------------
echo.

echo [1/5] Marsen.NetCore.Dojo...
python analyze_homcsv_enhanced.py --root "C:\users\merlijnu\outputs\Marsen.NetCore.Dojo" --solution "Marsen.NetCore.Dojo.Integration.Test"
if errorlevel 1 goto :error

echo [2/5] MoreLinq...
python analyze_homcsv_enhanced.py --root "C:\users\merlijnu\outputs\MoreLinq" --solution "MoreLinq"
if errorlevel 1 goto :error

echo [3/5] CsvHelper...
python analyze_homcsv_enhanced.py --root "C:\users\merlijnu\outputs\CsvHelper" --solution "CsvHelper"
if errorlevel 1 goto :error

echo [4/5] GuardClauses...
python analyze_homcsv_enhanced.py --root "C:\users\merlijnu\outputs\GuardClauses" --solution "GuardClauses"
if errorlevel 1 goto :error

echo [5/5] TimeProviderExtensions...
python analyze_homcsv_enhanced.py --root "C:\users\merlijnu\outputs\TimeProviderExtensions" --solution "TimeProviderExtensions"
if errorlevel 1 goto :error

echo.
echo Step 1 Complete!
echo.

REM ============================================================================
REM STEP 2: Run GLM Binomial ANOVA
REM ============================================================================
echo [STEP 2/7] Running GLM Binomial ANOVA...
echo --------------------------------------------------------------------------------
echo.

python glm_binomial_anova.py --solution Marsen.NetCore.Dojo.Integration.Test CsvHelper GuardClauses MoreLinq TimeProviderExtensions --repo Marsen.NetCore.Dojo CsvHelper GuardClauses MoreLinq TimeProviderExtensions
if errorlevel 1 goto :error

echo.
echo Step 2 Complete!
echo.

REM ============================================================================
REM STEP 3: Display GLM Results (Per Repository)
REM ============================================================================
echo [STEP 3/7] Displaying GLM Results (Per Repository)...
echo --------------------------------------------------------------------------------
echo.

python display_glm_results.py --glm_dir "C:/Users/MerlijnU/analysis/Marsen.NetCore.Dojo.Integration.Test/anova" --solution "Marsen.NetCore.Dojo.Integration.Test"
if errorlevel 1 goto :error

python display_glm_results.py --glm_dir "C:/Users/MerlijnU/analysis/MoreLinq/anova" --solution "MoreLinq"
if errorlevel 1 goto :error

python display_glm_results.py --glm_dir "C:/Users/MerlijnU/analysis/CsvHelper/anova" --solution "CsvHelper"
if errorlevel 1 goto :error

python display_glm_results.py --glm_dir "C:/Users/MerlijnU/analysis/GuardClauses/anova" --solution "GuardClauses"
if errorlevel 1 goto :error

python display_glm_results.py --glm_dir "C:/Users/MerlijnU/analysis/TimeProviderExtensions/anova" --solution "TimeProviderExtensions"
if errorlevel 1 goto :error

echo.
echo Step 3 Complete!
echo.

REM ============================================================================
REM STEP 4: Display Aggregated Results
REM ============================================================================
echo [STEP 4/7] Displaying Aggregated GLM Results...
echo --------------------------------------------------------------------------------
echo.

python display_glm_results.py --glm_dir "C:/Users/MerlijnU/analysis/anova" --solution "All"
if errorlevel 1 goto :error

echo.
echo Step 4 Complete!
echo.

REM ============================================================================
REM STEP 5: Analyze Heuristic Performance by Algorithm
REM ============================================================================
echo [STEP 5/7] Analyzing Heuristic Performance by Algorithm...
echo --------------------------------------------------------------------------------
echo.

python analyze_heuristic_performance.py --glm_dir "C:/Users/MerlijnU/analysis/anova" --save_csv
if errorlevel 1 goto :error

echo.
echo Step 5 Complete!
echo.

REM ============================================================================
REM STEP 6: Analyze Universal Configuration
REM ============================================================================
echo [STEP 6/7] Analyzing Universal Configuration...
echo --------------------------------------------------------------------------------
echo.

python analyze_universal_config.py
if errorlevel 1 goto :error

echo.
echo Step 6 Complete!
echo.

REM ============================================================================
REM STEP 7: Verify LaTeX Data
REM ============================================================================
echo [STEP 7/7] Verifying LaTeX Table Data...
echo --------------------------------------------------------------------------------
echo.

python verify_latex_data.py
if errorlevel 1 goto :error

echo.
echo Step 7 Complete!
echo.

REM ============================================================================
REM SUCCESS
REM ============================================================================
echo.
echo ================================================================================
echo PIPELINE COMPLETE - ALL STEPS SUCCEEDED!
echo ================================================================================
echo.
echo Key output files:
echo   - OPTIMAL_CONFIGS_LATEX_TABLES.tex
echo   - C:\Users\MerlijnU\analysis\part4_best_configs_*.csv
echo   - C:\Users\MerlijnU\analysis\anova\*_all_repos.csv
echo.
echo Next: Compile OPTIMAL_CONFIGS_LATEX_TABLES.tex with pdflatex
echo.
pause
exit /b 0

:error
echo.
echo ================================================================================
echo ERROR: Pipeline failed at step above!
echo ================================================================================
echo.
pause
exit /b 1