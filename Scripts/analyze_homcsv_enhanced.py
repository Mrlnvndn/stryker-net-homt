#!/usr/bin/env python3
"""
Enhanced L12 Orthogonal Array Analysis with Bootstrap Resampling
==================================================================

WHAT THIS SCRIPT DOES:
----------------------
Analyzes mutation testing experiments using an L12 orthogonal array design to 
evaluate the effects of 6 binary heuristic factors on:
  1. SSHOM Success Rate (percentage of Strong-Semantic Higher-Order Mutants)
  2. Runtime (milliseconds for HOM generation)

Compares two algorithms (genetic vs local) and generates publication-ready plots
showing main effects and interactions with non-parametric bootstrap confidence intervals.

WHY WE USE THIS APPROACH:
-------------------------
1. **L12 Orthogonal Array Design**: Efficiently tests 6 factors (64 combinations) 
   with only 12 experimental configurations, providing balanced coverage while 
   minimizing experimental runs.

2. **Bootstrap Resampling (B=2000)**: Provides robust confidence interval estimation 
   without parametric assumptions, suitable for:
   - Small sample sizes (n=30 per cell: 6 OA configs × 5 reps)
   - Non-normal distributions (binomial proportions, skewed runtimes)
   - Complex estimands (differences, ratios with corrected denominators)

3. **Row-Level Bootstrap**: Preserves correlation structure within experimental runs,
   where HOMs from the same run share configuration and execution environment.

HOW IT WORKS:
-------------
1. **Data Loading**: Reads raw experimental CSV files (mutation-report-hom.csv)
   - 12 OA configurations × 5 repetitions × 2 algorithms = 120 total runs
   - Extracts: sshom_2/3/4 counts, total_homs_with_filtered, hom_generation_ms

2. **Bootstrap Confidence Intervals**:
   For SSHOM (binomial proportions):
     - Resample 30 rows (6 OA configs × 5 reps) with replacement
     - Recalculate proportion: p = (sshom_2+sshom_3+sshom_4) / total_homs
     - Repeat 2000 times → percentile CI [Q₀.₀₂₅, Q₀.₉₇₅]
   
   For Runtime (continuous data):
     - Resample raw millisecond values with replacement
     - Calculate mean of resampled data
     - Repeat 2000 times → percentile CI

3. **Effect Calculation**:
   - Point estimate: Effect = mean(ON) - mean(OFF) from RAW data
   - CI for effect: Bootstrap differences Δ = ON_samples - OFF_samples
   - Performed for all (algorithm, heuristic) combinations

4. **Visualization**:
   - Plot 1: Main effects bar charts (Panel A: SSHOM, Panel B: Runtime)
   - Plot 2: Interaction line plots (6 heuristics × 2 algorithms)
   - Error bars from bootstrap percentile CIs (currently disabled)

HOW TO USE:
-----------
Basic usage:
  python analyze_homcsv_enhanced.py --root <data_dir> --solution <name>

Example:
  python analyze_homcsv_enhanced.py \\
    --root "C:/outputs" \\
    --solution "MoreLinq" \\
    --bootstrap 2000 \\
    --seed 42

Arguments:
  --root PATH          Root directory with structure: <root>/<Solution>/<Algorithm>/OAxx/repyy/reports/*.csv
  --solution NAME      Solution/repository name to analyze (e.g., "MoreLinq", "TimeProviderExtensions")
  --bootstrap N        Number of bootstrap samples (default: 2000)
  --seed N            Random seed for reproducibility (default: 42)

Expected Directory Structure:
  <root>/
    <Solution>/
      genetic/
        OA01/
          rep01/reports/mutation-report-hom.csv
          rep02/reports/mutation-report-hom.csv
          ...
        OA02/
          ...
      local/
        OA01/
          ...

Required CSV Columns (from mutation-report-hom.csv):
  - sshom_2, sshom_3, sshom_4: SSHOM counts by distance
  - total_homs_with_filtered: Total HOMs including filtered
  - hom_generation_ms: Runtime in milliseconds
  - used_heuristics: Comma-separated list of active heuristics

Output Files (saved to C:/Users/<user>/analysis/<Solution>/latex_data/):
  1. main_effects_bootstrap_latex.csv        - Main effects on SSHOM with bootstrap CIs
  2. main_effects_runtime_bootstrap_latex.csv - Main effects on runtime with bootstrap CIs
  3. interaction_effects_latex.csv           - Interaction effects with bootstrap CIs
  4. plot1_main_effects.png                  - Main effects visualization (2 panels)
  5. plot2_interactions.png                  - Interaction plots (6 small multiples)
  6. analysis_summary.txt                    - Text report with data overview

STATISTICAL DETAILS:
--------------------
- Bootstrap Method: Stratified row-level resampling with replacement
- Sample Size: B=2000 bootstrap iterations per estimate
- Confidence Level: 95% (percentile method: 2.5th and 97.5th percentiles)
- Point Estimates: Calculated directly from raw data (not bootstrap mean)
- Effect CIs: Bootstrap distribution of differences (ON - OFF)
- Reproducibility: Fixed seed ensures identical results across runs

For more details on the statistical methodology, see the thesis chapter on
"Bootstrap Resampling for L12 Orthogonal Array Analysis".

DEPENDENCIES:
-------------
Required: pandas, numpy, matplotlib, pathlib
Optional: scipy (for faster bootstrap, falls back to manual implementation)

Author: Merlijn van Uden
Date: 2025-10-03
"""

import pandas as pd
import numpy as np
from pathlib import Path
import glob
import warnings
import logging
import argparse
import re
import matplotlib.pyplot as plt

# Try to import scipy modules, fall back to manual implementation if not available
try:
    from scipy.stats import bootstrap
    HAS_SCIPY_BOOTSTRAP = True
except ImportError:
    HAS_SCIPY_BOOTSTRAP = False

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

# Suppress warnings
warnings.filterwarnings('ignore')


# ==============================================================================
# SHARED BOOTSTRAP FUNCTIONS FOR CONSISTENT ERROR BARS
# ==============================================================================

