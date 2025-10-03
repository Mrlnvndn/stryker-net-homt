#!/usr/bin/env python3
"""
GLM-based ANOVA-style Analysis of SSHOM Success Rates

Performs binomial GLM analysis with logit link on SSHOM success rates across multiple repositories.
Tests main effects and Algorithm×Heuristic interactions using likelihood ratio tests.

Usage:
    python glm_binomial_anova.py --solution <SolutionName> [--repo <RepoName>]
    python glm_binomial_anova.py --solution Solution1 Solution2 Solution3
    
Expected data structure:
    Input:  C:\\Users\\MerlijnU\\outputs\\{repo}\\{solution}\\**/mutation-report-hom.csv
    Output: C:\\Users\\MerlijnU\\analysis\\{solution}\\anova\\glm_*.csv
    
Expected CSV columns (from analyze_homcsv.py output):
- solution, algorithm, total_homs, sshom_2, sshom_3, sshom_4, used_heuristics
- Heuristics parsed from used_heuristics string or individual heuristic columns

Author: Generated for Merlijn's thesis GLM analysis workflow
"""

import argparse
import warnings
from pathlib import Path
from typing import Dict, List, Optional, Tuple, Union
import re

import numpy as np
import pandas as pd
import statsmodels.api as sm
from statsmodels.genmod.families import Binomial
from statsmodels.formula.api import glm
from statsmodels.genmod.generalized_linear_model import GLMResults

# Configure warnings
warnings.filterwarnings('ignore', category=RuntimeWarning)
warnings.filterwarnings('ignore', category=UserWarning)

# Define heuristics in consistent order (matching your L12 design)
HEURISTICS = [
    'CodeLocation',
    'EmptyAssessingTests', 
    'MutatorType',
    'OverlappingTests',
    'MaxSizeLimit',
    'SyntaxNodeConflict'
]

def extract_solution_from_path(data_dir) -> str:
    """
    Extract solution name from data directory path.
    Expected structure: .../MoreLinq/MoreLinq/... or similar
    Returns the parent directory name as solution.
    
    Args:
        data_dir: Path to data directory (can be Path or string)
        
    Returns:
        Solution name extracted from path
    """
    # Convert to Path object if it's a string
    if isinstance(data_dir, str):
        data_dir = Path(data_dir)
    
    # Get the directory name - for "C:\Users\MerlijnU\outputs\MoreLinq\MoreLinq"
    # we want "MoreLinq" (the parent of the inner MoreLinq)
    path_parts = data_dir.resolve().parts
    
    # Look for the solution name in the path
    # The data_dir is typically the inner directory, so parent is the solution
    if len(path_parts) >= 2:
        # Get parent directory name
        return path_parts[-2] if path_parts[-1] == path_parts[-2] else path_parts[-1]
    
    # Fallback to the directory name itself
    return data_dir.name

def find_csv_files(data_dir: Path) -> List[Path]:
    """
    Find all CSV files in the data directory structure.
    
    Args:
        data_dir: Path to directory containing CSV files
        
    Returns:
        List of CSV file paths
    """
    csv_pattern =  "**/mutation-report-hom.csv"
    
    csv_files = []
    found_files = list(data_dir.rglob(csv_pattern))
    csv_files.extend([f for f in found_files if f.is_file() and f not in csv_files])
    
    return csv_files

def parse_heuristics_from_string(used_heuristics_str: str) -> Dict[str, int]:
    """
    Parse heuristics from the 'used_heuristics' string.
    
    Args:
        used_heuristics_str: String containing active heuristics
        
    Returns:
        Dictionary mapping heuristic names to 0/1 values
    """
    heuristic_flags = {h: 0 for h in HEURISTICS}
    
    if pd.isna(used_heuristics_str) or not isinstance(used_heuristics_str, str):
        return heuristic_flags
    
    # Clean and tokenize the string
    used_heuristics_str = str(used_heuristics_str).strip()
    if not used_heuristics_str or used_heuristics_str.lower() in ['', 'nan', 'none', 'null']:
        return heuristic_flags
    
    # Split on various delimiters
    tokens = re.split(r"[,\s;\-|]+", used_heuristics_str)
    tokens = [t.strip() for t in tokens if t.strip()]
    
    # Match tokens against known heuristics
    for heuristic in HEURISTICS:
        for token in tokens:
            if heuristic.lower() in token.lower() or token.lower() in heuristic.lower():
                heuristic_flags[heuristic] = 1
                break
    
    return heuristic_flags

