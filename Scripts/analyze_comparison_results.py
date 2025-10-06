#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""
Analyze HOMT Comparison Experiment Results

This script aggregates and analyzes the outputs from run-homt-comparison.ps1
to compare different HOMT configurations across multiple repositories.

Expected input structure (per repository):
    C:\Users\MerlijnU\outputs\<RepoName>\comparison\
    ├── baseline-fom-only\
    │   ├── run01\reports\mutation-report.csv
    │   ├── run02\reports\mutation-report.csv
    │   └── ... (run01-run05)
    ├── accelerate-with-heuristics-reduced\
    │   ├── run01\reports\mutation-report-hom.csv
    │   └── ... (run01-run05)
    └── validate-with-heuristics\
        ├── run01\reports\mutation-report-hom.csv
        └── ... (run01-run05)

Output files:
    1. all_runs_combined.csv - Raw aggregated data
    2. baseline_comparison.csv - Baseline vs HOMT performance
    3. accelerate_summary.csv - Accelerate mode statistics
    4. validate_summary.csv - SSHOM quality metrics
    5. algorithm_performance_summary.csv - Per-algorithm comparison
    6. Multiple PNG visualizations
    7. EXECUTIVE_SUMMARY.txt - Narrative findings

Usage:
    python analyze_comparison_results.py --out "C:\Users\MerlijnU\analysis\comparison"
"""

import argparse
import re
from pathlib import Path
from typing import Dict, List, Tuple
import pandas as pd
import numpy as np
from scipy import stats
import matplotlib.pyplot as plt
import seaborn as sns

# Repository configurations (matching run-homt-comparison.ps1)
REPOSITORIES = [
    {
        'name': 'CsvHelper',
        'output_dir': 'C:\\Users\\MerlijnU\\outputs\\CsvHelper\\comparison'
    },
    {
        'name': 'GuardClauses',
        'output_dir': 'C:\\Users\\MerlijnU\\outputs\\GuardClauses\\comparison'
    },
    {
        'name': 'Marsen.NetCore.Dojo',
        'output_dir': 'C:\\Users\\MerlijnU\\outputs\\Marsen.NetCore.Dojo\\comparison'
    },
    {
        'name': 'TimeProviderExtensions',
        'output_dir': 'C:\\Users\\MerlijnU\\outputs\\TimeProviderExtensions\\comparison'
    },
    {
        'name': 'MoreLINQ',
        'output_dir': 'C:\\Users\\MerlijnU\\outputs\\MoreLINQ\\comparison'
    }
]

CONFIGURATIONS = [
    'baseline-fom-only',
    'accelerate-with-heuristics-reduced',
    'validate-with-heuristics'
]
RUN_COUNT = 5


def parse_per_algorithm_stats(stats_str: str) -> Dict[str, Dict[str, float]]:
    """Parse the per_algorithm_stats field into a structured dictionary.
    
    Format: "GeneticSearch:raw=227;kept=75;...;median=1.0000|LocalSearchV2:raw=227;..."
    """
    if pd.isna(stats_str) or not stats_str:
        return {}
    
    result = {}
    algorithms = stats_str.split('|')
    
    for alg_str in algorithms:
        if ':' not in alg_str:
            continue
            
        alg_name, stats_part = alg_str.split(':', 1)
        alg_name = alg_name.strip()
        
        stats_dict = {}
        for pair in stats_part.split(';'):
            if '=' in pair:
                key, value = pair.split('=', 1)
                try:
                    stats_dict[key.strip()] = float(value.strip())
                except ValueError:
                    stats_dict[key.strip()] = value.strip()
        
        result[alg_name] = stats_dict
    
    return result


def bootstrap_ci(data: np.ndarray, n_boot: int = 10000, ci: float = 0.95) -> Tuple[float, float, float]:
    """Calculate mean and bootstrap confidence interval."""
    if len(data) == 0:
        return np.nan, np.nan, np.nan
    
    mean_val = np.mean(data)
    
    if len(data) == 1:
        return mean_val, mean_val, mean_val
    
    boot_means = []
    for _ in range(n_boot):
        sample = np.random.choice(data, size=len(data), replace=True)
        boot_means.append(np.mean(sample))
    
    boot_means = np.array(boot_means)
    alpha = 1 - ci
    ci_lo = np.percentile(boot_means, alpha/2 * 100)
    ci_hi = np.percentile(boot_means, (1 - alpha/2) * 100)
    
    return mean_val, ci_lo, ci_hi


def load_run_data(repo_name: str, output_dir: str, config: str, run_num: int) -> pd.Series:
    """Load a single run's mutation-report-hom.csv data."""
    csv_path = Path(output_dir) / config / f"run{run_num:02d}" / "reports" / "mutation-report-hom.csv"
    
    if not csv_path.exists():
        print(f"  WARNING: Missing {csv_path}")
        return None
    
    try:
        df = pd.read_csv(csv_path, encoding='utf-8')
        if len(df) == 0:
            print(f"  WARNING: Empty CSV {csv_path}")
            return None
        
        # Return the first (and typically only) row
        return df.iloc[0]
    except Exception as e:
        print(f"  ERROR reading {csv_path}: {e}")
        return None


def collect_all_data() -> pd.DataFrame:
    """Collect all experiment data into a single DataFrame."""
    all_rows = []
    
    for repo in REPOSITORIES:
        repo_name = repo['name']
        output_dir = repo['output_dir']
        
        print(f"\nCollecting data for {repo_name}...")
        
        for config in CONFIGURATIONS:
            for run_num in range(1, RUN_COUNT + 1):
                data = load_run_data(repo_name, output_dir, config, run_num)
                
                if data is None:
                    continue
                
                # Add metadata
                row = data.to_dict()
                row['repo_name'] = repo_name
                row['config'] = config
                row['run_number'] = run_num
                
                # Calculate corrected SSHOM rate
                total_homs = row.get('total_homs', 0)
                filtered_empty = row.get('filtered_empty_total', 0)
                filtered_invalid = row.get('filtered_invalid_total', 0)
                sshom_2 = row.get('sshom_2', 0)
                sshom_3 = row.get('sshom_3', 0)
                sshom_4 = row.get('sshom_4', 0)
                
                total_sshoms = sshom_2 + sshom_3 + sshom_4
                denominator = total_homs + filtered_empty + filtered_invalid
                
                if denominator > 0:
                    row['sshom_rate_corrected'] = 100.0 * total_sshoms / denominator
                else:
                    row['sshom_rate_corrected'] = 0.0
                
                # Parse per_algorithm_stats
                row['parsed_algorithm_stats'] = parse_per_algorithm_stats(row.get('per_algorithm_stats', ''))
                
                all_rows.append(row)
    
    df = pd.DataFrame(all_rows)
    print(f"\nCollected {len(df)} runs total")
    return df


