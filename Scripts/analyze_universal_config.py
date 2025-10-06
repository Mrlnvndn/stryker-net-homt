#!/usr/bin/env python3
"""
Analyze universal best heuristic configuration across algorithms and repositories.
"""
import pandas as pd
from pathlib import Path

# Load all best configs
repos = ['CsvHelper', 'MoreLinq', 'TimeProviderExtensions']
all_configs = []

for repo in repos:
    df = pd.read_csv(f'C:/Users/MerlijnU/analysis/part4_best_configs_{repo}.csv')
    df['repository'] = repo
    all_configs.append(df)

configs_df = pd.concat(all_configs, ignore_index=True)

# Get best config for each (repo, algorithm)
best_per_combo = configs_df[configs_df['rank'] == 1]

print('=' * 80)
print('BEST CONFIGURATIONS PER REPOSITORY × ALGORITHM')
print('=' * 80)
for _, row in best_per_combo.iterrows():
    repo = row['repository']
    algo = row['algorithm']
    config = row['config']
    print(f'{repo:<30} {algo:<10} {config}')

print()
print('=' * 80)
print('HEURISTIC FREQUENCY ANALYSIS (in optimal configs)')
print('=' * 80)

heuristics = ['CodeLocation', 'EmptyAssessingTests', 'MaxSizeLimit', 
              'MutatorType', 'OverlappingTests', 'SyntaxNodeConflict']

freq_data = []
for h in heuristics:
    count = best_per_combo['config'].str.contains(h).sum()
    pct = count / len(best_per_combo) * 100
    bar = '█' * int(pct / 10)
    print(f'{h:<25} {count}/6 configs ({pct:>5.1f}%) {bar}')
    freq_data.append({'heuristic': h, 'count': count, 'pct': pct})

print()
print('=' * 80)
print('UNIVERSAL CONFIGURATION RECOMMENDATION')
print('=' * 80)
print('Based on frequency in optimal configs (appearing in ≥50% of combinations):')
print()

universal = []
for item in freq_data:
    if item['count'] >= 3:  # At least 50% of 6 combinations
        universal.append(item['heuristic'])
        print(f'  ✓ {item["heuristic"]} (in {item["count"]}/6 configs)')

print()
if universal:
    print(f'RECOMMENDED UNIVERSAL CONFIG: {", ".join(universal)}')
else:
    print('No heuristics appear in ≥50% of optimal configs.')
    print('Consider lowering threshold or using algorithm-specific configs.')

print()
print('Note: This maximizes agreement across algorithms and repositories.')
print('      For best performance, use algorithm-specific configs instead.')
