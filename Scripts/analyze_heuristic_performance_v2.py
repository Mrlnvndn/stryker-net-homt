#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Analyze Heuristic Performance by Algorithm

This script analyzes which heuristics perform best for Genetic vs Local algorithms
separately across repositories with improved robustness and configurability.

Usage:
    python analyze_heuristic_performance.py --glm_dir <path_to_aggregated_results>
"""

import pandas as pd
import numpy as np
from pathlib import Path
import argparse
import warnings
import sys
from typing import Optional
from itertools import product

warnings.filterwarnings('ignore')

# Fix Windows console encoding for Unicode
if sys.platform == 'win32':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except:
        pass

# ===== HELPER FUNCTIONS =====

def safe_first(s: pd.Series) -> float:
    """Safely extract first value from series, return NaN if empty."""
    return s.iloc[0] if len(s) > 0 and not s.empty else np.nan

def format_ci(lo: float, hi: float) -> str:
    """Format confidence interval, handling infinite upper bounds (separation)."""
    if pd.isna(lo) and pd.isna(hi):
        return "—"
    if pd.isna(lo):
        lo_str = "?"
    else:
        lo_str = f"{lo:.3f}"
    
    if pd.isna(hi):
        return f"({lo_str}–?)"
    if not np.isfinite(hi):
        return f"({lo_str}–inf) [separation]"
    return f"({lo_str}–{hi:.3f})"

def format_or(or_val: float) -> str:
    """Format OR value for display."""
    if pd.isna(or_val):
        return "—"
    if not np.isfinite(or_val):
        return "inf"
    return f"{or_val:.3f}"

def effect_label(or_val: float, thresholds: dict) -> str:
    """Classify effect size based on thresholds."""
    if pd.isna(or_val) or not np.isfinite(or_val):
        return "unclear"
    if or_val >= thresholds['large']:
        return "★★★ strong increase"
    elif or_val >= thresholds['small']:
        return "★★ moderate increase"
    elif or_val >= 1.0:
        return "★ slight increase"
    elif or_val >= thresholds['negligible']:
        return "≈ negligible"
    elif or_val >= thresholds['slight_drop']:
        return "★ slight decrease"
    else:
        return "★★★ strong decrease"

# ===== MAIN ANALYSIS FUNCTION =====

def analyze_algorithm_specific_performance(glm_dir: Path, args):
    """Analyze heuristic performance separately for each algorithm."""
    
    print("=" * 120)
    print("ALGORITHM-SPECIFIC HEURISTIC PERFORMANCE ANALYSIS")
    print("=" * 120)
    print()
    print("Legend: ★ = slight, ★★ = moderate, ★★★ = strong")
    print("        'separation' = infinite upper CI (complete/quasi-complete separation)")
    print()
    
    # Thresholds for effect interpretation
    thresholds = {
        'large': args.large,
        'small': args.small,
        'negligible': args.negligible,
        'slight_drop': args.slight_drop
    }
    
    # ===== LOAD AND PREPARE DATA =====
    
    # Robust file discovery
    matches = sorted(glm_dir.glob("*coef_or_all_repos*.csv"))
    if not matches:
        raise FileNotFoundError(f"No '*coef_or_all_repos*.csv' found in {glm_dir}")
    
    or_file = matches[0]
    or_df = pd.read_csv(or_file)
    
    print(f"Loaded: {or_file.name}")
    print(f"Repositories: {sorted(or_df['repository'].unique())}")
    print()
    
    # Coerce numeric columns
    for col in ["OR", "OR_CI_low", "OR_CI_high"]:
        if col in or_df.columns:
            or_df[col] = pd.to_numeric(or_df[col], errors="coerce")
    
    # Normalize term parsing - resilient to naming variations
    ALG_PATTERN = r"algorithm\[T\.genetic\]"
    or_df["is_main"] = or_df["term"].str.startswith("H_") & ~or_df["term"].str.contains(":")
    or_df["is_inter"] = or_df["term"].str.contains(ALG_PATTERN) & or_df["term"].str.contains(":")
    or_df["is_alg_baseline"] = or_df["term"].str.contains(ALG_PATTERN) & ~or_df["term"].str.contains(":")
    
    # Extract clean heuristic name
    or_df["heuristic"] = (or_df["term"]
        .str.replace(ALG_PATTERN + ":", "", regex=True)
        .str.replace("H_", "", regex=False))
    
    # Filter datasets
    main_effects = or_df[or_df["is_main"]].copy()
    interactions = or_df[or_df["is_inter"]].copy()
    
    # ====================================================================
    # PART 1: MAIN EFFECTS (Base effect of each heuristic)
    # ====================================================================
    print("=" * 120)
    print("PART 1: MAIN EFFECTS - Base Heuristic Performance (Independent of Algorithm)")
    print("=" * 120)
    print("These show whether a heuristic increases or decreases SSHOM success overall.")
    print()
    
    for heuristic in sorted(main_effects['heuristic'].unique()):
        heur_data = main_effects[main_effects['heuristic'] == heuristic]
        
        print(f"\n{heuristic}")
        print("-" * 120)
        print(f"{'Repository':<28} {'OR':>8} {'95% CI':>25} {'Effect':<40}")
        print("-" * 120)
        
        for _, row in heur_data.iterrows():
            repo = row['repository']
            or_val = row['OR']
            ci_low = row.get('OR_CI_low', np.nan)
            ci_high = row.get('OR_CI_high', np.nan)
            
            or_str = format_or(or_val).rjust(8)
            ci_str = format_ci(ci_low, ci_high).rjust(25)
            effect = effect_label(or_val, thresholds)
            
            print(f"{repo:<28} {or_str} {ci_str} {effect:<40}")
    
    # ====================================================================
    # PART 2: INTERACTION EFFECTS (Algorithm-specific performance)
    # ====================================================================
    print("\n\n")
    print("=" * 120)
    print("PART 2: INTERACTION EFFECTS - Which Heuristics Favor Genetic vs Local?")
    print("=" * 120)
    print("These show whether a heuristic helps Genetic MORE or LESS than it helps Local.")
    print("OR > 1.0: Genetic benefits MORE from this heuristic (genetic gains advantage)")
    print("OR < 1.0: Local benefits MORE from this heuristic (local gains advantage)")
    print()
    
    for heuristic in sorted(interactions['heuristic'].unique()):
        heur_data = interactions[interactions['heuristic'] == heuristic]
        
        print(f"\n{heuristic}")
        print("-" * 120)
        print(f"{'Repository':<28} {'OR':>8} {'95% CI':>25} {'Favors':<40}")
        print("-" * 120)
        
        for _, row in heur_data.iterrows():
            repo = row['repository']
            or_val = row['OR']
            ci_low = row.get('OR_CI_low', np.nan)
            ci_high = row.get('OR_CI_high', np.nan)
            
            or_str = format_or(or_val).rjust(8)
            ci_str = format_ci(ci_low, ci_high).rjust(25)
            
            if pd.notna(or_val) and np.isfinite(or_val):
                if or_val > 1.20:
                    effect = "★★★ Strongly favors Genetic"
                elif or_val > 1.05:
                    effect = "★★ Moderately favors Genetic"
                elif or_val > 1.0:
                    effect = "★ Slightly favors Genetic"
                elif or_val > 0.95:
                    effect = "≈ Neutral"
                elif or_val > 0.80:
                    effect = "★ Slightly favors Local"
                else:
                    effect = "★★★ Strongly favors Local"
            else:
                effect = "—"
            
            print(f"{repo:<28} {or_str} {ci_str} {effect:<40}")
    
    # ====================================================================
    # PART 3: RANKINGS BY ALGORITHM
    # ====================================================================
    print("\n\n")
    print("=" * 120)
    print("PART 3: HEURISTIC RANKINGS BY ALGORITHM")
    print("=" * 120)
    
    repos = sorted(or_df['repository'].unique())
    
    for repo in repos:
        print(f"\n{'='*120}")
        print(f"REPOSITORY: {repo}")
        print(f"{'='*120}")
        
        # Get algorithm baseline for this repo
        alg_baseline_row = or_df[(or_df['repository'] == repo) & or_df['is_alg_baseline']]
        genetic_baseline_or = safe_first(alg_baseline_row['OR']) if not alg_baseline_row.empty else 1.0
        
        if pd.notna(genetic_baseline_or):
            print(f"\nAlgorithm baseline: Genetic vs Local OR = {genetic_baseline_or:.3f}")
            print(f"  (Genetic has {genetic_baseline_or:.1%} the odds of Local with all heuristics OFF)")
        print()
        
        # Get main effects and interactions for this repo
        repo_main = main_effects[main_effects['repository'] == repo].copy()
        repo_int = interactions[interactions['repository'] == repo].copy()
        
        # Calculate algorithm-specific effects
        results = []
        
        for heuristic in sorted(repo_main['heuristic'].unique()):
            main_row = repo_main[repo_main['heuristic'] == heuristic]
            int_row = repo_int[repo_int['heuristic'] == heuristic]
            
            main_or = safe_first(main_row['OR'])
            int_or = safe_first(int_row['OR']) if not int_row.empty else 1.0
            
            # For Genetic: main_effect * interaction
            # For Local: main_effect only (reference level)
            if pd.notna(main_or) and pd.notna(int_or):
                genetic_or = main_or * int_or
                local_or = main_or
            elif pd.notna(main_or):
                genetic_or = main_or
                local_or = main_or
            else:
                genetic_or = np.nan
                local_or = np.nan
            
            results.append({
                'heuristic': heuristic,
                'main_or': main_or,
                'interaction_or': int_or,
                'genetic_or': genetic_or,
                'local_or': local_or
            })
        
        results_df = pd.DataFrame(results)
        
        # Replace infinities with NaN for ratio calculations
        results_df = results_df.replace([np.inf, -np.inf], np.nan)
        
        # Calculate genetic advantage (safe division)
        results_df['genetic_advantage'] = np.nan
        mask = (results_df['local_or'].notna()) & (results_df['local_or'] != 0)
        results_df.loc[mask, 'genetic_advantage'] = results_df.loc[mask, 'genetic_or'] / results_df.loc[mask, 'local_or']
        
        # Save rankings CSV if requested
        if args.save_csv:
            csv_path = glm_dir.parent / f"part3_rankings_{repo}.csv"
            results_df.to_csv(csv_path, index=False)
            print(f"Saved rankings: {csv_path.name}")
        
        # Sort and display by genetic performance
        print(f"BEST HEURISTICS FOR GENETIC ALGORITHM:")
        print("-" * 80)
        genetic_sorted = results_df.sort_values('genetic_or', ascending=False, na_position='last')
        for i, (_, row) in enumerate(genetic_sorted.head(args.top).iterrows(), 1):
            h = row['heuristic']
            or_val = row['genetic_or']
            or_str = format_or(or_val)
            pct = f"(+{(or_val-1)*100:.0f}%)" if pd.notna(or_val) and or_val >= 1 else f"({(or_val-1)*100:.0f}%)" if pd.notna(or_val) else ""
            print(f"{i}. {h:<25} OR={or_str:>8} {pct}")
        
        # Sort and display by local performance
        print(f"\nBEST HEURISTICS FOR LOCAL ALGORITHM:")
        print("-" * 80)
        local_sorted = results_df.sort_values('local_or', ascending=False, na_position='last')
        for i, (_, row) in enumerate(local_sorted.head(args.top).iterrows(), 1):
            h = row['heuristic']
            or_val = row['local_or']
            or_str = format_or(or_val)
            pct = f"(+{(or_val-1)*100:.0f}%)" if pd.notna(or_val) and or_val >= 1 else f"({(or_val-1)*100:.0f}%)" if pd.notna(or_val) else ""
            print(f"{i}. {h:<25} OR={or_str:>8} {pct}")
        
        # Show biggest differences
        print(f"\nHEURISTICS WITH BIGGEST ALGORITHM DIFFERENCES:")
        print("-" * 80)
        print("(Shows which heuristics create the largest performance gap between algorithms)")
        diff_sorted = results_df.sort_values('genetic_advantage', ascending=False, na_position='last')
        for i, (_, row) in enumerate(diff_sorted.head(args.top).iterrows(), 1):
            h = row['heuristic']
            ratio = row['genetic_advantage']
            g_or = row['genetic_or']
            l_or = row['local_or']
            
            if pd.notna(ratio):
                if ratio > 1:
                    favor = f"Favors Genetic ({ratio:.2f}× advantage)"
                elif ratio < 1:
                    favor = f"Favors Local ({1/ratio:.2f}× advantage)"
                else:
                    favor = "Neutral"
            else:
                favor = "—"
            
            print(f"{i}. {h:<25} G:{format_or(g_or):>8} L:{format_or(l_or):>8} → {favor}")
    
    # ====================================================================
    # PART 4: OPTIMAL COMBINATIONS
    # ====================================================================
    print("\n\n")
    print("=" * 120)
    print("PART 4: OPTIMAL HEURISTIC COMBINATIONS")
    print("=" * 120)
    print("Finds the best ON/OFF configuration for each algorithm based on GLM predictions.")
    print()
    print("NOTE: Each algorithm's IMPROVEMENT is calculated relative to its OWN baseline (all heuristics OFF).")
    print("      Totals are then expressed relative to the Local baseline for cross-algorithm comparison.")
    print()
    
    for repo in repos:
        print(f"\n{'='*120}")
        print(f"REPOSITORY: {repo}")
        print(f"{'='*120}")
        
        # Get main effects and interactions for this repo
        repo_main = main_effects[main_effects['repository'] == repo].copy()
        repo_int = interactions[interactions['repository'] == repo].copy()
        
        # Get base algorithm effect (Genetic vs Local)
        alg_baseline_row = or_df[(or_df['repository'] == repo) & or_df['is_alg_baseline']]
        genetic_baseline_or = safe_first(alg_baseline_row['OR']) if not alg_baseline_row.empty else 1.0
        local_baseline_or = 1.0  # Local is reference level
        
        if not pd.notna(genetic_baseline_or) or not np.isfinite(genetic_baseline_or):
            genetic_baseline_or = 1.0
        
        heuristics = sorted(repo_main['heuristic'].unique())
        n_heuristics = len(heuristics)
        
        # Check for combinatorial explosion
        if n_heuristics > args.limit_heuristics:
            print(f"⚠ WARNING: {n_heuristics} heuristics would create {2**n_heuristics} configs.")
            print(f"  Limiting to first {args.limit_heuristics} heuristics. Use --limit_heuristics to adjust.")
            heuristics = heuristics[:args.limit_heuristics]
            n_heuristics = len(heuristics)
        
        print(f"\nEvaluating {2**n_heuristics} possible configurations ({n_heuristics} heuristics)...")
        print()
        
        # Generate all possible combinations (ON/OFF for each heuristic)
        best_genetic_config = None
        best_genetic_impr = 0
        best_local_config = None
        best_local_impr = 0
        
        all_configs = []
        
        for config in product([0, 1], repeat=n_heuristics):
            config_dict = dict(zip(heuristics, config))
            
            # Calculate improvement factors (multiplicative vs each algorithm's baseline)
            genetic_impr = 1.0  # improvement vs genetic baseline
            local_impr = 1.0    # improvement vs local baseline
            
            for heuristic, is_on in config_dict.items():
                if is_on:
                    # Get main effect
                    main_row = repo_main[repo_main['heuristic'] == heuristic]
                    main_or = safe_first(main_row['OR']) if not main_row.empty else np.nan
                    
                    # Get interaction effect (for genetic)
                    int_row = repo_int[repo_int['heuristic'] == heuristic]
                    int_or = safe_first(int_row['OR']) if not int_row.empty else np.nan
                    
                    if pd.notna(main_or) and np.isfinite(main_or):
                        # Local gets main effect only
                        local_impr *= main_or
                        
                        # Genetic gets main * interaction
                        if pd.notna(int_or) and np.isfinite(int_or):
                            genetic_impr *= (main_or * int_or)
                        else:
                            genetic_impr *= main_or
            
            # Calculate totals vs local baseline (for comparison)
            genetic_total = genetic_baseline_or * genetic_impr
            local_total = local_baseline_or * local_impr
            
            all_configs.append({
                'config': config_dict,
                'genetic_impr': genetic_impr,
                'genetic_total': genetic_total,
                'local_impr': local_impr,
                'local_total': local_total,
                'n_heuristics_on': sum(config)
            })
            
            # Track best by IMPROVEMENT within each algorithm
            if genetic_impr > best_genetic_impr:
                best_genetic_impr = genetic_impr
                best_genetic_config = config_dict.copy()
            
            if local_impr > best_local_impr:
                best_local_impr = local_impr
                best_local_config = config_dict.copy()
        
        # Calculate totals for best configs
        best_genetic_total = genetic_baseline_or * best_genetic_impr
        best_local_total = local_baseline_or * best_local_impr
        
        # Display optimal configurations
        print(f"OPTIMAL CONFIGURATION FOR GENETIC ALGORITHM:")
        print("-" * 120)
        print(f"Predicted improvement vs genetic baseline: {best_genetic_impr:.3f}×")
        print(f"Heuristics enabled ({sum(best_genetic_config.values())}/{len(best_genetic_config)}):")
        for heuristic in sorted(best_genetic_config.keys()):
            status = "✓ ON " if best_genetic_config[heuristic] else "✗ OFF"
            print(f"  {status}  {heuristic}")
        
        print()
        print(f"OPTIMAL CONFIGURATION FOR LOCAL ALGORITHM:")
        print("-" * 120)
        print(f"Predicted improvement vs local baseline: {best_local_impr:.3f}×")
        print(f"Heuristics enabled ({sum(best_local_config.values())}/{len(best_local_config)}):")
        for heuristic in sorted(best_local_config.keys()):
            status = "✓ ON " if best_local_config[heuristic] else "✗ OFF"
            print(f"  {status}  {heuristic}")
        
        # Show top N configurations for each algorithm
        print()
        print(f"TOP {args.top} CONFIGURATIONS FOR GENETIC:")
        print("-" * 120)
        configs_df = pd.DataFrame(all_configs)
        top_genetic = configs_df.nlargest(args.top, 'genetic_impr')
        for i, (idx, row) in enumerate(top_genetic.iterrows(), 1):
            config_dict = row['config']
            on_heuristics = [h for h, v in config_dict.items() if v]
            on_str = ", ".join(on_heuristics) if on_heuristics else "(none)"
            print(f"{i}. Improvement={row['genetic_impr']:.3f}×  ({len(on_heuristics)} ON)  {on_str}")
        
        print()
        print(f"TOP {args.top} CONFIGURATIONS FOR LOCAL:")
        print("-" * 120)
        top_local = configs_df.nlargest(args.top, 'local_impr')
        for i, (idx, row) in enumerate(top_local.iterrows(), 1):
            config_dict = row['config']
            on_heuristics = [h for h, v in config_dict.items() if v]
            on_str = ", ".join(on_heuristics) if on_heuristics else "(none)"
            print(f"{i}. Improvement={row['local_impr']:.3f}×  ({len(on_heuristics)} ON)  {on_str}")
        
        # Save best configs CSV if requested
        if args.save_csv:
            best_configs_data = []
            
            # Add best genetic config
            best_configs_data.append({
                'algorithm': 'genetic',
                'rank': 1,
                'improvement': best_genetic_impr,
                'total_vs_local_baseline': best_genetic_total,
                'n_on': sum(best_genetic_config.values()),
                'config': ','.join([h for h, v in best_genetic_config.items() if v])
            })
            
            # Add best local config
            best_configs_data.append({
                'algorithm': 'local',
                'rank': 1,
                'improvement': best_local_impr,
                'total_vs_local_baseline': best_local_total,
                'n_on': sum(best_local_config.values()),
                'config': ','.join([h for h, v in best_local_config.items() if v])
            })
            
            # Add top N for each
            for i, (idx, row) in enumerate(top_genetic.iterrows(), 1):
                if i > 1:  # Skip rank 1, already added
                    config_dict = row['config']
                    best_configs_data.append({
                        'algorithm': 'genetic',
                        'rank': i,
                        'improvement': row['genetic_impr'],
                        'total_vs_local_baseline': row['genetic_total'],
                        'n_on': row['n_heuristics_on'],
                        'config': ','.join([h for h, v in config_dict.items() if v])
                    })
            
            for i, (idx, row) in enumerate(top_local.iterrows(), 1):
                if i > 1:  # Skip rank 1, already added
                    config_dict = row['config']
                    best_configs_data.append({
                        'algorithm': 'local',
                        'rank': i,
                        'improvement': row['local_impr'],
                        'total_vs_local_baseline': row['local_total'],
                        'n_on': row['n_heuristics_on'],
                        'config': ','.join([h for h, v in config_dict.items() if v])
                    })
            
            csv_path = glm_dir.parent / f"part4_best_configs_{repo}.csv"
            pd.DataFrame(best_configs_data).to_csv(csv_path, index=False)
            print(f"\nSaved best configs: {csv_path.name}")
        
        # Compare optimal configurations
        print()
        print(f"COMPARATIVE ANALYSIS:")
        print("-" * 120)
        
        # Check if configurations differ
        config_diff = {h: (best_genetic_config[h], best_local_config[h]) 
                      for h in heuristics if best_genetic_config[h] != best_local_config[h]}
        
        if config_diff:
            print(f"Configurations DIFFER on {len(config_diff)} heuristic(s):")
            for h, (g_val, l_val) in config_diff.items():
                g_str = "ON" if g_val else "OFF"
                l_str = "ON" if l_val else "OFF"
                print(f"  • {h}: Genetic wants {g_str}, Local wants {l_str}")
        else:
            print("Configurations are IDENTICAL - same optimal settings for both algorithms!")
        
        # Compare total effects (both expressed vs local baseline)
        print()
        print("ALGORITHM COMPARISON (both vs local baseline):")
        print("-" * 120)
        print(f"Genetic: baseline {genetic_baseline_or:.3f} × improvement {best_genetic_impr:.3f} = total {best_genetic_total:.3f}")
        print(f"Local:   baseline {local_baseline_or:.3f} × improvement {best_local_impr:.3f} = total {best_local_total:.3f}")
        
        print()
        if best_genetic_total > best_local_total:
            ratio = best_genetic_total / best_local_total
            print(f"✓ GENETIC performs better overall with optimal config ({ratio:.2f}× advantage)")
        elif best_local_total > best_genetic_total:
            ratio = best_local_total / best_genetic_total
            print(f"✓ LOCAL performs better overall with optimal config ({ratio:.2f}× advantage)")
        else:
            print("≈ TIE - Both algorithms perform equally with optimal configs")
    
    print("\n" + "=" * 120)
    print("ANALYSIS COMPLETE")
    print("=" * 120)


def main():
    parser = argparse.ArgumentParser(
        description="Analyze which heuristics perform best for each algorithm",
        epilog="""