def analyze_accelerate_mode(df: pd.DataFrame, out_dir: Path):
    """Analyze accelerate mode: runtime and test group efficiency."""
    print("\n=== ACCELERATE MODE ANALYSIS ===")
    
    accel_df = df[df['config'] == 'accelerate-with-heuristics-reduced'].copy()
    
    if len(accel_df) == 0:
        print("No accelerate-reduced data found!")
        return
    
    results = []
    
    for repo_name in accel_df['repo_name'].unique():
        repo_data = accel_df[accel_df['repo_name'] == repo_name]
        
        # Runtime statistics (convert ms to seconds)
        runtimes = repo_data['total_test_duration_ms'].values / 1000.0
        test_runs = repo_data['test_runs_count'].values
        hom_gen_times = repo_data['hom_generation_ms'].values / 1000.0
        
        # Total HOMs
        total_homs = repo_data['total_homs'].values
        
        # Mutation score
        mut_scores = repo_data['mutation_score_percent'].values
        
        # Bootstrap CIs
        runtime_mean, runtime_lo, runtime_hi = bootstrap_ci(runtimes)
        test_runs_mean, test_runs_lo, test_runs_hi = bootstrap_ci(test_runs)
        hom_gen_mean, hom_gen_lo, hom_gen_hi = bootstrap_ci(hom_gen_times)
        total_homs_mean, homs_lo, homs_hi = bootstrap_ci(total_homs)
        mut_score_mean, mut_lo, mut_hi = bootstrap_ci(mut_scores)
        
        results.append({
            'repo_name': repo_name,
            'n_runs': len(repo_data),
            'avg_runtime_sec': runtime_mean,
            'runtime_ci_lo': runtime_lo,
            'runtime_ci_hi': runtime_hi,
            'runtime_sd': np.std(runtimes, ddof=1),
            'avg_test_runs': test_runs_mean,
            'test_runs_ci_lo': test_runs_lo,
            'test_runs_ci_hi': test_runs_hi,
            'test_runs_sd': np.std(test_runs, ddof=1),
            'avg_hom_gen_sec': hom_gen_mean,
            'hom_gen_ci_lo': hom_gen_lo,
            'hom_gen_ci_hi': hom_gen_hi,
            'avg_total_homs': total_homs_mean,
            'total_homs_ci_lo': homs_lo,
            'total_homs_ci_hi': homs_hi,
            'avg_mutation_score': mut_score_mean,
            'mut_score_ci_lo': mut_lo,
            'mut_score_ci_hi': mut_hi,
            'efficiency_ratio': runtime_mean / test_runs_mean if test_runs_mean > 0 else np.nan
        })
    
    results_df = pd.DataFrame(results)
    output_path = out_dir / "accelerate_summary.csv"
    results_df.to_csv(output_path, index=False, encoding='utf-8')
    print(f"\nSaved: {output_path}")
    print(results_df.to_string(index=False))
    
    return results_df


def analyze_validate_mode(df: pd.DataFrame, out_dir: Path):
    """Analyze validate mode: runtime, SSHOM rates, and prediction accuracy."""
    print("\n=== VALIDATE MODE ANALYSIS ===")
    
    validate_df = df[df['config'] == 'validate-with-heuristics'].copy()
    
    if len(validate_df) == 0:
        print("No validate data found!")
        return
    
    results = []
    
    for repo_name in validate_df['repo_name'].unique():
        repo_data = validate_df[validate_df['repo_name'] == repo_name]
        
        # Runtime statistics (convert ms to seconds)
        runtimes = repo_data['total_test_duration_ms'].values / 1000.0
        
        # SSHOM rates
        sshom_rates_orig = repo_data['sshom_rate_percent'].values
        sshom_rates_corr = repo_data['sshom_rate_corrected'].values
        
        # Total HOMs and SSHOMs
        total_homs = repo_data['total_homs'].values
        total_sshoms = (repo_data['sshom_2'] + repo_data['sshom_3'] + repo_data['sshom_4']).values
        
        # Mutation score
        mut_scores = repo_data['mutation_score_percent'].values
        
        # Prediction scores
        avg_pred_scores = repo_data['avg_perdicted_score_all_kept'].values
        median_pred_scores = repo_data['median_predicted_score_all_kept'].values
        
        # Extract dup_across per algorithm
        dup_across_per_alg = {}
        for _, row in repo_data.iterrows():
            parsed_stats = row['parsed_algorithm_stats']
            for alg_name, stats in parsed_stats.items():
                if alg_name not in dup_across_per_alg:
                    dup_across_per_alg[alg_name] = []
                dup_across_per_alg[alg_name].append(stats.get('dupAcross', 0))
        
        # Bootstrap CIs
        runtime_mean, runtime_lo, runtime_hi = bootstrap_ci(runtimes)
        sshom_orig_mean, sshom_orig_lo, sshom_orig_hi = bootstrap_ci(sshom_rates_orig)
        sshom_corr_mean, sshom_corr_lo, sshom_corr_hi = bootstrap_ci(sshom_rates_corr)
        total_homs_mean, homs_lo, homs_hi = bootstrap_ci(total_homs)
        total_sshoms_mean, sshoms_lo, sshoms_hi = bootstrap_ci(total_sshoms)
        mut_score_mean, mut_lo, mut_hi = bootstrap_ci(mut_scores)
        avg_pred_mean, avg_pred_lo, avg_pred_hi = bootstrap_ci(avg_pred_scores)
        
        # Build result dict
        result = {
            'repo_name': repo_name,
            'n_runs': len(repo_data),
            'avg_runtime_sec': runtime_mean,
            'runtime_ci_lo': runtime_lo,
            'runtime_ci_hi': runtime_hi,
            'runtime_sd': np.std(runtimes, ddof=1),
            'avg_sshom_rate_orig': sshom_orig_mean,
            'sshom_orig_ci_lo': sshom_orig_lo,
            'sshom_orig_ci_hi': sshom_orig_hi,
            'avg_sshom_rate_corrected': sshom_corr_mean,
            'sshom_corr_ci_lo': sshom_corr_lo,
            'sshom_corr_ci_hi': sshom_corr_hi,
            'avg_total_homs': total_homs_mean,
            'total_homs_ci_lo': homs_lo,
            'total_homs_ci_hi': homs_hi,
            'avg_total_sshoms': total_sshoms_mean,
            'total_sshoms_ci_lo': sshoms_lo,
            'total_sshoms_ci_hi': sshoms_hi,
            'avg_mutation_score': mut_score_mean,
            'mut_score_ci_lo': mut_lo,
            'mut_score_ci_hi': mut_hi,
            'avg_prediction_score': avg_pred_mean,
            'pred_score_ci_lo': avg_pred_lo,
            'pred_score_ci_hi': avg_pred_hi
        }
        
        # Add dup_across per algorithm
        for alg_name, dup_values in dup_across_per_alg.items():
            dup_array = np.array(dup_values)
            dup_mean, dup_lo, dup_hi = bootstrap_ci(dup_array)
            result[f'avg_dup_across_{alg_name}'] = dup_mean
            result[f'dup_across_ci_lo_{alg_name}'] = dup_lo
            result[f'dup_across_ci_hi_{alg_name}'] = dup_hi
        
        results.append(result)
    
    results_df = pd.DataFrame(results)
    output_path = out_dir / "validate_summary.csv"
    results_df.to_csv(output_path, index=False, encoding='utf-8')
    print(f"\nSaved: {output_path}")
    print(results_df.to_string(index=False))
    
    return results_df