def bootstrap_prop_ci(df, heuristic_col, level_val, algorithm, B=2000, seed=42, 
                      return_samples=False):
    """
    Hierarchical stratified bootstrap respecting L12 orthogonal array structure.
    
    For a given (algorithm, heuristic level) cell:
    - Groups rows by OA row (expect 6 OA rows per level in L12 design)
    - In each bootstrap replicate:
      * Sample exactly 6 OA rows with replacement
      * For each selected OA row, resample its 5 repeats with replacement
      * Aggregate successes/trials and compute p_hat = total_successes / total_trials
    - Returns percentile CI [2.5%, 97.5%] from bootstrap distribution
    
    Parameters
    ----------
    df : pd.DataFrame
        Data with columns: algorithm, heuristic_col, oa_row, rep (optional),
        sshom_2/3/4, total_homs_with_filtered
    heuristic_col : str
        Name of the heuristic column to filter on
    level_val : int
        Level value (0 or 1) to filter for
    algorithm : str
        Algorithm name to filter for
    B : int
        Number of bootstrap samples (default 2000)
    seed : int
        Random seed for reproducibility (default 42)
    return_samples : bool
        If True, return bootstrap samples array (default False)
    
    Returns
    -------
    dict with keys:
        - mean: Point estimate (S_all/N_all on original data)
        - lo: 2.5th percentile of bootstrap distribution
        - hi: 97.5th percentile of bootstrap distribution
        - samples: Bootstrap sample array (if return_samples=True)
        - n_trials: Total trials (denominator) in original data
    """
    # Filter to the specific cell
    mask = (df['algorithm'] == algorithm) & (df[heuristic_col] == level_val)
    cell_data = df[mask].copy()
    
    if len(cell_data) == 0:
        logger.warning(f"No data for {algorithm}/{heuristic_col}={level_val}")
        return {'mean': np.nan, 'lo': np.nan, 'hi': np.nan, 'n_trials': 0}
    
    # Check for rep column
    have_rep = 'rep' in cell_data.columns
    
    # Use corrected denominator column
    denom_col = 'total_homs_with_filtered' if 'total_homs_with_filtered' in cell_data.columns else 'total_homs'
    
    # Get available SSHOM columns
    sshom_cols = [c for c in ['sshom_2', 'sshom_3', 'sshom_4'] if c in cell_data.columns]
    
    if not sshom_cols:
        logger.warning(f"No SSHOM columns for {algorithm}/{heuristic_col}={level_val}")
        return {'mean': np.nan, 'lo': np.nan, 'hi': np.nan, 'n_trials': 0}
    
    # Calculate point estimate on original data
    S_all = cell_data[sshom_cols].fillna(0).to_numpy().sum()
    N_all = cell_data[denom_col].fillna(0).to_numpy().sum()
    
    if N_all == 0:
        logger.warning(f"Zero trials for {algorithm}/{heuristic_col}={level_val}")
        return {'mean': np.nan, 'lo': np.nan, 'hi': np.nan, 'n_trials': 0}
    
    mean_prop = S_all / N_all
    
    # Group by OA row - expect 6 rows per level in L12 design
    if 'oa_row' not in cell_data.columns:
        logger.error(f"Missing 'oa_row' column for hierarchical bootstrap")
        # Fallback to simple row-level bootstrap
        rng = np.random.default_rng(seed)
        n_rows = len(cell_data)
        bootstrap_props = []
        
        for _ in range(B):
            boot_indices = rng.integers(0, n_rows, size=n_rows)
            boot_sample = cell_data.iloc[boot_indices]
            S_boot = boot_sample[sshom_cols].fillna(0).to_numpy().sum()
            N_boot = boot_sample[denom_col].fillna(0).to_numpy().sum()
            p_boot = 0.0 if N_boot == 0 else S_boot / N_boot
            bootstrap_props.append(np.clip(p_boot, 0.0, 1.0))
        
        bootstrap_props = np.array(bootstrap_props)
    else:
        # Hierarchical bootstrap respecting L12 structure
        groups = cell_data.groupby('oa_row', dropna=False)
        oa_keys = list(groups.groups.keys())
        n_oa_rows = len(oa_keys)
        
        # Warn if not exactly 6 OA rows (L12 design requirement)
        if n_oa_rows != 6:
            logger.warning(f"Expected 6 OA rows for {algorithm}/{heuristic_col}={level_val}, found {n_oa_rows}")
        
        take_rows = min(6, n_oa_rows)
        rng = np.random.default_rng(seed)
        B = int(B)
        bootstrap_props = np.empty(B, dtype=float)
        
        for b in range(B):
            # Sample OA rows with replacement
            chosen_oa_rows = rng.choice(oa_keys, size=take_rows, replace=True)
            S_b = 0.0
            N_b = 0.0
            
            for oa_row in chosen_oa_rows:
                g = groups.get_group(oa_row)
                
                # Resample repeats within this OA row with replacement
                if have_rep and len(g) > 1:
                    idx = rng.integers(0, len(g), size=len(g))
                    g = g.iloc[idx]
                
                # Aggregate successes and trials
                S_b += g[sshom_cols].fillna(0).to_numpy().sum()
                N_b += g[denom_col].fillna(0).to_numpy().sum()
            
            bootstrap_props[b] = 0.0 if N_b == 0 else S_b / N_b
    
    # Calculate percentile CI
    lo, hi = np.percentile(bootstrap_props, [2.5, 97.5])
    
    # Clip mean to [0, 1]
    mean_prop = np.clip(mean_prop, 0.0, 1.0)
    
    result = {
        'mean': float(mean_prop),
        'lo': float(lo),
        'hi': float(hi),
        'n_trials': int(N_all)
    }
    
    if return_samples:
        result['samples'] = bootstrap_props
    
    return result


# Configure matplotlib for high-quality plots
plt.style.use('default')
plt.rcParams['figure.facecolor'] = 'white'
plt.rcParams['axes.facecolor'] = 'white'
plt.rcParams['font.size'] = 10
plt.rcParams['axes.labelsize'] = 11
plt.rcParams['axes.titlesize'] = 12
plt.rcParams['legend.fontsize'] = 10
plt.rcParams['xtick.labelsize'] = 9
plt.rcParams['ytick.labelsize'] = 9

