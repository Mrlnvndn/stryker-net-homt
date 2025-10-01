#!/usr/bin/env python3
"""
Analyze Stryker HOMT (HomCsv) outputs organized as:
<root>/<Solution>/<Algorithm or RunTag-Algorithm>/OAxx/repyy/
  - Hom*.csv
  - stryker-output.log (optional)

This script:
1) Scans the tree for CSVs and parses them into a tidy table (runs_all.csv).
2) Aggregates across repetitions per (solution, algorithm, oa_row, used_heuristics) (runs_agg_by_oa.csv).
3) Estimates heuristic main effects (ON vs OFF) per algorithm with bootstrap CIs (heuristic_effects.csv).
4) Generates a few figures (PNG):
   - coreA_sshom_rate_by_solution.png     : Bar plot of mean SSHOM rate per solution, split by algorithm.
   - coreA_pareto_time_vs_sshom.png       : Scatter of time vs SSHOM rate (median per OA row), by algorithm.
   - effects_<metric>_per_algorithm.png   : Bar plot of heuristic effects (ON-OFF) by algorithm.
   - time_effects_per_algorithm.png       : Bar plot of time effects (ms ON-OFF) by algorithm.

Dependencies: pandas, numpy, matplotlib (builtin in many Python envs). No seaborn, no statsmodels.
We avoid external stats libs; CIs via simple bootstrap/permutation.

Usage:
  python analyze_homcsv.py --root "<root>" --out "<outdir>" [--metric sshom_rate_percent]

Author: generated for Merlijn's thesis workflow.
"""

import argparse
import re
from pathlib import Path
from typing import Dict, List, Optional

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt

# -------- Configuration --------
HEURISTICS = [
    "CodeLocation",
    "EmptyAssessingTests",
    "MutatorType",
    "MaxSizeLimit",
    "OverlappingTests",
    "SyntaxNodeConflict",
]
ALGORITHMS = {"genetic", "local", "both"}

# -------- Helpers to find/parse --------
def find_run_csvs(root: Path) -> List[Path]:
    return [p for p in root.rglob("*.csv") if p.is_file()]

def parse_path_metadata(csv_path: Path) -> Dict[str, Optional[str]]:
    # Expected structure: .../MoreLinq/MoreLinq/genetic/OA03/rep02/reports/mutation-report-hom.csv
    parts = list(csv_path.resolve().parents)
    rep = oa_row = None
    alg_dir = sol_dir = None
    
    # Work backwards from the file:
    # parts[0] = reports
    # parts[1] = rep02
    # parts[2] = OA03  
    # parts[3] = genetic
    # parts[4] = MoreLinq (inner)
    # parts[5] = MoreLinq (outer) - we want this one
    
    if len(parts) >= 2 and re.match(r"rep\d{2}$", parts[1].name, re.IGNORECASE):
        try: rep = int(parts[1].name[3:])
        except: pass
    if len(parts) >= 3 and re.match(r"OA\d{2}$", parts[2].name, re.IGNORECASE):
        try: oa_row = int(parts[2].name[2:])
        except: pass
    if len(parts) >= 4: alg_dir = parts[3].name
    if len(parts) >= 5: sol_dir = parts[4].name  # This will be the inner "MoreLinq"

    algorithm = None; run_tag = None
    if alg_dir:
        toks = alg_dir.split("-")
        if toks[-1].lower() in ALGORITHMS:
            algorithm = toks[-1].lower()
            run_tag = "-".join(toks[:-1]) or None
        else:
            algorithm = alg_dir.lower()

    return {
        "solution": sol_dir,
        "algorithm": algorithm,
        "run_tag": run_tag,
        "oa_row": oa_row,
        "rep": rep
    }

def read_homcsv(csv_path: Path) -> Optional[pd.DataFrame]:
    try:
        return pd.read_csv(csv_path, engine="python")
    except Exception as e:
        print(f"[WARN] Failed CSV read: {csv_path} -> {e}")
        return None

def pick_summary_row(df: pd.DataFrame) -> pd.Series:
    if len(df) == 1:
        return df.iloc[0]
    # prefer rows with key fields, else last row
    cand = df
    for key in ["mode","used_heuristics","total_homs","sshom_rate_percent"]:
        if key in df.columns:
            cand = cand[cand[key].notna()]
    if len(cand) >= 1:
        return cand.iloc[-1]
    return df.iloc[-1]