def analyze_baseline_comparison(df: pd.DataFrame, out_dir: Path):
    """Compare baseline FOM-only vs accelerate-reduced mode for thesis."""
    print("\n=== BASELINE COMPARISON ANALYSIS ===")
    
    # Filter for relevant configurations
    baseline_df = df[df['config'] == 'baseline-fom-only'].copy()
    accel_red_df = df[df['config'] == 'accelerate-with-heuristics-reduced'].copy()
    
    if len(baseline_df) == 0:
        print("WARNING: No baseline data found!")
    if len(accel_red_df) == 0:
        print("WARNING: No accelerate-reduced data found!")
    
    results = []
    
    for repo_name in df['repo_name'].unique():
        baseline_repo = baseline_df[baseline_df['repo_name'] == repo_name]
        accel_red_repo = accel_red_df[accel_red_df['repo_name'] == repo_name]
        
        result = {'repo_name': repo_name}
        
        # Baseline statistics
        if len(baseline_repo) > 0:
            baseline_runtimes = baseline_repo['total_test_duration_ms'].values / 1000.0
            baseline_test_runs = baseline_repo.get('test_runs_count', pd.Series([np.nan] * len(baseline_repo))).values
            baseline_mutants = baseline_repo.get('total_mutants', pd.Series([np.nan] * len(baseline_repo))).values
            baseline_mut_score = baseline_repo.get('mutation_score_percent', pd.Series([np.nan] * len(baseline_repo))).values
            
            runtime_mean, runtime_lo, runtime_hi = bootstrap_ci(baseline_runtimes)
            test_runs_mean, tr_lo, tr_hi = bootstrap_ci(baseline_test_runs)
            mutants_mean, mut_lo, mut_hi = bootstrap_ci(baseline_mutants)
            mut_score_mean, ms_lo, ms_hi = bootstrap_ci(baseline_mut_score)
            
            result['baseline_n_runs'] = len(baseline_repo)
            result['baseline_runtime_sec'] = runtime_mean
            result['baseline_runtime_ci_lo'] = runtime_lo
            result['baseline_runtime_ci_hi'] = runtime_hi
            result['baseline_runtime_sd'] = np.std(baseline_runtimes, ddof=1)
            result['baseline_test_runs'] = test_runs_mean
            result['baseline_test_runs_ci_lo'] = tr_lo
            result['baseline_test_runs_ci_hi'] = tr_hi
            result['baseline_total_mutants'] = mutants_mean
            result['baseline_mutants_ci_lo'] = mut_lo
            result['baseline_mutants_ci_hi'] = mut_hi
            result['baseline_mutation_score'] = mut_score_mean
            result['baseline_mut_score_ci_lo'] = ms_lo
            result['baseline_mut_score_ci_hi'] = ms_hi
        
        # Accelerate-reduced statistics
        if len(accel_red_repo) > 0:
            accel_red_runtimes = accel_red_repo['total_test_duration_ms'].values / 1000.0
            accel_red_test_runs = accel_red_repo['test_runs_count'].values
            accel_red_homs = accel_red_repo['total_homs'].values
            accel_red_mut_score = accel_red_repo['mutation_score_percent'].values
            
            runtime_mean, runtime_lo, runtime_hi = bootstrap_ci(accel_red_runtimes)
            test_runs_mean, tr_lo, tr_hi = bootstrap_ci(accel_red_test_runs)
            homs_mean, homs_lo, homs_hi = bootstrap_ci(accel_red_homs)
            mut_score_mean, ms_lo, ms_hi = bootstrap_ci(accel_red_mut_score)
            
            result['accel_red_n_runs'] = len(accel_red_repo)
            result['accel_red_runtime_sec'] = runtime_mean
            result['accel_red_runtime_ci_lo'] = runtime_lo
            result['accel_red_runtime_ci_hi'] = runtime_hi
            result['accel_red_runtime_sd'] = np.std(accel_red_runtimes, ddof=1)
            result['accel_red_test_runs'] = test_runs_mean
            result['accel_red_test_runs_ci_lo'] = tr_lo
            result['accel_red_test_runs_ci_hi'] = tr_hi
            result['accel_red_total_homs'] = homs_mean
            result['accel_red_homs_ci_lo'] = homs_lo
            result['accel_red_homs_ci_hi'] = homs_hi
            result['accel_red_mutation_score'] = mut_score_mean
            result['accel_red_mut_score_ci_lo'] = ms_lo
            result['accel_red_mut_score_ci_hi'] = ms_hi
        
        # Calculate speedup ratio (baseline vs accelerate-reduced only)
        if 'baseline_runtime_sec' in result and 'accel_red_runtime_sec' in result:
            if result['accel_red_runtime_sec'] > 0:
                result['speedup_vs_baseline'] = result['baseline_runtime_sec'] / result['accel_red_runtime_sec']
        
        # Calculate test run reduction percentage
        if 'baseline_test_runs' in result and 'accel_red_test_runs' in result:
            if result['baseline_test_runs'] > 0:
                result['test_reduction_percent'] = 100 * (1 - result['accel_red_test_runs'] / result['baseline_test_runs'])
        
        results.append(result)
    
    results_df = pd.DataFrame(results)
    output_path = out_dir / "baseline_comparison.csv"
    results_df.to_csv(output_path, index=False, encoding='utf-8')
    print(f"\nSaved: {output_path}")
    
    # Print key comparisons
    print("\nKey Metrics Summary:")
    print("=" * 100)
    for _, row in results_df.iterrows():
        print(f"\n{row['repo_name']}:")
        if 'baseline_runtime_sec' in row and not pd.isna(row['baseline_runtime_sec']):
            print(f"  Baseline runtime:        {row['baseline_runtime_sec']:8.1f}s")
        if 'accel_red_runtime_sec' in row and not pd.isna(row['accel_red_runtime_sec']):
            print(f"  Accelerate-reduced:      {row['accel_red_runtime_sec']:8.1f}s")
        if 'speedup_vs_baseline' in row and not pd.isna(row['speedup_vs_baseline']):
            print(f"  Speedup vs baseline:     {row['speedup_vs_baseline']:.2f}x")
        if 'test_reduction_percent' in row and not pd.isna(row['test_reduction_percent']):
            print(f"  Test reduction:          {row['test_reduction_percent']:.1f}%")
    
    return results_df