def load_data(data_dir: Path) -> pd.DataFrame:
    """
    Load and combine all CSV files from the data directory.
    
    Args:
        data_dir: Path to directory containing CSV files
        
    Returns:
        Combined DataFrame with all experimental data
    """
    print(f"Loading data from: {data_dir}")
    
    csv_files = find_csv_files(data_dir)
    if not csv_files:
        raise ValueError(f"No CSV files found in {data_dir}")
    
    print(f"Found {len(csv_files)} CSV files")
    
    all_data = []
    
    for csv_file in csv_files:
        try:
            df = pd.read_csv(csv_file, engine="python")
            if df.empty:
                continue
                
            # Add source file info for debugging
            df['source_file'] = str(csv_file.name)
            all_data.append(df)
            
        except Exception as e:
            print(f"Warning: Failed to load {csv_file}: {e}")
            continue
    
    if not all_data:
        raise ValueError("No valid CSV files could be loaded")
    
    # Combine all data
    combined_df = pd.concat(all_data, ignore_index=True)
    print(f"Loaded {len(combined_df)} total observations")
    print(f"Available columns: {list(combined_df.columns)}")
    
    return combined_df

def prepare_glm_data(df: pd.DataFrame, data_dir: Path) -> pd.DataFrame:
    """
    Prepare data for GLM analysis by creating necessary columns and cleaning data.
    
    Args:
        df: Raw experimental data
        data_dir: Path to data directory (for extracting solution name)
        
    Returns:
        DataFrame prepared for GLM analysis
    """
    print("Preparing data for GLM analysis...")
    
    # Make a copy to avoid modifying original
    data = df.copy()
    
    # Calculate total SSHOM successes
    sshom_cols = ['sshom_2', 'sshom_3', 'sshom_4']
    available_sshom_cols = [col for col in sshom_cols if col in data.columns]
    
    if not available_sshom_cols:
        raise ValueError(f"No SSHOM columns found. Expected: {sshom_cols}")
    
    # Sum available SSHOM columns, filling NaN with 0
    # This ensures consistency with analyze_homcsv.py calculation
    data['total_sshoms'] = data[available_sshom_cols].fillna(0).sum(axis=1)
    
    # Calculate total_homs_with_filtered if not present (includes filtered HOMs)
    # This matches the calculation in analyze_homcsv_enhanced.py
    if 'total_homs_with_filtered' not in data.columns:
        print("Calculating 'total_homs_with_filtered' from raw data...")
        
        # Try multiple possible column names for total HOMs
        hom_total_candidates = ['total_homs', 'hom_total', 'total_hom', 'homs_total']
        hom_total_col = None
        
        for candidate in hom_total_candidates:
            if candidate in data.columns:
                hom_total_col = candidate
                print(f"Found HOM total column: {candidate}")
                break
        
        if hom_total_col is not None:
            original_hom_total = data[hom_total_col].fillna(0)
            
            # Add filtered HOMs to get true total
            # Include ALL filtered HOM categories to get accurate denominator
            filtered_cols = ['filtered_empty_total', 'filtered_invalid_total']
            total_homs_with_filtered = original_hom_total.copy()
            
            for col in filtered_cols:
                if col in data.columns:
                    total_homs_with_filtered += data[col].fillna(0)
                    print(f"  Added {col} to total_homs_with_filtered")
            
            data['total_homs_with_filtered'] = total_homs_with_filtered
            data['total_homs_kept_only'] = original_hom_total
            
            print(f"Calculated total_homs_with_filtered: mean = {total_homs_with_filtered.mean():.1f}")
        else:
            print("Warning: No HOM total column found, cannot calculate total_homs_with_filtered")
            if 'total_homs' in data.columns:
                data['total_homs_with_filtered'] = data['total_homs']
    
    # Use total_homs_with_filtered if available (includes filtered HOMs), otherwise fall back to total_homs
    if 'total_homs_with_filtered' in data.columns:
        print("Using 'total_homs_with_filtered' for GLM analysis (includes filtered HOMs)...")
        data['total_homs_for_analysis'] = data['total_homs_with_filtered']
    elif 'total_homs' in data.columns:
        print("Warning: 'total_homs_with_filtered' not found, falling back to 'total_homs'...")
        data['total_homs_for_analysis'] = data['total_homs']
    else:
        raise ValueError("Neither 'total_homs_with_filtered' nor 'total_homs' columns found")
    
    # Ensure the column exists for backward compatibility
    if 'total_homs' not in data.columns:
        data['total_homs'] = data['total_homs_for_analysis']
    
    # Create missing required columns from available data
    
    # 1. Create 'algorithm' column from 'used_algorithms' if it exists
    if 'used_algorithms' in data.columns and 'algorithm' not in data.columns:
        print("Creating 'algorithm' column from 'used_algorithms'...")
        # Parse the algorithms from the string (assuming comma-separated or similar)
        data['algorithm'] = data['used_algorithms'].astype(str).apply(
            lambda x: x.strip().split(',')[0] if pd.notna(x) and x.strip() else 'unknown'
        )
    
    # 2. Create 'solution' column from data directory path
    if 'solution' not in data.columns:
        print("Creating 'solution' column from data directory path...")
        solution_name = extract_solution_from_path(data_dir)
        data['solution'] = solution_name
        print(f"Extracted solution name: '{solution_name}'")
    
    # Ensure we have required columns after creation
    required_cols = ['solution', 'algorithm', 'total_homs']
    missing_cols = [col for col in required_cols if col not in data.columns]
    
    if missing_cols:
        raise ValueError(f"Still missing required columns after creation: {missing_cols}")
    
    # Parse heuristics from used_heuristics if available
    if 'used_heuristics' in data.columns:
        print("Parsing heuristics from 'used_heuristics' column...")
        
        for idx, row in data.iterrows():
            heuristic_flags = parse_heuristics_from_string(row.get('used_heuristics', ''))
            for heuristic, value in heuristic_flags.items():
                data.loc[idx, f'H_{heuristic}'] = value
    else:
        # Try to find individual heuristic columns
        print("Looking for individual heuristic columns...")
        for heuristic in HEURISTICS:
            possible_cols = [
                f'H_{heuristic}',
                f'heur_{heuristic}_on',
                heuristic,
                f'{heuristic}_on'
            ]
            
            found_col = None
            for col in possible_cols:
                if col in data.columns:
                    found_col = col
                    break
            
            if found_col:
                data[f'H_{heuristic}'] = data[found_col].astype(int)
            else:
                print(f"Warning: No column found for heuristic {heuristic}, setting to 0")
                data[f'H_{heuristic}'] = 0
    
    # Clean and validate data
    print("Cleaning and validating data...")
    
    # Remove rows with missing critical data
    initial_len = len(data)
    data = data.dropna(subset=['solution', 'algorithm', 'total_homs_for_analysis'])
    print(f"Removed {initial_len - len(data)} rows with missing solution/algorithm/total_homs_for_analysis")
    
    # Remove rows with zero or negative trials
    initial_len = len(data)
    data = data[data['total_homs_for_analysis'] > 0]
    print(f"Removed {initial_len - len(data)} rows with zero or negative total_homs_for_analysis")
    
    # Ensure successes <= trials
    initial_len = len(data)
    data = data[data['total_sshoms'] <= data['total_homs_for_analysis']]
    print(f"Removed {initial_len - len(data)} rows where total_sshoms > total_homs_for_analysis")
    
    # Calculate success rate percentage for reporting
    # Formula: (sshom_2 + sshom_3 + sshom_4) / total_homs_with_filtered * 100 - matches analyze_homcsv.py
    data['success_rate_pct'] = 100.0 * data['total_sshoms'] / data['total_homs_for_analysis']
    
    # Ensure algorithm is categorical with consistent reference level (local as baseline)
    data['algorithm'] = data['algorithm'].str.lower().str.strip()
    algorithm_values = data['algorithm'].unique()
    print(f"Found algorithms: {algorithm_values}")
    
    # Set categorical with local as reference (first) level
    if 'local' in algorithm_values and 'genetic' in algorithm_values:
        data['algorithm'] = pd.Categorical(data['algorithm'], 
                                         categories=['local', 'genetic'], 
                                         ordered=False)
    else:
        # Use whatever algorithms we have
        data['algorithm'] = pd.Categorical(data['algorithm'])
    
    # Ensure heuristic columns are integer (0/1)
    for heuristic in HEURISTICS:
        col_name = f'H_{heuristic}'
        if col_name in data.columns:
            data[col_name] = data[col_name].fillna(0).astype(int)
        else:
            print(f"Warning: Creating missing heuristic column {col_name} with all zeros")
            data[col_name] = 0
    
    print(f"Final dataset: {len(data)} observations")
    
    # Print summary statistics
    print("\nDataset summary:")
    print(f"Solutions: {data['solution'].nunique()} unique ({list(data['solution'].unique())})")
    print(f"Algorithms: {data['algorithm'].value_counts().to_dict()}")
    print(f"Mean success rate: {data['success_rate_pct'].mean():.1f}%")
    print(f"Mean trials per observation (with filtered): {data['total_homs_for_analysis'].mean():.1f}")
    
    return data