This script analyzes:
1. Main effects: Base performance of each heuristic (independent of algorithm)
2. Interactions: Which heuristics favor Genetic vs Local
3. Rankings: Best heuristics for each algorithm separately
4. Optimal combinations: Best ON/OFF configuration predictions

Example:
    python analyze_heuristic_performance.py --glm_dir "C:/Users/MerlijnU/analysis/anova"
    python analyze_heuristic_performance.py --glm_dir "C:/Users/MerlijnU/analysis/anova" --save_csv --top 10
        """
    )
    parser.add_argument("--glm_dir", required=True,
                       help="Directory containing aggregated GLM results")
    parser.add_argument("--top", type=int, default=5,
                       help="Number of top configurations to show (default: 5)")
    parser.add_argument("--save_csv", action="store_true",
                       help="Save rankings and best configs to CSV files")
    parser.add_argument("--large", type=float, default=1.20,
                       help="Threshold for 'large' effect (default: 1.20)")
    parser.add_argument("--small", type=float, default=1.05,
                       help="Threshold for 'small' effect (default: 1.05)")
    parser.add_argument("--negligible", type=float, default=0.95,
                       help="Lower threshold for 'negligible' effect (default: 0.95)")
    parser.add_argument("--slight_drop", type=float, default=0.80,
                       help="Threshold for 'slight drop' vs strong decrease (default: 0.80)")
    parser.add_argument("--limit_heuristics", type=int, default=16,
                       help="Maximum heuristics to evaluate (avoid combinatorial explosion, default: 16)")
    
    args = parser.parse_args()
    
    glm_dir = Path(args.glm_dir)
    if not glm_dir.exists():
        print(f"Error: Directory not found: {glm_dir}")
        return 1
    
    try:
        analyze_algorithm_specific_performance(glm_dir, args)
        return 0
    except Exception as e:
        print(f"Error: {e}")
        import traceback
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())