def analyze_algorithm_performance(df: pd.DataFrame, out_dir: Path):
    """Analyze per-algorithm prediction scores (highest, lowest, avg, median)."""
    print("\n=== ALGORITHM PERFORMANCE ANALYSIS ===")
    
    all_alg_stats = []
    
    for _, row in df.iterrows():
        parsed_stats = row['parsed_algorithm_stats']
        
        for alg_name, stats in parsed_stats.items():
            all_alg_stats.append({
                'repo_name': row['repo_name'],
                'config': row['config'],
                'run_number': row['run_number'],
                'algorithm': alg_name,
                'raw_candidates': stats.get('raw', np.nan),
                'kept_homs': stats.get('kept', np.nan),
                'dup_within': stats.get('dupWithin', np.nan),
                'dup_across': stats.get('dupAcross', np.nan),
                'empty': stats.get('empty', np.nan),
                'invalid': stats.get('invalid', np.nan),
                'highest_score': stats.get('highest', np.nan),
                'lowest_score': stats.get('lowest', np.nan),
                'avg_score': stats.get('avg', np.nan),
                'median_score': stats.get('median', np.nan)
            })
    
    alg_df = pd.DataFrame(all_alg_stats)
    
    if len(alg_df) == 0:
        print("No algorithm statistics found!")
        return
    
    # Save detailed per-run algorithm stats
    detail_path = out_dir / "algorithm_stats_detailed.csv"
    alg_df.to_csv(detail_path, index=False, encoding='utf-8')
    print(f"\nSaved detailed stats: {detail_path}")
    
    # Aggregate by repo, config, and algorithm
    summary_rows = []
    
    for config in CONFIGURATIONS:
        config_data = alg_df[alg_df['config'] == config]
        
        for repo_name in config_data['repo_name'].unique():
            repo_data = config_data[config_data['repo_name'] == repo_name]
            
            for alg_name in repo_data['algorithm'].unique():
                alg_data = repo_data[repo_data['algorithm'] == alg_name]
                
                # Bootstrap CIs for key metrics
                kept_homs = alg_data['kept_homs'].values
                highest_scores = alg_data['highest_score'].values
                lowest_scores = alg_data['lowest_score'].values
                avg_scores = alg_data['avg_score'].values
                median_scores = alg_data['median_score'].values
                
                kept_mean, kept_lo, kept_hi = bootstrap_ci(kept_homs)
                highest_mean, high_lo, high_hi = bootstrap_ci(highest_scores)
                lowest_mean, low_lo, low_hi = bootstrap_ci(lowest_scores)
                avg_mean, avg_lo, avg_hi = bootstrap_ci(avg_scores)
                median_mean, med_lo, med_hi = bootstrap_ci(median_scores)
                
                summary_rows.append({
                    'repo_name': repo_name,
                    'config': config,
                    'algorithm': alg_name,
                    'n_runs': len(alg_data),
                    'avg_kept_homs': kept_mean,
                    'kept_ci_lo': kept_lo,
                    'kept_ci_hi': kept_hi,
                    'avg_highest_score': highest_mean,
                    'highest_ci_lo': high_lo,
                    'highest_ci_hi': high_hi,
                    'avg_lowest_score': lowest_mean,
                    'lowest_ci_lo': low_lo,
                    'lowest_ci_hi': low_hi,
                    'avg_avg_score': avg_mean,
                    'avg_ci_lo': avg_lo,
                    'avg_ci_hi': avg_hi,
                    'avg_median_score': median_mean,
                    'median_ci_lo': med_lo,
                    'median_ci_hi': med_hi
                })
    
    summary_df = pd.DataFrame(summary_rows)
    summary_path = out_dir / "algorithm_performance_summary.csv"
    summary_df.to_csv(summary_path, index=False, encoding='utf-8')
    print(f"\nSaved summary: {summary_path}")
    print(summary_df.to_string(index=False))
    
    return summary_df


