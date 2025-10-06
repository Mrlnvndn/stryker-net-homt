import pandas as pd
import numpy as np

repos = ['CsvHelper', 'GuardClauses', 'Marsen.NetCore.Dojo.Integration.Test', 'MoreLinq', 'TimeProviderExtensions']

print("GENETIC ALGORITHM (excluding GuardClauses for separation):")
print("-" * 70)
genetic_improvements = []
genetic_counts = []
for r in repos:
    if r == 'GuardClauses':
        continue
    df = pd.read_csv(f'C:/Users/MerlijnU/analysis/part4_best_configs_{r}.csv')
    best = df[df['algorithm']=='genetic'].iloc[0]
    improvement = best['improvement']
    n = len(best['config'].split(','))
    genetic_improvements.append(improvement)
    genetic_counts.append(n)
    print(f'{r:40s}: {improvement:12.2f}×  ({n} heuristics)')

print(f'\nMedian improvement: {np.median(genetic_improvements):.2f}')
print(f'Values: {sorted(genetic_improvements)}')

print("\n" + "="*70)
print("LOCAL ALGORITHM:")
print("-" * 70)
local_improvements = []
local_counts = []
for r in repos:
    df = pd.read_csv(f'C:/Users/MerlijnU/analysis/part4_best_configs_{r}.csv')
    best = df[df['algorithm']=='local'].iloc[0]
    improvement = best['improvement']
    n = len(best['config'].split(','))
    local_improvements.append(improvement)
    local_counts.append(n)
    print(f'{r:40s}: {improvement:12.2f}×  ({n} heuristics)')

print(f'\nMean heuristic count: {np.mean(local_counts):.2f}')
print(f'Std heuristic count: {np.std(local_counts, ddof=1):.2f}')
print(f'Median improvement: {np.median(local_improvements):.2f}')
print(f'Improvement values: {sorted(local_improvements)}')
