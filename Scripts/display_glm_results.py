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

warnings.filterwarnings('ignore')

# Configure matplotlib for publication quality
plt.style.use('default')
plt.rcParams['figure.facecolor'] = 'white'
plt.rcParams['axes.facecolor'] = 'white'
plt.rcParams['font.size'] = 10

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
    
    def load_glm_results(self):
        """Load GLM results files."""
        # Look for LR test results
        lr_files = list(self.glm_dir.glob("*lr_tests*.csv"))
        or_files = list(self.glm_dir.glob("*coef_or*.csv"))
        
        if not lr_files:
            print(f"No LR test files found in {self.glm_dir}")
            return None, None
            
        if not or_files:
            print(f"No odds ratio files found in {self.glm_dir}")
            return None, None
        
        # Load the files
        lr_df = pd.read_csv(lr_files[0])
        or_df = pd.read_csv(or_files[0])
        
        print(f"Loaded LR tests: {len(lr_df)} terms")
        print(f"Loaded OR data: {len(or_df)} coefficients")
        
        return lr_df, or_df
    
    def create_significance_heatmap(self, lr_df):
        """Create a heatmap showing statistical significance of effects."""
        print("Creating significance heatmap...")
        
        # Filter out the McFadden R² row
        effects_df = lr_df[lr_df['term'] != 'MODEL_MCFADDEN_R2'].copy()
        
        # Separate main effects and interactions
        main_effects = effects_df[~effects_df['term'].str.contains(':')].copy()
        interactions = effects_df[effects_df['term'].str.contains(':')].copy()
        
        # Create significance levels
        def get_significance_level(p_val):
            if p_val < 0.001:
                return "***"
            elif p_val < 0.01:
                return "**"
            elif p_val < 0.05:
                return "*"
            elif p_val < 0.10:
                return "†"  # Marginally significant
            else:
                return "ns"
        
        def get_significance_numeric(p_val):
            """Convert p-value to numeric significance for heatmap colors."""
            if p_val < 0.001:
                return 4  # Highly significant
            elif p_val < 0.01:
                return 3  # Very significant
            elif p_val < 0.05:
                return 2  # Significant
            elif p_val < 0.1:
                return 1  # Marginally significant
            else:
                return 0  # Not significant
        
        effects_df['significance'] = effects_df['p_value'].apply(get_significance_level)
        effects_df['sig_numeric'] = effects_df['p_value'].apply(get_significance_numeric)
        
        # Separate main effects and interactions
        main_effects = effects_df[~effects_df['term'].str.contains(':')].copy()
        interactions = effects_df[effects_df['term'].str.contains(':')].copy()
        
        # Create figure with two subplots
        fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(16, 8))
        
        # Plot 1: Main Effects Heatmap
        if not main_effects.empty:
            main_effects_plot = main_effects.copy()
            main_effects_plot['sig_numeric'] = main_effects_plot['p_value'].apply(get_significance_numeric)
            main_effects_plot['significance'] = main_effects_plot['p_value'].apply(get_significance_level)
            
            # Clean up term names for display
            main_effects_plot['display_term'] = main_effects_plot['term'].str.replace('H_', '').str.replace('algorithm', 'Algorithm')
            
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
            
            ax1.set_title('GLM Main Effects Significance\n(Supports Plot 1 from L12 Analysis)', 
                         fontsize=12, fontweight='bold')
            ax1.set_xlabel('Statistical Significance')
            ax1.set_ylabel('')
        
        # Plot 2: Interaction Effects Heatmap  
        if not interactions.empty:
            interactions_plot = interactions.copy()
            interactions_plot['sig_numeric'] = interactions_plot['p_value'].apply(get_significance_numeric)
            interactions_plot['significance'] = interactions_plot['p_value'].apply(get_significance_level)
            
            # Clean up interaction terms
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
            
            ax2.set_title('GLM Interaction Effects Significance\n(Supports Plot 2 from L12 Analysis)', 
                         fontsize=12, fontweight='bold')
            ax2.set_xlabel('Statistical Significance') 
            ax2.set_ylabel('')
        
        # Add significance legend
        legend_elements = [
            Rectangle((0,0),1,1, facecolor='#d3d3d3', label='ns (p ≥ 0.05)'),
            Rectangle((0,0),1,1, facecolor='#ffffb3', label='† (p < 0.10)'),
            Rectangle((0,0),1,1, facecolor='#febf99', label='* (p < 0.05)'),
            Rectangle((0,0),1,1, facecolor='#f87274', label='** (p < 0.01)'),
            Rectangle((0,0),1,1, facecolor='#b30000', label='*** (p < 0.001)')
        ]
        
        fig.legend(handles=legend_elements, 
                  title='Significance Levels',
                  loc='center', 
                  bbox_to_anchor=(0.5, 0.02),
                  ncol=5,
                  fontsize=10)
        
        plt.suptitle(f'GLM Statistical Significance Results - {self.solution_name}', 
                    fontsize=14, fontweight='bold', y=0.95)
        plt.tight_layout()
        plt.subplots_adjust(bottom=0.15)
        
        # Save plot
        plot_path = self.output_dir / f"glm_significance_heatmap_{self.solution_name}.png"
        plt.savefig(plot_path, dpi=300, bbox_inches='tight')
        plt.close()
        
        print(f"Saved significance heatmap: {plot_path}")
        return plot_path
    
    def create_effect_sizes_table(self, or_df, lr_df):
        """Create a formatted table of effect sizes and significance."""
        print("Creating effect sizes table...")
        
        # Get McFadden's R²
        mcfadden = lr_df[lr_df['term'] == 'MODEL_MCFADDEN_R2']
        mcfadden_r2 = mcfadden['lr_chi2'].iloc[0] if not mcfadden.empty else "Not available"
        
        # Process odds ratios
        or_clean = or_df.copy()
        
        # Clean up coefficient names
        # Note: glm_binomial_anova.py uses local as reference, so coefficient is algorithm[T.genetic]
        # This represents "Genetic vs Local" (Genetic compared to Local baseline)
        or_clean['display_term'] = (or_clean['term']
                                  .str.replace('H_', '')
                                  .str.replace('algorithm[T.genetic]', 'Algorithm: Genetic vs Local')
                                  .str.replace(':', '×'))
        
        # Merge with significance data
        lr_clean = lr_df[lr_df['term'] != 'MODEL_MCFADDEN_R2'].copy()
        lr_clean['lr_term'] = lr_clean['term']
        
        # Create mapping for term names
        term_mapping = {}
        for _, row in or_clean.iterrows():
            original_term = row['term']
            # Map OR terms to LR terms
            if 'algorithm[T.genetic]' in original_term:
                lr_match = original_term.replace('algorithm[T.genetic]', 'algorithm')
            else:
                lr_match = original_term
            term_mapping[original_term] = lr_match
        
        # Add significance info
        or_clean['lr_term'] = or_clean['term'].map(term_mapping)
        merged = or_clean.merge(lr_clean[['term', 'p_value', 'lr_chi2']], 
                               left_on='lr_term', right_on='term', 
                               how='left', suffixes=('', '_lr'))
        
        # Format the results table
        results_table = []
        
        # Add header info
        results_table.append("GLM BINOMIAL ANOVA RESULTS")
        results_table.append("="*60)
        results_table.append(f"Solution: {self.solution_name}")
        results_table.append(f"Model Fit (McFadden's R²): {mcfadden_r2}")
        results_table.append("")
        
        # Main effects section
        main_effects = merged[~merged['term'].str.contains(':')].copy()
        if not main_effects.empty:
            results_table.append("MAIN EFFECTS:")
            results_table.append("-"*40)
            results_table.append(f"{'Effect':<25} {'OR':<8} {'95% CI':<15} {'p-value':<10} {'Sig':<5}")
            results_table.append("-"*63)
            
            for _, row in main_effects.iterrows():
                display_name = row['display_term']
                or_val = f"{row['OR']:.3f}"
                ci = f"({row['OR_CI_low']:.3f}-{row['OR_CI_high']:.3f})"
                p_val = f"{row['p_value']:.4f}" if pd.notna(row['p_value']) else "---"
                
                # Significance stars
                if pd.notna(row['p_value']):
                    if row['p_value'] < 0.001:
                        sig = "***"
                    elif row['p_value'] < 0.01:
                        sig = "**" 
                    elif row['p_value'] < 0.05:
                        sig = "*"
                    else:
                        sig = "ns"
                else:
                    sig = "---"
                
                results_table.append(f"{display_name:<25} {or_val:<8} {ci:<15} {p_val:<10} {sig:<5}")
        
        # Interaction effects section
        interactions = merged[merged['term'].str.contains(':')].copy()
        if not interactions.empty:
            results_table.append("")
            results_table.append("INTERACTION EFFECTS:")
            results_table.append("-"*40)
            results_table.append(f"{'Effect':<25} {'OR':<8} {'95% CI':<15} {'p-value':<10} {'Sig':<5}")
            results_table.append("-"*63)
            
            for _, row in interactions.iterrows():
                display_name = row['display_term']
                or_val = f"{row['OR']:.3f}"
                ci = f"({row['OR_CI_low']:.3f}-{row['OR_CI_high']:.3f})"
                p_val = f"{row['p_value']:.4f}" if pd.notna(row['p_value']) else "---"
                
                # Significance stars
                if pd.notna(row['p_value']):
                    if row['p_value'] < 0.001:
                        sig = "***"
                    elif row['p_value'] < 0.01:
                        sig = "**"
                    elif row['p_value'] < 0.05:
                        sig = "*"
                    else:
                        sig = "ns"
                else:
                    sig = "---"
                
                results_table.append(f"{display_name:<25} {or_val:<8} {ci:<15} {p_val:<10} {sig:<5}")
        
        # Add interpretation notes
        results_table.extend([
            "",
            "INTERPRETATION:",
            "-"*40,
            "OR > 1.0: Higher odds of SSHOM success when factor is ON vs OFF",
            "OR < 1.0: Lower odds of SSHOM success when factor is ON vs OFF", 
            "OR = 1.0: No effect of factor on SSHOM success",
            "",
            "Significance: *** p<0.001, ** p<0.01, * p<0.05, ns = not significant",
            "",
            "CONNECTION TO L12 PLOTS:",
            "- Main effects validate Plot 1 (EMM main effects)",
            "- Interaction effects validate Plot 2 (Algorithm×Heuristic interactions)",
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
    
    def create_odds_ratio_forest_plot(self, or_df, lr_df):
        """Create a forest plot of odds ratios with confidence intervals."""
        print("Creating odds ratio forest plot...")
        
        # Merge OR data with significance
        or_clean = or_df.copy()
        lr_clean = lr_df[lr_df['term'] != 'MODEL_MCFADDEN_R2'].copy()
        
        # Create term mapping
        term_mapping = {}
        for _, row in or_clean.iterrows():
            original_term = row['term']
            if 'algorithm[T.genetic]' in original_term:
                lr_match = original_term.replace('algorithm[T.genetic]', 'algorithm')
            else:
                lr_match = original_term
            term_mapping[original_term] = lr_match
        
        or_clean['lr_term'] = or_clean['term'].map(term_mapping)
        merged = or_clean.merge(lr_clean[['term', 'p_value']], 
                               left_on='lr_term', right_on='term', 
                               how='left', suffixes=('', '_lr'))
        
        # Clean up display names
        # Note: algorithm[T.genetic] means "Genetic vs Local" (Local is the reference)
        merged['display_term'] = (merged['term']
                                .str.replace('H_', '')
                                .str.replace('algorithm[T.genetic]', 'Genetic vs Local')
                                .str.replace(':', ' × '))
        
        # Separate main effects and interactions
        main_effects = merged[~merged['term'].str.contains(':')].copy()
        interactions = merged[merged['term'].str.contains(':')].copy()
        
        # Create the forest plot
        fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(12, 10))
        
        # Plot 1: Main Effects
        if not main_effects.empty:
            y_pos = np.arange(len(main_effects))
            
            # Colors based on significance
            colors = []
            for _, row in main_effects.iterrows():
                if pd.notna(row['p_value']) and row['p_value'] < 0.05:
                    colors.append('#d62728')  # Red for significant
                else:
                    colors.append('#1f77b4')  # Blue for non-significant
            
            # Plot odds ratios with confidence intervals
            ax1.errorbar(main_effects['OR'], y_pos,
                        xerr=[main_effects['OR'] - main_effects['OR_CI_low'],
                              main_effects['OR_CI_high'] - main_effects['OR']],
                        fmt='o', capsize=5, capthick=2, elinewidth=2,
                        color='black', ecolor='gray', markersize=8)
            
            # Color the points by significance
            for i, (color, row) in enumerate(zip(colors, main_effects.itertuples())):
                ax1.scatter(row.OR, i, color=color, s=100, zorder=5)
            
            # Add reference line at OR = 1
            ax1.axvline(x=1, color='red', linestyle='--', alpha=0.7, label='OR = 1 (No Effect)')
            
            ax1.set_yticks(y_pos)
            ax1.set_yticklabels(main_effects['display_term'])
            ax1.set_xlabel('Odds Ratio (95% CI)')
            ax1.set_title('Main Effects - Odds of SSHOM Success\n(Supports Plot 1: Main Effects)', 
                         fontweight='bold')
            ax1.grid(True, alpha=0.3)
            ax1.legend()
            
            # Add significance annotations
            for i, (_, row) in enumerate(main_effects.iterrows()):
                if pd.notna(row['p_value']):
                    if row['p_value'] < 0.001:
                        sig_text = "***"
                    elif row['p_value'] < 0.01:
                        sig_text = "**"
                    elif row['p_value'] < 0.05:
                        sig_text = "*"
                    else:
                        sig_text = "ns"
                    
                    ax1.text(row['OR_CI_high'] + 0.1, i, sig_text, 
                            va='center', fontweight='bold', fontsize=12)
        
        # Plot 2: Interaction Effects
        if not interactions.empty:
            y_pos2 = np.arange(len(interactions))
            
            # Colors based on significance
            colors2 = []
            for _, row in interactions.iterrows():
                if pd.notna(row['p_value']) and row['p_value'] < 0.05:
                    colors2.append('#d62728')  # Red for significant
                else:
                    colors2.append('#1f77b4')  # Blue for non-significant
            
            # Plot odds ratios with confidence intervals
            ax2.errorbar(interactions['OR'], y_pos2,
                        xerr=[interactions['OR'] - interactions['OR_CI_low'],
                              interactions['OR_CI_high'] - interactions['OR']],
                        fmt='o', capsize=5, capthick=2, elinewidth=2,
                        color='black', ecolor='gray', markersize=8)
            
            # Color the points by significance
            for i, (color, row) in enumerate(zip(colors2, interactions.itertuples())):
                ax2.scatter(row.OR, i, color=color, s=100, zorder=5)
            
            # Add reference line at OR = 1
            ax2.axvline(x=1, color='red', linestyle='--', alpha=0.7, label='OR = 1 (No Effect)')
            
            ax2.set_yticks(y_pos2)
            ax2.set_yticklabels(interactions['display_term'])
            ax2.set_xlabel('Odds Ratio (95% CI)')
            ax2.set_title('Interaction Effects - Algorithm × Heuristic\n(Supports Plot 2: Interaction Effects)', 
                         fontweight='bold')
            ax2.grid(True, alpha=0.3)
            ax2.legend()
            
            # Add significance annotations
            for i, (_, row) in enumerate(interactions.iterrows()):
                if pd.notna(row['p_value']):
                    if row['p_value'] < 0.001:
                        sig_text = "***"
                    elif row['p_value'] < 0.01:
                        sig_text = "**"
                    elif row['p_value'] < 0.05:
                        sig_text = "*"
                    else:
                        sig_text = "ns"
                    
                    ax2.text(row['OR_CI_high'] + 0.1, i, sig_text, 
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
    
    def run_complete_display(self):
        """Run complete GLM results display pipeline."""
        print(f"Displaying GLM results for {self.solution_name}")
        print("="*60)
        
        # Load GLM results
        lr_df, or_df = self.load_glm_results()
        
        if lr_df is None or or_df is None:
            print("Could not load GLM results files.")
            return
        
        # Generate all displays
        generated_files = []
        
        # 1. Statistical significance heatmap
        heatmap_path = self.create_significance_heatmap(lr_df)
        generated_files.append(heatmap_path)
        
        # 2. Formatted results table
        table_path, merged_data = self.create_effect_sizes_table(or_df, lr_df)
        generated_files.append(table_path)
        
        # 3. Forest plot of odds ratios
        forest_path = self.create_odds_ratio_forest_plot(or_df, lr_df)
        generated_files.append(forest_path)
        
        print("\n" + "="*60)
        print("GLM DISPLAY COMPLETE!")
        print("="*60)
        print(f"Generated files in: {self.output_dir}")
        print("\nFILES CREATED:")
        for file_path in generated_files:
            print(f"- {file_path.name}")
        
        print(f"\nTHESIS INTEGRATION:")
        print("- Use the significance heatmap to show which effects are statistically validated")
        print("- Include the forest plot to show effect sizes (odds ratios)")
        print("- Reference the results table for exact statistical values")
        print("- These GLM results provide the statistical foundation for your L12 plots")
        print("="*60)
        
        return generated_files


def main():
    """Main function with command line interface."""
    parser = argparse.ArgumentParser(description="Display GLM binomial ANOVA results")
    parser.add_argument("--glm_dir", required=True, help="Directory containing GLM results CSV files")
    parser.add_argument("--solution", help="Solution name for labeling")
    
    args = parser.parse_args()
    
    # Initialize display
    display = GLMResultsDisplay(args.glm_dir, args.solution)
    
    # Run complete display
    display.run_complete_display()


if __name__ == "__main__":
    main()