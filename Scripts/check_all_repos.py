import pandas as pd

repos = ['CsvHelper', 'GuardClauses', 'Marsen.NetCore.Dojo.Integration.Test', 'MoreLinq', 'TimeProviderExtensions']

print("RANK 1 CONFIGS (OPTIMAL):")
print("="*100)

for r in repos:
    df = pd.read_csv(f'C:/Users/MerlijnU/analysis/part4_best_configs_{r}.csv')
    best = df[df['rank']==1]
    print(f"\n{r}:")
    for _, row in best.iterrows():
        imp = row['improvement']
        if imp > 1e10:
            imp_str = f"{imp:.2e} [SEPARATION]"
        else:
            imp_str = f"{imp:.2f}×"
        print(f"  {row['algorithm']:8s}: {imp_str:25s} config={row['config']}")