def create_comparison_visualizations(df: pd.DataFrame, accel_summary: pd.DataFrame, 
                                     validate_summary: pd.DataFrame, out_dir: Path):
    """Create visualizations comparing accelerate and validate modes."""
    print("\n=== CREATING VISUALIZATIONS ===")
    
    sns.set_style("whitegrid")
    
    # 1. Runtime comparison
    fig, ax = plt.subplots(figsize=(12, 6))
    
    repos = sorted(df['repo_name'].unique())
    x = np.arange(len(repos))
    width = 0.35
    
    accel_runtimes = []
    accel_errors = []
    validate_runtimes = []
    validate_errors = []
    
    for repo in repos:
        accel_row = accel_summary[accel_summary['repo_name'] == repo]
        validate_row = validate_summary[validate_summary['repo_name'] == repo]
        
        if len(accel_row) > 0:
            mean = accel_row['avg_runtime_sec'].iloc[0]
            lo = accel_row['runtime_ci_lo'].iloc[0]
            hi = accel_row['runtime_ci_hi'].iloc[0]
            accel_runtimes.append(mean)
            accel_errors.append([mean - lo, hi - mean])
        else:
            accel_runtimes.append(0)
            accel_errors.append([0, 0])
        
        if len(validate_row) > 0:
            mean = validate_row['avg_runtime_sec'].iloc[0]
            lo = validate_row['runtime_ci_lo'].iloc[0]
            hi = validate_row['runtime_ci_hi'].iloc[0]
            validate_runtimes.append(mean)
            validate_errors.append([mean - lo, hi - mean])
        else:
            validate_runtimes.append(0)
            validate_errors.append([0, 0])
    
    accel_errors = np.array(accel_errors).T
    validate_errors = np.array(validate_errors).T
    
    ax.bar(x - width/2, accel_runtimes, width, label='Accelerate', 
           yerr=accel_errors, capsize=5, alpha=0.8, color='#2ecc71')
    ax.bar(x + width/2, validate_runtimes, width, label='Validate', 
           yerr=validate_errors, capsize=5, alpha=0.8, color='#3498db')
    
    ax.set_xlabel('Repository', fontsize=12, fontweight='bold')
    ax.set_ylabel('Average Runtime (seconds)', fontsize=12, fontweight='bold')
    ax.set_title('Runtime Comparison: Accelerate vs Validate Mode', fontsize=14, fontweight='bold')
    ax.set_xticks(x)
    ax.set_xticklabels(repos, rotation=45, ha='right')
    ax.legend()
    ax.grid(axis='y', alpha=0.3)
    
    plt.tight_layout()
    plot_path = out_dir / "runtime_comparison.png"
    plt.savefig(plot_path, dpi=300, bbox_inches='tight')
    print(f"Saved: {plot_path}")
    plt.close()
    
    # 2. SSHOM Rate (Validate mode only)
    fig, ax = plt.subplots(figsize=(12, 6))
    
    sshom_orig = []
    sshom_corr = []
    sshom_orig_err = []
    sshom_corr_err = []
    
    for repo in repos:
        validate_row = validate_summary[validate_summary['repo_name'] == repo]
        
        if len(validate_row) > 0:
            orig_mean = validate_row['avg_sshom_rate_orig'].iloc[0]
            orig_lo = validate_row['sshom_orig_ci_lo'].iloc[0]
            orig_hi = validate_row['sshom_orig_ci_hi'].iloc[0]
            sshom_orig.append(orig_mean)
            sshom_orig_err.append([orig_mean - orig_lo, orig_hi - orig_mean])
            
            corr_mean = validate_row['avg_sshom_rate_corrected'].iloc[0]
            corr_lo = validate_row['sshom_corr_ci_lo'].iloc[0]
            corr_hi = validate_row['sshom_corr_ci_hi'].iloc[0]
            sshom_corr.append(corr_mean)
            sshom_corr_err.append([corr_mean - corr_lo, corr_hi - corr_mean])
        else:
            sshom_orig.append(0)
            sshom_orig_err.append([0, 0])
            sshom_corr.append(0)
            sshom_corr_err.append([0, 0])
    
    sshom_orig_err = np.array(sshom_orig_err).T
    sshom_corr_err = np.array(sshom_corr_err).T
    
    ax.bar(x - width/2, sshom_orig, width, label='Original (kept HOMs only)', 
           yerr=sshom_orig_err, capsize=5, alpha=0.8, color='#e74c3c')
    ax.bar(x + width/2, sshom_corr, width, label='Corrected (with filtered HOMs)', 
           yerr=sshom_corr_err, capsize=5, alpha=0.8, color='#9b59b6')
    
    ax.set_xlabel('Repository', fontsize=12, fontweight='bold')
    ax.set_ylabel('SSHOM Rate (%)', fontsize=12, fontweight='bold')
    ax.set_title('SSHOM Success Rate: Original vs Corrected (Validate Mode)', fontsize=14, fontweight='bold')
    ax.set_xticks(x)
    ax.set_xticklabels(repos, rotation=45, ha='right')
    ax.legend()
    ax.grid(axis='y', alpha=0.3)
    
    plt.tight_layout()
    plot_path = out_dir / "sshom_rate_comparison.png"
    plt.savefig(plot_path, dpi=300, bbox_inches='tight')
    print(f"Saved: {plot_path}")
    plt.close()
    
    # 3. Test runs efficiency (Accelerate mode)
    fig, ax = plt.subplots(figsize=(12, 6))
    
    test_runs = []
    test_runs_err = []
    
    for repo in repos:
        accel_row = accel_summary[accel_summary['repo_name'] == repo]
        
        if len(accel_row) > 0:
            mean = accel_row['avg_test_runs'].iloc[0]
            lo = accel_row['test_runs_ci_lo'].iloc[0]
            hi = accel_row['test_runs_ci_hi'].iloc[0]
            test_runs.append(mean)
            test_runs_err.append([mean - lo, hi - mean])
        else:
            test_runs.append(0)
            test_runs_err.append([0, 0])
    
    test_runs_err = np.array(test_runs_err).T
    
    ax.bar(x, test_runs, yerr=test_runs_err, capsize=5, alpha=0.8, color='#f39c12')
    ax.set_xlabel('Repository', fontsize=12, fontweight='bold')
    ax.set_ylabel('Average Test Run Groups', fontsize=12, fontweight='bold')
    ax.set_title('Test Execution Efficiency (Accelerate Mode)', fontsize=14, fontweight='bold')
    ax.set_xticks(x)
    ax.set_xticklabels(repos, rotation=45, ha='right')
    ax.grid(axis='y', alpha=0.3)
    
    plt.tight_layout()
    plot_path = out_dir / "test_runs_efficiency.png"
    plt.savefig(plot_path, dpi=300, bbox_inches='tight')
    print(f"Saved: {plot_path}")
    plt.close()