def fit_glm_binomial(df_repo: pd.DataFrame) -> Optional[GLMResults]:
    """
    Fit binomial GLM with logit link for a single repository.
    
    Args:
        df_repo: Data for a single repository
        
    Returns:
        Fitted GLM results or None if fitting failed
    """
    try:
        # Check if we have sufficient data
        if len(df_repo) < 10:
            print(f"Warning: Only {len(df_repo)} observations, need at least 10 for GLM")
            return None
            
        if df_repo['algorithm'].nunique() < 2:
            print("Warning: Need at least 2 algorithm levels for GLM")
            return None
        
        # Prepare formula with main effects and Algorithm×Heuristic interactions
        heuristic_terms = [f'H_{h}' for h in HEURISTICS]
        interaction_terms = [f'algorithm:H_{h}' for h in HEURISTICS]
        
        # Build formula: response ~ Algorithm + H1 + H2 + ... + Algorithm:H1 + Algorithm:H2 + ...
        main_terms = ['algorithm'] + heuristic_terms
        all_terms = main_terms + interaction_terms
        formula = f"I(total_sshoms / total_homs_for_analysis) ~ {' + '.join(all_terms)}"
        
        print(f"Fitting GLM with formula: {formula}")
        
        # Fit GLM with binomial family and logit link
        # Use total_homs_for_analysis as frequency weights (includes filtered HOMs)
        model = glm(
            formula=formula,
            data=df_repo,
            family=Binomial(),
            freq_weights=df_repo['total_homs_for_analysis']
        )
        
        results = model.fit()
        
        # Check for convergence
        if not results.converged:
            print("Warning: GLM did not converge")
            return None
            
        return results
        
    except Exception as e:
        print(f"Error fitting GLM: {e}")
        return None

