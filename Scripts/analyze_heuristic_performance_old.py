#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Analyze Heuristic Performance by Algorithm

This script analyzes which heuristics perform best for Genetic vs Local algorithms
separately across repositories.

Usage:
    python analyze_heuristic_performance.py --glm_dir <path_to_aggregated_results>
"""

import pandas as pd
import numpy as np
from pathlib import Path
import argparse
import warnings
import sys

warnings.filterwarnings('ignore')

# Fix Windows console encoding for Unicode
if sys.platform == 'win32':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except:
        pass

def analyze_algorithm_specific_performance(glm_dir: Path):
    """Analyze heuristic performance separately for each algorithm."""
    
    print("=" * 120)
    print("ALGORITHM-SPECIFIC HEURISTIC PERFORMANCE ANALYSIS")
    print("=" * 120)
    
    # Load aggregated results
    or_file = list(glm_dir.glob("*coef_or_all_repos*.csv"))[0]
    or_df = pd.read_csv(or_file)
    
    print(f"\nLoaded: {or_file.name}")
    print(f"Repositories: {sorted(or_df['repository'].unique())}")
    print()
    
    # ====================================================================
    # PART 1: MAIN EFFECTS (Base effect of each heuristic)
    # ====================================================================
    print("=" * 120)
    print("PART 1: MAIN EFFECTS - Base Heuristic Performance (Independent of Algorithm)")
    print("=" * 120)
    print("These show whether a heuristic increases or decreases SSHOM success overall.")
    print()
    
    # Filter main effects (heuristics only, no algorithm)
    main_effects = or_df[
        or_df['term'].str.startswith('H_') & 
        ~or_df['term'].str.contains(':')
    ].copy()
    
    main_effects['heuristic'] = main_effects['term'].str.replace('H_', '')
    
    # Create summary by heuristic
    for heuristic in sorted(main_effects['heuristic'].unique()):
        heur_data = main_effects[main_effects['heuristic'] == heuristic]
        
        print(f"\n{heuristic}")
        print("-" * 120)
        print(f"{'Repository':<30} {'OR':<10} {'95% CI':<25} {'Effect':<40}")
        print("-" * 120)
        
        for _, row in heur_data.iterrows():
            repo = row['repository']
            or_val = row['OR']
            ci_low = row['OR_CI_low'] if pd.notna(row['OR_CI_low']) else np.nan
            ci_high = row['OR_CI_high'] if pd.notna(row['OR_CI_high']) else np.nan
            
            or_str = f"{or_val:.3f}" if pd.notna(or_val) else "—"
            
            if pd.notna(ci_low) and pd.notna(ci_high):
                ci_str = f"({ci_low:.3f}–{ci_high:.3f})"
            else:
                ci_str = "—"
            
            # Interpret effect
            if pd.notna(or_val):
                if or_val > 1.2:
                    effect = "✓ Increases SSHOM success (helpful)"
                elif or_val > 1.05:
                    effect = "→ Slight increase"
                elif or_val > 0.95:
                    effect = "≈ Minimal effect"
                elif or_val > 0.8:
                    effect = "→ Slight decrease"
                else:
                    effect = "✗ Decreases SSHOM success (filters aggressively)"
            else:
                effect = "?"
            
            print(f"{repo:<30} {or_str:<10} {ci_str:<25} {effect:<40}")
    
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
    
    # Filter interaction effects
    interactions = or_df[
        or_df['term'].str.contains('algorithm') & 
        or_df['term'].str.contains(':')
    ].copy()
    
    interactions['heuristic'] = interactions['term'].str.replace('algorithm\\[T\\.genetic\\]:', '', regex=True).str.replace('H_', '')
    
    # Create summary by heuristic
    for heuristic in sorted(interactions['heuristic'].unique()):
        heur_data = interactions[interactions['heuristic'] == heuristic]
        
        print(f"\n{heuristic}")
        print("-" * 120)
        print(f"{'Repository':<30} {'OR':<10} {'95% CI':<25} {'Favors':<40}")
        print("-" * 120)
        
        for _, row in heur_data.iterrows():
            repo = row['repository']
            or_val = row['OR']
            ci_low = row['OR_CI_low'] if pd.notna(row['OR_CI_low']) else np.nan
            ci_high = row['OR_CI_high'] if pd.notna(row['OR_CI_high']) else np.nan
            
            or_str = f"{or_val:.3f}" if pd.notna(or_val) else "—"
            
            if pd.notna(ci_low) and pd.notna(ci_high):
                if ci_high > 100:  # Separation issue
                    ci_str = f"({ci_low:.3f}–inf) [separation]"
                else:
                    ci_str = f"({ci_low:.3f}–{ci_high:.3f})"
            else:
                ci_str = "—"
            
            # Interpret effect
            if pd.notna(or_val):
                if or_val > 2.0:
                    effect = "★★★ GENETIC (strong advantage)"
                elif or_val > 1.5:
                    effect = "★★ GENETIC (moderate advantage)"
                elif or_val > 1.2:
                    effect = "★ GENETIC (slight advantage)"
                elif or_val > 0.95:
                    effect = "≈ Neither (similar benefit)"
                elif or_val > 0.67:
                    effect = "★ LOCAL (slight advantage)"
                elif or_val > 0.5:
                    effect = "★★ LOCAL (moderate advantage)"
                else:
                    effect = "★★★ LOCAL (strong advantage)"
            else:
                effect = "?"
            
            print(f"{repo:<30} {or_str:<10} {ci_str:<25} {effect:<40}")
    
    # ====================================================================
    # PART 3: RANKINGS
    # ====================================================================
    print("\n\n")
    print("=" * 120)
    print("PART 3: HEURISTIC RANKINGS BY ALGORITHM")
    print("=" * 120)
    
    # For each repository, calculate algorithm-specific effects
    repos = sorted(or_df['repository'].unique())
    
    for repo in repos:
        print(f"\n{'='*120}")
        print(f"REPOSITORY: {repo}")
        print(f"{'='*120}")
        
        # Get main effects and interactions for this repo
        repo_main = main_effects[main_effects['repository'] == repo].copy()
        repo_int = interactions[interactions['repository'] == repo].copy()
        
        # Calculate algorithm-specific effects
        # For Genetic: main_effect + interaction
        # For Local: main_effect only (reference level)
        
        results = []
        
        for heuristic in sorted(repo_main['heuristic'].unique()):
            main_row = repo_main[repo_main['heuristic'] == heuristic]
            int_row = repo_int[repo_int['heuristic'] == heuristic]
            
            if main_row.empty:
                continue
            
            main_or = main_row['OR'].iloc[0]
            
            # Local effect = main effect only
            local_or = main_or
            
            # Genetic effect = main effect * interaction
            if not int_row.empty:
                int_or = int_row['OR'].iloc[0]
                genetic_or = main_or * int_or if pd.notna(main_or) and pd.notna(int_or) else np.nan
            else:
                genetic_or = main_or
            
            results.append({
                'heuristic': heuristic,
                'local_or': local_or,
                'genetic_or': genetic_or,
                'interaction_or': int_row['OR'].iloc[0] if not int_row.empty else 1.0
            })
        
        results_df = pd.DataFrame(results)
        
        # Sort by genetic performance
        print(f"\nBEST HEURISTICS FOR GENETIC ALGORITHM:")
        print("-" * 80)
        genetic_sorted = results_df.sort_values('genetic_or', ascending=False)
        for i, row in enumerate(genetic_sorted.iterrows(), 1):
            _, r = row
            effect = "↑ Increases" if r['genetic_or'] > 1.0 else "↓ Decreases"
            print(f"{i}. {r['heuristic']:<30} OR={r['genetic_or']:.3f}  {effect} SSHOM success for Genetic")
        
        # Sort by local performance
        print(f"\nBEST HEURISTICS FOR LOCAL ALGORITHM:")
        print("-" * 80)
        local_sorted = results_df.sort_values('local_or', ascending=False)
        for i, row in enumerate(local_sorted.iterrows(), 1):
            _, r = row
            effect = "↑ Increases" if r['local_or'] > 1.0 else "↓ Decreases"
            print(f"{i}. {r['heuristic']:<30} OR={r['local_or']:.3f}  {effect} SSHOM success for Local")
        
        # Show biggest differences
        results_df['genetic_advantage'] = results_df['genetic_or'] / results_df['local_or']
        print(f"\nHEURISTICS WITH BIGGEST ALGORITHM DIFFERENCES:")
        print("-" * 80)
        print("(Shows which heuristics create the largest performance gap between algorithms)")
        diff_sorted = results_df.sort_values('genetic_advantage', ascending=False)
        for i, row in enumerate(diff_sorted.iterrows(), 1):
            _, r = row
            if r['genetic_advantage'] > 1.0:
                advantage = f"Genetic gains {(r['genetic_advantage']-1)*100:.1f}% advantage"
            else:
                advantage = f"Local gains {(1/r['genetic_advantage']-1)*100:.1f}% advantage"
            print(f"{i}. {r['heuristic']:<30} Ratio={r['genetic_advantage']:.3f}  {advantage}")
    
    # ====================================================================
    # PART 4: OPTIMAL COMBINATIONS
    # ====================================================================
    print("\n\n")
    print("=" * 120)
    print("PART 4: OPTIMAL HEURISTIC COMBINATIONS")
    print("=" * 120)
    print("Finds the best ON/OFF configuration for each algorithm based on GLM predictions.")
    print()
    print("NOTE: Each algorithm's predictions are relative to its OWN baseline (all heuristics OFF):")
    print("  - Genetic baseline: genetic algorithm with all heuristics OFF")
    print("  - Local baseline: local algorithm with all heuristics OFF")
    print("This shows how much heuristics improve EACH algorithm independently.")
    print()
    
    for repo in repos:
        print(f"\n{'='*120}")
        print(f"REPOSITORY: {repo}")
        print(f"{'='*120}")
        
        # Get main effects and interactions for this repo
        repo_main = main_effects[main_effects['repository'] == repo].copy()
        repo_int = interactions[interactions['repository'] == repo].copy()
        
        # Get base algorithm effect
        algo_effect = or_df[
            (or_df['repository'] == repo) & 
            (or_df['term'] == 'algorithm[T.genetic]')
        ]
        
        base_genetic_or = algo_effect['OR'].iloc[0] if not algo_effect.empty else np.nan
        base_local_or = 1.0  # Local is reference level
        
        heuristics = sorted(repo_main['heuristic'].unique())
        n_heuristics = len(heuristics)
        
        print(f"\nEvaluating {2**n_heuristics} possible configurations ({n_heuristics} heuristics)...")
        print()
        
        # Generate all possible combinations (ON/OFF for each heuristic)
        from itertools import product
        
        best_genetic_config = None
        best_genetic_or = 0
        best_local_config = None
        best_local_or = 0
        
        all_configs = []
        
        for config in product([0, 1], repeat=n_heuristics):
            config_dict = dict(zip(heuristics, config))
            
            # Calculate predicted OR for Genetic
            genetic_or = base_genetic_or if pd.notna(base_genetic_or) else 1.0
            for heuristic, is_on in config_dict.items():
                if is_on:
                    # Add main effect
                    main_row = repo_main[repo_main['heuristic'] == heuristic]
                    if not main_row.empty:
                        main_or = main_row['OR'].iloc[0]
                        if pd.notna(main_or):
                            genetic_or *= main_or
                    
                    # Add interaction effect
                    int_row = repo_int[repo_int['heuristic'] == heuristic]
                    if not int_row.empty:
                        int_or = int_row['OR'].iloc[0]
                        if pd.notna(int_or):
                            genetic_or *= int_or
            
            # Calculate predicted OR for Local (reference level, no interactions)
            local_or = base_local_or
            for heuristic, is_on in config_dict.items():
                if is_on:
                    main_row = repo_main[repo_main['heuristic'] == heuristic]
                    if not main_row.empty:
                        main_or = main_row['OR'].iloc[0]
                        if pd.notna(main_or):
                            local_or *= main_or
            
            all_configs.append({
                'config': config_dict,
                'genetic_or': genetic_or,
                'local_or': local_or,
                'n_heuristics_on': sum(config)
            })
            
            if genetic_or > best_genetic_or:
                best_genetic_or = genetic_or
                best_genetic_config = config_dict.copy()
            
            if local_or > best_local_or:
                best_local_or = local_or
                best_local_config = config_dict.copy()
        
        # Display optimal configurations
        print(f"OPTIMAL CONFIGURATION FOR GENETIC ALGORITHM:")
        print("-" * 120)
        print(f"Predicted relative success odds: {best_genetic_or:.3f}x baseline (genetic, all OFF)")
        print(f"Heuristics enabled ({sum(best_genetic_config.values())}/{len(best_genetic_config)}):")
        for heuristic in sorted(best_genetic_config.keys()):
            status = "✓ ON " if best_genetic_config[heuristic] else "✗ OFF"
            print(f"  {status}  {heuristic}")
        
        print()
        print(f"OPTIMAL CONFIGURATION FOR LOCAL ALGORITHM:")
        print("-" * 120)
        print(f"Predicted relative success odds: {best_local_or:.3f}x baseline (local, all OFF)")
        print(f"Heuristics enabled ({sum(best_local_config.values())}/{len(best_local_config)}):")
        for heuristic in sorted(best_local_config.keys()):
            status = "✓ ON " if best_local_config[heuristic] else "✗ OFF"
            print(f"  {status}  {heuristic}")
        
        # Show top 5 configurations for each algorithm
        print()
        print(f"TOP 5 CONFIGURATIONS FOR GENETIC:")
        print("-" * 120)
        configs_df = pd.DataFrame(all_configs)
        top_genetic = configs_df.nlargest(5, 'genetic_or')
        for i, (idx, row) in enumerate(top_genetic.iterrows(), 1):
            config_str = ", ".join([h for h, v in row['config'].items() if v])
            if not config_str:
                config_str = "(none)"
            print(f"{i}. OR={row['genetic_or']:.3f}  ({row['n_heuristics_on']} ON)  {config_str}")
        
        print()
        print(f"TOP 5 CONFIGURATIONS FOR LOCAL:")
        print("-" * 120)
        top_local = configs_df.nlargest(5, 'local_or')
        for i, (idx, row) in enumerate(top_local.iterrows(), 1):
            config_str = ", ".join([h for h, v in row['config'].items() if v])
            if not config_str:
                config_str = "(none)"
            print(f"{i}. OR={row['local_or']:.3f}  ({row['n_heuristics_on']} ON)  {config_str}")
        
        # Compare optimal configurations
        print()
        print(f"COMPARATIVE ANALYSIS:")
        print("-" * 120)
        
        # Check if configurations differ
        config_diff = {h: (best_genetic_config[h], best_local_config[h]) 
                      for h in heuristics if best_genetic_config[h] != best_local_config[h]}
        
        if config_diff:
            print(f"Configurations DIFFER on {len(config_diff)} heuristics:")
            for heuristic, (genetic_setting, local_setting) in config_diff.items():
                g_str = "ON" if genetic_setting else "OFF"
                l_str = "ON" if local_setting else "OFF"
                print(f"  • {heuristic}: Genetic wants {g_str}, Local wants {l_str}")
        else:
            print("Configurations are IDENTICAL - same optimal settings for both algorithms!")
        
        # Compare absolute performance with optimal configs
        print()
        print("ALGORITHM COMPARISON:")
        print("-" * 120)
        
        # Get baseline ORs (algorithm effect from GLM)
        genetic_baseline = base_genetic_or if pd.notna(base_genetic_or) else 1.0
        local_baseline = 1.0
        
        # Calculate total predicted effect (baseline × improvement from heuristics)
        genetic_total_effect = genetic_baseline * best_genetic_or
        local_total_effect = local_baseline * best_local_or
        
        print(f"Genetic: baseline {genetic_baseline:.3f} × improvement {best_genetic_or:.3f} = total effect {genetic_total_effect:.3f}")
        print(f"Local:   baseline {local_baseline:.3f} × improvement {best_local_or:.3f} = total effect {local_total_effect:.3f}")
        
        print()
        if genetic_total_effect > local_total_effect:
            ratio = genetic_total_effect / local_total_effect
            print(f"✓ GENETIC performs better overall with optimal config ({ratio:.2f}x advantage)")
        elif local_total_effect > genetic_total_effect:
            ratio = local_total_effect / genetic_total_effect
            print(f"✓ LOCAL performs better overall with optimal config ({ratio:.2f}x advantage)")
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

Example:
    python analyze_heuristic_performance.py --glm_dir "C:/Users/MerlijnU/analysis/anova"
        """
    )
    parser.add_argument("--glm_dir", required=True,
                       help="Directory containing aggregated GLM results")
    
    args = parser.parse_args()
    
    glm_dir = Path(args.glm_dir)
    if not glm_dir.exists():
        print(f"Error: Directory not found: {glm_dir}")
        return
    
    analyze_algorithm_specific_performance(glm_dir)


if __name__ == "__main__":
    main()