def parse_per_algorithm_stats(s: str) -> Dict[str, Dict[str, float]]:
    out = {}
    if not isinstance(s,str) or not s.strip():
        return out
    for part in s.split("|"):
        if ":" not in part: continue
        alg, rest = part.split(":",1)
        sub = {}
        for kv in rest.split(";"):
            if "=" in kv:
                k,v = kv.split("=",1)
                k=k.strip(); v=v.strip()
                try: sub[k]=float(v)
                except: pass
        out[alg.strip()] = sub
    return out

def heuristics_on_list(used_heuristics: Optional[str]) -> List[str]:
    if not isinstance(used_heuristics,str) or not used_heuristics.strip():
        return []
    # Include pipe | in the split pattern
    tokens = re.split(r"[,\s;\-|]+", used_heuristics.strip())
    tokens = [t for t in tokens if t]
    on=[]
    for h in HEURISTICS:
        for t in tokens:
            if h.lower()==t.lower():
                on.append(h); break
    return on

def to_float(x):
    try:
        return float(str(x).replace(",","."))
    except Exception:
        return np.nan

# -------- Main processing --------
def build_runs_table(root: Path) -> pd.DataFrame:
    csvs = find_run_csvs(root)
    print(f"Found {len(csvs)} CSV files")
    
    # Debug: show first few CSV paths
    for i, csv in enumerate(csvs[:5]):
        print(f"  CSV {i+1}: {csv}")
    if len(csvs) > 5:
        print(f"  ... and {len(csvs)-5} more")
    
    rows = []
    for p in csvs:
        meta = parse_path_metadata(p)
        print(f"Parsing: {p}")
        print(f"  Metadata: {meta}")
        
        df = read_homcsv(p)
        if df is None or df.empty: 
            print(f"  Skipping: empty/invalid CSV")
            continue

        row = pick_summary_row(df)

        def get(c, default=None):
            return row[c] if c in row.index else default

        used_algorithms = get("used_algorithms", meta["algorithm"])
        used_heuristics = get("used_heuristics", "")
        heur_on = heuristics_on_list(used_heuristics)

        metrics = dict(
            total_homs=to_float(get("total_homs")),
            hom_2=to_float(get("hom_2")),
            hom_3=to_float(get("hom_3")),
            hom_4=to_float(get("hom_4")),
            sshom_rate_percent=to_float(get("sshom_rate_percent")),
            sshom_2=to_float(get("sshom_2")),
            sshom_3=to_float(get("sshom_3")),
            sshom_4=to_float(get("sshom_4")),
            hom_generation_ms=to_float(get("hom_generation_ms")),
            total_test_duration_ms=to_float(get("total_test_duration_ms")),
            mutation_score_percent=to_float(get("mutation_score_percent")),
            raw_candidates_total=to_float(get("raw_candidates_total")),
            dup_within_total=to_float(get("dup_within_total")),
            dup_across_total=to_float(get("dup_across_total")),
            filtered_empty_total=to_float(get("filtered_empty_total")),
            filtered_invalid_total=to_float(get("filtered_invalid_total")),
            total_foms_in_pool=to_float(get("total_foms_in_pool")),
            unique_constituent_foms_in_homs_count=to_float(get("unique_constituent_foms_in_homs_count")),
            missing_foms_count=to_float(get("missing_foms_count")),
            initial_tests_count=to_float(get("initial_tests_count")),
            analysis_time_ms=to_float(get("analysis_time_ms")),
            test_runs_count=to_float(get("test_runs_count")),
            avg_predicted_score_all_kept=to_float(get("avg_predicted_score_all_kept") or get("avg_perdicted_score_all_kept")),
            median_predicted_score_all_kept=to_float(get("median_predicted_score_all_kept") or get("median_perdicted_score_all_kept")),
            avg_predicted_score_sshoms=to_float(get("avg_predicted_score_sshoms")),
            median_predicted_score_sshoms=to_float(get("median_predicted_score_sshoms")),
        )

        per_algo = parse_per_algorithm_stats(get("per_algorithm_stats",""))
        flat = {}
        for alg_name, stats in per_algo.items():
            prefix = f"per_algo_{alg_name.replace(' ','')}_"
            for k,v in stats.items():
                flat[prefix+k]=v

        # OA01 is never random - it's always the "all heuristics OFF" baseline condition
        is_random = False

        out = dict(
            solution = meta["solution"],
            algorithm = meta["algorithm"] or used_algorithms,
            run_tag = meta["run_tag"],
            oa_row = meta["oa_row"],
            rep = meta["rep"],
            used_algorithms = used_algorithms,
            used_heuristics = used_heuristics or "",
            mode = get("mode", None),
            random_seed = get("random_seed", None),
            is_random_row = is_random,
            csv_path = str(p),
        )
        for h in HEURISTICS: out[f"heur_{h}_on"] = (h in heur_on)
        out.update(metrics); out.update(flat)
        rows.append(out)

    runs = pd.DataFrame(rows)
    # ensure heuristic flags exist
    for h in HEURISTICS:
        col=f"heur_{h}_on"
        if col not in runs.columns: runs[col]=False
    return runs