def lr_tests_by_term(df_repo: pd.DataFrame) -> pd.DataFrame:
    """
    Perform likelihood ratio tests for each term in the model.
    
    Args:
        df_repo: Data for a single repository
        
    Returns:
        DataFrame with LR test results
    """
    # Fit full model
    full_model = fit_glm_binomial(df_repo)
    if full_model is None:
        return pd.DataFrame()
    
    # Get null deviance for effect size calculation
    null_deviance = full_model.null_deviance
    full_deviance = full_model.deviance
    
    # Terms to test (excluding intercept)
    heuristic_terms = [f'H_{h}' for h in HEURISTICS]
    interaction_terms = [f'algorithm:H_{h}' for h in HEURISTICS]
    test_terms = ['algorithm'] + heuristic_terms + interaction_terms
    
    lr_results = []
    
    print(f"Performing LR tests for {len(test_terms)} terms...")
    
    for term in test_terms:
        try:
            # Create reduced formula by removing the term
            if term == 'algorithm':
                # Remove algorithm and all its interactions
                reduced_terms = heuristic_terms.copy()
            elif term.startswith('H_') and ':' not in term:
                # Remove main effect and its interaction
                reduced_terms = ['algorithm'] + [t for t in heuristic_terms if t != term]
                reduced_terms.extend([t for t in interaction_terms if term not in t])
            elif ':' in term:
                # Remove just the interaction term
                reduced_terms = ['algorithm'] + heuristic_terms
                reduced_terms.extend([t for t in interaction_terms if t != term])
            else:
                continue
            
            if not reduced_terms:
                reduced_formula = "I(total_sshoms / total_homs_for_analysis) ~ 1"
            else:
                reduced_formula = f"I(total_sshoms / total_homs_for_analysis) ~ {' + '.join(reduced_terms)}"

            # Fit reduced model
            reduced_model = glm(
                formula=reduced_formula,
                data=df_repo,
                family=Binomial(),
                freq_weights=df_repo['total_homs_for_analysis']
            ).fit()
            
            # Likelihood ratio test
            lr_stat = reduced_model.deviance - full_model.deviance
            df_diff = reduced_model.df_resid - full_model.df_resid
            
            if df_diff > 0 and lr_stat >= 0:
                # Calculate p-value using chi-square distribution
                from scipy.stats import chi2
                p_value = 1 - chi2.cdf(lr_stat, df_diff)
                
                # Partial deviance explained
                partial_deviance_explained = lr_stat / null_deviance if null_deviance > 0 else 0
                
                lr_results.append({
                    'term': term,
                    'lr_chi2': lr_stat,
                    'df': df_diff,
                    'p_value': p_value,
                    'partial_deviance_explained': partial_deviance_explained
                })
            
        except Exception as e:
            print(f"Warning: LR test failed for term {term}: {e}")
            continue
    
    # Add model-level statistics
    if lr_results:
        mcfadden_r2 = 1 - (full_deviance / null_deviance) if null_deviance > 0 else 0
        lr_results.append({
            'term': 'MODEL_MCFADDEN_R2',
            'lr_chi2': mcfadden_r2,
            'df': np.nan,
            'p_value': np.nan,
            'partial_deviance_explained': np.nan
        })
    
    return pd.DataFrame(lr_results)

