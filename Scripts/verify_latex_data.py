import pandas as pd
import numpy as np

print("="*100)
print("VERIFICATION: LaTeX Table Data vs Python Script Output")
print("="*100)

repos = ['CsvHelper', 'GuardClauses', 'Marsen.NetCore.Dojo.Integration.Test', 'MoreLinq', 'TimeProviderExtensions']

print("\nTABLE 1 VERIFICATION: Optimal Configurations")
print("-"*100)

latex_data = {
    'CsvHelper': {
        'genetic': {'config': 'CodeLocation,EmptyAssessingTests,MaxSizeLimit,SyntaxNodeConflict', 'improvement': 7.68},
        'local': {'config': 'EmptyAssessingTests,SyntaxNodeConflict', 'improvement': 1.33}
    },
    'GuardClauses': {
        'genetic': {'config': 'CodeLocation,EmptyAssessingTests,MaxSizeLimit,MutatorType,OverlappingTests', 'improvement': '8.86e+16'},
        'local': {'config': 'CodeLocation,EmptyAssessingTests,MaxSizeLimit,MutatorType', 'improvement': 1.92}
    },
    'Marsen.NetCore.Dojo.Integration.Test': {
        'genetic': {'config': 'CodeLocation,MaxSizeLimit,MutatorType,OverlappingTests', 'improvement': 3403.26},
        'local': {'config': 'EmptyAssessingTests,MaxSizeLimit,OverlappingTests,SyntaxNodeConflict', 'improvement': 32.32}
    },
    'MoreLinq': {
        'genetic': {'config': 'CodeLocation,EmptyAssessingTests,MutatorType,OverlappingTests', 'improvement': 43.44},
        'local': {'config': 'CodeLocation,EmptyAssessingTests,MaxSizeLimit', 'improvement': 30.65}
    },
    'TimeProviderExtensions': {
        'genetic': {'config': 'CodeLocation,EmptyAssessingTests,MaxSizeLimit,MutatorType,OverlappingTests', 'improvement': 331.09},
        'local': {'config': 'MaxSizeLimit', 'improvement': 1.16}
    }
}

all_match = True

for repo in repos:
    print(f"\n{repo}:")
    df = pd.read_csv(f'C:/Users/MerlijnU/analysis/part4_best_configs_{repo}.csv')
    
    for algo in ['genetic', 'local']:
        best = df[df['algorithm'] == algo].iloc[0]
        csv_config = best['config']
        csv_improvement = best['improvement']
        
        latex_config = latex_data[repo][algo]['config']
        latex_improvement = latex_data[repo][algo]['improvement']
        
        # Compare configs
        config_match = csv_config == latex_config
        
        # Compare improvements (special handling for separation)
        if isinstance(latex_improvement, str) and 'e+' in latex_improvement:
            # Separation case
            improvement_match = csv_improvement > 1e15
        else:
            improvement_match = abs(csv_improvement - latex_improvement) < 0.01
        
        status = "✓" if (config_match and improvement_match) else "✗"
        
        print(f"  {algo:8s} {status}")
        print(f"    Config:      CSV={csv_config}")
        print(f"                 LaTeX={latex_config}")
        print(f"                 Match: {config_match}")
        print(f"    Improvement: CSV={csv_improvement:.2f}")
        print(f"                 LaTeX={latex_improvement}")
        print(f"                 Match: {improvement_match}")
        
        if not (config_match and improvement_match):
            all_match = False

print("\n" + "="*100)
print("TABLE 2 VERIFICATION: Binary Matrix Sums")
print("-"*100)

# Count heuristics in optimal configs
heuristics = ['CodeLocation', 'EmptyAssessingTests', 'MaxSizeLimit', 'MutatorType', 'OverlappingTests', 'SyntaxNodeConflict']
abbrev = ['CL', 'EAT', 'MSL', 'MT', 'OT', 'SNC']

counts_genetic = {h: 0 for h in heuristics}
counts_local = {h: 0 for h in heuristics}

for repo in repos:
    df = pd.read_csv(f'C:/Users/MerlijnU/analysis/part4_best_configs_{repo}.csv')
    
    for algo in ['genetic', 'local']:
        best = df[df['algorithm'] == algo].iloc[0]
        config_list = best['config'].split(',')
        
        for h in heuristics:
            if h in config_list:
                if algo == 'genetic':
                    counts_genetic[h] += 1
                else:
                    counts_local[h] += 1

latex_sums_genetic = [5, 4, 4, 4, 4, 1]
latex_sums_local = [2, 4, 4, 1, 1, 2]
latex_totals = [7, 8, 8, 5, 5, 3]

print("\nHeuristic         CSV(G) LaTeX(G) CSV(L) LaTeX(L) CSV(T) LaTeX(T) Match")
print("-"*100)

