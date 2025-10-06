#!/usr/bin/env python3
"""
GLM Results Display Script - Aggregated Multi-Repository Version
Formats aggregated GLM binomial ANOVA results across multiple repositories.

This script creates publication-ready tables and visualizations for meta-analysis
of GLM results across multiple repositories. Unlike display_glm_results.py which
handles single-repository data, this script properly handles the repository dimension.

Usage:
    python display_glm_results_aggregated.py --glm_dir <path_to_aggregated_results>
"""

import pandas as pd
import numpy as np
from pathlib import Path
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle
import argparse
import warnings
import re

warnings.filterwarnings('ignore')

# Configure matplotlib for publication quality
plt.style.use('default')
plt.rcParams['figure.facecolor'] = 'white'
plt.rcParams['axes.facecolor'] = 'white'
plt.rcParams['font.size'] = 10

# ===== SIGNIFICANCE HELPERS =====
def sig_symbol(v):
    """Convert significance value to symbol."""
    if pd.isna(v):
        return '—'
    return '***' if v < 1e-3 else '**' if v < 1e-2 else '*' if v < 5e-2 else '†' if v < 1e-1 else 'ns'

def sig_level(v):
    """Convert significance value to numeric level for heatmap colors."""
    if pd.isna(v):
        return 0
    return 4 if v < 1e-3 else 3 if v < 1e-2 else 2 if v < 5e-2 else 1 if v < 1e-1 else 0

def collapse_alg_term(term: str) -> str:
    """Collapse algorithm term variants to canonical form for merging OR with LR."""
    t = re.sub(r'algorithm\[T\.[^\]]+\]', 'algorithm', term)
    t = re.sub(r'algorithm\[T\.[^\]]+\]:(H_[A-Za-z0-9_]+)', r'algorithm:\1', t)
    return t

def clean_term_name(term: str) -> str:
    """Clean term names for display.
    
    Examples:
        algorithm[T.genetic] -> Algorithm: Genetic vs Local
        H_CodeLocation -> CodeLocation
        algorithm[T.genetic]:H_CodeLocation -> Algorithm×CodeLocation
    """
    if ':' in term:
        # Interaction term - split, clean each part, rejoin with ×
        parts = term.split(':')
        cleaned_parts = []
        for part in parts:
            if 'algorithm[T.genetic]' in part:
                cleaned_parts.append('Algorithm')
            else:
                cleaned_parts.append(part.replace('H_', ''))
        return '×'.join(cleaned_parts)
    else:
        # Main effect
        if 'algorithm[T.genetic]' in term:
            return 'Algorithm: Genetic vs Local'
        else:
            return term.replace('H_', '')

