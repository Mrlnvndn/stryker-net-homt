#!/usr/bin/env python3
"""
GLM Results Display Script
Formats GLM binomial ANOVA results for thesis presentation alongside L12 orthogonal array plots.

This script creates publication-ready tables and visualizations of GLM results that complement
the main effects (Plot 1) and interaction effects (Plot 2) from analyze_homcsv_enhanced.py.

IMPORTANT - Algorithm Naming Convention:
- glm_binomial_anova.py normalizes algorithms to 'local' and 'genetic' (lowercase)
- 'local' is set as the reference level in the GLM
- Therefore, the coefficient in GLM output is 'algorithm[T.genetic]'
- This represents "Genetic vs Local" (Genetic compared to Local baseline)

Usage:
    python display_glm_results.py --glm_dir <path_to_glm_results> --solution <solution_name>
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
    """Collapse algorithm term variants to canonical form for merging OR with LR.
    
    Examples:
        algorithm[T.genetic] -> algorithm
        algorithm[T.genetic]:H_CodeLocation -> algorithm:H_CodeLocation
    """
    # Algorithm main effect
    t = re.sub(r'algorithm\[T\.[^\]]+\]', 'algorithm', term)
    # Algorithm interactions
    t = re.sub(r'algorithm\[T\.[^\]]+\]:(H_[A-Za-z0-9_]+)', r'algorithm:\1', t)
    return t

def color_for_sig(v):
    """Return color based on significance level for forest plots."""
    lvl = sig_level(v)
    # ns/† (0-1) -> blue-ish; * ** *** (2-4) -> red-ish
    return '#1f77b4' if lvl <= 1 else '#d62728'

class GLMResultsDisplay:
    def __init__(self, glm_dir, solution_name=None):
        """Initialize with GLM results directory."""
        self.glm_dir = Path(glm_dir)
        self.solution_name = solution_name or "Unknown"
        
        # Define output directory 
        self.output_dir = self.glm_dir.parent / "glm_display"
        self.output_dir.mkdir(exist_ok=True)
        
        print(f"GLM Results Directory: {self.glm_dir}")
        print(f"Output Directory: {self.output_dir}")
    
    def load_glm_results(self, lr_file_override=None, or_file_override=None):
        """Load GLM results files with optional overrides and smart file selection."""
        # Use overrides if provided
        if lr_file_override:
            lr_files = [Path(lr_file_override)]
        else:
            lr_files = list(self.glm_dir.glob("*lr_tests*.csv"))
            
        if or_file_override:
            or_files = [Path(or_file_override)]
        else:
            or_files = list(self.glm_dir.glob("*coef_or*.csv"))
        
        if not lr_files:
            print(f"No LR test files found in {self.glm_dir}")
            return None, None, None, False, None
            
        if not or_files:
            print(f"No odds ratio files found in {self.glm_dir}")
            return None, None, None, False, None
        
        # Smart file selection: prefer most recent if multiple matches
        lr_file = sorted(lr_files, key=lambda x: x.stat().st_mtime, reverse=True)[0]
        or_file = sorted(or_files, key=lambda x: x.stat().st_mtime, reverse=True)[0]
        
        # Try to load model summary for McFadden's R²
        summary_files = list(self.glm_dir.glob("*model_summary*.csv"))
        summary_df = None
        if summary_files:
            summary_file = sorted(summary_files, key=lambda x: x.stat().st_mtime, reverse=True)[0]
            summary_df = pd.read_csv(summary_file)
            print(f"Loading model summary from: {summary_file.name}")
        
        print(f"Loading LR tests from: {lr_file.name}")
        print(f"Loading OR data from: {or_file.name}")
        
        # Load the files
        lr_df = pd.read_csv(lr_file)
        or_df = pd.read_csv(or_file)
        
        # Detect penalized fit
        penal_col = 'penalized_fit' if 'penalized_fit' in or_df.columns else None
        is_penalized = (penal_col is not None and or_df[penal_col].any()) or \
                       (not set(['OR_CI_low','OR_CI_high']).issubset(set(or_df.columns)))
        
        # Determine significance source (prefer FDR q-values)
        sig_source = 'q_value' if ('q_value' in lr_df.columns and lr_df['q_value'].notna().any()) else 'p_value'
        
        print(f"Loaded LR tests: {len(lr_df)} terms")
        print(f"Loaded OR data: {len(or_df)} coefficients")
        print(f"Significance source: {sig_source}")
        print(f"Penalized fit detected: {is_penalized}")
        
        return lr_df, or_df, sig_source, is_penalized, summary_df
    
    def create_significance_heatmap(self, lr_df, sig_source, is_penalized):
        """Create a heatmap showing statistical significance of effects."""
        print("Creating significance heatmap...")
        
        # Filter out the McFadden R² row and model-level statistics
        effects_df = lr_df[~lr_df['term'].str.contains('MODEL_', na=False)].copy()
        
        if effects_df.empty:
            print("No effects to plot (all data filtered or penalized fit without LR tests)")
            return None
        
        # Apply unified significance functions
        effects_df['significance'] = effects_df[sig_source].apply(sig_symbol)
        effects_df['sig_numeric'] = effects_df[sig_source].apply(sig_level)
        
        # Separate main effects and interactions
        main_effects = effects_df[~effects_df['term'].str.contains(':')].copy()
        interactions = effects_df[effects_df['term'].str.contains(':')].copy()
        
        # Create figure with two subplots
        fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(16, 8))
        
        # Plot 1: Main Effects Heatmap
        if not main_effects.empty:
            main_effects_plot = main_effects.copy()
            
            # Clean up term names for display - Algorithm: Genetic vs Local
            main_effects_plot['display_term'] = main_effects_plot['term'].str.replace('H_', '').str.replace('algorithm', 'Algorithm: Genetic vs Local')
            
            # Create matrix for heatmap
            heatmap_data = main_effects_plot.set_index('display_term')[['sig_numeric']]
            
            # Custom colormap
            colors = ['#d3d3d3', '#ffffb3', '#febf99', '#f87274', '#b30000']  # Gray to red
            n_colors = len(colors)
            cmap = plt.matplotlib.colors.ListedColormap(colors)
            
            # Create heatmap using matplotlib
            im1 = ax1.imshow(heatmap_data.T.values, cmap=cmap, aspect='auto', vmin=0, vmax=4)
            
            # Add text annotations
            for i, term in enumerate(heatmap_data.index):
                sig_text = main_effects_plot.set_index('display_term').loc[term, 'significance']
                ax1.text(i, 0, sig_text, ha='center', va='center', fontweight='bold')
            
            ax1.set_xticks(range(len(heatmap_data.index)))
            ax1.set_xticklabels(heatmap_data.index, rotation=45, ha='right')
            ax1.set_yticks([])
            
            # Add colorbar
            cbar1 = plt.colorbar(im1, ax=ax1)
            cbar1.set_label('Significance Level')
            
            sig_label = 'FDR q' if sig_source == 'q_value' else 'p'
            title_suffix = " [Penalized fit]" if is_penalized else ""
            ax1.set_title(f'Main Effects{title_suffix}', 
                         fontsize=12, fontweight='bold')
            ax1.set_xlabel('Terms')
            ax1.set_ylabel('')
        
        # Plot 2: Interaction Effects Heatmap  
        if not interactions.empty:
            interactions_plot = interactions.copy()
            
            # Clean up interaction terms - normalize algorithm labeling
            interactions_plot['display_term'] = (interactions_plot['term']
                                               .str.replace('algorithm:', 'Alg×')
                                               .str.replace('H_', ''))
            
            # Create matrix for heatmap
            int_heatmap_data = interactions_plot.set_index('display_term')[['sig_numeric']]
            
            # Create heatmap using matplotlib
            im2 = ax2.imshow(int_heatmap_data.T.values, cmap=cmap, aspect='auto', vmin=0, vmax=4)
            
            # Add text annotations
            for i, term in enumerate(int_heatmap_data.index):
                sig_text = interactions_plot.set_index('display_term').loc[term, 'significance']
                ax2.text(i, 0, sig_text, ha='center', va='center', fontweight='bold')
            
            ax2.set_xticks(range(len(int_heatmap_data.index)))
            ax2.set_xticklabels(int_heatmap_data.index, rotation=45, ha='right')
            ax2.set_yticks([])
            
            # Add colorbar
            cbar2 = plt.colorbar(im2, ax=ax2)
            cbar2.set_label('Significance Level')
            
            sig_label = 'FDR q' if sig_source == 'q_value' else 'p'
            title_suffix = " [Penalized fit]" if is_penalized else ""
            ax2.set_title(f'Interactions{title_suffix}', 
                         fontsize=12, fontweight='bold')
            ax2.set_xlabel('Terms') 
            ax2.set_ylabel('')
        
        # Add significance legend with correct source label and thresholds
        sig_label = 'FDR q' if sig_source == 'q_value' else 'p'
        legend_elements = [
            Rectangle((0,0),1,1, facecolor='#d3d3d3', label=f'ns ({sig_label} ≥ 0.10)'),
            Rectangle((0,0),1,1, facecolor='#ffffb3', label=f'† ({sig_label} < 0.10)'),
            Rectangle((0,0),1,1, facecolor='#febf99', label=f'* ({sig_label} < 0.05)'),
            Rectangle((0,0),1,1, facecolor='#f87274', label=f'** ({sig_label} < 0.01)'),
            Rectangle((0,0),1,1, facecolor='#b30000', label=f'*** ({sig_label} < 0.001)')
        ]
        
        fig.legend(handles=legend_elements, 
                  title='Significance Levels',
                  loc='center', 
                  bbox_to_anchor=(0.5, 0.02),
                  ncol=5,
                  fontsize=10)
        
        # Check if LR tests are unavailable
        no_lr_available = lr_df.empty or lr_df[sig_source].dropna().empty
        heatmap_suffix = " (LR tests unavailable)" if no_lr_available else ""
        plt.suptitle(f'GLM effects by term — Significance ({sig_label}){heatmap_suffix}', 
                    fontsize=13, fontweight='bold', y=0.95)
        plt.tight_layout()
        plt.subplots_adjust(bottom=0.15)
        
        # Save plot
        plot_path = self.output_dir / f"glm_significance_heatmap_{self.solution_name}.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        plt.close()
        
        print(f"Saved significance heatmap: {plot_path}")
        return plot_path
    
    def create_effect_sizes_table(self, or_df, lr_df, sig_source, is_penalized, summary_df=None):
        """Create a formatted table of effect sizes and significance."""
        print("Creating effect sizes table...")
        
        # Get McFadden's R² - prefer model summary, fallback to LR tests
        mcfadden_r2 = "Not available"
        if summary_df is not None and 'mcfadden_r2' in summary_df.columns:
            mcfadden_r2 = f"{float(summary_df['mcfadden_r2'].iloc[0]):.4f}"
        else:
            mcf = lr_df[lr_df['term'] == 'MODEL_MCFADDEN_R2']
            if not mcf.empty:
                mcfadden_r2 = f"{float(mcf['lr_chi2'].iloc[0]):.4f}"
        
        # Process odds ratios
        or_clean = or_df.copy()
        
        # Use collapse_alg_term for robust matching
        or_clean['lr_term'] = or_clean['term'].apply(collapse_alg_term)
        
        # Clean up coefficient names
        # For interactions: algorithm[T.genetic]:H_Term -> Algorithm×Term
        # For main effects: algorithm[T.genetic] -> Algorithm: Genetic vs Local
        def clean_term_name(term):
            if ':' in term:
                # Interaction term
                return term.replace(r'algorithm[T.genetic]:', 'Algorithm×').replace('H_', '')
            else:
                # Main effect
                return term.replace('algorithm[T.genetic]', 'Algorithm: Genetic vs Local').replace('H_', '')
        
        or_clean['display_term'] = or_clean['term'].apply(clean_term_name)
        
        # Merge with significance data (filter out model-level rows)
        lr_clean = lr_df[~lr_df['term'].str.contains('MODEL_', na=False)].copy()
        
        # Merge on collapsed term
        merge_cols = ['term', sig_source, 'lr_chi2'] if sig_source in lr_clean.columns else ['term']
        merged = or_clean.merge(lr_clean[merge_cols], 
                               left_on='lr_term', right_on='term', 
                               how='left', suffixes=('', '_lr'))
        
        # Fill missing values with placeholder
        if sig_source in merged.columns:
            merged[sig_source] = merged[sig_source].fillna(np.nan)
        
        # Format the results table
        results_table = []
        
        # Add header info
        results_table.append("GLM BINOMIAL ANOVA RESULTS")
        results_table.append("="*60)
        results_table.append(f"Solution: {self.solution_name}")
        results_table.append(f"Model Fit (McFadden's R²): {mcfadden_r2}")
        if is_penalized:
            results_table.append("NOTE: Penalized fit (ridge) - CIs/p-values limited or unavailable")
        sig_label = f"Significance: FDR q-values" if sig_source == 'q_value' else "Significance: p-values"
        results_table.append(sig_label)
        results_table.append("")
        
        # Main effects section
        main_effects = merged[~merged['term'].str.contains(':')].copy()
        if not main_effects.empty:
            results_table.append("MAIN EFFECTS:")
            results_table.append("-"*40)
            sig_col_name = 'q-value' if sig_source == 'q_value' else 'p-value'
            results_table.append(f"{'Effect':<25} {'OR':<8} {'95% CI':<15} {sig_col_name:<10} {'Sig':<5}")
            results_table.append("-"*63)
            
            for _, row in main_effects.iterrows():
                display_name = row['display_term']
                or_val = f"{row['OR']:.3f}" if pd.notna(row['OR']) else "—"
                
                # Handle missing CIs (penalized fits)
                if 'OR_CI_low' in row and 'OR_CI_high' in row and pd.notna(row['OR_CI_low']):
                    ci = f"({row['OR_CI_low']:.3f}-{row['OR_CI_high']:.3f})"
                else:
                    ci = "—"
                
                # Use selected significance source
                sig_val = row[sig_source] if sig_source in row and pd.notna(row[sig_source]) else np.nan
                sig_formatted = f"{sig_val:.4f}" if pd.notna(sig_val) else "—"
                
                # Use unified sig_symbol function
                sig = sig_symbol(sig_val)
                
                results_table.append(f"{display_name:<25} {or_val:<8} {ci:<15} {sig_formatted:<10} {sig:<5}")
        
        # Interaction effects section
        interactions = merged[merged['term'].str.contains(':')].copy()
        if not interactions.empty:
            results_table.append("")
            results_table.append("INTERACTION EFFECTS:")
            results_table.append("-"*40)
            sig_col_name = 'q-value' if sig_source == 'q_value' else 'p-value'
            results_table.append(f"{'Effect':<25} {'OR':<8} {'95% CI':<15} {sig_col_name:<10} {'Sig':<5}")
            results_table.append("-"*63)
            
            for _, row in interactions.iterrows():
                display_name = row['display_term']
                or_val = f"{row['OR']:.3f}" if pd.notna(row['OR']) else "—"
                
                # Handle missing CIs (penalized fits)
                if 'OR_CI_low' in row and 'OR_CI_high' in row and pd.notna(row['OR_CI_low']):
                    ci = f"({row['OR_CI_low']:.3f}-{row['OR_CI_high']:.3f})"
                else:
                    ci = "—"
                
                # Use selected significance source
                sig_val = row[sig_source] if sig_source in row and pd.notna(row[sig_source]) else np.nan
                sig_formatted = f"{sig_val:.4f}" if pd.notna(sig_val) else "—"
                
                # Use unified sig_symbol function
                sig = sig_symbol(sig_val)
                
                results_table.append(f"{display_name:<25} {or_val:<8} {ci:<15} {sig_formatted:<10} {sig:<5}")
        
        # Add interpretation notes
        sig_label = 'q' if sig_source == 'q_value' else 'p'
        results_table.extend([
            "",
            "INTERPRETATION:",
            "-"*40,
            "Algorithm: Genetic vs Local (OR>1 favors Genetic)",
            "OR > 1.0: Higher odds of SSHOM success when factor is ON vs OFF",
            "OR < 1.0: Lower odds of SSHOM success when factor is ON vs OFF", 
            "OR = 1.0: No effect of factor on SSHOM success",
            "",
            f"Significance: *** {sig_label}<0.001, ** {sig_label}<0.01, * {sig_label}<0.05, † {sig_label}<0.10, ns = not significant",
            "",
            "NOTE: GLM provides statistical foundation for experimental results",
            "="*60
        ])
        
        # Save to file
        results_text = "\n".join(results_table)
        
        table_path = self.output_dir / f"glm_results_table_{self.solution_name}.txt"
        with open(table_path, 'w') as f:
            f.write(results_text)
        
        print(results_text)
        print(f"\nSaved results table: {table_path}")
        
        return table_path, merged
    
    def create_odds_ratio_forest_plot(self, or_df, lr_df, sig_source, is_penalized):
        """Create a forest plot of odds ratios with confidence intervals."""
        print("Creating odds ratio forest plot...")
        
        # Merge OR data with significance using collapse_alg_term
        or_clean = or_df.copy()
        or_clean['lr_term'] = or_clean['term'].apply(collapse_alg_term)
        
        lr_clean = lr_df[~lr_df['term'].str.contains('MODEL_', na=False)].copy()
        
        merge_cols = ['term', sig_source] if sig_source in lr_clean.columns else ['term']
        merged = or_clean.merge(lr_clean[merge_cols], 
                               left_on='lr_term', right_on='term', 
                               how='left', suffixes=('', '_lr'))
        
        # Clean up display names
        # For interactions: algorithm[T.genetic]:H_Term -> Algorithm × Term
        # For main effects: algorithm[T.genetic] -> Algorithm: Genetic vs Local
        def clean_term_name(term):
            if ':' in term:
                # Interaction term
                return term.replace('algorithm[T.genetic]:', 'Algorithm × ').replace('H_', '')
            else:
                # Main effect
                return term.replace('algorithm[T.genetic]', 'Algorithm: Genetic vs Local').replace('H_', '')
        
        merged['display_term'] = merged['term'].apply(clean_term_name)
        
        # Separate main effects and interactions
        main_effects = merged[~merged['term'].str.contains(':')].copy()
        interactions = merged[merged['term'].str.contains(':')].copy()
        
        # Sort by significance (or magnitude if penalized)
        if sig_source in main_effects.columns and not is_penalized:
            main_effects = main_effects.sort_values(sig_source, ascending=True)
            interactions = interactions.sort_values(sig_source, ascending=True) if not interactions.empty else interactions
        else:
            # Sort by OR magnitude if penalized
            main_effects['log_or_abs'] = np.abs(np.log(main_effects['OR']))
            main_effects = main_effects.sort_values('log_or_abs', ascending=False)
            if not interactions.empty:
                interactions['log_or_abs'] = np.abs(np.log(interactions['OR']))
                interactions = interactions.sort_values('log_or_abs', ascending=False)
        
        # Create the forest plot
        fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(12, 10))
        
        # Plot 1: Main Effects
        if not main_effects.empty:
            y_pos = np.arange(len(main_effects))
            
            # Colors based on significance using unified policy
            colors_main = [color_for_sig(row.get(sig_source, np.nan)) for _, row in main_effects.iterrows()]
            
            # Check if CIs are available
            has_ci = 'OR_CI_low' in main_effects.columns and main_effects['OR_CI_low'].notna().any()
            
            if has_ci:
                # Plot confidence intervals (no marker on errorbar)
                ax1.errorbar(main_effects['OR'], y_pos,
                            xerr=[main_effects['OR'] - main_effects['OR_CI_low'],
                                  main_effects['OR_CI_high'] - main_effects['OR']],
                            fmt='none', ecolor='gray', capsize=5, elinewidth=2)
                # Single colored scatter for points
                ax1.scatter(main_effects['OR'], y_pos, c=colors_main, s=90, zorder=5)
            else:
                # Plot point estimates only (penalized fit) - diamond markers
                ax1.scatter(main_effects['OR'], y_pos, c=colors_main, s=100, marker='D', zorder=5, label='Point estimate (no CI)')
            
            # Add reference line at OR = 1
            ax1.axvline(1.0, color='gray', linestyle='--', linewidth=1, label='OR = 1 (No Effect)')
            
            # Log scale for x-axis
            ax1.set_xscale('log')
            
            ax1.set_yticks(y_pos)
            ax1.set_yticklabels(main_effects['display_term'])
            ax1.set_xlabel('Odds Ratio (95% CI)' if has_ci else 'Odds Ratio (point estimate)')
            
            subtitle = "(Algorithm: Genetic vs Local; OR>1 favors Genetic)"
            if is_penalized:
                subtitle += "\n[Penalized fit]"
            ax1.set_title(f'Main Effects - Odds of SSHOM Success\n{subtitle}', 
                         fontweight='bold')
            ax1.grid(True, alpha=0.3, which='both')
            ax1.legend()
            
            # Add significance annotations
            for i, (_, row) in enumerate(main_effects.iterrows()):
                sig_val = row[sig_source] if sig_source in row and pd.notna(row[sig_source]) else np.nan
                sig_text = sig_symbol(sig_val)
                
                x_pos = row['OR_CI_high'] if has_ci and pd.notna(row.get('OR_CI_high')) else row['OR']
                ax1.text(x_pos * 1.1, i, sig_text, 
                        va='center', fontweight='bold', fontsize=12)
        
        # Plot 2: Interaction Effects
        if not interactions.empty:
            y_pos2 = np.arange(len(interactions))
            
            # Colors based on significance using unified policy
            colors_int = [color_for_sig(row.get(sig_source, np.nan)) for _, row in interactions.iterrows()]
            
            # Check if CIs are available
            has_ci2 = 'OR_CI_low' in interactions.columns and interactions['OR_CI_low'].notna().any()
            
            if has_ci2:
                # Plot confidence intervals (no marker on errorbar)
                ax2.errorbar(interactions['OR'], y_pos2,
                            xerr=[interactions['OR'] - interactions['OR_CI_low'],
                                  interactions['OR_CI_high'] - interactions['OR']],
                            fmt='none', ecolor='gray', capsize=5, elinewidth=2)
                # Single colored scatter for points
                ax2.scatter(interactions['OR'], y_pos2, c=colors_int, s=90, zorder=5)
            else:
                # Plot point estimates only (penalized fit) - diamond markers
                ax2.scatter(interactions['OR'], y_pos2, c=colors_int, s=100, marker='D', zorder=5, label='Point estimate (no CI)')
            
            # Add reference line at OR = 1
            ax2.axvline(1.0, color='gray', linestyle='--', linewidth=1, label='OR = 1 (No Effect)')
            
            # Log scale for x-axis
            ax2.set_xscale('log')
            
            ax2.set_yticks(y_pos2)
            ax2.set_yticklabels(interactions['display_term'])
            ax2.set_xlabel('Odds Ratio (95% CI)' if has_ci2 else 'Odds Ratio (point estimate)')
            
            subtitle2 = "(Algorithm × Heuristic)"
            if is_penalized:
                subtitle2 += "\n[Penalized fit]"
            ax2.set_title(f'Interaction Effects\n{subtitle2}', 
                         fontweight='bold')
            ax2.grid(True, alpha=0.3, which='both')
            ax2.legend()
            
            # Add significance annotations
            for i, (_, row) in enumerate(interactions.iterrows()):
                sig_val = row[sig_source] if sig_source in row and pd.notna(row[sig_source]) else np.nan
                sig_text = sig_symbol(sig_val)
                
                x_pos = row['OR_CI_high'] if has_ci2 and pd.notna(row.get('OR_CI_high')) else row['OR']
                ax2.text(x_pos * 1.1, i, sig_text, 
                        va='center', fontweight='bold', fontsize=12)
        
        plt.suptitle(f'GLM Odds Ratios - {self.solution_name}', 
                    fontsize=14, fontweight='bold')
        plt.tight_layout()
        
        # Save plot
        plot_path = self.output_dir / f"glm_forest_plot_{self.solution_name}.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        plt.close()
        
        print(f"Saved forest plot: {plot_path}")
        return plot_path
    
    def run_complete_display(self, lr_file_override=None, or_file_override=None):
        """Run complete GLM results display pipeline."""
        print(f"Displaying GLM results for {self.solution_name}")
        print("="*60)
        
        # Load GLM results
        lr_df, or_df, sig_source, is_penalized, summary_df = self.load_glm_results(lr_file_override, or_file_override)
        
        if lr_df is None or or_df is None:
            print("Could not load GLM results files.")
            return
        
        # Generate all displays
        generated_files = []
        
        # 1. Statistical significance heatmap (skip if penalized and no LR tests)
        if not is_penalized or not lr_df[~lr_df['term'].str.contains('MODEL_', na=False)].empty:
            heatmap_path = self.create_significance_heatmap(lr_df, sig_source, is_penalized)
            if heatmap_path:
                generated_files.append(heatmap_path)
        
        # 2. Formatted results table
        table_path, merged_data = self.create_effect_sizes_table(or_df, lr_df, sig_source, is_penalized, summary_df)
        generated_files.append(table_path)
        
        # 3. Forest plot of odds ratios
        forest_path = self.create_odds_ratio_forest_plot(or_df, lr_df, sig_source, is_penalized)
        generated_files.append(forest_path)
        
        print("\n" + "="*60)
        print("GLM DISPLAY COMPLETE!")
        print("="*60)
        print(f"Generated files in: {self.output_dir}")
        print("\nFILES CREATED:")
        for file_path in generated_files:
            print(f"- {file_path.name}")
        
        sig_label = 'FDR q-values' if sig_source == 'q_value' else 'p-values'
        print(f"\nTHESIS INTEGRATION:")
        print(f"- Significance based on: {sig_label}")
        if is_penalized:
            print("- NOTE: Penalized fit (ridge) - CIs/LR tests limited")
        print("- Use the significance heatmap to show which effects are statistically validated")
        print("- Include the forest plot to show effect sizes (odds ratios)")
        print("- Reference the results table for exact statistical values")
        print("- Algorithm interpretation: Genetic vs Local (OR>1 favors Genetic)")
        print("="*60)
        
        return generated_files


def main():
    """Main function with command line interface."""
    parser = argparse.ArgumentParser(description="Display GLM binomial ANOVA results")
    parser.add_argument("--glm_dir", required=True, help="Directory containing GLM results CSV files")
    parser.add_argument("--solution", help="Solution name for labeling")
    parser.add_argument("--lr_file", help="Override: specific LR test CSV file")
    parser.add_argument("--or_file", help="Override: specific odds ratio CSV file")
    
    args = parser.parse_args()
    
    # Initialize display
    display = GLMResultsDisplay(args.glm_dir, args.solution)
    
    # Run complete display
    display.run_complete_display(args.lr_file, args.or_file)


if __name__ == "__main__":
    main()