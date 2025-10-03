#!/usr/bin/env python3
"""
Enhanced L12 Orthogonal Array Analysis Script
Generates LaTeX-ready CSV data for three specific plots:
1. Main effects with 95% CIs (SSHOM success rate and runtime)
2. Algorithm×Heuristic interaction effects with CIs
3. Pareto frontier analysis

Usage: 
  python analyze_homcsv_enhanced.py [--root <path>] [--solution <name>]
  
  --root: Root directory containing experimental data structure:
          <root>/<Solution>/<Algorithm>/OAxx/repyy/reports/*.csv
  --solution: Optional filter to analyze only specific solution

Expected CSV structure matches Stryker HOMT output with columns:
- sshom_rate_percent: SSHOM success rate
- total_test_duration_ms: Runtime in milliseconds  
- used_heuristics: String listing active heuristics

Outputs: 
- Four CSV files with analysis data
- Three PNG plots ready for thesis inclusion:
  1. plot1_main_effects.png - Main effects with 95% CIs (two panels)  
  2. plot2_interactions.png - Algorithm×Heuristic interactions (six multiples)
  3. plot3_pareto_frontier.png - Pareto frontier trade-off analysis
- Analysis summary report
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
import matplotlib.patches as patches
from matplotlib.patches import ConnectionPatch

# Try to import scipy modules, fall back to manual implementation if not available
try:
    from scipy.stats import bootstrap
    HAS_SCIPY_BOOTSTRAP = True
except ImportError:
    HAS_SCIPY_BOOTSTRAP = False
    
try:
    from scipy.spatial import ConvexHull
    HAS_SCIPY_CONVEX = True
except ImportError:
    HAS_SCIPY_CONVEX = False

# Try to import statsmodels for GLM-based EMM analysis
try:
    import statsmodels.api as sm
    from statsmodels.genmod.generalized_linear_model import GLM
    from statsmodels.genmod import families
    HAS_STATSMODELS = True
except ImportError:
    HAS_STATSMODELS = False

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

# Suppress warnings
warnings.filterwarnings('ignore')

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
    def __init__(self, base_path=None):
        """Initialize the analysis with path configuration."""
        if base_path is None:
            self.base_path = Path.cwd()
        else:
            self.base_path = Path(base_path)
            
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
        
    def find_run_csvs(self, root_path):
        """Find all CSV files in the expected directory structure."""
        # Look for CSV files in the nested structure: <Solution>/<Algorithm>/OAxx/repyy/reports/*.csv
        csv_files = []
        
        # Search patterns for CSV files - ONLY match mutation-report-hom.csv to avoid pulling in
        # GLM outputs, analysis results, and other CSV artifacts
        patterns = [
            "**/reports/mutation-report-hom.csv",  # Specific filename in reports directory
            "**/mutation-report-hom.csv",  # Specific filename (fallback if not in reports/)
        ]
        
        for pattern in patterns:
            found_files = list(root_path.rglob(pattern))
            csv_files.extend([f for f in found_files if f.is_file() and f not in csv_files])
        
        return csv_files
    
    def parse_path_metadata(self, csv_path):
        """Extract metadata from the file path structure."""
        import re
        
        # Expected structure: .../Solution/Algorithm/OAxx/repyy/reports/mutation-report-hom.csv
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
        csv_files = self.find_run_csvs(self.base_path)
        
        if not csv_files:
            logger.warning(f"No CSV files found in {self.base_path}")
            return pd.DataFrame()
            
        logger.info(f"Found {len(csv_files)} CSV files")
        
        all_data = []
        
        for csv_file in csv_files:
            try:
                # Parse metadata from file path
                metadata = self.parse_path_metadata(csv_file)
                
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
                
                # Ensure consistent SSHOM percentage calculation: (sshom_2 + sshom_3 + sshom_4) / total_homs_with_filtered * 100
                # This includes filtered HOMs in the denominator for accurate success rate calculation
                # Matches the updated analyze_homcsv.py and glm_binomial_anova.py calculation
                # Try multiple naming conventions for SSHOM columns
                sshom_col_candidates = [
                    ['sshom_2', 'sshom_3', 'sshom_4'],  # Most common format
                    ['sshoms_2nd', 'sshoms_3rd', 'sshoms_4th'],  # Alternative format
                ]
                
                available_sshom_cols = []
                for candidate_set in sshom_col_candidates:
                    found_cols = [col for col in candidate_set if col in df.columns]
                    if found_cols:
                        available_sshom_cols = found_cols
                        logger.debug(f"Found SSHOM columns: {found_cols}")
                        break
                
                # Calculate total HOMs including filtered ones
                # Try multiple possible column names for total HOMs
                hom_total_candidates = ['total_homs', 'hom_total', 'total_hom', 'homs_total']
                hom_total_col = None
                
                for candidate in hom_total_candidates:
                    if candidate in df.columns:
                        hom_total_col = candidate
                        logger.debug(f"Found HOM total column: {candidate}")
                        break
                
                if hom_total_col is not None:
                    original_hom_total = df[hom_total_col].fillna(0)
                    
                    # Add filtered HOMs to get true total
                    # Include ALL filtered HOM categories to get accurate denominator
                    filtered_cols = ['filtered_empty_total', 'filtered_invalid_total']
                    total_homs_with_filtered = original_hom_total.copy()
                    
                    for col in filtered_cols:
                        if col in df.columns:
                            total_homs_with_filtered += df[col].fillna(0)
                            logger.debug(f"Added {col} to total_homs_with_filtered")
                    
                    df['total_homs_with_filtered'] = total_homs_with_filtered
                    df['total_homs_kept_only'] = original_hom_total
                    
                    logger.debug(f"Calculated total_homs_with_filtered (includes filtered HOMs): {total_homs_with_filtered.iloc[0] if len(total_homs_with_filtered) > 0 else 0}")
                else:
                    logger.warning(f"No HOM total column found in {csv_file}. Available columns: {list(df.columns)}")
                    df['total_homs_with_filtered'] = 0
                    df['total_homs_kept_only'] = 0
                
                # ALWAYS recalculate sshom_rate_percent if we have the necessary data
                # The CSV files contain the OLD calculation (without filtered HOMs), so we must override it
                if available_sshom_cols and 'total_homs_with_filtered' in df.columns:
                    # Calculate total SSHOMs
                    df['total_sshoms'] = df[available_sshom_cols].fillna(0).sum(axis=1)
                    
                    # Store the original (uncorrected) value from CSV for comparison
                    if 'sshom_rate_percent' in df.columns:
                        df['sshom_rate_percent_original'] = df['sshom_rate_percent'].copy()
                    
                    # Recalculate sshom_rate_percent using total_homs_with_filtered (includes filtered HOMs)
                    # This is the CORRECTED rate that will be used for all plots and analysis
                    # This OVERWRITES the incorrect value from the CSV file
                    df['sshom_rate_percent'] = 100.0 * df['total_sshoms'] / df['total_homs_with_filtered'].replace(0, 1)  # Avoid division by zero
                    
                    # Also calculate the old rate for comparison (without filtered HOMs)
                    if 'total_homs_kept_only' in df.columns:
                        df['sshom_rate_without_filtered_percent'] = 100.0 * df['total_sshoms'] / df['total_homs_kept_only'].replace(0, 1)
                    
                    logger.info(f"CORRECTED sshom_rate_percent calculated: {df['sshom_rate_percent'].iloc[0]:.2f}% (includes {df['total_homs_with_filtered'].iloc[0]:.0f} total HOMs with filtered)")
                    if 'sshom_rate_percent_original' in df.columns:
                        logger.info(f"  Original CSV value was: {df['sshom_rate_percent_original'].iloc[0]:.2f}% (excluded filtered HOMs)")
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
                    
        return merged_data
    
    def bootstrap_confidence_interval(self, data, confidence_level=0.95):
        """Calculate bootstrap confidence interval for a data series."""
        if len(data) < 2:
            mean_val = data.iloc[0] if len(data) == 1 else 0
            return mean_val, mean_val, mean_val
            
        mean_val = np.mean(data)
        
        if HAS_SCIPY_BOOTSTRAP:
            try:
                def mean_statistic(x):
                    return np.mean(x)
                
                # Perform scipy bootstrap resampling
                result = bootstrap((data,), mean_statistic, n_resamples=1000, 
                                 confidence_level=confidence_level, random_state=42)
                
                ci_low = result.confidence_interval.low
                ci_high = result.confidence_interval.high
                
                return mean_val, ci_low, ci_high
                
            except Exception as e:
                logger.warning(f"Scipy bootstrap failed: {e}, using manual bootstrap")
        
        # Manual bootstrap implementation
        try:
            n_bootstrap = 1000
            bootstrap_means = []
            np.random.seed(42)
            
            for _ in range(n_bootstrap):
                bootstrap_sample = np.random.choice(data, size=len(data), replace=True)
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
    
    def calculate_emm_effects(self, data, response_column, confidence_level=0.95):
        """Calculate GLM-based EMM main effects with proper confidence intervals.
        
        Uses Binomial GLM (logit link) to model SSHOM success counts:
        - Endog = successes (sshom_2 + sshom_3 + sshom_4)
        - Trials = total_homs_with_filtered
        - Computes EMMs by averaging predictions over the balanced L12 design
        - Gets 95% CIs via delta method; falls back to bootstrap if separation occurs
        """
        logger.info(f"Calculating GLM-based EMM effects for {response_column}")
        
        emm_effects = []
        
        # Check if we have statsmodels
        if not HAS_STATSMODELS:
            logger.warning("Statsmodels not available, falling back to bootstrap method")
            return self.generate_main_effects_data(data, response_column)
        
        # Import itertools for combinatorial logic
        try:
            from itertools import product
        except ImportError:
            logger.warning("itertools not available, falling back to bootstrap method")
            return self.generate_main_effects_data(data, response_column)
        
        try:
            for algorithm in self.algorithms:
                alg_data = data[data['algorithm'] == algorithm].copy()
                
                if len(alg_data) < 6:  # Need sufficient data for model fitting
                    logger.warning(f"Insufficient data for {algorithm} GLM EMM analysis")
                    continue
                
                # Prepare data for GLM modeling - ensure all heuristic columns exist
                model_data = alg_data.copy()
                missing_heuristics = []
                for heuristic in self.heuristics:
                    if heuristic not in model_data.columns:
                        missing_heuristics.append(heuristic)
                
                if missing_heuristics:
                    logger.warning(f"Heuristics missing for {algorithm}: {missing_heuristics}")
                    continue
                
                # For GLM, we need success counts and total trials
                # Check if we're dealing with SSHOM rate (need to back-calculate counts)
                if 'sshom' in response_column.lower() or 'success' in response_column.lower():
                    # Try to find success count columns
                    sshom_cols = ['sshom_2', 'sshom_3', 'sshom_4']
                    available_sshom = [col for col in sshom_cols if col in model_data.columns]
                    
                    if not available_sshom:
                        logger.warning(f"No SSHOM count columns found for {algorithm}, using bootstrap")
                        alg_bootstrap = self.generate_main_effects_data(alg_data, response_column)
                        # Convert to EMM format
                        for heuristic in self.heuristics:
                            bootstrap_data = alg_bootstrap[alg_bootstrap['factor'] == heuristic]
                            if len(bootstrap_data) >= 2:
                                low_data = bootstrap_data[bootstrap_data['level'] == 0]
                                high_data = bootstrap_data[bootstrap_data['level'] == 1]
                                if len(low_data) > 0 and len(high_data) > 0:
                                    effect = high_data['mean'].iloc[0] - low_data['mean'].iloc[0]
                                    ci_low = effect - 2 * np.sqrt(
                                        (high_data['mean'].iloc[0] - high_data['ci_low'].iloc[0])**2 +
                                        (low_data['ci_high'].iloc[0] - low_data['mean'].iloc[0])**2
                                    )
                                    ci_high = effect + 2 * np.sqrt(
                                        (high_data['ci_high'].iloc[0] - high_data['mean'].iloc[0])**2 +
                                        (low_data['mean'].iloc[0] - low_data['ci_low'].iloc[0])**2
                                    )
                                    emm_effects.append({
                                        'algorithm': algorithm,
                                        'factor': heuristic,
                                        'effect': effect,
                                        'emm_low': low_data['mean'].iloc[0],
                                        'emm_high': high_data['mean'].iloc[0],
                                        'ci_low': ci_low,
                                        'ci_high': ci_high,
                                        'se_diff': (ci_high - ci_low) / 3.92,
                                        'method': 'Bootstrap_no_counts'
                                    })
                        continue
                    
                    # Calculate success counts
                    model_data['_successes'] = model_data[available_sshom].fillna(0).sum(axis=1)
                    
                    # Get total trials (including filtered HOMs)
                    if 'total_homs_with_filtered' in model_data.columns:
                        model_data['_trials'] = model_data['total_homs_with_filtered'].fillna(1).astype(int)
                    else:
                        logger.warning(f"No total_homs_with_filtered for {algorithm}, using bootstrap")
                        continue
                    
                    # Filter out rows with zero trials
                    model_data = model_data[model_data['_trials'] > 0].copy()
                    
                    if len(model_data) < 6:
                        logger.warning(f"Insufficient valid data after filtering for {algorithm}")
                        continue
                    
                    endog = model_data['_successes'].values
                    trials = model_data['_trials'].values
                    use_glm = True
                else:
                    # For runtime or other continuous responses, use OLS-style approach
                    # but convert to probability scale for consistency
                    use_glm = False
                
                try:
                    if use_glm:
                        # Build design matrix for GLM
                        exog_list = []
                        for heuristic in self.heuristics:
                            if heuristic in model_data.columns:
                                exog_list.append(model_data[heuristic].values)
                        
                        exog = np.column_stack([np.ones(len(model_data))] + exog_list)
                        
                        # Fit Binomial GLM with logit link
                        try:
                            glm_model = GLM(endog, exog, family=families.Binomial(), freq_weights=trials)
                            glm_result = glm_model.fit()
                            
                            # Check for convergence warnings
                            if not glm_result.converged:
                                logger.warning(f"GLM did not converge for {algorithm}, trying with scale='X2'")
                                glm_result = glm_model.fit(scale='X2')
                        except Exception as e:
                            logger.warning(f"GLM fitting failed for {algorithm}: {e}, using bootstrap")
                            # Fall back to bootstrap
                            alg_bootstrap = self.generate_main_effects_data(alg_data, response_column)
                            for heuristic in self.heuristics:
                                bootstrap_data = alg_bootstrap[alg_bootstrap['factor'] == heuristic]
                                if len(bootstrap_data) >= 2:
                                    low_data = bootstrap_data[bootstrap_data['level'] == 0]
                                    high_data = bootstrap_data[bootstrap_data['level'] == 1]
                                    if len(low_data) > 0 and len(high_data) > 0:
                                        effect = high_data['mean'].iloc[0] - low_data['mean'].iloc[0]
                                        ci_low = effect - 2 * np.sqrt(
                                            (high_data['mean'].iloc[0] - high_data['ci_low'].iloc[0])**2 +
                                            (low_data['ci_high'].iloc[0] - low_data['mean'].iloc[0])**2
                                        )
                                        ci_high = effect + 2 * np.sqrt(
                                            (high_data['ci_high'].iloc[0] - high_data['mean'].iloc[0])**2 +
                                            (low_data['mean'].iloc[0] - low_data['ci_low'].iloc[0])**2
                                        )
                                        emm_effects.append({
                                            'algorithm': algorithm,
                                            'factor': heuristic,
                                            'effect': effect,
                                            'emm_low': low_data['mean'].iloc[0],
                                            'emm_high': high_data['mean'].iloc[0],
                                            'ci_low': ci_low,
                                            'ci_high': ci_high,
                                            'se_diff': (ci_high - ci_low) / 3.92,
                                            'method': 'Bootstrap_glm_fail'
                                        })
                            continue
                        
                        # Calculate EMMs for each heuristic
                        for h_idx, heuristic in enumerate(self.heuristics):
                            if heuristic not in model_data.columns:
                                continue
                            
                            # Get all unique combinations of OTHER heuristics in the L12 design
                            other_heuristics = [h for h in self.heuristics if h != heuristic and h in model_data.columns]
                            
                            # Create all combinations (2^n combinations for n other heuristics)
                            other_combinations = list(product([0, 1], repeat=len(other_heuristics)))
                            
                            # Collect predictions for LOW and HIGH levels of target heuristic
                            emm_low_probs = []
                            emm_high_probs = []
                            emm_low_ses = []
                            emm_high_ses = []
                            
                            for combo in other_combinations:
                                # Build prediction design matrix
                                pred_row_low = [1]  # Intercept
                                pred_row_high = [1]
                                
                                for i, h in enumerate(self.heuristics):
                                    if h == heuristic:
                                        pred_row_low.append(0)
                                        pred_row_high.append(1)
                                    elif h in other_heuristics:
                                        idx_in_combo = other_heuristics.index(h)
                                        pred_row_low.append(combo[idx_in_combo])
                                        pred_row_high.append(combo[idx_in_combo])
                                
                                pred_exog_low = np.array([pred_row_low])
                                pred_exog_high = np.array([pred_row_high])
                                
                                try:
                                    # Get predictions on probability scale
                                    pred_low = glm_result.get_prediction(pred_exog_low)
                                    pred_high = glm_result.get_prediction(pred_exog_high)
                                    
                                    # Convert to percentage scale
                                    emm_low_probs.append(pred_low.predicted_mean[0] * 100)
                                    emm_high_probs.append(pred_high.predicted_mean[0] * 100)
                                    emm_low_ses.append(pred_low.se_mean[0] * 100)
                                    emm_high_ses.append(pred_high.se_mean[0] * 100)
                                    
                                except Exception as e:
                                    logger.debug(f"Prediction failed for combination {combo}: {e}")
                                    continue
                            
                            # Calculate marginal means by averaging across all combinations
                            if emm_low_probs and emm_high_probs:
                                emm_low = np.mean(emm_low_probs)
                                emm_high = np.mean(emm_high_probs)
                                
                                # Calculate the effect (difference in percentage points)
                                effect = emm_high - emm_low
                                
                                # Calculate SE for the marginal means
                                # SE of mean of predictions = sqrt(sum(SE_i^2)) / n
                                se_emm_low = np.sqrt(np.sum(np.array(emm_low_ses)**2)) / len(emm_low_ses)
                                se_emm_high = np.sqrt(np.sum(np.array(emm_high_ses)**2)) / len(emm_high_ses)
                                
                                # SE for difference (assuming independence)
                                se_diff = np.sqrt(se_emm_low**2 + se_emm_high**2)
                                
                                # Calculate 95% CI for the difference (use z-score for large samples)
                                z_critical = 1.96
                                effect_ci_low = effect - z_critical * se_diff
                                effect_ci_high = effect + z_critical * se_diff
                                
                                emm_effects.append({
                                    'algorithm': algorithm,
                                    'factor': heuristic,
                                    'effect': effect,
                                    'emm_low': emm_low,
                                    'emm_high': emm_high,
                                    'ci_low': effect_ci_low,
                                    'ci_high': effect_ci_high,
                                    'se_diff': se_diff,
                                    'method': 'GLM',
                                    'n_combinations': len(emm_low_probs)
                                })
                            else:
                                logger.warning(f"No valid predictions for {algorithm}-{heuristic}")
                                continue
                    else:
                        # For non-GLM responses (e.g., runtime), fall back to bootstrap
                        logger.info(f"Using bootstrap for {response_column} (non-GLM)")
                        alg_bootstrap = self.generate_main_effects_data(alg_data, response_column)
                        for heuristic in self.heuristics:
                            bootstrap_data = alg_bootstrap[alg_bootstrap['factor'] == heuristic]
                            if len(bootstrap_data) >= 2:
                                low_data = bootstrap_data[bootstrap_data['level'] == 0]
                                high_data = bootstrap_data[bootstrap_data['level'] == 1]
                                if len(low_data) > 0 and len(high_data) > 0:
                                    effect = high_data['mean'].iloc[0] - low_data['mean'].iloc[0]
                                    ci_low = effect - 2 * np.sqrt(
                                        (high_data['mean'].iloc[0] - high_data['ci_low'].iloc[0])**2 +
                                        (low_data['ci_high'].iloc[0] - low_data['mean'].iloc[0])**2
                                    )
                                    ci_high = effect + 2 * np.sqrt(
                                        (high_data['ci_high'].iloc[0] - high_data['mean'].iloc[0])**2 +
                                        (low_data['mean'].iloc[0] - low_data['ci_low'].iloc[0])**2
                                    )
                                    emm_effects.append({
                                        'algorithm': algorithm,
                                        'factor': heuristic,
                                        'effect': effect,
                                        'emm_low': low_data['mean'].iloc[0],
                                        'emm_high': high_data['mean'].iloc[0],
                                        'ci_low': ci_low,
                                        'ci_high': ci_high,
                                        'se_diff': (ci_high - ci_low) / 3.92,
                                        'method': 'Bootstrap_runtime'
                                    })
                        
                except Exception as e:
                    logger.warning(f"Model fitting failed for {algorithm}: {e}")
                    # Fall back to bootstrap method for this algorithm
                    alg_bootstrap = self.generate_main_effects_data(alg_data, response_column)
                    
                    # Convert bootstrap results to EMM format
                    for heuristic in self.heuristics:
                        bootstrap_data = alg_bootstrap[alg_bootstrap['factor'] == heuristic]
                        
                        if len(bootstrap_data) >= 2:
                            low_data = bootstrap_data[bootstrap_data['level'] == 0]
                            high_data = bootstrap_data[bootstrap_data['level'] == 1]
                            
                            if len(low_data) > 0 and len(high_data) > 0:
                                effect = high_data['mean'].iloc[0] - low_data['mean'].iloc[0]
                                
                                # Use bootstrap CIs (conservative)
                                ci_low = effect - 2 * np.sqrt(
                                    (high_data['mean'].iloc[0] - high_data['ci_low'].iloc[0])**2 +
                                    (low_data['ci_high'].iloc[0] - low_data['mean'].iloc[0])**2
                                )
                                ci_high = effect + 2 * np.sqrt(
                                    (high_data['ci_high'].iloc[0] - high_data['mean'].iloc[0])**2 +
                                    (low_data['mean'].iloc[0] - low_data['ci_low'].iloc[0])**2
                                )
                                
                                emm_effects.append({
                                    'algorithm': algorithm,
                                    'factor': heuristic,
                                    'effect': effect,
                                    'emm_low': low_data['mean'].iloc[0],
                                    'emm_high': high_data['mean'].iloc[0],
                                    'ci_low': ci_low,
                                    'ci_high': ci_high,
                                    'se_diff': (ci_high - ci_low) / 3.92,
                                    'method': 'Bootstrap_fallback'
                                })
                    
        except Exception as e:
            logger.error(f"EMM calculation failed: {e}")
            # Fall back to original bootstrap method
            return self.generate_main_effects_data(data, response_column)
        
        emm_df = pd.DataFrame(emm_effects)
        logger.info(f"Generated {len(emm_df)} EMM effects")
        
        return emm_df
    
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
        """Generate Algorithm×Heuristic interaction data with confidence intervals."""
        logger.info(f"Generating interaction effects data for {response_column}")
        
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
                        
                    response_values = subset[response_column].dropna()
                    
                    if len(response_values) > 0:
                        mean_val, ci_low, ci_high = self.bootstrap_confidence_interval(response_values)
                        
                        interactions.append({
                            'heuristic': heuristic,
                            'level': level,
                            'algorithm': algorithm,
                            'mean': mean_val,
                            'ci_low': ci_low,
                            'ci_high': ci_high,
                            'n_obs': len(response_values)
                        })
                        
        return pd.DataFrame(interactions)
    
    def calculate_pareto_frontier(self, data, x_col, y_col):
        """Calculate Pareto frontier for two objectives (minimize x, maximize y)."""
        logger.info(f"Calculating Pareto frontier for {x_col} vs {y_col}")
        
        # Prepare data points
        points = data[[x_col, y_col]].dropna().copy()
        
        if len(points) < 3:
            logger.warning("Not enough points for Pareto frontier calculation")
            data['is_pareto'] = 0
            return data
        
        # For Pareto frontier: minimize runtime (x), maximize success rate (y)
        # Transform to minimize both: (x, -y)
        transformed_points = points.copy()
        transformed_points[y_col] = -transformed_points[y_col]
        
        # Find Pareto frontier points
        pareto_mask = np.zeros(len(points), dtype=bool)
        
        for i in range(len(points)):
            is_pareto = True
            current_point = transformed_points.iloc[i]
            
            for j in range(len(points)):
                if i == j:
                    continue
                    
                other_point = transformed_points.iloc[j]
                
                # Check if other point dominates current point
                if (other_point[x_col] <= current_point[x_col] and 
                    other_point[y_col] <= current_point[y_col] and
                    (other_point[x_col] < current_point[x_col] or 
                     other_point[y_col] < current_point[y_col])):
                    is_pareto = False
                    break
                    
            pareto_mask[i] = is_pareto
        
        # Add Pareto frontier indicator to original data
        data_copy = data.copy()
        data_copy['is_pareto'] = 0
        data_copy.loc[points.index[pareto_mask], 'is_pareto'] = 1
        
        logger.info(f"Found {np.sum(pareto_mask)} Pareto optimal points")
        
        return data_copy
    
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
        """Generate Plot 1: Main effects with 95% CIs (two panels) using EMM differences."""
        logger.info("Generating main effects plots with EMM-based confidence intervals...")
        logger.info(f"Plot 1 using CORRECTED {sshom_col} (includes filtered HOMs in denominator)")
        
        # Set up the figure with two subplots
        fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(14, 6))
        
        # Define colors for algorithms
        colors = {'genetic': '#1f77b4', 'local': '#ff7f0e'}
        
        # Define factors outside conditionals to avoid UnboundLocalError if SSHOM data is empty
        factors = self.heuristics
        x_pos = np.arange(len(factors))
        width = 0.35
        
        # Plot 1A: SSHOM Success Rate Main Effects
        if not main_effects_sshom.empty:
            
            for i, alg in enumerate(['genetic', 'local']):
                alg_data = main_effects_sshom[main_effects_sshom['algorithm'] == alg]
                
                means = []
                ci_lows = []
                ci_highs = []
                
                for factor in factors:
                    factor_data = alg_data[alg_data['factor'] == factor]
                    
                    if len(factor_data) > 0:
                        # Check if this is EMM data (has 'effect' column) or bootstrap data
                        if 'effect' in factor_data.columns:
                            # EMM-based analysis
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
                
                ax1.bar(x_pos + i*width, means, width, 
                       yerr=[ci_lows, ci_highs], capsize=3,
                       label=alg.title(), color=colors[alg], alpha=0.8)
            
            ax1.set_xlabel('Heuristic Factors')
            ax1.set_ylabel(f'Effect on {sshom_col} (EMM High - Low)')
            ax1.set_title('Panel A: Main Effects on SSHOM Success Rate\n(EMM Differences with 95% CIs)')
            ax1.set_xticks(x_pos + width/2)
            ax1.set_xticklabels(factors, rotation=45, ha='right')
            ax1.axhline(y=0, color='black', linestyle='-', alpha=0.3)
            ax1.legend()
            ax1.grid(True, alpha=0.3)
        
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
                        # Check if this is EMM data (has 'effect' column) or bootstrap data
                        if 'effect' in factor_data.columns:
                            # EMM-based analysis
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
            ax2.set_ylabel(f'Effect on {runtime_col} (EMM High - Low)')
            ax2.set_title('Panel B: Main Effects on Runtime\n(EMM Differences with 95% CIs)')
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
                        
                        # Plot the line with error bars
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
    
    def plot_pareto_frontier(self, pareto_data):
        """Generate Plot 3: Pareto frontier scatter plot."""
        logger.info("Generating Pareto frontier plot...")
        
        fig, ax = plt.subplots(figsize=(10, 8))
        
        colors = {'genetic': '#1f77b4', 'local': '#ff7f0e'}
        
        # Plot all points
        for alg in ['genetic', 'local']:
            alg_data = pareto_data[pareto_data['algorithm'] == alg]
            
            if not alg_data.empty:
                # Non-Pareto points
                non_pareto = alg_data[alg_data['is_pareto'] == 0]
                if len(non_pareto) > 0:
                    ax.scatter(non_pareto['runtime'], non_pareto['sshom_success'], 
                              color=colors[alg], alpha=0.6, s=50, label=f'{alg.title()} (dominated)')
                
                # Pareto optimal points
                pareto_points = alg_data[alg_data['is_pareto'] == 1]
                if len(pareto_points) > 0:
                    ax.scatter(pareto_points['runtime'], pareto_points['sshom_success'], 
                              color=colors[alg], alpha=1.0, s=100, marker='D', 
                              edgecolors='black', linewidth=1, 
                              label=f'{alg.title()} (Pareto optimal)')
        
        # Draw Pareto frontier line
        pareto_optimal = pareto_data[pareto_data['is_pareto'] == 1].copy()
        if len(pareto_optimal) > 1:
            # Sort by runtime for proper line drawing
            pareto_optimal = pareto_optimal.sort_values('runtime')
            ax.plot(pareto_optimal['runtime'], pareto_optimal['sshom_success'], 
                   color='red', linewidth=2, linestyle='--', alpha=0.8, 
                   label='Pareto Frontier')
        
        ax.set_xlabel('Runtime (ms)')
        ax.set_ylabel('SSHOM Success Rate (%)')
        ax.set_title('Pareto Frontier Analysis: SSHOM Success Rate vs Runtime Trade-off')
        ax.legend()
        ax.grid(True, alpha=0.3)
        
        # Add annotation
        ax.text(0.02, 0.98, 
               'Points on frontier represent\noptimal trade-offs between\nsuccess rate and runtime',
               transform=ax.transAxes, fontsize=10, 
               bbox=dict(boxstyle='round', facecolor='white', alpha=0.8),
               verticalalignment='top')
        
        plt.tight_layout()
        
        # Save the plot
        plot_path = self.output_dir / "plot3_pareto_frontier.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        plt.close()
        logger.info(f"Saved Pareto frontier plot: {plot_path}")
        
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
        
        # Generate CSV 1: EMM-based main effects for SSHOM success rate
        main_effects_sshom = self.calculate_emm_effects(experiment_data, actual_sshom_col)
        self.save_latex_csv(main_effects_sshom, 'main_effects_emm_latex.csv')
        
        # Generate CSV 2: EMM-based main effects for runtime  
        main_effects_runtime = self.calculate_emm_effects(experiment_data, actual_runtime_col)
        self.save_latex_csv(main_effects_runtime, 'main_effects_runtime_emm_latex.csv')
        
        # Also generate bootstrap versions for comparison
        main_effects_sshom_bootstrap = self.generate_main_effects_data(experiment_data, actual_sshom_col)
        self.save_latex_csv(main_effects_sshom_bootstrap, 'main_effects_bootstrap_latex.csv')
        
        main_effects_runtime_bootstrap = self.generate_main_effects_data(experiment_data, actual_runtime_col)
        self.save_latex_csv(main_effects_runtime_bootstrap, 'main_effects_runtime_bootstrap_latex.csv')
        
        # Generate CSV 3: Interaction effects for SSHOM success rate
        interaction_effects = self.generate_interaction_effects_data(experiment_data, actual_sshom_col)
        self.save_latex_csv(interaction_effects, 'interaction_effects_latex.csv')
        
        # Generate CSV 4: Pareto frontier data
        pareto_data = self.calculate_pareto_frontier(
            experiment_data, 
            actual_runtime_col, 
            actual_sshom_col
        )
        
        # Select relevant columns for Pareto plot
        pareto_output = pareto_data[['algorithm', actual_runtime_col, actual_sshom_col, 'is_pareto']].copy()
        pareto_output.columns = ['algorithm', 'runtime', 'sshom_success', 'is_pareto']
        
        self.save_latex_csv(pareto_output, 'pareto_data_latex.csv')
        
        # Generate the three main plots
        logger.info("Generating publication-quality plots...")
        
        plot_files = []
        
        # Plot 1: Main effects with 95% CIs (two panels)
        plot1_path = self.plot_main_effects(main_effects_sshom, main_effects_runtime, 
                                           actual_sshom_col, actual_runtime_col)
        plot_files.append(plot1_path)
        
        # Plot 2: Algorithm×Heuristic interaction plots (six small multiples)
        plot2_path = self.plot_interaction_effects(interaction_effects, actual_sshom_col)
        plot_files.append(plot2_path)
        
        # Plot 3: Pareto frontier scatter plot
        plot3_path = self.plot_pareto_frontier(pareto_output)
        plot_files.append(plot3_path)
        
        # Generate summary report
        self.generate_summary_report(experiment_data, actual_sshom_col, actual_runtime_col)
        
        logger.info("Analysis complete! Generated 6 CSV files, 1 summary text file, and 3 PNG plots:")
        logger.info(f"Output directory: {self.output_dir}")
        logger.info("CSV FILES (EMM-BASED):")
        logger.info("- main_effects_emm_latex.csv (EMM main effects data)")  
        logger.info("- main_effects_runtime_emm_latex.csv (EMM runtime effects data)")
        logger.info("CSV FILES (BOOTSTRAP-BASED):")
        logger.info("- main_effects_bootstrap_latex.csv (bootstrap main effects data)")  
        logger.info("- main_effects_runtime_bootstrap_latex.csv (bootstrap runtime effects data)")
        logger.info("CSV FILES (OTHER):")
        logger.info("- interaction_effects_latex.csv (interaction data)")
        logger.info("- pareto_data_latex.csv (Pareto frontier data)")
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
            "EMM-BASED ANALYSIS (RECOMMENDED):",
            "1. main_effects_emm_latex.csv - EMM main effects on SSHOM success rate",
            "2. main_effects_runtime_emm_latex.csv - EMM main effects on runtime",
            "",
            "BOOTSTRAP ANALYSIS (FOR COMPARISON):",
            "3. main_effects_bootstrap_latex.csv - Bootstrap main effects on SSHOM success rate", 
            "4. main_effects_runtime_bootstrap_latex.csv - Bootstrap main effects on runtime",
            "",
            "OTHER DATA:",
            "5. interaction_effects_latex.csv - Algorithm×Heuristic interactions",
            "6. pareto_data_latex.csv - Pareto frontier analysis data",
            "",
            "PNG PLOTS:",
            "1. plot1_main_effects.png - EMM main effects with proper 95% CIs (two panels)",
            "2. plot2_interactions.png - Interaction plots (six small multiples)",  
            "3. plot3_pareto_frontier.png - Pareto frontier scatter plot",
            "",
            "STATISTICAL METHODS:",
            f"- GLM-based EMM Analysis: {'Available (statsmodels)' if HAS_STATSMODELS else 'Unavailable (fallback to bootstrap)'}",
            "- Method: Binomial GLM with logit link for SSHOM rates",
            "- Confidence Intervals: Delta method (95% CIs) with bootstrap fallback",
            "- L12 Design-aware: Yes (EMM method accounts for orthogonal structure)",
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
        
        # Save summary to file
        summary_path = self.output_dir / "analysis_summary.txt"
        with open(summary_path, 'w') as f:
            f.write(summary_text)
            
        # Also print to console
        print(summary_text)
        
        logger.info(f"Summary report saved to: {summary_path}")


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
    
    args = parser.parse_args()
    
    # Determine the root path
    if args.root:
        root_path = Path(args.root)
    else:
        root_path = Path.cwd()
    
    if not root_path.exists():
        logger.error(f"Root path does not exist: {root_path}")
        return
    
    logger.info(f"Analyzing data from: {root_path}")
    
    # Initialize analyzer with specified path
    analyzer = L12AnalysisEnhanced(base_path=root_path)
    
    # Run complete analysis
    plot_files = analyzer.run_complete_analysis()
    
    print("\n" + "="*60)
    print("ANALYSIS COMPLETE!")
    print("="*60)
    print(f"\nGenerated files in: {analyzer.output_dir}")
    print("\nCSV DATA FILES:")
    print("EMM-BASED (RECOMMENDED):")
    print("- main_effects_emm_latex.csv")
    print("- main_effects_runtime_emm_latex.csv")
    print("BOOTSTRAP-BASED (COMPARISON):")
    print("- main_effects_bootstrap_latex.csv")
    print("- main_effects_runtime_bootstrap_latex.csv")
    print("OTHER:")
    print("- interaction_effects_latex.csv")
    print("- pareto_data_latex.csv")
    print("- analysis_summary.txt")
    
    print("\nPNG PLOT FILES:")
    if plot_files:
        for plot_file in plot_files:
            print(f"- {plot_file.name}")
    else:
        print("- plot1_main_effects.png")
        print("- plot2_interactions.png")
        print("- plot3_pareto_frontier.png")
    
    print("\nPLOT DESCRIPTIONS:")
    print("1. plot1_main_effects.png - GLM-based EMM main effects with proper 95% CIs")
    print("   • Panel A: SSHOM success rate effects (GLM EMM differences)")
    print("   • Panel B: Runtime effects (bootstrap differences)")
    print("   • Uses Binomial GLM (logit link) for SSHOM rates")
    print("   • Accounts for L12 orthogonal array structure")
    print("   • Proper confidence intervals via delta method")
    print("2. plot2_interactions.png - Algorithm×Heuristic interactions")
    print("   • Six small multiples (one per heuristic)")
    print("   • Shows if heuristics work differently per algorithm")
    print("3. plot3_pareto_frontier.png - Trade-off analysis")
    print("   • Runtime vs SSHOM success rate")
    print("   • Pareto frontier highlighted")
    
    print("\nSTATISTICAL METHODS:")
    print("- GLM-based EMM Analysis: Binomial GLM with logit link for SSHOM rates")
    print("- Confidence Intervals: Delta method (95% CIs) with bootstrap fallback")
    print("- Significance scheme: *** p<0.001, ** p<0.01, * p<0.05, † p<0.10, ns ≥0.10")
    
    print("\nNext steps:")
    print("1. Review the PNG plots - ready for thesis inclusion!")
    print("2. Check analysis_summary.txt for data overview")
    print("3. Use GLM-based EMM CSV files for primary analysis (statistically rigorous)")
    print("4. Use bootstrap CSV files for sensitivity analysis")
    print("5. Install statsmodels if needed: pip install statsmodels")
    print("="*60)
    
    
if __name__ == "__main__":
    main()