class L12AnalysisEnhanced:
    def __init__(self, base_path=None, solution=None, bootstrap_n=2000, bootstrap_seed=42):
        """Initialize the analysis with path configuration."""
        self.base_path = Path(base_path)
        self.solution = solution
        self.bootstrap_n = bootstrap_n
        self.bootstrap_seed = bootstrap_seed

        # L12 orthogonal array configuration - MUST MATCH run-homt-oa.ps1
        # PowerShell HMap order: H1=CodeLocation, H2=EmptyAssessingTests, H3=MutatorType,
        #                        H4=OverlappingTests, H5=MaxSizeLimit, H6=SyntaxNodeConflict
        self.heuristics = [
            'CodeLocation',        # H1 - Position 0
            'EmptyAssessingTests', # H2 - Position 1  
            'MutatorType',         # H3 - Position 2
            'OverlappingTests',    # H4 - Position 3
            'MaxSizeLimit',        # H5 - Position 4 
            'SyntaxNodeConflict'   # H6 - Position 5
        ]
        
        # L12 orthogonal array design matrix - converted from PowerShell $OA_L12 (1,2) to Python (0,1)
        self.l12_matrix = [
            [0, 0, 0, 0, 0, 0],  # OA01: H1=1,H2=1,H3=1,H4=1,H5=1,H6=1 -> all low
            [0, 0, 0, 0, 0, 1],  # OA02: H1=1,H2=1,H3=1,H4=1,H5=1,H6=2 -> H6 high
            [0, 0, 1, 1, 1, 0],  # OA03: H1=1,H2=1,H3=2,H4=2,H5=2,H6=1 -> H3,H4,H5 high
            [0, 1, 0, 1, 1, 0],  # OA04: H1=1,H2=2,H3=1,H4=2,H5=2,H6=1 -> H2,H4,H5 high
            [0, 1, 1, 0, 1, 1],  # OA05: H1=1,H2=2,H3=2,H4=1,H5=2,H6=2 -> H2,H3,H5,H6 high
            [0, 1, 1, 1, 0, 1],  # OA06: H1=1,H2=2,H3=2,H4=2,H5=1,H6=2 -> H2,H3,H4,H6 high
            [1, 0, 1, 1, 0, 0],  # OA07: H1=2,H2=1,H3=2,H4=2,H5=1,H6=1 -> H1,H3,H4 high
            [1, 0, 1, 0, 1, 1],  # OA08: H1=2,H2=1,H3=2,H4=1,H5=2,H6=2 -> H1,H3,H5,H6 high
            [1, 0, 0, 1, 1, 1],  # OA09: H1=2,H2=1,H3=1,H4=2,H5=2,H6=2 -> H1,H4,H5,H6 high
            [1, 1, 1, 0, 0, 0],  # OA10: H1=2,H2=2,H3=2,H4=1,H5=1,H6=1 -> H1,H2,H3 high
            [1, 1, 0, 1, 0, 1],  # OA11: H1=2,H2=2,H3=1,H4=2,H5=1,H6=2 -> H1,H2,H4,H6 high
            [1, 1, 0, 0, 1, 0],  # OA12: H1=2,H2=2,H3=1,H4=1,H5=2,H6=1 -> H1,H2,H5 high
        ]
        
        self.algorithms = ['genetic', 'local']
    
    @staticmethod
    def format_pvalue(p):
        """Format p-value with consistent display: '<0.0001' instead of '0.0000'.
        
        Args:
            p: p-value to format
            
        Returns:
            Formatted p-value string
        """
        if p < 0.0001:
            return "<0.0001"
        elif p < 0.001:
            return f"{p:.4f}"
        elif p < 0.01:
            return f"{p:.3f}"
        else:
            return f"{p:.2f}"
    
    @staticmethod
    def get_significance_marker(p):
        """Get significance marker based on unified scheme.
        
        Significance levels:
        *** p<0.001 (highly significant)
        ** p<0.01 (very significant)
        * p<0.05 (significant)
        † p<0.10 (marginally significant)
        ns ≥0.10 (not significant)
        
        Args:
            p: p-value
            
        Returns:
            Significance marker string
        """
        if p < 0.001:
            return "***"
        elif p < 0.01:
            return "**"
        elif p < 0.05:
            return "*"
        elif p < 0.10:
            return "†"
        else:
            return "ns"
    
    @staticmethod
    def get_significance_legend_text():
        """Get standardized legend text for significance markers.
        
        Returns:
            Legend text string
        """
        return "Significance: *** p<0.001, ** p<0.01, * p<0.05, † p<0.10, ns ≥0.10"
        
    def find_run_csvs(self, root_path, solution):
        """Find all CSV files in the expected directory structure."""
        # Look for CSV files in the nested structure: <Repo>/<Solution>/<Algorithm>/OAxx/repyy/reports/*.csv
        csv_files = []
        
        # Search patterns for CSV files - ONLY match mutation-report-hom.csv to avoid pulling in
        # GLM outputs, analysis results, and other CSV artifacts

        pattern = "**/reports/mutation-report-hom.csv"  # Specific filename in reports directory
        
        # add solution to end of root_path
        if solution:
            root_path = root_path / solution

        found_files = list(root_path.rglob(pattern))
        csv_files.extend([f for f in found_files if f.is_file() and f not in csv_files])

        return csv_files
    
    def parse_path_metadata(self, csv_path):
        """Extract metadata from the file path structure."""
        import re
        
        # Expected structure: C:\Users\MerlijnU\outputs\MoreLinq\MoreLinq\genetic\OA01\rep01\reports\mutation-report-hom.csv
        parts = list(csv_path.resolve().parents)
        
        metadata = {
            "solution": None,
            "algorithm": None,
            "oa_row": None,
            "rep": None,
            "run_id": None
        }
        
        # Work backwards from the file to find structure
        for i, part in enumerate(parts):
            part_name = part.name
            
            # Look for repetition pattern (rep01, rep02, etc.)
            if re.match(r"rep\d{2}$", part_name, re.IGNORECASE):
                try:
                    metadata["rep"] = int(part_name[3:])
                except:
                    metadata["rep"] = None
            
            # Look for OA pattern (OA01, OA02, etc.)
            elif re.match(r"OA\d{2}$", part_name, re.IGNORECASE):
                try:
                    metadata["oa_row"] = int(part_name[2:])
                    metadata["run_id"] = metadata["oa_row"]  # Map OA number to run_id
                except:
                    metadata["oa_row"] = None
            
            # Look for algorithm directory
            elif part_name.lower() in ['genetic', 'local']:
                metadata["algorithm"] = part_name.lower()
            
            # Look for solution directory (should be parent of algorithm)
            elif metadata["algorithm"] and i < len(parts) - 1:
                # This should be the solution name
                if not metadata["solution"]:
                    metadata["solution"] = part_name
        
        return metadata

    def load_experimental_data(self):
        """Load and combine all CSV files from the experiment runs."""
        logger.info("Loading experimental data...")
        
        # Find CSV files in the nested directory structure
        csv_files = self.find_run_csvs(self.base_path, self.solution)
        
        if not csv_files:
            logger.warning(f"No CSV files found in {self.base_path}")
            return pd.DataFrame()
            
        logger.info(f"Found {len(csv_files)} CSV files")
        
        all_data = []
        
        for csv_file in csv_files:
            try:
                
                # Parse metadata from file path
                metadata = self.parse_path_metadata(csv_file)

                # Log metadata extraction issues
                if not metadata["solution"] or not metadata["algorithm"] or not metadata["oa_row"] or not metadata["rep"]:
                    logger.warning(f"Could not fully parse metadata from path: {csv_file}. Parsed: {metadata}")

                # Read the CSV file
                df = pd.read_csv(csv_file, engine="python")
                
                if df.empty:
                    logger.warning(f"Empty CSV file: {csv_file}")
                    continue
                
                # Add metadata columns
                df['source_file'] = str(csv_file)
                df['solution'] = metadata["solution"]
                df['algorithm'] = metadata["algorithm"] 
                df['oa_row'] = metadata["oa_row"]
                df['rep'] = metadata["rep"]
                df['run_id'] = metadata["run_id"]
                                
                available_sshom_cols = ['sshom_2', 'sshom_3', 'sshom_4']
                                
                hom_total_col = 'total_homs'
                
                original_hom_total = df[hom_total_col].fillna(0)
                    

                filtered_cols = ['filtered_empty_total', 'filtered_invalid_total']
                total_homs_with_filtered = original_hom_total.copy()
                    
                for col in filtered_cols:
                    if col in df.columns:
                        total_homs_with_filtered += df[col].fillna(0)
                        logger.debug(f"Added {col} to total_homs_with_filtered")
                
                df['total_homs_with_filtered'] = total_homs_with_filtered
                df['total_homs_kept_only'] = original_hom_total
                
                logger.debug(f"Calculated total_homs_with_filtered (includes filtered HOMs): {total_homs_with_filtered.iloc[0] if len(total_homs_with_filtered) > 0 else 0}")
                
                # ALWAYS recalculate sshom_rate_percent if we have the necessary data
                # The CSV files contain the OLD calculation (without filtered HOMs), so we must override it
                if available_sshom_cols and 'total_homs_with_filtered' in df.columns:
                    # Calculate total SSHOMs
                    df['total_sshoms'] = df[available_sshom_cols].fillna(0).sum(axis=1)
                    
                    # Store the original (uncorrected) value from CSV for comparison
                    if 'sshom_rate_percent' in df.columns:
                        df['sshom_rate_percent_original'] = df['sshom_rate_percent'].copy()
                    
                    # Recalculate sshom_rate_percent using total_homs_with_filtered (includes filtered HOMs)
                    # Set to NaN when denominator is zero (do NOT replace 0 with 1)
                    denom = df['total_homs_with_filtered']
                    df['sshom_rate_percent'] = np.where(denom > 0, 100.0 * df['total_sshoms'] / denom, np.nan)
                    
                    # Also calculate the old rate for comparison (without filtered HOMs)
                    if 'total_homs_kept_only' in df.columns:
                        denom_old = df['total_homs_kept_only']
                        df['sshom_rate_without_filtered_percent'] = np.where(denom_old > 0, 100.0 * df['total_sshoms'] / denom_old, np.nan)
                    
                    # Count rows with valid SSHOM rates
                    valid_rows = df['sshom_rate_percent'].notna().sum()
                    total_rows = len(df)
                    if valid_rows < total_rows:
                        logger.warning(f"  {total_rows - valid_rows}/{total_rows} rows have zero denominator (set to NaN)")
                    
                    if valid_rows > 0:
                        logger.debug(f"CORRECTED sshom_rate_percent calculated: {df['sshom_rate_percent'].dropna().iloc[0]:.2f}% (includes filtered HOMs)")
                        if 'sshom_rate_percent_original' in df.columns:
                            logger.debug(f"  Original CSV value was: {df['sshom_rate_percent_original'].dropna().iloc[0]:.2f}% (excluded filtered HOMs)")
                    logger.debug(f"Recalculated sshom_rate_percent using {available_sshom_cols} columns with filtered HOMs included")
                else:
                    # Provide detailed diagnostics about what's missing
                    missing_info = []
                    if not available_sshom_cols:
                        missing_info.append("SSHOM columns (tried: sshom_2/3/4, sshoms_2nd/3rd/4th)")
                    if 'total_homs_with_filtered' not in df.columns:
                        missing_info.append("total_homs_with_filtered column")
                    
                    logger.warning(f"Cannot calculate corrected SSHOM rate - missing: {', '.join(missing_info)} in {csv_file}")
                    logger.warning(f"  Available columns: {list(df.columns)}")
                
                # If we have multiple rows in CSV, pick the summary row (typically the last row)
                if len(df) > 1:
                    # Look for rows that contain summary data (non-null key fields)
                    key_fields = ["total_homs_with_filtered", "sshom_rate_percent", "used_heuristics"]
                    summary_candidates = df.dropna(subset=[col for col in key_fields if col in df.columns])
                    
                    if not summary_candidates.empty:
                        df = summary_candidates.tail(1)  # Take last summary row
                    else:
                        df = df.tail(1)  # Take last row if no clear summary
                
                all_data.append(df)
                
                logger.debug(f"Loaded: {csv_file} -> Solution: {metadata['solution']}, "
                           f"Algorithm: {metadata['algorithm']}, OA: {metadata['oa_row']}, Rep: {metadata['rep']}")
                
            except Exception as e:
                logger.error(f"Error loading {csv_file}: {e}")
                continue
                
        if not all_data:
            logger.error("No valid CSV data could be loaded")
            return pd.DataFrame()
            
        combined_df = pd.concat(all_data, ignore_index=True)
        logger.info(f"Loaded {len(combined_df)} total observations")
        
        # Log some statistics about what was loaded
        if not combined_df.empty:
            solutions = combined_df['solution'].dropna().nunique()
            algorithms = combined_df['algorithm'].dropna().nunique()
            oa_rows = combined_df['oa_row'].dropna().nunique()
            logger.info(f"Data summary: {solutions} solutions, {algorithms} algorithms, {oa_rows} OA rows")
        
        return combined_df
    
    def parse_heuristics_from_data(self, used_heuristics_str):
        """Parse the used_heuristics string to determine which heuristics are ON."""
        import re
        
        if pd.isna(used_heuristics_str) or not isinstance(used_heuristics_str, str):
            return []
        
        # Clean the string and handle various formats
        used_heuristics_str = str(used_heuristics_str).strip()
        
        # Handle special cases
        if used_heuristics_str.lower() in ['', 'nan', 'none', 'null']:
            return []
        
        # Split on common delimiters
        tokens = re.split(r"[,\s;\-|]+", used_heuristics_str)
        tokens = [t.strip() for t in tokens if t.strip()]
        
        # Match tokens against known heuristics
        heuristics_on = []
        for heuristic in self.heuristics:
            for token in tokens:
                if heuristic.lower() in token.lower() or token.lower() in heuristic.lower():
                    if heuristic not in heuristics_on:
                        heuristics_on.append(heuristic)
                    break
        
        return heuristics_on

    def build_experiment_table(self, data):
        """Map experimental data to L12 orthogonal array configuration."""
        logger.info("Building experiment configuration table...")
        
        # Create the L12 design table
        experiment_configs = []
        
        for run_id, levels in enumerate(self.l12_matrix, 1):
            config = {'run_id': run_id}
            
            # Add heuristic levels
            for i, heuristic in enumerate(self.heuristics):
                config[heuristic] = levels[i]
                
            experiment_configs.append(config)
            
        experiment_df = pd.DataFrame(experiment_configs)
        
        # Start with a copy of the data
        merged_data = data.copy()
        
        # Parse heuristics from the used_heuristics column if it exists
        if 'used_heuristics' in merged_data.columns:
            logger.info("Parsing heuristics from 'used_heuristics' column")
            
            for idx, row in merged_data.iterrows():
                heuristics_on = self.parse_heuristics_from_data(row.get('used_heuristics', ''))
                
                # Set heuristic flags based on parsed data
                for heuristic in self.heuristics:
                    merged_data.loc[idx, heuristic] = 1 if heuristic in heuristics_on else 0
        
        # Try to match with L12 design if run_id is available
        if 'run_id' in merged_data.columns:
            # Merge with L12 design matrix, preferring experimental data
            merged_with_design = merged_data.merge(experiment_df, on='run_id', how='left', suffixes=('', '_design'))
            
            # Fill missing heuristic values with design matrix values
            for heuristic in self.heuristics:
                design_col = f"{heuristic}_design"
                if design_col in merged_with_design.columns:
                    merged_data[heuristic] = merged_with_design[heuristic].fillna(merged_with_design[design_col])
                    
        else:
            # If no run_id, add L12 configuration info with defaults
            logger.warning("No run_id found in data")
            for heuristic in self.heuristics:
                if heuristic not in merged_data.columns:
                    merged_data[heuristic] = 0  # Default to OFF
        
        # Drop rows with NaN in sshom_rate_percent (zero denominator cases)
        if 'sshom_rate_percent' in merged_data.columns:
            n_before = len(merged_data)
            merged_data = merged_data.dropna(subset=['sshom_rate_percent'])
            n_after = len(merged_data)
            if n_after < n_before:
                logger.warning(f"Dropped {n_before - n_after} rows with NaN SSHOM rate (zero denominator)")
                    
        return merged_data
    
    def bootstrap_confidence_interval(self, data, confidence_level=0.95):
        """
        Calculate bootstrap confidence interval for continuous data (e.g., runtime).
        Uses the SAME B and seed parameters as bootstrap_prop_ci for consistency.
        """
        if len(data) < 2:
            mean_val = data.iloc[0] if len(data) == 1 else 0
            return mean_val, mean_val, mean_val
            
        mean_val = np.mean(data)
        
        # Use consistent bootstrap parameters from instance
        B = self.bootstrap_n
        seed = self.bootstrap_seed
        
        if HAS_SCIPY_BOOTSTRAP:
            try:
                def mean_statistic(x):
                    return np.mean(x)
                
                # Perform scipy bootstrap resampling with instance parameters
                result = bootstrap((data,), mean_statistic, n_resamples=B, 
                                 confidence_level=confidence_level, random_state=seed)
                
                ci_low = result.confidence_interval.low
                ci_high = result.confidence_interval.high
                
                return mean_val, ci_low, ci_high
                
            except Exception as e:
                logger.warning(f"Scipy bootstrap failed: {e}, using manual bootstrap")
        
        # Manual bootstrap implementation with consistent parameters
        try:
            bootstrap_means = []
            rng = np.random.default_rng(seed)
            
            for _ in range(B):
                bootstrap_sample = rng.choice(data, size=len(data), replace=True)
                bootstrap_means.append(np.mean(bootstrap_sample))
            
            bootstrap_means = np.array(bootstrap_means)
            alpha = 1 - confidence_level
            ci_low = np.percentile(bootstrap_means, 100 * alpha/2)
            ci_high = np.percentile(bootstrap_means, 100 * (1 - alpha/2))
            
            return mean_val, ci_low, ci_high
            
        except Exception as e:
            logger.warning(f"Manual bootstrap failed: {e}, using standard error")
            std_err = np.std(data) / np.sqrt(len(data))
            ci_low = mean_val - 1.96 * std_err
            ci_high = mean_val + 1.96 * std_err
            return mean_val, ci_low, ci_high
    
    def calculate_bootstrap_main_effects(self, data, response_column, confidence_level=0.95):
        """
        Calculate main effects using hierarchical bootstrap respecting L12 design.
        
        This method ALWAYS uses bootstrap percentile CIs - no GLM, no fallbacks.
        For each (algorithm, heuristic) combination:
        - Computes bootstrap CI for level=0 and level=1
        - Effect = mean(level=1) - mean(level=0)
        - Effect CI from bootstrap samples of differences (Δ = ON_samples - OFF_samples)
        
        Returns DataFrame with columns (SSHOM):
        - algorithm, factor, effect_pp, level_off, level_on, ci_low_pp, ci_high_pp, 
          se_diff_pp, trials_off, trials_on, B, seed, method
        
        Returns DataFrame with columns (Runtime):
        - algorithm, factor, effect, level_off, level_on, ci_low, ci_high, 
          se_diff, n_off, n_on, B, seed, method
        """
        logger.info(f"========================================")
        logger.info(f"Calculating bootstrap-based main effects for {response_column}")
        logger.info(f"Bootstrap: B={self.bootstrap_n}, seed={self.bootstrap_seed}")
        logger.info(f"Total data rows: {len(data)}")
        logger.info(f"Algorithms in data: {data['algorithm'].unique() if 'algorithm' in data.columns else 'NO ALGORITHM COLUMN'}")
        logger.info(f"========================================")
        
        bootstrap_effects = []
        
        for algorithm in self.algorithms:
            logger.info(f"\nProcessing {algorithm}...")
            alg_data = data[data['algorithm'] == algorithm].copy()
            
            if len(alg_data) < 6:
                logger.warning(f"Insufficient data for {algorithm}: {len(alg_data)} rows")
                continue
            
            for heuristic in self.heuristics:
                if heuristic not in alg_data.columns:
                    logger.warning(f"Heuristic {heuristic} not in data for {algorithm}")
                    continue
                
                # Determine if this is SSHOM (binomial) or runtime (continuous) data
                is_sshom_data = 'sshom' in response_column.lower()
                
                # Use different seeds for OFF vs ON to get independent bootstrap samples
                import hashlib
                base_string = f"{algorithm}_{heuristic}_{self.bootstrap_seed}"
                hash_val = int(hashlib.md5(base_string.encode()).hexdigest()[:8], 16)
                
                if is_sshom_data:
                    # For SSHOM: Use bootstrap_prop_ci on raw binomial counts
                    off_boot = bootstrap_prop_ci(
                        alg_data, heuristic, 0, algorithm,
                        B=self.bootstrap_n, seed=self.bootstrap_seed + hash_val, return_samples=True
                    )
                    on_boot = bootstrap_prop_ci(
                        alg_data, heuristic, 1, algorithm,
                        B=self.bootstrap_n, seed=self.bootstrap_seed + hash_val + 1, return_samples=True
                    )
                else:
                    # For Runtime: Use traditional bootstrap on continuous values
                    off_data = alg_data[alg_data[heuristic] == 0][response_column].dropna()
                    on_data = alg_data[alg_data[heuristic] == 1][response_column].dropna()
                    
                    if len(off_data) == 0 or len(on_data) == 0:
                        logger.warning(f"No runtime data for {algorithm}-{heuristic}")
                        continue
                    
                    # Bootstrap the means with independent seeds
                    rng_off = np.random.RandomState(self.bootstrap_seed + hash_val)
                    rng_on = np.random.RandomState(self.bootstrap_seed + hash_val + 1)
                    off_samples = []
                    on_samples = []
                    
                    for _ in range(self.bootstrap_n):
                        off_boot_sample = rng_off.choice(off_data, size=len(off_data), replace=True)
                        on_boot_sample = rng_on.choice(on_data, size=len(on_data), replace=True)
                        off_samples.append(np.mean(off_boot_sample))
                        on_samples.append(np.mean(on_boot_sample))
                    
                    off_samples = np.array(off_samples)
                    on_samples = np.array(on_samples)
                    
                    # Create dict format matching bootstrap_prop_ci output
                    off_boot = {
                        'mean': np.mean(off_data),
                        'lo': np.percentile(off_samples, 2.5),
                        'hi': np.percentile(off_samples, 97.5),
                        'samples': off_samples
                    }
                    on_boot = {
                        'mean': np.mean(on_data),
                        'lo': np.percentile(on_samples, 2.5),
                        'hi': np.percentile(on_samples, 97.5),
                        'samples': on_samples
                    }
                
                if np.isnan(off_boot['mean']) or np.isnan(on_boot['mean']):
                    logger.warning(f"No data for {algorithm}-{heuristic}")
                    continue
                
                # Calculate effect and its CI from bootstrap samples
                effect = on_boot['mean'] - off_boot['mean']
                
                if 'samples' in off_boot and 'samples' in on_boot:
                    # Bootstrap the difference
                    diff_samples = on_boot['samples'] - off_boot['samples']
                    effect_ci_low = np.percentile(diff_samples, 2.5)
                    effect_ci_high = np.percentile(diff_samples, 97.5)
                    se_diff = np.std(diff_samples)
                else:
                    # Fallback: symmetric CI from individual CIs
                    se_off = (off_boot['hi'] - off_boot['lo']) / 3.92
                    se_on = (on_boot['hi'] - on_boot['lo']) / 3.92
                    se_diff = np.sqrt(se_off**2 + se_on**2)
                    effect_ci_low = effect - 1.96 * se_diff
                    effect_ci_high = effect + 1.96 * se_diff
                
                # Get trial counts for SSHOM data
                trials_off = off_boot.get('n_trials', 0) if is_sshom_data else len(alg_data[alg_data[heuristic] == 0])
                trials_on = on_boot.get('n_trials', 0) if is_sshom_data else len(alg_data[alg_data[heuristic] == 1])
                
                # Convert to percentage if this is SSHOM data
                if 'sshom' in response_column.lower():
                    bootstrap_effects.append({
                        'algorithm': algorithm,
                        'factor': heuristic,
                        'effect_pp': effect * 100,  # percentage points
                        'level_off': off_boot['mean'] * 100,
                        'level_on': on_boot['mean'] * 100,
                        'ci_low_pp': effect_ci_low * 100,
                        'ci_high_pp': effect_ci_high * 100,
                        'se_diff_pp': se_diff * 100,
                        'trials_off': trials_off,
                        'trials_on': trials_on,
                        'B': self.bootstrap_n,
                        'seed': self.bootstrap_seed,
                        'method': 'Bootstrap'
                    })
                else:
                    # Runtime: already in milliseconds
                    bootstrap_effects.append({
                        'algorithm': algorithm,
                        'factor': heuristic,
                        'effect': effect,
                        'level_off': off_boot['mean'],
                        'level_on': on_boot['mean'],
                        'ci_low': effect_ci_low,
                        'ci_high': effect_ci_high,
                        'se_diff': se_diff,
                        'n_off': trials_off,
                        'n_on': trials_on,
                        'B': self.bootstrap_n,
                        'seed': self.bootstrap_seed,
                        'method': 'Bootstrap'
                    })
        
        effects_df = pd.DataFrame(bootstrap_effects)
        
        logger.info(f"\n{'='*60}")
        logger.info(f"BOOTSTRAP CALCULATION SUMMARY")
        logger.info(f"{'='*60}")
        logger.info(f"Total effects generated: {len(effects_df)}")
        if len(effects_df) > 0:
            logger.info(f"Algorithms: {effects_df['algorithm'].unique().tolist()}")
        logger.info(f"{'='*60}\n")
        
        return effects_df
    
    def generate_main_effects_data(self, data, response_column):
        """Generate main effects data with confidence intervals."""
        logger.info(f"Generating main effects data for {response_column}")
        
        main_effects = []
        
        for algorithm in self.algorithms:
            alg_data = data[data['algorithm'] == algorithm]
            
            for heuristic in self.heuristics:
                if heuristic not in alg_data.columns:
                    logger.warning(f"Heuristic {heuristic} not found in data")
                    continue
                    
                # Calculate effects for low and high levels
                for level in [0, 1]:
                    level_data = alg_data[alg_data[heuristic] == level]
                    
                    if len(level_data) == 0:
                        continue
                        
                    response_values = level_data[response_column].dropna()
                    
                    if len(response_values) > 0:
                        mean_val, ci_low, ci_high = self.bootstrap_confidence_interval(response_values)
                        
                        main_effects.append({
                            'algorithm': algorithm,
                            'factor': heuristic,
                            'level': level,
                            'mean': mean_val,
                            'ci_low': ci_low,
                            'ci_high': ci_high,
                            'n_obs': len(response_values)
                        })
                        
        return pd.DataFrame(main_effects)
    
    def generate_interaction_effects_data(self, data, response_column):
        """
        Generate Algorithm×Heuristic interaction data with confidence intervals.
        
        Uses bootstrap_prop_ci() for SSHOM data (binomial proportions),
        uses bootstrap_confidence_interval() for runtime data (continuous).
        """
        logger.info(f"Generating interaction effects data for {response_column}")
        
        # Check if this is SSHOM data (binomial) vs runtime data (continuous)
        is_sshom_data = 'sshom_rate' in response_column.lower()
        
        interactions = []
        
        for heuristic in self.heuristics:
            if heuristic not in data.columns:
                continue
                
            for level in [0, 1]:
                for algorithm in self.algorithms:
                    subset = data[
                        (data['algorithm'] == algorithm) & 
                        (data[heuristic] == level)
                    ]
                    
                    if len(subset) == 0:
                        continue
                    
                    # Use appropriate bootstrap method based on data type
                    if is_sshom_data:
                        # For SSHOM: Use row-level bootstrap on binomial proportions
                        result = bootstrap_prop_ci(
                            subset, 
                            heuristic_col=heuristic,
                            level_val=level,
                            algorithm=algorithm,
                            B=self.bootstrap_n,
                            seed=self.bootstrap_seed,
                            return_samples=False
                        )
                        # bootstrap_prop_ci returns proportion [0,1], convert to percentage
                        mean_val = result['mean'] * 100
                        ci_low = result['lo'] * 100
                        ci_high = result['hi'] * 100
                    else:
                        # For runtime: Use traditional bootstrap on continuous values
                        response_values = subset[response_column].dropna()
                        
                        if len(response_values) == 0:
                            continue
                            
                        mean_val, ci_low, ci_high = self.bootstrap_confidence_interval(response_values)
                    
                    interactions.append({
                        'heuristic': heuristic,
                        'level': level,
                        'algorithm': algorithm,
                        'mean': mean_val,
                        'ci_low': ci_low,
                        'ci_high': ci_high,
                        'n_obs': len(subset),
                        'method': 'Bootstrap (binomial)' if is_sshom_data else 'Bootstrap (continuous)'
                    })
                        
        return pd.DataFrame(interactions)
    
    def setup_output_directory(self, solution_name):
        """Set up the output directory structure."""
        if solution_name:
            self.output_dir = Path("C:\\Users\\MerlijnU\\analysis") / solution_name / "latex_data"
        else:
            # If no solution name, use a default folder
            self.output_dir = Path("C:\\Users\\MerlijnU\\analysis") / "unknown_solution" / "latex_data"
        
        # Create the directory if it doesn't exist
        self.output_dir.mkdir(parents=True, exist_ok=True)
        logger.info(f"Output directory set to: {self.output_dir}")
        return self.output_dir
    
    def save_latex_csv(self, df, filename):
        """Save DataFrame as CSV formatted for LaTeX consumption."""
        output_path = self.output_dir / filename
        df.to_csv(output_path, index=False)
        logger.info(f"Saved LaTeX CSV: {output_path}")
    
    def plot_main_effects(self, main_effects_sshom, main_effects_runtime, sshom_col, runtime_col):
        """Generate Plot 1: Main effects with 95% CIs (two panels) using bootstrap differences."""
        logger.info("\n" + "="*60)
        logger.info("GENERATING MAIN EFFECTS PLOTS")
        logger.info("="*60)
        logger.info(f"Plot 1 using CORRECTED {sshom_col} (includes filtered HOMs in denominator)")
        logger.info(f"Bootstrap parameters: B={self.bootstrap_n}, seed={self.bootstrap_seed}")
        
        # Log incoming data
        logger.info(f"\nSSHOM data received: {len(main_effects_sshom)} rows")
        if not main_effects_sshom.empty:
            logger.info(f"  Algorithms in SSHOM data: {main_effects_sshom['algorithm'].unique().tolist()}")
            logger.info(f"  Factors in SSHOM data: {main_effects_sshom['factor'].unique().tolist() if 'factor' in main_effects_sshom.columns else 'NO FACTOR COLUMN'}")
            logger.info(f"  Columns: {main_effects_sshom.columns.tolist()}")
        else:
            logger.error("❌ SSHOM data is EMPTY!")
            
        logger.info(f"\nRuntime data received: {len(main_effects_runtime)} rows")
        if not main_effects_runtime.empty:
            logger.info(f"  Algorithms in runtime data: {main_effects_runtime['algorithm'].unique().tolist()}")
        
        # Set up the figure with two subplots
        fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(14, 6))
        
        # Define colors for algorithms
        colors = {'genetic': '#1f77b4', 'local': '#ff7f0e'}
        
        # Define factors outside conditionals to avoid UnboundLocalError if SSHOM data is empty
        factors = self.heuristics
        x_pos = np.arange(len(factors))
        width = 0.35
        
        # Plot 1A: SSHOM Success Rate Main Effects
        logger.info(f"\nPlotting Panel A (SSHOM)...")
        if not main_effects_sshom.empty:
            
            for i, alg in enumerate(['genetic', 'local']):
                alg_data = main_effects_sshom[main_effects_sshom['algorithm'] == alg]
                
                logger.info(f"\n  Processing {alg}:")
                logger.info(f"    Rows for {alg}: {len(alg_data)}")
                
                if len(alg_data) == 0:
                    logger.error(f"    ❌ NO DATA for {alg} - will have NO BARS!")
                else:
                    logger.info(f"    ✓ Data available for {alg}")
                    logger.info(f"    Sample data:\n{alg_data.head()}")

                means = []
                ci_lows = []
                ci_highs = []
                
                for factor in factors:
                    factor_data = alg_data[alg_data['factor'] == factor]
                    
                    logger.debug(f"      {alg}-{factor}: {len(factor_data)} rows")
                    
                    if len(factor_data) > 0:
                        # Check which column name convention is used (new: effect_pp, old: effect)
                        if 'effect_pp' in factor_data.columns:
                            # New format with explicit _pp suffix for percentage points
                            effect = factor_data['effect_pp'].iloc[0]
                            ci_low = factor_data['ci_low_pp'].iloc[0]
                            ci_high = factor_data['ci_high_pp'].iloc[0]
                            logger.debug(f"        Using bootstrap data: effect={effect:.2f} pp")
                        elif 'effect' in factor_data.columns:
                            # Old format (fallback for compatibility)
                            effect = factor_data['effect'].iloc[0]
                            ci_low = factor_data['ci_low'].iloc[0]
                            ci_high = factor_data['ci_high'].iloc[0]
                            logger.debug(f"        Using legacy data: effect={effect:.2f}")
                        else:
                            # Bootstrap-based analysis (fallback)
                            if len(factor_data) >= 2:
                                low_level = factor_data[factor_data['level'] == 0]['mean'].iloc[0] if len(factor_data[factor_data['level'] == 0]) > 0 else 0
                                high_level = factor_data[factor_data['level'] == 1]['mean'].iloc[0] if len(factor_data[factor_data['level'] == 1]) > 0 else 0
                                effect = high_level - low_level
                                
                                # Improved CI for differences using both levels
                                high_data = factor_data[factor_data['level'] == 1]
                                low_data = factor_data[factor_data['level'] == 0]
                                
                                if len(high_data) > 0 and len(low_data) > 0:
                                    # Calculate proper SE for difference
                                    high_se = (high_data['ci_high'].iloc[0] - high_data['ci_low'].iloc[0]) / 3.92
                                    low_se = (low_data['ci_high'].iloc[0] - low_data['ci_low'].iloc[0]) / 3.92
                                    diff_se = np.sqrt(high_se**2 + low_se**2)  # Assuming independence
                                    
                                    ci_low = effect - 1.96 * diff_se
                                    ci_high = effect + 1.96 * diff_se
                                else:
                                    ci_low = ci_high = effect
                            else:
                                effect = ci_low = ci_high = 0
                    else:
                        effect = ci_low = ci_high = 0
                    
                    means.append(effect)
                    ci_lows.append(effect - ci_low)  # Error bar size below
                    ci_highs.append(ci_high - effect)  # Error bar size above
                
                # TODO: Re-enable error bars after fixing bootstrap correlation issue
                ax1.bar(x_pos + i*width, means, width, 
                       yerr=[ci_lows, ci_highs], capsize=3,  
                       label=alg.title(), color=colors[alg], alpha=0.8)
            
            ax1.set_xlabel('Heuristic Factors')
            ax1.set_ylabel('SSHOM rate (pp, ON - OFF)')
            ax1.set_title('Panel A: Main Effects on SSHOM Success Rate\n(Bootstrap difference, 95% CI)')
            ax1.set_xticks(x_pos + width/2)
            ax1.set_xticklabels(factors, rotation=45, ha='right')
            ax1.axhline(y=0, color='black', linestyle='-', alpha=0.3)
            ax1.legend()
            ax1.grid(True, alpha=0.3)
            # Clip y-axis to reasonable percentage range
            y_min, y_max = ax1.get_ylim()
            ax1.set_ylim(max(-100, y_min), min(100, y_max))
        
        # Plot 1B: Runtime Main Effects
        if not main_effects_runtime.empty:
            for i, alg in enumerate(['genetic', 'local']):
                alg_data = main_effects_runtime[main_effects_runtime['algorithm'] == alg]
                
                means = []
                ci_lows = []
                ci_highs = []
                
                for factor in factors:
                    factor_data = alg_data[alg_data['factor'] == factor]
                    
                    if len(factor_data) > 0:
                        # Check column format (new: effect_pp, old: effect)
                        if 'effect' in factor_data.columns:
                            # Legacy format (old column names)
                            effect = factor_data['effect'].iloc[0]
                            ci_low = factor_data['ci_low'].iloc[0]
                            ci_high = factor_data['ci_high'].iloc[0]
                        else:
                            # Bootstrap-based analysis (fallback)
                            if len(factor_data) >= 2:
                                low_level = factor_data[factor_data['level'] == 0]['mean'].iloc[0] if len(factor_data[factor_data['level'] == 0]) > 0 else 0
                                high_level = factor_data[factor_data['level'] == 1]['mean'].iloc[0] if len(factor_data[factor_data['level'] == 1]) > 0 else 0
                                effect = high_level - low_level
                                
                                # Improved CI for differences using both levels
                                high_data = factor_data[factor_data['level'] == 1]
                                low_data = factor_data[factor_data['level'] == 0]
                                
                                if len(high_data) > 0 and len(low_data) > 0:
                                    # Calculate proper SE for difference
                                    high_se = (high_data['ci_high'].iloc[0] - high_data['ci_low'].iloc[0]) / 3.92
                                    low_se = (low_data['ci_high'].iloc[0] - low_data['ci_low'].iloc[0]) / 3.92
                                    diff_se = np.sqrt(high_se**2 + low_se**2)  # Assuming independence
                                    
                                    ci_low = effect - 1.96 * diff_se
                                    ci_high = effect + 1.96 * diff_se
                                else:
                                    ci_low = ci_high = effect
                            else:
                                effect = ci_low = ci_high = 0   
                    else:
                        effect = ci_low = ci_high = 0
                    
                    means.append(effect)
                    ci_lows.append(effect - ci_low)  # Error bar size below
                    ci_highs.append(ci_high - effect)  # Error bar size above
                
                ax2.bar(x_pos + i*width, means, width, 
                       yerr=[ci_lows, ci_highs], capsize=3,
                       label=alg.title(), color=colors[alg], alpha=0.8)
            
            ax2.set_xlabel('Heuristic Factors')
            ax2.set_ylabel('Runtime (ms, ON - OFF)')
            ax2.set_title('Panel B: Main Effects on Runtime\n(Bootstrap difference, 95% CI)')
            ax2.set_xticks(x_pos + width/2)
            ax2.set_xticklabels(factors, rotation=45, ha='right')
            ax2.axhline(y=0, color='black', linestyle='-', alpha=0.3)
            ax2.legend()
            ax2.grid(True, alpha=0.3)
        
        plt.tight_layout()
        
        # Save the plot
        plot_path = self.output_dir / "plot1_main_effects.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        plt.close()
        logger.info(f"Saved main effects plot: {plot_path}")
        
        return plot_path
    
    def plot_interaction_effects(self, interaction_effects, sshom_col):
        """Generate Plot 2: Algorithm×Heuristic interaction plots (six small multiples)."""
        logger.info("Generating interaction effects plots...")
        
        # Set up figure with 2x3 subplots for the 6 heuristics
        fig, axes = plt.subplots(2, 3, figsize=(15, 10))
        axes = axes.flatten()
        
        colors = {'genetic': '#1f77b4', 'local': '#ff7f0e'}
        
        for i, heuristic in enumerate(self.heuristics):
            ax = axes[i]
            heuristic_data = interaction_effects[interaction_effects['heuristic'] == heuristic]
            
            if not heuristic_data.empty:
                # Plot lines for each algorithm
                for alg in ['genetic', 'local']:
                    alg_data = heuristic_data[heuristic_data['algorithm'] == alg]
                    
                    if not alg_data.empty:
                        # Get data for OFF (level 0) and ON (level 1)
                        levels = [0, 1]
                        means = []
                        ci_lows = []
                        ci_highs = []
                        
                        for level in levels:
                            level_data = alg_data[alg_data['level'] == level]
                            if len(level_data) > 0:
                                mean_val = level_data['mean'].iloc[0]
                                ci_low = level_data['ci_low'].iloc[0]
                                ci_high = level_data['ci_high'].iloc[0]
                            else:
                                mean_val = ci_low = ci_high = 0
                            
                            means.append(mean_val)
                            ci_lows.append(mean_val - ci_low)
                            ci_highs.append(ci_high - mean_val)
                        
                        ax.plot(levels, means, marker='o', linewidth=2,
                               label=alg.title(), color=colors[alg])
                        ax.errorbar(levels, means, yerr=[ci_lows, ci_highs], 
                                    marker='o', linewidth=2, capsize=3,
                                    label=alg.title(), color=colors[alg])  
            
            ax.set_xlabel(f'{heuristic}\n(0=OFF, 1=ON)')
            ax.set_ylabel(f'{sshom_col}')
            ax.set_title(heuristic, fontsize=10)
            ax.set_xticks([0, 1])
            ax.set_xticklabels(['OFF', 'ON'])
            ax.grid(True, alpha=0.3)
            
            # Add legend only to first subplot
            if i == 0:
                ax.legend()
        
        # Remove empty subplots if any
        for j in range(len(self.heuristics), len(axes)):
            fig.delaxes(axes[j])
        
        plt.suptitle('Algorithm × Heuristic Interaction Effects on SSHOM Success Rate', fontsize=14)
        plt.tight_layout()
        
        # Save the plot
        plot_path = self.output_dir / "plot2_interactions.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        plt.close()
        logger.info(f"Saved interaction effects plot: {plot_path}")
        
        return plot_path
        
    def run_complete_analysis(self):
        """Run the complete analysis pipeline and generate all three CSV files."""
        logger.info("Starting complete L12 orthogonal array analysis...")
        
        # Load experimental data
        raw_data = self.load_experimental_data()
        
        if raw_data.empty:
            logger.error("No data loaded. Please check CSV files in the directory.")
            return
            
        # Build experiment configuration mapping
        experiment_data = self.build_experiment_table(raw_data)
        
        # Determine solution name for output directory
        solution_name = None
        if 'solution' in experiment_data.columns:
            unique_solutions = experiment_data['solution'].dropna().unique()
            if len(unique_solutions) > 0:
                solution_name = unique_solutions[0]  # Use first solution found
        
        # Set up output directory
        self.setup_output_directory(solution_name)
        
        # Column names based on actual CSV structure from analyze_homcsv.py
        response_columns = {
            'sshom_success_rate': [
                'sshom_rate_percent',  # Primary column name
                'SSHOM_Success_Rate', 'success_rate', 'SSHOM_success', 'sshom'
            ],
            'runtime': [
                'total_test_duration_ms',  # Primary column name
                'hom_generation_ms',      # Alternative time metric
                'Runtime', 'runtime', 'Runtime_ms', 'time', 'execution_time'
            ]
        }
        
        # Find actual column names in the data
        actual_sshom_col = None
        actual_runtime_col = None
        
        # Look for SSHOM success rate column
        for col in experiment_data.columns:
            if any(target.lower() in col.lower() for target in response_columns['sshom_success_rate']):
                actual_sshom_col = col
                break
                
        # Look for runtime column
        for col in experiment_data.columns:
            if any(target.lower() in col.lower() for target in response_columns['runtime']):
                actual_runtime_col = col
                break
                
        if actual_sshom_col is None:
            logger.error("Could not find SSHOM success rate column")
            return
            
        if actual_runtime_col is None:
            logger.error("Could not find runtime column") 
            return
            
        logger.info(f"Using columns: {actual_sshom_col} and {actual_runtime_col}")
        
        # Generate CSV 1: Bootstrap main effects for SSHOM success rate
        main_effects_sshom = self.calculate_bootstrap_main_effects(experiment_data, actual_sshom_col)
        self.save_latex_csv(main_effects_sshom, 'main_effects_bootstrap_latex.csv')
        
        # Generate CSV 2: Bootstrap main effects for runtime  
        main_effects_runtime = self.calculate_bootstrap_main_effects(experiment_data, actual_runtime_col)
        self.save_latex_csv(main_effects_runtime, 'main_effects_runtime_bootstrap_latex.csv')
        
        # Generate CSV 3: Interaction effects for SSHOM success rate
        interaction_effects = self.generate_interaction_effects_data(experiment_data, actual_sshom_col)
        self.save_latex_csv(interaction_effects, 'interaction_effects_latex.csv')
        
        # Generate the two main plots
        logger.info("Generating publication-quality plots...")
        
        plot_files = []
        
        # Plot 1: Main effects with 95% CIs (two panels)
        plot1_path = self.plot_main_effects(main_effects_sshom, main_effects_runtime, 
                                           actual_sshom_col, actual_runtime_col)
        plot_files.append(plot1_path)
        
        # Plot 2: Algorithm×Heuristic interaction plots (six small multiples)
        plot2_path = self.plot_interaction_effects(interaction_effects, actual_sshom_col)
        plot_files.append(plot2_path)
        
        # Generate summary report
        self.generate_summary_report(experiment_data, actual_sshom_col, actual_runtime_col)
        
        logger.info("Analysis complete! Generated 3 CSV files, 1 summary text file, and 2 PNG plots:")
        logger.info(f"Output directory: {self.output_dir}")
        logger.info("CSV DATA FILES:")
        logger.info("- main_effects_bootstrap_latex.csv (bootstrap main effects for SSHOM)")  
        logger.info("- main_effects_runtime_bootstrap_latex.csv (bootstrap main effects for runtime)")
        logger.info("- interaction_effects_latex.csv (bootstrap interaction effects)")
        logger.info("SUMMARY:")
        logger.info("- analysis_summary.txt (analysis report)")
        logger.info("PNG PLOTS:")
        for plot_file in plot_files:
            logger.info(f"- {plot_file.name}")
            
        return plot_files
        
    def generate_summary_report(self, data, sshom_col, runtime_col):
        """Generate a summary report of the analysis."""
        
        summary_lines = [
            "L12 ORTHOGONAL ARRAY ANALYSIS SUMMARY",
            "=" * 50,
            f"Total observations: {len(data)}",
            f"Algorithms analyzed: {', '.join(self.algorithms)}",
            f"Heuristics analyzed: {', '.join(self.heuristics)}",
            "",
            "RESPONSE VARIABLES:",
            f"- SSHOM Success Rate: {sshom_col}",
            f"  Range: {data[sshom_col].min():.2f} - {data[sshom_col].max():.2f}",
            f"  Mean: {data[sshom_col].mean():.2f} ± {data[sshom_col].std():.2f}",
            "",
            f"- Runtime: {runtime_col}",  
            f"  Range: {data[runtime_col].min():.2f} - {data[runtime_col].max():.2f}",
            f"  Mean: {data[runtime_col].mean():.2f} ± {data[runtime_col].std():.2f}",
            "",
            "ALGORITHM COMPARISON:",
        ]
        
        for algorithm in self.algorithms:
            alg_data = data[data['algorithm'] == algorithm]
            if len(alg_data) > 0:
                summary_lines.extend([
                    f"  {algorithm.upper()}:",
                    f"    SSHOM Success: {alg_data[sshom_col].mean():.2f} ± {alg_data[sshom_col].std():.2f}",
                    f"    Runtime: {alg_data[runtime_col].mean():.2f} ± {alg_data[runtime_col].std():.2f}",
                ])
                
        summary_lines.extend([
            "",
            "FILES GENERATED:",
            "CSV DATA FILES:",
            "1. main_effects_bootstrap_latex.csv - Bootstrap main effects on SSHOM success rate",
            "2. main_effects_runtime_bootstrap_latex.csv - Bootstrap main effects on runtime",
            "3. interaction_effects_latex.csv - Algorithm×Heuristic interaction effects",
            "",
            "PNG PLOTS:",
            "1. plot1_main_effects.png - Main effects with bootstrap 95% CIs (two panels)",
            "2. plot2_interactions.png - Interaction plots (six small multiples)",
            "",
            "STATISTICAL METHODS:",
            "- Bootstrap-based Analysis: Stratified row-level resampling",
            f"- Method: {self.bootstrap_n} bootstrap samples (seed={self.bootstrap_seed}) for all CI calculations",
            "- Confidence Intervals: 95% percentile CIs (2.5th and 97.5th percentiles)",
            "- L12 Design-aware: Yes (hierarchical bootstrap respects orthogonal structure)",
            "",
            "SIGNIFICANCE SCHEME (unified across all analyses):",
            "- *** p<0.001 (highly significant)",
            "- ** p<0.01 (very significant)", 
            "- * p<0.05 (significant)",
            "- † p<0.10 (marginally significant)",
            "- ns ≥0.10 (not significant)",
            "=" * 50
        ])
        
        summary_text = "\n".join(summary_lines)
        
        # Save summary to file with UTF-8 encoding to support Unicode characters
        summary_path = self.output_dir / "analysis_summary.txt"
        with open(summary_path, 'w', encoding='utf-8') as f:
            f.write(summary_text)
            
        # Also print to console (encode to handle Unicode in Windows console)
        try:
            print(summary_text)
        except UnicodeEncodeError:
            # Fallback for Windows console that doesn't support UTF-8
            print(summary_text.encode('ascii', 'replace').decode('ascii'))
        
        logger.info(f"Summary report saved to: {summary_path}")