for i, h in enumerate(heuristics):
    csv_g = counts_genetic[h]
    csv_l = counts_local[h]
    csv_t = csv_g + csv_l
    
    latex_g = latex_sums_genetic[i]
    latex_l = latex_sums_local[i]
    latex_t = latex_totals[i]
    
    match = (csv_g == latex_g and csv_l == latex_l and csv_t == latex_t)
    status = "✓" if match else "✗"
    
    print(f"{abbrev[i]:4s} {h:20s} {csv_g:3d}    {latex_g:3d}      {csv_l:3d}    {latex_l:3d}      {csv_t:3d}    {latex_t:3d}      {status}")
    
    if not match:
        all_match = False

print("\n" + "="*100)
print("TABLE 3 VERIFICATION: Heuristic Frequencies")
print("-"*100)

latex_frequencies = {
    'EmptyAssessingTests': (8, 10, 80.0),
    'MaxSizeLimit': (8, 10, 80.0),
    'CodeLocation': (7, 10, 70.0),
    'OverlappingTests': (5, 10, 50.0),
    'MutatorType': (5, 10, 50.0),
    'SyntaxNodeConflict': (3, 10, 30.0)
}

print("\nHeuristic              CSV Count  LaTeX Count  CSV %   LaTeX %  Match")
print("-"*100)

for h in heuristics:
    csv_count = counts_genetic[h] + counts_local[h]
    csv_pct = (csv_count / 10) * 100
    
    latex_count, latex_total, latex_pct = latex_frequencies[h]
    
    match = (csv_count == latex_count and abs(csv_pct - latex_pct) < 0.1)
    status = "✓" if match else "✗"
    
    print(f"{h:22s} {csv_count:3d}/10      {latex_count:3d}/10       {csv_pct:5.1f}%  {latex_pct:5.1f}%   {status}")
    
    if not match:
        all_match = False

print("\n" + "="*100)
print("TABLE 7 VERIFICATION: Statistical Summary")
print("-"*100)

# Calculate stats from CSV data
genetic_heuristic_counts = []
local_heuristic_counts = []
genetic_improvements = []
local_improvements = []

for repo in repos:
    df = pd.read_csv(f'C:/Users/MerlijnU/analysis/part4_best_configs_{repo}.csv')
    
    for algo in ['genetic', 'local']:
        best = df[df['algorithm'] == algo].iloc[0]
        n_on = len(best['config'].split(','))
        improvement = best['improvement']
        
        if algo == 'genetic':
            # Exclude GuardClauses-Genetic for separation
            if repo != 'GuardClauses':
                genetic_heuristic_counts.append(n_on)
                genetic_improvements.append(improvement)
        else:
            local_heuristic_counts.append(n_on)
            local_improvements.append(improvement)

csv_stats = {
    'genetic_mean': np.mean(genetic_heuristic_counts),
    'genetic_std': np.std(genetic_heuristic_counts, ddof=1),
    'genetic_min': min(genetic_improvements),
    'genetic_max': max(genetic_improvements),
    'genetic_median': np.median(genetic_improvements),
    'local_mean': np.mean(local_heuristic_counts),
    'local_std': np.std(local_heuristic_counts, ddof=1),
    'local_min': min(local_improvements),
    'local_max': max(local_improvements),
    'local_median': np.median(local_improvements),
}

latex_stats = {
    'genetic_mean': 4.25,
    'genetic_std': 0.50,
    'genetic_min': 7.68,
    'genetic_max': 3403.26,
    'genetic_median': 187.26,
    'local_mean': 2.80,
    'local_std': 1.30,
    'local_min': 1.16,
    'local_max': 32.32,
    'local_median': 1.92,
}

print("\nMetric                        Genetic CSV  Genetic LaTeX  Local CSV  Local LaTeX  Match")
print("-"*100)

for metric in ['mean', 'std', 'min', 'max', 'median']:
    g_csv = csv_stats[f'genetic_{metric}']
    g_latex = latex_stats[f'genetic_{metric}']
    l_csv = csv_stats[f'local_{metric}']
    l_latex = latex_stats[f'local_{metric}']
    
    g_match = abs(g_csv - g_latex) < 0.01
    l_match = abs(l_csv - l_latex) < 0.01
    match = g_match and l_match
    status = "✓" if match else "✗"
    
    print(f"{metric.capitalize():20s}      {g_csv:8.2f}     {g_latex:8.2f}       {l_csv:8.2f}     {l_latex:8.2f}       {status}")
    
    if not match:
        all_match = False

print("\n" + "="*100)
if all_match:
    print("✓✓✓ ALL DATA VERIFIED: LaTeX tables match Python script output perfectly!")
else:
    print("✗✗✗ DISCREPANCIES FOUND: Some LaTeX data does not match Python output")
print("="*100)