def aggregate_by_oa(runs: pd.DataFrame) -> pd.DataFrame:
    keep = runs[~runs["is_random_row"].fillna(False)].copy()
    metric_cols = [c for c in keep.columns if keep[c].dtype.kind in "fc" and c not in ("oa_row","rep")]
    group_keys = ["solution","algorithm","oa_row","used_heuristics"]
    agg = keep.groupby(group_keys)[metric_cols].agg(['mean','median','std','count'])
    agg.columns = [f"{a}_{b}" for a,b in agg.columns]
    return agg.reset_index()

# -------- Effects estimation --------
def bootstrap_ci(a: np.ndarray, b: np.ndarray, n_boot=2000, ci=0.95, rng=None):
    """Bootstrap CI for mean difference (a_mean - b_mean)."""
    if rng is None: rng = np.random.default_rng(0)
    a = np.array(a, dtype=float); b = np.array(b, dtype=float)
    a = a[~np.isnan(a)]; b = b[~np.isnan(b)]
    if len(a)==0 or len(b)==0:
        return np.nan, (np.nan, np.nan)
    diffs = []
    for _ in range(n_boot):
        sa = rng.choice(a, size=len(a), replace=True)
        sb = rng.choice(b, size=len(b), replace=True)
        diffs.append(sa.mean() - sb.mean())
    diffs = np.sort(diffs)
    lo = np.percentile(diffs, (1-ci)/2*100)
    hi = np.percentile(diffs, (1+ci)/2*100)
    return float(np.mean(diffs)), (float(lo), float(hi))

def compute_effects(runs: pd.DataFrame, metric: str, time_metric="total_test_duration_ms") -> pd.DataFrame:
    # First aggregate by OA row to get mean across repetitions
    agg_data = aggregate_by_oa(runs)
    print(f"Computing effects using {len(agg_data)} aggregated data points...")
    
    # Use median values from aggregation (more robust than mean)
    metric_col = f"{metric}_median"
    time_col = f"{time_metric}_median"
    
    if metric_col not in agg_data.columns:
        print(f"Warning: {metric_col} not found in aggregated data. Available columns: {list(agg_data.columns)}")
        return pd.DataFrame()
    
    out_rows = []
    for alg in sorted(set(agg_data["algorithm"].dropna().str.lower())):
        print(f"  Processing algorithm: {alg}")
        sub = agg_data[agg_data["algorithm"].str.lower()==alg]
        
        for h in HEURISTICS:
            # Determine which rows have this heuristic ON vs OFF
            heur_pattern = f"\\b{re.escape(h)}\\b"  # Word boundary to match exact heuristic names
            on_mask = sub["used_heuristics"].str.contains(heur_pattern, case=False, na=False, regex=True)
            off_mask = ~on_mask
            
            on_vals = sub.loc[on_mask, metric_col].astype(float).values
            off_vals = sub.loc[off_mask, metric_col].astype(float).values
            
            print(f"    {h}: {len(on_vals)} ON, {len(off_vals)} OFF")
            
            diff_mean, (ci_lo, ci_hi) = bootstrap_ci(on_vals, off_vals, n_boot=2000, ci=0.95)

            if time_col in sub.columns:
                t_on = sub.loc[on_mask, time_col].astype(float).values
                t_off = sub.loc[off_mask, time_col].astype(float).values
                t_diff, (t_lo, t_hi) = bootstrap_ci(t_on, t_off, n_boot=2000, ci=0.95)
            else:
                t_diff=t_lo=t_hi=np.nan

            out_rows.append({
                "algorithm": alg,
                "heuristic": h,
                "metric": metric,
                "on_mean": float(np.nanmean(on_vals)) if on_vals.size else np.nan,
                "off_mean": float(np.nanmean(off_vals)) if off_vals.size else np.nan,
                "diff_on_minus_off": diff_mean,
                "ci95_lo": ci_lo,
                "ci95_hi": ci_hi,
                "n_on": int(len(on_vals)),
                "n_off": int(len(off_vals)),
                "time_diff_ms": t_diff,
                "time_ci95_lo": t_lo,
                "time_ci95_hi": t_hi,
            })
    
    return pd.DataFrame(out_rows)