def extract_or_table(full_model: GLMResults) -> pd.DataFrame:
    """
    Extract odds ratios and confidence intervals from fitted GLM.
    
    Args:
        full_model: Fitted GLM results
        
    Returns:
        DataFrame with coefficients, odds ratios, and CIs
    """
    if full_model is None:
        return pd.DataFrame()
    
    try:
        # Get parameter estimates
        params = full_model.params
        bse = full_model.bse
        pvalues = full_model.pvalues
        
        # Calculate odds ratios and CIs
        odds_ratios = np.exp(params)
        or_ci_low = np.exp(params - 1.96 * bse)
        or_ci_high = np.exp(params + 1.96 * bse)
        
        # Create results table
        or_table = pd.DataFrame({
            'term': params.index,
            'beta': params.values,
            'SE': bse.values,
            'OR': odds_ratios.values,
            'OR_CI_low': or_ci_low.values,
            'OR_CI_high': or_ci_high.values,
            'p_value': pvalues.values
        })
        
        # Remove intercept for cleaner output
        or_table = or_table[or_table['term'] != 'Intercept']
        
        return or_table
        
    except Exception as e:
        print(f"Error extracting OR table: {e}")
        return pd.DataFrame()

def save_repo_results(repo: str, lr_df: pd.DataFrame, or_df: pd.DataFrame, 
                     outdir: Path) -> List[str]:
    """
    Save results for a single repository.
    
    Args:
        repo: Repository name
        lr_df: Likelihood ratio test results
        or_df: Odds ratio results
        outdir: Output directory
        
    Returns:
        List of generated file names
    """
    outdir.mkdir(parents=True, exist_ok=True)
    generated_files = []
    
    # Clean repository name for filename
    clean_repo = re.sub(r'[^\w\-_]', '_', repo)
    
    # Save LR test results
    if not lr_df.empty:
        lr_path = outdir / f"glm_lr_tests_{clean_repo}.csv"
        lr_df.to_csv(lr_path, index=False)
        generated_files.append(lr_path.name)
        print(f"Saved LR tests: {lr_path}")
        
        # Print significant results to console
        significant = lr_df[(lr_df['p_value'] < 0.05) & (lr_df['term'] != 'MODEL_MCFADDEN_R2')]
        if not significant.empty:
            print(f"\n=== Significant effects in {repo} (p < 0.05) ===")
            for _, row in significant.iterrows():
                print(f"  {row['term']}: Chi2 = {row['lr_chi2']:.3f}, df = {row['df']}, p = {row['p_value']:.4f}")
        else:
            print(f"\nNo significant effects found in {repo}")
        
        # Print McFadden's R²
        mcfadden = lr_df[lr_df['term'] == 'MODEL_MCFADDEN_R2']
        if not mcfadden.empty:
            print(f"McFadden's R² = {mcfadden['lr_chi2'].iloc[0]:.4f}")
    
    # Save OR results
    if not or_df.empty:
        or_path = outdir / f"glm_coef_or_{clean_repo}.csv"
        or_df.to_csv(or_path, index=False)
        generated_files.append(or_path.name)
        print(f"Saved OR table: {or_path}")
    
    return generated_files

