import pandas as pd
import numpy as np
import glob
from statsmodels.genmod.generalized_linear_model import GLM
from statsmodels.genmod import families

# Load local data
files = glob.glob('C:/users/merlijnu/outputs/TimeProviderExtensions/**/local/**/mutation-report-hom.csv', recursive=True)
df = pd.concat([pd.read_csv(f) for f in files])

# Calculate success counts
df['_successes'] = df['sshom_2'] + df.get('sshom_3', 0).fillna(0) + df.get('sshom_4', 0).fillna(0)
df['_trials'] = df['total_homs']
df = df[df['_trials'] > 0]

print(f'Local data: {len(df)} rows')
print(f'Successes range: {df["_successes"].min()} to {df["_successes"].max()}')
print(f'Trials range: {df["_trials"].min()} to {df["_trials"].max()}')
print(f'Success rate range: {(df["_successes"]/df["_trials"]*100).min():.1f}% to {(df["_successes"]/df["_trials"]*100).max():.1f}%')
print(f'Raw sshom_rate_percent range: {df["sshom_rate_percent"].min():.1f}% to {df["sshom_rate_percent"].max():.1f}%')

# Check if sshom columns exist
print(f'\nSSHOM columns in data: {[c for c in df.columns if "sshom" in c.lower()]}')
print(f'\nSample data:')
print(df[['sshom_rate_percent', 'sshom_2', '_successes', '_trials', 'total_homs']].head(10))
