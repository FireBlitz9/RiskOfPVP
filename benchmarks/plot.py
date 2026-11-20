"""Render the checked-in offline results as the README benchmark figure."""

import argparse
from collections import Counter
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
from matplotlib import pyplot as plt
from matplotlib.patches import FancyBboxPatch, Patch, Rectangle
from matplotlib.ticker import MaxNLocator
import numpy as np


HERE = Path(__file__).resolve().parent
BACKGROUND = "#f1f3f9"
INK = "#191a2b"
MUTED = "#4d5468"
COLORS = ("#a75035", "#6642c9")


def panel(fig, index, title, subtitle, accent):
    left = .030 + index * .319
    card = FancyBboxPatch((left, .15), .302, .62, transform=fig.transFigure,
                          boxstyle="round,pad=0.005,rounding_size=0.011",
                          facecolor="white", edgecolor="#ccd1e0", linewidth=.8, zorder=-1)
    fig.add_artist(card)
    fig.add_artist(Rectangle((left + .016, .711), .010, .026, transform=fig.transFigure,
                             facecolor=accent, edgecolor="none"))
    fig.text(left + .034, .724, title, fontsize=17.5, weight="bold", color=INK, va="center")
    fig.text(left + .017, .674, subtitle, fontsize=11.5, color=MUTED, va="center")
    ax = fig.add_axes([left + .052, .270, .234, .343])
    for spine in ax.spines.values():
        spine.set_visible(False)
    ax.set_axisbelow(True)
    ax.grid(axis="y", color="#cfd4e1", linestyle=(0, (1, 3)), linewidth=1)
    ax.tick_params(axis="both", length=0, labelcolor=MUTED, labelsize=10.5, pad=6)
    return ax


def paired(ax, groups, series, ceiling, label):
    positions = np.arange(len(groups))
    width = .38
    for index, (values, color) in enumerate(zip(series, COLORS)):
        bars = ax.bar(positions + (index - .5) * width, values, width=width * .75, color=color)
        for bar, value in zip(bars, values):
            ax.annotate(label(value), (bar.get_x() + bar.get_width() / 2, value),
                        xytext=(0, 4), textcoords="offset points", ha="center", va="bottom",
                        fontsize=11, color=INK, weight="bold")
    ax.set_xticks(positions, groups)
    ax.set_xlim(-.52, len(groups) - .48)
    ax.set_ylim(0, ceiling)


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--results", type=Path, default=HERE / "results.json")
    cli.add_argument("--output", type=Path, default=HERE.parent / "assets" / "ultra-benchmarks-1.3.2.png")
    args = cli.parse_args()
    data = json.loads(args.results.read_text(encoding="utf-8"))
    versions = [data["versions"][key] for key in ("previous", "upgraded")]
    groups = ("Combat guardrails", "Threat response", "Build choices")
    totals = Counter(case["group"] for case in versions[0]["scenarios"])
    passed = [Counter(case["group"] for case in v["scenarios"] if case["passed"]) for v in versions]
    sizes = ("24", "96", "600")
    duration = [[v["metrics"][size]["median_us"] for size in sizes] for v in versions]
    memory = [[v["metrics"][size]["median_bytes"] / 1024 for size in sizes] for v in versions]
    offers = sum(metric["offers"] for metric in versions[0]["metrics"].values())
    plt.rcParams["font.family"] = "DejaVu Sans"
    fig = plt.figure(figsize=(14.8, 5.35), facecolor=BACKGROUND)
    fig.text(.03, .920, "RiskOfPVP  /  Ultra bot benchmarks", fontsize=25, weight="bold", color=INK)
    fig.text(.03, .862, f"{sum(totals.values())} offline scenarios   •   {offers:,} timed reward evaluations per generation",
             fontsize=13.5, color=MUTED)
    fig.legend([Patch(color=color) for color in COLORS], ["Previous Ultra · v1.3.1", "Upgraded Ultra · v1.3.2"],
               loc="upper right", bbox_to_anchor=(.964, .954), frameon=False, fontsize=12,
               labelcolor=INK, handlelength=1.25, labelspacing=.7)

    ax = panel(fig, 0, "Decision checks", "Expected outcomes passed · higher is better", "#6642c9")
    labels = [f"{name.replace(' ', chr(10), 1)}\n{totals[name]} checks" for name in groups]
    paired(ax, labels, [[p[g] for g in groups] for p in passed], 12.8, lambda x: str(x))
    ax.set_yticks([0, 3, 6, 9, 12])

    ax = panel(fig, 1, "Reward scoring time", "Median µs per offer · lower is better", "#126a9d")
    paired(ax, [f"{size} pickups" for size in sizes], duration, max(map(max, duration)) * 1.24, lambda x: f"{x:.1f}")
    ax.yaxis.set_major_locator(MaxNLocator(nbins=4))

    ax = panel(fig, 2, "Allocated memory", "Median KiB per offer · lower is better", "#177964")
    paired(ax, [f"{size} pickups" for size in sizes], memory, max(map(max, memory)) * 1.24, lambda x: f"{x:.1f}")
    ax.yaxis.set_major_locator(MaxNLocator(nbins=4))

    fig.text(.03, .070, "Production C# bot rules and reward chooser  /  Simulated inputs and inventories  /  Game closed",
             fontsize=11.5, color=MUTED)
    fig.savefig(args.output, dpi=165, facecolor=BACKGROUND)
    plt.close(fig)


if __name__ == "__main__":
    main()