def aggregate_results(source_dir: Path, aggregated_dir: Path, repositories: List[str]) -> List[str]:
    """
    Combine results from all repositories into summary files.
    
    Args:
        source_dir: Source directory containing individual repo results
        aggregated_dir: Directory where aggregated files will be saved
        repositories: List of repository names to aggregate
        
    Returns:
        List of generated aggregated file names
    """
    print("\n=== Aggregating Results ===")
    generated_files = []
    
    # Create aggregated directory
    aggregated_dir.mkdir(parents=True, exist_ok=True)
    
    # Aggregate LR test results - only for the specific repositories analyzed
    all_lr = []
    for repo in repositories:
        clean_repo = re.sub(r'[^\w\-_]', '_', repo)
        # Files are saved in {source_dir}/{repo}/anova/ subdirectory
        lr_file = source_dir / repo / 'anova' / f"glm_lr_tests_{clean_repo}.csv"
        if lr_file.exists():
            try:
                df = pd.read_csv(lr_file)
                df['repository'] = repo
                all_lr.append(df)
                print(f"  Found LR tests for {repo}: {lr_file}")
            except Exception as e:
                print(f"Warning: Failed to read {lr_file}: {e}")
        else:
            print(f"  LR test file not found: {lr_file}")
    
    if all_lr:
        print(f"Aggregating {len(all_lr)} LR test files...")
        combined_lr = pd.concat(all_lr, ignore_index=True)
        combined_lr_path = aggregated_dir / "glm_lr_tests_all_repos.csv"
        combined_lr.to_csv(combined_lr_path, index=False)
        generated_files.append(combined_lr_path.name)
        print(f"Saved combined LR tests: {combined_lr_path}")
    else:
        print("Warning: No LR test files found for aggregation")
    
    # Aggregate OR results - only for the specific repositories analyzed
    all_or = []
    for repo in repositories:
        clean_repo = re.sub(r'[^\w\-_]', '_', repo)
        # Files are saved in {source_dir}/{repo}/anova/ subdirectory
        or_file = source_dir / repo / 'anova' / f"glm_coef_or_{clean_repo}.csv"
        if or_file.exists():
            try:
                df = pd.read_csv(or_file)
                df['repository'] = repo
                all_or.append(df)
                print(f"  Found OR results for {repo}: {or_file}")
            except Exception as e:
                print(f"Warning: Failed to read {or_file}: {e}")
        else:
            print(f"  OR file not found: {or_file}")
    
    if all_or:
        print(f"Aggregating {len(all_or)} OR files...")
        combined_or = pd.concat(all_or, ignore_index=True)
        combined_or_path = aggregated_dir / "glm_coef_or_all_repos.csv"
        combined_or.to_csv(combined_or_path, index=False)
        generated_files.append(combined_or_path.name)
        print(f"Saved combined OR table: {combined_or_path}")
    else:
        print("Warning: No OR files found for aggregation")
    
    return generated_files