def create_baseline_visualizations(baseline_comparison: pd.DataFrame, out_dir: Path):
    """Create visualizations for baseline vs accelerate comparison."""
    print("\n=== CREATING BASELINE COMPARISON VISUALIZATIONS ===")
    
    sns.set_style("whitegrid")
    
    repos = baseline_comparison['repo_name'].values
    x = np.arange(len(repos))
    
    # 1. Runtime comparison: Baseline vs Accelerate-Reduced
    fig, ax = plt.subplots(figsize=(14, 7))
    
    width = 0.35
    
    baseline_runtimes = []
    baseline_errors = []
    accel_red_runtimes = []
    accel_red_errors = []
    
    for _, row in baseline_comparison.iterrows():
        # Baseline
        if 'baseline_runtime_sec' in row and not pd.isna(row['baseline_runtime_sec']):
            mean = row['baseline_runtime_sec']
            lo = row['baseline_runtime_ci_lo']
            hi = row['baseline_runtime_ci_hi']
            baseline_runtimes.append(mean)
            baseline_errors.append([mean - lo, hi - mean])
        else:
            baseline_runtimes.append(0)
            baseline_errors.append([0, 0])
        
        # Accelerate-Reduced
        if 'accel_red_runtime_sec' in row and not pd.isna(row['accel_red_runtime_sec']):
            mean = row['accel_red_runtime_sec']
            lo = row['accel_red_runtime_ci_lo']
            hi = row['accel_red_runtime_ci_hi']
            accel_red_runtimes.append(mean)
            accel_red_errors.append([mean - lo, hi - mean])
        else:
            accel_red_runtimes.append(0)
            accel_red_errors.append([0, 0])
    
    baseline_errors = np.array(baseline_errors).T
    accel_red_errors = np.array(accel_red_errors).T
    
    ax.bar(x - width/2, baseline_runtimes, width, label='Baseline (FOM only)', 
           yerr=baseline_errors, capsize=5, alpha=0.8, color='#95a5a6')
    ax.bar(x + width/2, accel_red_runtimes, width, label='HOMT (Accelerate-Reduced)', 
           yerr=accel_red_errors, capsize=5, alpha=0.8, color='#2ecc71')
    
    ax.set_xlabel('Repository', fontsize=12, fontweight='bold')
    ax.set_ylabel('Average Runtime (seconds)', fontsize=12, fontweight='bold')
    ax.set_title('Runtime Comparison: Baseline FOM vs HOMT', fontsize=14, fontweight='bold')
    ax.set_xticks(x)
    ax.set_xticklabels(repos, rotation=45, ha='right')
    ax.legend(fontsize=10)
    ax.grid(axis='y', alpha=0.3)
    
    plt.tight_layout()
    plot_path = out_dir / "baseline_runtime_comparison.png"
    plt.savefig(plot_path, dpi=300, bbox_inches='tight')
    print(f"Saved: {plot_path}")
    plt.close()
    
    # 2. Speedup comparison
    fig, ax = plt.subplots(figsize=(14, 7))
    
    speedup_values = []
    
    for _, row in baseline_comparison.iterrows():
        speedup_values.append(row.get('speedup_vs_baseline', 0))
    
    ax.bar(x, speedup_values, width, label='HOMT vs Baseline', 
           alpha=0.8, color='#3498db')
    
    # Add reference line at 1.0x (no speedup)
    ax.axhline(y=1.0, color='red', linestyle='--', linewidth=2, alpha=0.7, label='No speedup (1.0x)')
    
    ax.set_xlabel('Repository', fontsize=12, fontweight='bold')
    ax.set_ylabel('Speedup Factor', fontsize=12, fontweight='bold')
    ax.set_title('Speedup: HOMT vs Baseline FOM Testing', fontsize=14, fontweight='bold')
    ax.set_xticks(x)
    ax.set_xticklabels(repos, rotation=45, ha='right')
    ax.legend(fontsize=10)
    ax.grid(axis='y', alpha=0.3)
    
    plt.tight_layout()
    plot_path = out_dir / "baseline_speedup_comparison.png"
    plt.savefig(plot_path, dpi=300, bbox_inches='tight')
    print(f"Saved: {plot_path}")
    plt.close()
    
    # 3. Test run reduction percentage
    fig, ax = plt.subplots(figsize=(14, 7))
    
    test_reduction_values = []
    
    for _, row in baseline_comparison.iterrows():
        test_reduction_values.append(row.get('test_reduction_percent', 0))
    
    ax.bar(x, test_reduction_values, width, 
           alpha=0.8, color='#e74c3c')
    
    ax.set_xlabel('Repository', fontsize=12, fontweight='bold')
    ax.set_ylabel('Test Run Reduction (%)', fontsize=12, fontweight='bold')
    ax.set_title('Test Execution Reduction: HOMT vs Baseline', fontsize=14, fontweight='bold')
    ax.set_xticks(x)
    ax.set_xticklabels(repos, rotation=45, ha='right')
    ax.grid(axis='y', alpha=0.3)
    
    plt.tight_layout()
    plot_path = out_dir / "baseline_test_reduction.png"
    plt.savefig(plot_path, dpi=300, bbox_inches='tight')
    print(f"Saved: {plot_path}")
    plt.close()


def create_additional_visualizations(df: pd.DataFrame, out_dir: Path):
    """Create additional requested visualizations for thesis."""
    print("\n=== CREATING ADDITIONAL VISUALIZATIONS ===")
    
    sns.set_style("whitegrid")
    
    # 1. SSHOM Rate by Repository (validate-with-heuristics only)
    print("Creating SSHOM rate by repository graph...")
    validate_df = df[df['config'] == 'validate-with-heuristics'].copy()
    
    if len(validate_df) > 0:
        fig, ax = plt.subplots(figsize=(12, 7))
        
        repos = sorted(validate_df['repo_name'].unique())
        x = np.arange(len(repos))
        
        sshom_rates = []
        sshom_errors = []
        
        for repo in repos:
            repo_data = validate_df[validate_df['repo_name'] == repo]
            rates = repo_data['sshom_rate_corrected'].values
            
            if len(rates) > 0:
                mean, ci_lo, ci_hi = bootstrap_ci(rates)
                sshom_rates.append(mean)
                sshom_errors.append([mean - ci_lo, ci_hi - mean])
            else:
                sshom_rates.append(0)
                sshom_errors.append([0, 0])
        
        sshom_errors = np.array(sshom_errors).T
        
        bars = ax.bar(x, sshom_rates, yerr=sshom_errors, capsize=5, alpha=0.8, 
                     color='#9b59b6', edgecolor='black', linewidth=1.5)
        
        # Add value labels on top of bars
        for i, (bar, rate) in enumerate(zip(bars, sshom_rates)):
            height = bar.get_height()
            ax.text(bar.get_x() + bar.get_width()/2., height,
                   f'{rate:.1f}%',
                   ha='center', va='bottom', fontweight='bold', fontsize=10)
        
        ax.set_xlabel('Repository', fontsize=13, fontweight='bold')
        ax.set_ylabel('Average SSHOM Rate (%)', fontsize=13, fontweight='bold')
        ax.set_title('SSHOM Success Rate by Repository (Validate Mode)', 
                    fontsize=15, fontweight='bold')
        ax.set_xticks(x)
        ax.set_xticklabels(repos, rotation=45, ha='right', fontsize=11)
        ax.set_ylim(0, max(sshom_rates) * 1.15)  # Add 15% headroom for labels
        ax.grid(axis='y', alpha=0.3, linestyle='--')
        
        plt.tight_layout()
        plot_path = out_dir / "sshom_rate_by_repository.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        print(f"Saved: {plot_path}")
        plt.close()
    else:
        print("WARNING: No validate-with-heuristics data found, skipping SSHOM rate graph")
    
    # 2. Runtime Comparison: Baseline vs Accelerate-Reduced (focused view)
    print("Creating baseline vs accelerate runtime comparison graph...")
    baseline_df = df[df['config'] == 'baseline-fom-only'].copy()
    accel_df = df[df['config'] == 'accelerate-with-heuristics-reduced'].copy()
    
    if len(baseline_df) > 0 and len(accel_df) > 0:
        fig, ax = plt.subplots(figsize=(12, 7))
        
        repos = sorted(df['repo_name'].unique())
        x = np.arange(len(repos))
        width = 0.35
        
        baseline_times = []
        baseline_errors = []
        accel_times = []
        accel_errors = []
        
        for repo in repos:
            # Baseline data
            baseline_repo = baseline_df[baseline_df['repo_name'] == repo]
            if len(baseline_repo) > 0:
                times = baseline_repo['total_test_duration_ms'].values / 1000.0
                mean, ci_lo, ci_hi = bootstrap_ci(times)
                baseline_times.append(mean)
                baseline_errors.append([mean - ci_lo, ci_hi - mean])
            else:
                baseline_times.append(0)
                baseline_errors.append([0, 0])
            
            # Accelerate data
            accel_repo = accel_df[accel_df['repo_name'] == repo]
            if len(accel_repo) > 0:
                times = accel_repo['total_test_duration_ms'].values / 1000.0
                mean, ci_lo, ci_hi = bootstrap_ci(times)
                accel_times.append(mean)
                accel_errors.append([mean - ci_lo, ci_hi - mean])
            else:
                accel_times.append(0)
                accel_errors.append([0, 0])
        
        baseline_errors = np.array(baseline_errors).T
        accel_errors = np.array(accel_errors).T
        
        bars1 = ax.bar(x - width/2, baseline_times, width, label='Baseline (FOM only)', 
                      yerr=baseline_errors, capsize=5, alpha=0.85, 
                      color='#e74c3c', edgecolor='black', linewidth=1.2)
        bars2 = ax.bar(x + width/2, accel_times, width, label='HOMT (Accelerate-Reduced)', 
                      yerr=accel_errors, capsize=5, alpha=0.85, 
                      color='#2ecc71', edgecolor='black', linewidth=1.2)
        
        # Add value labels on top of bars
        for bars in [bars1, bars2]:
            for bar in bars:
                height = bar.get_height()
                if height > 0:
                    ax.text(bar.get_x() + bar.get_width()/2., height,
                           f'{height:.1f}s',
                           ha='center', va='bottom', fontsize=9, fontweight='bold')
        
        ax.set_xlabel('Repository', fontsize=13, fontweight='bold')
        ax.set_ylabel('Average Runtime (seconds)', fontsize=13, fontweight='bold')
        ax.set_title('Runtime Comparison: Baseline FOM vs HOMT Acceleration', 
                    fontsize=15, fontweight='bold')
        ax.set_xticks(x)
        ax.set_xticklabels(repos, rotation=45, ha='right', fontsize=11)
        ax.legend(fontsize=11, loc='upper left')
        ax.grid(axis='y', alpha=0.3, linestyle='--')
        
        plt.tight_layout()
        plot_path = out_dir / "runtime_baseline_vs_accelerate.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        print(f"Saved: {plot_path}")
        plt.close()
    else:
        print("WARNING: Missing baseline or accelerate data, skipping runtime comparison graph")