# -------- Plotting (matplotlib only, single chart per figure) --------
def save_bar_per_solution(df: pd.DataFrame, out_dir: Path, value_col="sshom_rate_percent"):
    # aggregate by solution+algorithm (median for robustness)
    agg = df.groupby(["solution","algorithm"])[value_col].median().reset_index()
    sols = agg["solution"].unique().tolist()
    algs = sorted(agg["algorithm"].dropna().unique().tolist())
    x = np.arange(len(sols)); width = 0.8 / max(1,len(algs))

    fig = plt.figure(figsize=(max(6, len(sols)*0.6), 4))
    for i, alg in enumerate(algs):
        vals = [(agg[(agg.solution==s)&(agg.algorithm==alg)][value_col].values[0] if not agg[(agg.solution==s)&(agg.algorithm==alg)].empty else np.nan) for s in sols]
        xpos = x + i*width - (len(algs)-1)*width/2
        plt.bar(xpos, vals, width=width, label=alg)
    plt.xticks(x, sols, rotation=45, ha='right')
    plt.ylabel(value_col)
    plt.title(f"{value_col} (median per solution, split by algorithm)")
    plt.legend()
    fig.tight_layout()
    figpath = out_dir / "coreA_sshom_rate_by_solution.png"
    fig.savefig(figpath, dpi=200)
    plt.close(fig)
    return figpath

def save_pareto_scatter(agg_oa: pd.DataFrame, out_dir: Path,
                        x_col="total_test_duration_ms_median",
                        y_col="sshom_rate_percent_median"):
    # One point per OA row per solution per algorithm (median across reps)
    if x_col not in agg_oa.columns or y_col not in agg_oa.columns:
        return None
    fig = plt.figure(figsize=(6,5))
    for alg in sorted(agg_oa["algorithm"].dropna().unique().tolist()):
        sub = agg_oa[agg_oa["algorithm"]==alg]
        plt.scatter(sub[x_col], sub[y_col], label=alg, alpha=0.8)
    plt.xlabel(x_col); plt.ylabel(y_col)
    plt.title("Pareto: Time vs SSHOM rate (median per OA row)")
    plt.legend()
    fig.tight_layout()
    figpath = out_dir / "coreA_pareto_time_vs_sshom.png"
    fig.savefig(figpath, dpi=200)
    plt.close(fig)
    return figpath

def save_effect_bars(effects: pd.DataFrame, out_dir: Path, metric: str):
    # Bar of diff_on_minus_off per heuristic for each algorithm (faceted by algorithm in legend)
    fig = plt.figure(figsize=(8,4))
    algs = sorted(effects["algorithm"].dropna().unique().tolist())
    x_labels = HEURISTICS
    x = np.arange(len(x_labels))
    width = 0.8 / max(1,len(algs))

    for i, alg in enumerate(algs):
        sub = effects[(effects["algorithm"]==alg) & (effects["metric"]==metric)]
        diffs = [float(sub[sub["heuristic"]==h]["diff_on_minus_off"].values[0]) if not sub[sub["heuristic"]==h].empty else np.nan for h in x_labels]
        xpos = x + i*width - (len(algs)-1)*width/2
        plt.bar(xpos, diffs, width=width, label=alg)
    plt.axhline(0, linewidth=1)
    plt.xticks(x, x_labels, rotation=30, ha='right')
    plt.ylabel(f"Effect on {metric} (ON - OFF)")
    plt.title(f"Heuristic main effects on {metric}")
    plt.legend()
    fig.tight_layout()
    figpath = out_dir / f"effects_{metric}_per_algorithm.png"
    fig.savefig(figpath, dpi=200)
    plt.close(fig)
    return figpath