def safe_print(text):
    """Print text with fallback for Windows console Unicode issues."""
    try:
        print(text)
    except UnicodeEncodeError:
        # Replace Unicode characters for Windows console
        replacements = {'†': 'T', '≥': '>=', '×': 'x', '•': '*'}
        for old, new in replacements.items():
            text = text.replace(old, new)
        print(text)


def main():
    """Main execution function."""
    import argparse
    
    # Add command line argument parsing
    parser = argparse.ArgumentParser(
        description="Enhanced L12 Orthogonal Array Analysis for HOMT experiments"
    )
    parser.add_argument(
        "--root", 
        type=str, 
        help="Root directory containing experimental data (default: current directory)"
    )
    parser.add_argument(
        "--solution",
        type=str,
        help="Specific solution name to analyze (optional)"
    )
    parser.add_argument(
        "--bootstrap",
        type=int,
        default=2000,
        help="Number of bootstrap samples (default: 2000)"
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=42,
        help="Random seed for reproducibility (default: 42)"
    )
    
    args = parser.parse_args()
    
    # Validate root path
    if not args.root:
        logger.error("ERROR: --root parameter is required")
        logger.error("Usage: python analyze_homcsv_enhanced.py --root <path> [--solution <name>]")
        return
    
    root_path = Path(args.root)
    
    if not root_path.exists():
        logger.error(f"ERROR: Root path does not exist: {root_path}")
        logger.error(f"Please provide a valid directory path containing experimental data")
        return
    
    if not root_path.is_dir():
        logger.error(f"ERROR: Root path is not a directory: {root_path}")
        return
    
    logger.info(f"✓ Root path validated: {root_path}")
    logger.info(f"Bootstrap settings: B={args.bootstrap}, seed={args.seed}")
    
    # Initialize analyzer with specified path
    analyzer = L12AnalysisEnhanced(
        base_path=root_path, 
        solution=args.solution,
        bootstrap_n=args.bootstrap,
        bootstrap_seed=args.seed
    )
    
    # Run complete analysis
    plot_files = analyzer.run_complete_analysis()
    
    safe_print("\n" + "="*60)
    safe_print("ANALYSIS COMPLETE!")
    safe_print("="*60)
    safe_print(f"\nGenerated files in: {analyzer.output_dir}")
    safe_print("\nCSV DATA FILES:")
    safe_print("- main_effects_bootstrap_latex.csv (bootstrap main effects for SSHOM)")
    safe_print("- main_effects_runtime_bootstrap_latex.csv (bootstrap main effects for runtime)")
    safe_print("- interaction_effects_latex.csv (bootstrap interaction effects)")
    safe_print("- analysis_summary.txt (analysis report)")
    
    safe_print("\nPNG PLOT FILES:")
    if plot_files:
        for plot_file in plot_files:
            safe_print(f"- {plot_file.name}")
    else:
        safe_print("- plot1_main_effects.png")
        safe_print("- plot2_interactions.png")
    
    safe_print("\nPLOT DESCRIPTIONS:")
    safe_print("1. plot1_main_effects.png - Bootstrap main effects with 95% CIs")
    safe_print("   - Panel A: SSHOM success rate effects (bootstrap differences, ON - OFF)")
    safe_print("   - Panel B: Runtime effects (bootstrap differences, ON - OFF)")
    safe_print("   - Uses hierarchical bootstrap resampling respecting L12 structure")
    safe_print("   - Samples 6 OA rows with replacement, then 5 repeats per row")
    safe_print("   - Confidence intervals via percentile method (2.5th, 97.5th percentiles)")
    safe_print("2. plot2_interactions.png - Algorithm x Heuristic interactions")
    safe_print("   - Six small multiples (one per heuristic)")
    safe_print("   - Shows if heuristics work differently per algorithm")
    
    safe_print("\nSTATISTICAL METHODS:")
    safe_print("- Hierarchical Bootstrap: Respects L12 structure (6 OA rows x 5 repeats)")
    safe_print("- Confidence Intervals: 95% percentile CIs (2.5th and 97.5th percentiles)")
    safe_print("- Effect CIs: Bootstrap distribution of differences (ON_samples - OFF_samples)")
    
    safe_print("\nNext steps:")
    safe_print("1. Review the PNG plots - ready for thesis inclusion!")
    safe_print("2. Check analysis_summary.txt for data overview")
    safe_print("3. Use bootstrap CSV files for analysis (consistent method across all metrics)")
    safe_print("4. Verify reproducibility by re-running with same seed (--seed 42)")
    safe_print("="*60)
    
    
if __name__ == "__main__":
    main()