def generate_executive_summary(df: pd.DataFrame, accel_summary: pd.DataFrame,
                               validate_summary: pd.DataFrame, alg_summary: pd.DataFrame,
                               out_dir: Path):
    """Generate an executive summary addressing the research question."""
    print("\n=== GENERATING EXECUTIVE SUMMARY ===")
    
    summary_lines = []
    summary_lines.append("=" * 80)
    summary_lines.append("EXECUTIVE SUMMARY: SSHOM Effectiveness in Stryker.NET")
    summary_lines.append("=" * 80)
    summary_lines.append("")
    summary_lines.append("Research Question:")
    summary_lines.append("To what extent can predictive Strongly Subsuming Higher Order Mutants (SSHOMs)")
    summary_lines.append("be applied to accelerate mutation testing in real-world tools such as Stryker.NET?")
    summary_lines.append("")
    summary_lines.append("=" * 80)
    summary_lines.append("")
    
    # 1. Overall statistics
    total_runs = len(df)
    repos_tested = len(df['repo_name'].unique())
    
    summary_lines.append("1. EXPERIMENT OVERVIEW")
    summary_lines.append("-" * 80)
    summary_lines.append(f"   Repositories tested:     {repos_tested}")
    summary_lines.append(f"   Total experimental runs: {total_runs}")
    summary_lines.append(f"   Runs per configuration:  {RUN_COUNT}")
    summary_lines.append("")
    
    # 2. Accelerate mode performance
    summary_lines.append("2. ACCELERATE MODE PERFORMANCE (Predictive SSHOM Application)")
    summary_lines.append("-" * 80)
    
    avg_runtime = accel_summary['avg_runtime_sec'].mean()
    avg_test_runs = accel_summary['avg_test_runs'].mean()
    avg_homs = accel_summary['avg_total_homs'].mean()
    
    summary_lines.append(f"   Average runtime:         {avg_runtime:.1f} seconds")
    summary_lines.append(f"   Average test run groups: {avg_test_runs:.1f}")
    summary_lines.append(f"   Average HOMs generated:  {avg_homs:.1f}")
    summary_lines.append("")
    summary_lines.append("   Per Repository:")
    for _, row in accel_summary.iterrows():
        summary_lines.append(f"     {row['repo_name']:30s} {row['avg_runtime_sec']:8.1f}s "
                           f"({row['avg_test_runs']:6.1f} test groups, {row['avg_total_homs']:6.1f} HOMs)")
    summary_lines.append("")
    
    # 3. Validate mode performance
    summary_lines.append("3. VALIDATE MODE PERFORMANCE (Ground Truth Verification)")
    summary_lines.append("-" * 80)
    
    avg_val_runtime = validate_summary['avg_runtime_sec'].mean()
    avg_sshom_orig = validate_summary['avg_sshom_rate_orig'].mean()
    avg_sshom_corr = validate_summary['avg_sshom_rate_corrected'].mean()
    avg_total_sshoms = validate_summary['avg_total_sshoms'].mean()
    
    summary_lines.append(f"   Average runtime:              {avg_val_runtime:.1f} seconds")
    summary_lines.append(f"   Average SSHOM rate (orig):    {avg_sshom_orig:.2f}%")
    summary_lines.append(f"   Average SSHOM rate (corrected): {avg_sshom_corr:.2f}%")
    summary_lines.append(f"   Average SSHOMs found:         {avg_total_sshoms:.1f}")
    summary_lines.append("")
    summary_lines.append("   Per Repository:")
    for _, row in validate_summary.iterrows():
        summary_lines.append(f"     {row['repo_name']:30s} SSHOM: {row['avg_sshom_rate_corrected']:5.2f}% "
                           f"({row['avg_total_sshoms']:5.1f} SSHOMs, {row['avg_runtime_sec']:8.1f}s)")
    summary_lines.append("")
    
    # 4. Algorithm performance
    summary_lines.append("4. ALGORITHM PREDICTION ACCURACY")
    summary_lines.append("-" * 80)
    
    # Average across all repos and configs
    for alg in alg_summary['algorithm'].unique():
        alg_data = alg_summary[alg_summary['algorithm'] == alg]
        avg_highest = alg_data['avg_highest_score'].mean()
        avg_lowest = alg_data['avg_lowest_score'].mean()
        avg_avg = alg_data['avg_avg_score'].mean()
        avg_median = alg_data['avg_median_score'].mean()
        
        summary_lines.append(f"   {alg}:")
        summary_lines.append(f"     Highest prediction score: {avg_highest:.4f}")
        summary_lines.append(f"     Lowest prediction score:  {avg_lowest:.4f}")
        summary_lines.append(f"     Average prediction score: {avg_avg:.4f}")
        summary_lines.append(f"     Median prediction score:  {avg_median:.4f}")
        summary_lines.append("")
    
    # 5. Key findings
    summary_lines.append("5. KEY FINDINGS")
    summary_lines.append("-" * 80)
    
    # Calculate speedup potential
    if avg_val_runtime > 0 and avg_runtime > 0:
        speedup_ratio = avg_val_runtime / avg_runtime
        summary_lines.append(f"   a) Runtime Efficiency:")
        summary_lines.append(f"      Accelerate mode is {speedup_ratio:.2f}x faster than validate mode on average")
        summary_lines.append("")
    
    summary_lines.append(f"   b) SSHOM Success Rate:")
    summary_lines.append(f"      {avg_sshom_corr:.2f}% of generated HOMs are true SSHOMs (corrected calculation)")
    summary_lines.append(f"      This indicates {'high' if avg_sshom_corr > 15 else 'moderate' if avg_sshom_corr > 5 else 'low'} prediction accuracy")
    summary_lines.append("")
    
    summary_lines.append(f"   c) Test Execution Reduction:")
    summary_lines.append(f"      Average of {avg_test_runs:.1f} test run groups vs full mutation testing")
    summary_lines.append(f"      Represents significant test execution savings")
    summary_lines.append("")
    
    # Variability analysis
    sshom_cv = validate_summary['avg_sshom_rate_corrected'].std() / avg_sshom_corr * 100
    summary_lines.append(f"   d) Cross-Project Variability:")
    summary_lines.append(f"      SSHOM rate coefficient of variation: {sshom_cv:.1f}%")
    summary_lines.append(f"      This {'high' if sshom_cv > 50 else 'moderate' if sshom_cv > 25 else 'low'} variability suggests")
    summary_lines.append(f"      project characteristics influence SSHOM effectiveness")
    summary_lines.append("")
    
    # 6. Conclusions
    summary_lines.append("6. CONCLUSIONS")
    summary_lines.append("-" * 80)
    
    if avg_sshom_corr > 10 and speedup_ratio > 1.5:
        conclusion = "HIGHLY PROMISING"
        detail = "Predictive SSHOMs show strong potential for accelerating mutation testing"
    elif avg_sshom_corr > 5 and speedup_ratio > 1.2:
        conclusion = "PROMISING"
        detail = "Predictive SSHOMs demonstrate moderate effectiveness for acceleration"
    else:
        conclusion = "LIMITED EFFECTIVENESS"
        detail = "Current SSHOM predictions have limited practical acceleration potential"
    
    summary_lines.append(f"   Overall Assessment: {conclusion}")
    summary_lines.append(f"   {detail}")
    summary_lines.append("")
    summary_lines.append("   Recommendations:")
    summary_lines.append("   - Analyze project characteristics that correlate with high SSHOM rates")
    summary_lines.append("   - Investigate heuristic combinations for improved prediction accuracy")
    summary_lines.append("   - Consider adaptive strategies based on project complexity")
    summary_lines.append("")
    
    summary_lines.append("=" * 80)
    summary_lines.append("END OF SUMMARY")
    summary_lines.append("=" * 80)
    
    # Write to file
    summary_text = "\n".join(summary_lines)
    summary_path = out_dir / "EXECUTIVE_SUMMARY.txt"
    summary_path.write_text(summary_text, encoding='utf-8')
    print(f"\nSaved: {summary_path}")
    print("\n" + summary_text)