def analyze_repository(repo: str, df_repo: pd.DataFrame, outdir: Path) -> List[str]:
    """
    Perform complete GLM analysis for a single repository.
    
    Args:
        repo: Repository name
        df_repo: Data for the repository
        outdir: Output directory
        
    Returns:
        List of generated file names
    """
    print(f"\n{'='*60}")
    print(f"ANALYZING REPOSITORY: {repo}")
    print(f"{'='*60}")
    
    print(f"Observations: {len(df_repo)}")
    print(f"Algorithms: {df_repo['algorithm'].value_counts().to_dict()}")
    print(f"Mean success rate: {df_repo['success_rate_pct'].mean():.1f}%")
    
    # Use total_homs_for_analysis if available, otherwise fall back to total_homs
    if 'total_homs_for_analysis' in df_repo.columns:
        print(f"Mean trials per observation (with filtered): {df_repo['total_homs_for_analysis'].mean():.1f}")
    elif 'total_homs' in df_repo.columns:
        print(f"Mean trials per observation: {df_repo['total_homs'].mean():.1f}")
    
    # Check data quality
    if len(df_repo) < 10:
        print(f"WARNING: Only {len(df_repo)} observations, results may be unreliable")
    
    if df_repo['algorithm'].nunique() < 2:
        print(f"WARNING: Only one algorithm found, skipping repository")
        return []
    
    # Check for sufficient variation in heuristics
    heuristic_variation = []
    for h in HEURISTICS:
        col = f'H_{h}'
        if col in df_repo.columns:
            unique_vals = df_repo[col].nunique()
            heuristic_variation.append(unique_vals)
    
    if max(heuristic_variation) < 2:
        print("WARNING: No variation in heuristics, skipping repository")
        return []
    
    # Perform LR tests
    print("Performing likelihood ratio tests...")
    lr_df = lr_tests_by_term(df_repo)
    
    # Extract odds ratios
    print("Extracting odds ratios...")
    full_model = fit_glm_binomial(df_repo)
    or_df = extract_or_table(full_model)
    
    # Save results and return generated file names
    return save_repo_results(repo, lr_df, or_df, outdir)

