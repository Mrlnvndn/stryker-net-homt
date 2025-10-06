import pandas as pd
import numpy as np

repos = ['CsvHelper', 'GuardClauses', 'Marsen.NetCore.Dojo.Integration.Test', 'MoreLinq', 'TimeProviderExtensions']

print("LOCAL ALGORITHM HEURISTIC COUNTS:")
print("-" * 50)
local_counts = []
for r in repos:
    df = pd.read_csv(f'C:/Users/MerlijnU/analysis/part4_best_configs_{r}.csv')
    best = df[df['algorithm']=='local'].iloc[0]
    n = len(best['config'].split(','))
    local_counts.append(n)
    print(f'{r:40s}: {n} heuristics')

print(f'\nMean: {np.mean(local_counts):.2f}')
print(f'Std (sample): {np.std(local_counts, ddof=1):.2f}')
print(f'Values: {local_counts}')
print(f'Sum: {sum(local_counts)}, n={len(local_counts)}')