def save_time_effect_bars(effects: pd.DataFrame, out_dir: Path):
    fig = plt.figure(figsize=(8,4))
    algs = sorted(effects["algorithm"].dropna().unique().tolist())
    x_labels = HEURISTICS
    x = np.arange(len(x_labels))
    width = 0.8 / max(1,len(algs))

    for i, alg in enumerate(algs):
        sub = effects[effects["algorithm"]==alg]
        diffs = [float(sub[sub["heuristic"]==h]["time_diff_ms"].values[0]) if not sub[sub["heuristic"]==h].empty else np.nan for h in x_labels]
        xpos = x + i*width - (len(algs)-1)*width/2
        plt.bar(xpos, diffs, width=width, label=alg)
    plt.axhline(0, linewidth=1)
    plt.xticks(x, x_labels, rotation=30, ha='right')
    plt.ylabel("Effect on time (ms) (ON - OFF)")
    plt.title("Heuristic main effects on total_test_duration_ms")
    plt.legend()
    fig.tight_layout()
    figpath = out_dir / "time_effects_per_algorithm.png"
    fig.savefig(figpath, dpi=200)
    plt.close(fig)
    return figpath

# -------- CLI --------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True, help="Root folder that contains <Solution>/<alg>/OAxx/repyy/")
    ap.add_argument("--out", required=True, help="Output folder for analysis CSVs/figures")
    ap.add_argument("--metric", default="sshom_rate_percent", help="Metric for heuristic effects (default: sshom_rate_percent)")
    ap.add_argument("--solution", help="Optional: filter to specific solution name (e.g., 'MoreLinq')")
    args = ap.parse_args()

    root = Path(args.root)
    base_out_dir = Path(args.out)
    base_out_dir.mkdir(parents=True, exist_ok=True)

    print(f"Scanning for CSVs under: {root}")
    print(f"Root exists: {root.exists()}")
    
    # 1) Runs table
    runs = build_runs_table(root)
    print(f"Found {len(runs)} total runs")
    
    if runs.empty:
        print(f"No runs found under {root}")
        return
    
    # Debug: show what solutions were found
    available_solutions = sorted(runs['solution'].dropna().unique().tolist())
    print(f"Available solutions: {available_solutions}")
    
    # Filter by solution if specified
    if args.solution:
        initial_count = len(runs)
        print(f"Filtering for solution: '{args.solution}'")
        runs = runs[runs["solution"] == args.solution]
        print(f"After filtering: {len(runs)} runs")
        
        if runs.empty:
            print(f"No runs found for solution '{args.solution}'. Available solutions: {available_solutions}")
            return
        print(f"Filtered to solution '{args.solution}': {len(runs)}/{initial_count} runs")
        
        # Create solution-specific output directory
        out_dir = base_out_dir / args.solution
    else:
        # If analyzing all solutions, create directories for each
        solutions = runs['solution'].dropna().unique()
        if len(solutions) == 1:
            # Single solution - create subfolder
            out_dir = base_out_dir / solutions[0]
        else:
            # Multiple solutions - use base directory but note this in output
            out_dir = base_out_dir
    
    out_dir.mkdir(parents=True, exist_ok=True)
    print(f"Output directory: {out_dir}")

    runs_all_path = out_dir / "runs_all.csv"
    runs.to_csv(runs_all_path, index=False, encoding="utf-8")
    print(f"Wrote {runs_all_path} ({len(runs)} rows)")

    # 2) OA aggregation
    agg_oa = aggregate_by_oa(runs)
    agg_oa_path = out_dir / "runs_agg_by_oa.csv"
    agg_oa.to_csv(agg_oa_path, index=False, encoding="utf-8")
    print(f"Wrote {agg_oa_path} ({len(agg_oa)} rows)")

    # 3) Effects
    effects = compute_effects(runs, metric=args.metric, time_metric="total_test_duration_ms")
    effects_path = out_dir / "heuristic_effects.csv"
    effects.to_csv(effects_path, index=False, encoding="utf-8")
    print(f"Wrote {effects_path} ({len(effects)} rows)")

    # 4) Figures
    figs = []
    figs.append(save_bar_per_solution(runs, out_dir, value_col="sshom_rate_percent"))
    figs.append(save_pareto_scatter(agg_oa, out_dir,
                                    x_col="total_test_duration_ms_median",
                                    y_col="sshom_rate_percent_median"))
    figs.append(save_effect_bars(effects, out_dir, metric=args.metric))
    figs.append(save_time_effect_bars(effects, out_dir))
    figs = [str(f) for f in figs if f is not None]
    if figs:
        print("Saved figures:")
        for f in figs:
            print(" -", f)

    # Tiny recap
    print("\nSummary:")
    print(f"  Solutions: {runs['solution'].nunique()}")
    print(f"  Algorithms: {sorted(runs['algorithm'].dropna().unique().tolist())}")
    print(f"  Metric for effects: {args.metric}")
    print(f"  Output: {out_dir}")

if __name__ == "__main__":
    main()