def main():
    """Main analysis function."""
    parser = argparse.ArgumentParser(
        description="GLM-based ANOVA-style analysis of SSHOM success rates",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Example usage:
    python glm_binomial_anova.py --solution MoreLinq
    python glm_binomial_anova.py --solution ABCRepo XYZRepo
    python glm_binomial_anova.py --solution MoreLinq ABCRepo XYZRepo
    
    # When repo name differs from solution name:
    python glm_binomial_anova.py --solution Marsen.NetCore.Dojo.Integration.Test --repo Marsen.NetCore.Dojo
    python glm_binomial_anova.py --solution SolutionA SolutionB --repo RepoX RepoY
    
For single solution:
    data_dir = "C:\\Users\\MerlijnU\\outputs\\{repo}\\{solution}"
    out_dir = "C:\\Users\\MerlijnU\\analysis\\{solution}\\anova"
    
For multiple solutions:
    individual_files_dir = "C:\\Users\\MerlijnU\\analysis\\multi_solution_analysis"
    aggregated_files_dir = "C:\\Users\\MerlijnU\\analysis\\Anova"
        """
    )
    parser.add_argument(
        "--solution", 
        type=str, 
        nargs='+',
        default=["MoreLinq"],
        help="Solution name(s) (default: MoreLinq). Can specify multiple solutions. Automatically constructs data_dir and out_dir paths for each."
    )
    
    parser.add_argument(
        "--repo",
        type=str,
        nargs='+',
        default=None,
        help="Repository name(s) corresponding to each solution. If not provided, uses solution name(s). Must match number of solutions if provided."
    )
    
    args = parser.parse_args()
    
    # Handle multiple solutions
    solutions = args.solution if isinstance(args.solution, list) else [args.solution]
    
    # Handle repo parameter - if not provided, use solution names
    if args.repo is None:
        repos = solutions
    else:
        repos = args.repo if isinstance(args.repo, list) else [args.repo]
        # Validate that number of repos matches number of solutions
        if len(repos) != len(solutions):
            print(f"ERROR: Number of --repo arguments ({len(repos)}) must match number of --solution arguments ({len(solutions)})")
            return
    
    print("GLM-based ANOVA Analysis of SSHOM Success Rates")
    print("=" * 60)
    print(f"Solutions to analyze: {solutions}")
    if args.repo is not None:
        print(f"Repository names: {repos}")
    
    # Determine output directories
    if len(solutions) == 1:
        # Single solution: use solution-specific directory, no aggregation
        aggregated_out_dir = None
        print(f"Output directory: Solution-specific directories")
    else:
        # Multiple solutions: individual files in solution directories, aggregated in Anova
        aggregated_out_dir = Path("C:\\Users\\MerlijnU\\analysis\\Anova")
        print(f"Output directories: Solution-specific directories for individual files")
        print(f"Aggregated files directory: {aggregated_out_dir}")
    
    try:
        # Collect all data from all solutions
        all_prepared_data = []
        
        for idx, solution in enumerate(solutions):
            repo = repos[idx]  # Get corresponding repo name
            
            print(f"\n{'='*40}")
            print(f"PROCESSING SOLUTION: {solution}")
            if repo != solution:
                print(f"Repository: {repo}")
            print(f"{'='*40}")
            
            # Construct data directory using repo name
            # Path structure: C:\Users\MerlijnU\outputs\{repo}\{solution}
            data_dir = Path(f"C:\\Users\\MerlijnU\\outputs\\{repo}\\{solution}")
            print(f"Data directory: {data_dir}")
            
            # Load and prepare data for this solution
            raw_data = load_data(data_dir)
            prepared_data = prepare_glm_data(raw_data, data_dir)
            
            if prepared_data.empty:
                print(f"WARNING: No valid data for solution {solution}")
                continue
            
            # Add solution name to the data
            prepared_data['solution'] = solution
            all_prepared_data.append(prepared_data)
        
        if not all_prepared_data:
            print("ERROR: No valid data found for any solution")
            return
        
        # Combine all solution data
        combined_data = pd.concat(all_prepared_data, ignore_index=True)
        
        print(f"\n{'='*60}")
        print("COMBINED ANALYSIS")
        print(f"{'='*60}")
        print(f"Total observations across all solutions: {len(combined_data)}")
        
        # Group by repository/solution
        if 'solution' not in combined_data.columns:
            print("Warning: No 'solution' column found, treating all data as one repository")
            combined_data['solution'] = 'unknown_repository'
        
        repositories = combined_data['solution'].unique()
        print(f"Found {len(repositories)} repositories: {list(repositories)}")
        
        # Analyze each repository and track generated files
        all_generated_files = []
        individual_output_dirs = []
        
        for repo in repositories:
            df_repo = combined_data[combined_data['solution'] == repo].copy()
            # Create solution-specific output directory
            repo_out_dir = Path(f"C:\\Users\\MerlijnU\\analysis\\{repo}\\anova")
            individual_output_dirs.append(repo_out_dir)
            
            repo_files = analyze_repository(repo, df_repo, repo_out_dir)
            all_generated_files.extend(repo_files)
        
        # Aggregate results (only if multiple solutions)
        if len(solutions) > 1:
            # Create aggregated output directory
            aggregated_out_dir = Path("C:\\Users\\MerlijnU\\analysis\\Anova")
            # Pass the base analysis directory that contains all {repo}/anova/ subdirectories
            source_dir = Path("C:\\Users\\MerlijnU\\analysis")
            aggregated_file_names = aggregate_results(source_dir, aggregated_out_dir, list(repositories))
            all_generated_files.extend(aggregated_file_names)
        else:
            print(f"\nSingle solution analysis complete. Individual files generated for {solutions[0]}.")
        
        print(f"\n{'='*60}")
        print("ANALYSIS COMPLETE!")
        print(f"{'='*60}")
        if len(solutions) == 1:
            # For single solution, get the output directory used
            single_out_dir = Path(f"C:\\Users\\MerlijnU\\analysis\\{solutions[0]}\\anova")
            print(f"Results saved to: {single_out_dir}")
        else:
            print("Individual solution results saved to:")
            for i, repo in enumerate(repositories):
                print(f"  - {repo}: {individual_output_dirs[i]}")
            print(f"Aggregated results saved to: C:\\Users\\MerlijnU\\analysis\\Anova")
        print("\nGenerated files:")
        
        # List only the files generated in this run
        for file in sorted(all_generated_files):
            print(f"  - {file}")
        
        print(f"\nTotal files generated: {len(all_generated_files)}")
        
    except Exception as e:
        print(f"ERROR during analysis: {e}")
        import traceback
        traceback.print_exc()
        raise

if __name__ == "__main__":
    main()