def main():
    parser = argparse.ArgumentParser(description="Analyze HOMT comparison experiment results")
    parser.add_argument('--out', required=True, help="Output directory for analysis results")
    args = parser.parse_args()
    
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    
    print("=" * 80)
    print("HOMT COMPARISON EXPERIMENT ANALYSIS")
    print("=" * 80)
    print(f"\nOutput directory: {out_dir}")
    print(f"Analyzing {len(REPOSITORIES)} repositories with {RUN_COUNT} runs per configuration")
    
    # Collect all data
    df = collect_all_data()
    
    if len(df) == 0:
        print("\nERROR: No data collected! Check repository paths and CSV files.")
        return
    
    # Save combined raw data
    raw_path = out_dir / "all_runs_combined.csv"
    df.to_csv(raw_path, index=False, encoding='utf-8')
    print(f"\nSaved combined data: {raw_path}")
    
    # Run analyses
    baseline_comparison = analyze_baseline_comparison(df, out_dir)
    accel_summary = analyze_accelerate_mode(df, out_dir)
    validate_summary = analyze_validate_mode(df, out_dir)
    alg_summary = analyze_algorithm_performance(df, out_dir)
    
    # Create visualizations
    if accel_summary is not None and validate_summary is not None and baseline_comparison is not None:
        create_comparison_visualizations(df, accel_summary, validate_summary, out_dir)
        create_baseline_visualizations(baseline_comparison, out_dir)
    
    # Create additional requested visualizations
    create_additional_visualizations(df, out_dir)
    
    # Generate executive summary
    if accel_summary is not None and validate_summary is not None and alg_summary is not None:
        generate_executive_summary(df, accel_summary, validate_summary, alg_summary, out_dir)
    
    print("\n" + "=" * 80)
    print("ANALYSIS COMPLETE!")
    print("=" * 80)
    print(f"\nAll results saved to: {out_dir}")
    print("\nGenerated files:")
    print("  - all_runs_combined.csv (raw data)")
    print("  - baseline_comparison.csv (baseline vs HOMT comparison)")
    print("  - accelerate_summary.csv (HOMT accelerate-reduced statistics)")
    print("  - validate_summary.csv (validate mode statistics)")
    print("  - algorithm_stats_detailed.csv (per-run algorithm performance)")
    print("  - algorithm_performance_summary.csv (aggregated algorithm statistics)")
    print("  - baseline_runtime_comparison.png (baseline vs HOMT runtimes)")
    print("  - baseline_speedup_comparison.png (speedup factors)")
    print("  - baseline_test_reduction.png (test execution reduction)")
    print("  - runtime_comparison.png (accelerate vs validate)")
    print("  - sshom_rate_comparison.png (SSHOM rates)")
    print("  - test_runs_efficiency.png (test group efficiency)")
    print("  - sshom_rate_by_repository.png (SSHOM rates per repo in validate mode)")
    print("  - runtime_baseline_vs_accelerate.png (focused baseline vs accelerate comparison)")
    print("  - EXECUTIVE_SUMMARY.txt (comprehensive research findings)")


if __name__ == "__main__":
    main()