class AggregatedGLMDisplay:
    def __init__(self, glm_dir):
        """Initialize with aggregated GLM results directory."""
        self.glm_dir = Path(glm_dir)
        self.output_dir = self.glm_dir.parent / "glm_display_aggregated"
        self.output_dir.mkdir(exist_ok=True)
        
        print(f"Aggregated GLM Results Directory: {self.glm_dir}")
        print(f"Output Directory: {self.output_dir}")
    
    def load_aggregated_results(self):
        """Load aggregated GLM results files."""
        lr_files = list(self.glm_dir.glob("*lr_tests_all_repos*.csv"))
        or_files = list(self.glm_dir.glob("*coef_or_all_repos*.csv"))
        
        if not lr_files:
            print(f"No aggregated LR test files found in {self.glm_dir}")
            return None, None, None
            
        if not or_files:
            print(f"No aggregated odds ratio files found in {self.glm_dir}")
            return None, None, None
        
        lr_file = sorted(lr_files, key=lambda x: x.stat().st_mtime, reverse=True)[0]
        or_file = sorted(or_files, key=lambda x: x.stat().st_mtime, reverse=True)[0]
        
        print(f"Loading aggregated LR tests from: {lr_file.name}")
        print(f"Loading aggregated OR data from: {or_file.name}")
        
        lr_df = pd.read_csv(lr_file)
        or_df = pd.read_csv(or_file)
        
        # Determine significance source (prefer FDR q-values)
        sig_source = 'q_value' if ('q_value' in lr_df.columns and lr_df['q_value'].notna().any()) else 'p_value'
        
        print(f"Loaded {len(lr_df)} LR test rows across repositories")
        print(f"Loaded {len(or_df)} OR rows across repositories")
        print(f"Repositories: {sorted(lr_df['repository'].unique())}")
        print(f"Significance source: {sig_source}")
        
        return lr_df, or_df, sig_source
    
    def create_repository_comparison_table(self, lr_df, or_df, sig_source):
        """Create a table comparing results across repositories."""
        print("Creating repository comparison table...")
        
        # Normalize term names for joining
        lr_df = lr_df.copy()
        or_df = or_df.copy()
        lr_df['term_norm'] = lr_df['term'].apply(collapse_alg_term).str.lower()
        or_df['term_norm'] = or_df['term'].apply(collapse_alg_term).str.lower()
        
        # Get unique repositories
        repos = sorted(lr_df['repository'].unique())
        
        results_table = []
        results_table.append("=" * 150)
        results_table.append("GLM BINOMIAL ANOVA - AGGREGATED RESULTS ACROSS REPOSITORIES")
        results_table.append("=" * 150)
        results_table.append(f"Repositories analyzed: {len(repos)}")
        results_table.append(f"Repositories: {', '.join(repos)}")
        sig_label = 'FDR q-values' if sig_source == 'q_value' else 'p-values'
        results_table.append(f"Significance metric: {sig_label}")
        results_table.append("")
        
        # Process each term across repositories
        terms = sorted(lr_df['term'].unique())
        
        for term in terms:
            if 'MODEL_' in term:
                continue  # Skip model-level stats
            
            # Get data for this term across all repositories
            term_lr = lr_df[lr_df['term'] == term].copy()
            
            # Clean term name for display
            display_term = clean_term_name(term)
            
            results_table.append("-" * 150)
            results_table.append(f"TERM: {display_term}")
            results_table.append("-" * 150)
            sig_col_name = 'q-value' if sig_source == 'q_value' else 'p-value'
            results_table.append(f"{'Repository':<30} {sig_col_name:<12} {'Sig':<6} {'LR Chi²':<12} {'DF':<6} {'OR':<10} {'95% CI':<20}")
            results_table.append("-" * 150)
            
            for _, row in term_lr.iterrows():
                repo = row['repository']
                term_norm = row['term_norm']
                
                # Get OR data for this term and repository
                or_match = or_df[(or_df['term_norm'] == term_norm) & (or_df['repository'] == repo)]
                
                # Extract significance values
                sig_val = row[sig_source] if pd.notna(row[sig_source]) else np.nan
                sig_text = sig_symbol(sig_val)
                sig_formatted = f"{sig_val:.4f}" if pd.notna(sig_val) else "—"
                lr_chi2 = f"{row['lr_chi2']:.2f}" if pd.notna(row['lr_chi2']) else "—"
                df_val = f"{int(row['df'])}" if pd.notna(row['df']) else "—"
                
                # Extract OR and CI values
                if not or_match.empty:
                    or_val = or_match['OR'].iloc[0]
                    or_formatted = f"{or_val:.3f}" if pd.notna(or_val) else "—"
                    
                    ci_low = or_match['OR_CI_low'].iloc[0] if 'OR_CI_low' in or_match.columns else np.nan
                    ci_high = or_match['OR_CI_high'].iloc[0] if 'OR_CI_high' in or_match.columns else np.nan
                    
                    if pd.notna(ci_low) and pd.notna(ci_high):
                        ci_formatted = f"({ci_low:.3f}–{ci_high:.3f})"
                    else:
                        ci_formatted = "—"
                else:
                    or_formatted = "—"
                    ci_formatted = "—"
                
                results_table.append(f"{repo:<30} {sig_formatted:<12} {sig_text:<6} {lr_chi2:<12} {df_val:<6} {or_formatted:<10} {ci_formatted:<20}")
            
            results_table.append("")
        
        # Add interpretation
        sig_label = 'q' if sig_source == 'q_value' else 'p'
        results_table.extend([
            "",
            "INTERPRETATION GUIDE:",
            "-" * 150,
            f"Significance: *** {sig_label}<0.001, ** {sig_label}<0.01, * {sig_label}<0.05, † {sig_label}<0.10, ns = not significant",
            "",
            "NOTE: This table shows variability in statistical significance and effect sizes across repositories.",
            "      Terms significant in multiple repositories suggest robust effects.",
            "      Terms with inconsistent significance may indicate context-dependent effects.",
            "      OR > 1.0: Higher odds of SSHOM success; OR < 1.0: Lower odds of success",
            "=" * 150
        ])
        
        # Save to file
        results_text = "\n".join(results_table)
        table_path = self.output_dir / "glm_aggregated_comparison_table.txt"
        with open(table_path, 'w') as f:
            f.write(results_text)
        
        print(results_text)
        print(f"\nSaved comparison table: {table_path}")
        
        return table_path
    
    def create_consistency_heatmap(self, lr_df, sig_source):
        """Create heatmap showing which effects are significant across repositories."""
        print("Creating consistency heatmap...")
        
        # Filter out model-level stats
        effects_df = lr_df[~lr_df['term'].str.contains('MODEL_', na=False)].copy()
        
        if effects_df.empty:
            print("No effects to plot")
            return None
        
        # Apply significance levels
        effects_df['sig_numeric'] = effects_df[sig_source].apply(sig_level)
        
        # Clean term names
        effects_df['display_term'] = effects_df['term'].apply(clean_term_name)
        
        # Separate main effects and interactions
        main_effects = effects_df[~effects_df['term'].str.contains(':')].copy()
        interactions = effects_df[effects_df['term'].str.contains(':')].copy()
        
        # Create pivot tables for heatmaps
        if not main_effects.empty:
            main_pivot = main_effects.pivot(index='display_term', columns='repository', values='sig_numeric')
        else:
            main_pivot = pd.DataFrame()
        
        if not interactions.empty:
            int_pivot = interactions.pivot(index='display_term', columns='repository', values='sig_numeric')
        else:
            int_pivot = pd.DataFrame()
        
        # Create figure
        n_plots = (1 if not main_pivot.empty else 0) + (1 if not int_pivot.empty else 0)
        if n_plots == 0:
            print("No data to plot")
            return None
        
        # Scale figure size by number of repos and terms
        n_repos = len(lr_df['repository'].unique())
        n_main_terms = len(main_pivot) if not main_pivot.empty else 0
        n_int_terms = len(int_pivot) if not int_pivot.empty else 0
        
        fig_width = max(12, n_repos * 2.5)
        fig_height_main = max(6, n_main_terms * 0.6) if n_main_terms > 0 else 0
        fig_height_int = max(6, n_int_terms * 0.6) if n_int_terms > 0 else 0
        fig_height = fig_height_main + fig_height_int + 2  # +2 for legend space
        
        fig, axes = plt.subplots(n_plots, 1, figsize=(fig_width, fig_height))
        if n_plots == 1:
            axes = [axes]
        
        # Custom colormap
        colors = ['#d3d3d3', '#ffffb3', '#febf99', '#f87274', '#b30000']
        cmap = plt.matplotlib.colors.ListedColormap(colors)
        
        sig_label = 'q' if sig_source == 'q_value' else 'p'
        plot_idx = 0
        
        # Plot main effects
        if not main_pivot.empty:
            ax = axes[plot_idx]
            im = ax.imshow(main_pivot.values, cmap=cmap, aspect='auto', vmin=0, vmax=4)
            
            # Add colorbar
            cbar = plt.colorbar(im, ax=ax)
            cbar.set_label('Significance Level', fontsize=10)
            cbar.set_ticks([0, 1, 2, 3, 4])
            cbar.set_ticklabels(['ns', '†', '*', '**', '***'])
            
            # Add text annotations with safe extraction
            for i, term in enumerate(main_pivot.index):
                for j, repo in enumerate(main_pivot.columns):
                    # Safe extraction of significance value
                    matches = effects_df[
                        (effects_df['display_term'] == term) & 
                        (effects_df['repository'] == repo)
                    ]
                    if not matches.empty and sig_source in matches.columns:
                        sig_val = matches[sig_source].iloc[0]
                        sig_text = sig_symbol(sig_val)
                    else:
                        sig_text = '—'
                    ax.text(j, i, sig_text, ha='center', va='center', fontweight='bold', fontsize=9)
            
            ax.set_xticks(range(len(main_pivot.columns)))
            ax.set_xticklabels(main_pivot.columns, rotation=45, ha='right', fontsize=10)
            ax.set_yticks(range(len(main_pivot.index)))
            # Truncate long term names
            y_labels = [term[:35] + '...' if len(term) > 35 else term for term in main_pivot.index]
            ax.set_yticklabels(y_labels, fontsize=10)
            ax.set_title(f'Main Effects — Significance Across Repositories ({sig_label})', 
                        fontweight='bold', fontsize=13)
            
            plot_idx += 1
        
        # Plot interactions
        if not int_pivot.empty:
            ax = axes[plot_idx]
            im = ax.imshow(int_pivot.values, cmap=cmap, aspect='auto', vmin=0, vmax=4)
            
            # Add colorbar
            cbar = plt.colorbar(im, ax=ax)
            cbar.set_label('Significance Level', fontsize=10)
            cbar.set_ticks([0, 1, 2, 3, 4])
            cbar.set_ticklabels(['ns', '†', '*', '**', '***'])
            
            # Add text annotations with safe extraction
            for i, term in enumerate(int_pivot.index):
                for j, repo in enumerate(int_pivot.columns):
                    # Safe extraction of significance value
                    matches = effects_df[
                        (effects_df['display_term'] == term) & 
                        (effects_df['repository'] == repo)
                    ]
                    if not matches.empty and sig_source in matches.columns:
                        sig_val = matches[sig_source].iloc[0]
                        sig_text = sig_symbol(sig_val)
                    else:
                        sig_text = '—'
                    ax.text(j, i, sig_text, ha='center', va='center', fontweight='bold', fontsize=9)
            
            ax.set_xticks(range(len(int_pivot.columns)))
            ax.set_xticklabels(int_pivot.columns, rotation=45, ha='right', fontsize=10)
            ax.set_yticks(range(len(int_pivot.index)))
            # Truncate long term names
            y_labels = [term[:35] + '...' if len(term) > 35 else term for term in int_pivot.index]
            ax.set_yticklabels(y_labels, fontsize=10)
            ax.set_title(f'Interaction Effects — Significance Across Repositories ({sig_label})', 
                        fontweight='bold', fontsize=13)
        
        # Add legend
        legend_elements = [
            Rectangle((0,0),1,1, facecolor='#d3d3d3', label=f'ns ({sig_label} ≥ 0.10)'),
            Rectangle((0,0),1,1, facecolor='#ffffb3', label=f'† ({sig_label} < 0.10)'),
            Rectangle((0,0),1,1, facecolor='#febf99', label=f'* ({sig_label} < 0.05)'),
            Rectangle((0,0),1,1, facecolor='#f87274', label=f'** ({sig_label} < 0.01)'),
            Rectangle((0,0),1,1, facecolor='#b30000', label=f'*** ({sig_label} < 0.001)')
        ]
        
        fig.legend(handles=legend_elements, 
                  title='Significance Levels (cell text)',
                  loc='center', 
                  bbox_to_anchor=(0.5, 0.01),
                  ncol=5,
                  fontsize=10)
        
        plt.suptitle(f'GLM Effects Consistency Across Repositories — Significance ({sig_label})', 
                    fontsize=14, fontweight='bold', y=0.995)
        plt.tight_layout()
        plt.subplots_adjust(bottom=0.12)
        
        # Save plot
        plot_path = self.output_dir / "glm_aggregated_consistency_heatmap.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        plt.close()
        
        print(f"Saved consistency heatmap: {plot_path}")
        return plot_path
    
    def save_summary_csvs(self, lr_df, or_df, sig_source):
        """Save per-term, per-repo CSV summaries."""
        print("Saving summary CSVs...")
        
        # Normalize terms for joining
        lr_df = lr_df.copy()
        or_df = or_df.copy()
        lr_df['term_norm'] = lr_df['term'].apply(collapse_alg_term).str.lower()
        or_df['term_norm'] = or_df['term'].apply(collapse_alg_term).str.lower()
        
        # Filter out model-level stats
        lr_df = lr_df[~lr_df['term'].str.contains('MODEL_', na=False)]
        
        # Merge LR and OR data
        merged = lr_df.merge(or_df[['term_norm', 'repository', 'OR', 'OR_CI_low', 'OR_CI_high']], 
                            on=['term_norm', 'repository'], 
                            how='left')
        
        # Add display term
        merged['display_term'] = merged['term'].apply(clean_term_name)
        
        # Separate main effects and interactions
        main_effects = merged[~merged['term'].str.contains(':')].copy()
        interactions = merged[merged['term'].str.contains(':')].copy()
        
        # Select and rename columns
        output_cols = ['repository', 'term', 'display_term', 'OR', 'OR_CI_low', 'OR_CI_high', 
                      'p_value', 'q_value', 'lr_chi2', 'df']
        
        # Ensure all columns exist (add missing ones with NaN)
        for col in output_cols:
            if col not in main_effects.columns:
                main_effects[col] = np.nan
            if col not in interactions.columns:
                interactions[col] = np.nan
        
        # Save main effects
        if not main_effects.empty:
            main_path = self.output_dir / "main_effects_summary.csv"
            main_effects[output_cols].to_csv(main_path, index=False)
            print(f"Saved main effects summary: {main_path}")
        
        # Save interactions
        if not interactions.empty:
            int_path = self.output_dir / "interaction_effects_summary.csv"
            interactions[output_cols].to_csv(int_path, index=False)
            print(f"Saved interaction effects summary: {int_path}")
        
        return [main_path, int_path] if not main_effects.empty and not interactions.empty else []
    
    def run_aggregated_display(self, save_csv=False):
        """Run complete aggregated display pipeline."""
        print("=" * 60)
        print("AGGREGATED GLM ANALYSIS DISPLAY")
        print("=" * 60)
        
        # Load aggregated results
        lr_df, or_df, sig_source = self.load_aggregated_results()
        
        if lr_df is None or or_df is None:
            print("Could not load aggregated results files.")
            return
        
        # Generate displays
        generated_files = []
        
        # 1. Repository comparison table
        table_path = self.create_repository_comparison_table(lr_df, or_df, sig_source)
        generated_files.append(table_path)
        
        # 2. Consistency heatmap
        heatmap_path = self.create_consistency_heatmap(lr_df, sig_source)
        if heatmap_path:
            generated_files.append(heatmap_path)
        
        # 3. Optional CSV summaries
        if save_csv:
            csv_paths = self.save_summary_csvs(lr_df, or_df, sig_source)
            generated_files.extend(csv_paths)
        
        print("\n" + "=" * 60)
        print("AGGREGATED DISPLAY COMPLETE!")
        print("=" * 60)
        print(f"Generated files in: {self.output_dir}")
        print("\nFILES CREATED:")
        for file_path in generated_files:
            print(f"- {file_path.name}")
        
        sig_label = 'FDR q-values' if sig_source == 'q_value' else 'p-values'
        print(f"\nTHESIS INTEGRATION:")
        print(f"- Significance based on: {sig_label}")
        print("- Use consistency heatmap to show which effects replicate across repositories")
        print("- Use comparison table to report variability in effect sizes and significance")
        print("- Highlight effects that are consistently significant vs. context-dependent")
        if save_csv:
            print("- CSV summaries available for statistical meta-analysis")
        print("=" * 60)
        
        return generated_files


def main():
    """Main function with command line interface."""
    parser = argparse.ArgumentParser(
        description="Display aggregated GLM binomial ANOVA results across repositories",
        epilog="""
This script is designed for aggregated multi-repository GLM results.
For single-repository displays, use display_glm_results.py instead.

Expected input files:
    - glm_lr_tests_all_repos.csv
    - glm_coef_or_all_repos.csv

These are created by glm_binomial_anova.py when analyzing multiple solutions.

Example usage:
    python display_glm_results_aggregated.py --glm_dir "C:/Users/MerlijnU/analysis/anova"
    python display_glm_results_aggregated.py --glm_dir "C:/Users/MerlijnU/analysis/anova" --save_csv
        """
    )
    parser.add_argument("--glm_dir", required=True, 
                       help="Directory containing aggregated GLM results CSV files")
    parser.add_argument("--save_csv", action="store_true",
                       help="Save per-term, per-repo CSV summaries (main_effects_summary.csv, interaction_effects_summary.csv)")
    
    args = parser.parse_args()
    
    # Initialize display
    display = AggregatedGLMDisplay(args.glm_dir)
    
    # Run aggregated display
    display.run_aggregated_display(save_csv=args.save_csv)


if __name__ == "__main__":
    main()
