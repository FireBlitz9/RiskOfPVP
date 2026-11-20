"""Render matching three-generation charts for tactics and rewards."""

import argparse
from collections import Counter
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
from matplotlib import pyplot as plt
from matplotlib.patches import FancyBboxPatch, PathPatch
from matplotlib.path import Path as DrawingPath
from matplotlib.ticker import MaxNLocator
import numpy as np


HERE = Path(__file__).resolve().parent
ASSETS = HERE.parent / "assets"
GENERATIONS = ("Ultra V1", "Ultra V2", "Ultra V3")
COLORS = ("#496680", "#ad5438", "#6a43c8")
BACKGROUND, CARD, INK, MUTED = "#eef1f9", "#fcfdff", "#171a2b", "#50596c"


def rounded_box(fig, x, y, width, height, color, radius, layer, edge="none"):
    fig.add_artist(FancyBboxPatch(
        (x, y), width, height, transform=fig.transFigure,
        boxstyle=f"round,pad=0,rounding_size={radius}", facecolor=color,
        edgecolor=edge, linewidth=.8, zorder=layer,
    ))


def canvas(title, subtitle, note):
    plt.rcParams["font.family"] = ["Segoe UI", "DejaVu Sans"]
    fig = plt.figure(figsize=(16.2, 5.45), facecolor=BACKGROUND)
    fig.text(.033, .906, title, fontsize=25, weight="bold", color=INK, va="center")
    fig.text(.034, .850, subtitle, fontsize=13, color=MUTED, va="center")
    for i, (name, color) in enumerate(zip(GENERATIONS, COLORS)):
        left = .708 + i * .087
        rounded_box(fig, left, .876, .078, .056, "#f9faff", .016, 1, "#dee4f1")
        rounded_box(fig, left + .009, .894, .008, .020, color, .004, 2)
        fig.text(left + .023, .904, name, fontsize=12, weight="semibold", color=INK, va="center")
    fig.text(.034, .073, note, fontsize=11.5, color=MUTED, va="center")
    return fig


def panel(fig, index, title, subtitle):
    left, width = .030 + index * .319, .302
    rounded_box(fig, left + .003, .145, width, .612, "#dce2f1", .021, -2)
    rounded_box(fig, left, .157, width, .612, CARD, .021, -1, "#e0e5f0")
    fig.text(left + .018, .718, title, fontsize=17, weight="bold", color=INK, va="center")
    fig.text(left + .018, .669, subtitle, fontsize=11.5, color=MUTED, va="center")
    ax = fig.add_axes([left + .043, .265, width - .061, .342], facecolor="none")
    for spine in ax.spines.values():
        spine.set_visible(False)
    ax.set_axisbelow(True)
    ax.grid(axis="y", color="#d6deed", linestyle=(0, (1, 3)), linewidth=1)
    ax.tick_params(axis="both", length=0, labelcolor=MUTED, labelsize=10, pad=7)
    return ax


def rounded_bar(ax, center, width, value, color):
    if value <= 0:
        return
    left, right = center - width / 2, center + width / 2
    px_per_x, px_per_y = abs(ax.transData.get_matrix()[0, 0]), abs(ax.transData.get_matrix()[1, 1])
    radius = min(width * .30, 8 / px_per_x, value * .45 * px_per_y / px_per_x)
    rise = radius * px_per_x / px_per_y
    points = [
        (left, 0), (left, value - rise), (left, value), (left + radius, value),
        (right - radius, value), (right, value), (right, value - rise), (right, 0), (left, 0),
    ]
    commands = [DrawingPath.MOVETO, DrawingPath.LINETO, DrawingPath.CURVE3, DrawingPath.CURVE3,
                DrawingPath.LINETO, DrawingPath.CURVE3, DrawingPath.CURVE3, DrawingPath.LINETO, DrawingPath.CLOSEPOLY]
    ax.add_patch(PathPatch(DrawingPath(points, commands), facecolor=color, edgecolor="none"))


def grouped_bars(ax, labels, series, ceiling, format_value):
    ax.set_xlim(-.50, len(labels) - .50)
    ax.set_ylim(0, ceiling)
    ax.set_xticks(np.arange(len(labels)), labels)
    ax.yaxis.set_major_locator(MaxNLocator(nbins=4))
    if len(labels) == 5:
        ax.tick_params(axis="x", labelsize=9.5)
    step = .242
    for order, (data, color) in enumerate(zip(series, COLORS)):
        for i, value in enumerate(data):
            center = i + (order - 1) * step
            rounded_bar(ax, center, .201, value, color)
            ax.annotate(
                format_value(value), (center, value), xytext=(0, 4), textcoords="offset points",
                ha="center", va="bottom", fontsize=10 if len(labels) < 5 else 9.4,
                weight="semibold", color=INK if value else MUTED,
            )


def decimal(value):
    return "0" if value == 0 else f"{value:.2f}" if value < 10 else f"{value:.1f}"


def render_tactics(builds, output):
    summary = [build["summary"] for build in builds]
    groups = list(summary[0])
    labels = ["Corner\nchase", "Retreat", "Hill\nlanes", "Combat\ntiming", "Counter-\nbuilds"]
    passed = [[version[group]["passed"] for group in groups] for version in summary]
    durations = [[version[group]["mean_metric"] for group in groups[:2]] for version in summary]
    ticks = [[build["timing"][count]["p95_us_per_tick"] for count in ("1", "8", "24")] for build in builds]
    total = sum(g["total"] for g in summary[0].values())
    fig = canvas(
        "RiskOfPVP / Ultra tactics",
        f"{total} matched sequences per generation · 48 per category",
        "Scripted world inputs · Game closed · Unity movement and multiplayer frame time outside scope",
    )
    ax = panel(fig, 0, "Decision sequences", "Sequences passed · higher is better")
    grouped_bars(ax, labels, passed, 58, str)
    ax.set_yticks([0, 12, 24, 36, 48])
    ax = panel(fig, 1, "Route behavior", "Mean seconds per sequence · lower is better")
    grouped_bars(ax, ["Chase stalled", "Retreat exposed"], durations, max(map(max, durations)) * 1.26, decimal)
    ax = panel(fig, 2, "Decision cost", "P95 µs per synthetic tick · lower is better")
    grouped_bars(ax, ["1 bot", "8 bots", "24 bots"], ticks, max(map(max, ticks)) * 1.25, decimal)
    fig.savefig(output, dpi=165, facecolor=BACKGROUND)
    plt.close(fig)


def render_rewards(builds, output):
    groups = ["Combat guardrails", "Threat response", "Build choices"]
    totals = Counter(case["group"] for case in builds[0]["scenarios"])
    passed = [Counter(case["group"] for case in build["scenarios"] if case["passed"]) for build in builds]
    sizes = ("24", "96", "600")
    scoring = [[build["metrics"][size]["median_us"] for size in sizes] for build in builds]
    allocations = [[build["metrics"][size]["median_bytes"] / 1024 for size in sizes] for build in builds]
    offers = sum(metric["offers"] for metric in builds[0]["metrics"].values())
    fig = canvas(
        "RiskOfPVP / Ultra reward scoring",
        f"{sum(totals.values())} original checks · {offers:,} timed reward offers per generation",
        "Production C# reward chooser · Simulated inventories and offers · Game closed",
    )
    ax = panel(fig, 0, "Original decision checks", "Expected outcomes passed · higher is better")
    labels = [f"{group.replace(' ', chr(10), 1)}\n{totals[group]} checks" for group in groups]
    grouped_bars(ax, labels, [[p[group] for group in groups] for p in passed], 12.8, str)
    ax.set_yticks([0, 3, 6, 9, 12])
    labels = [f"{size} pickups" for size in sizes]
    ax = panel(fig, 1, "Reward scoring time", "Median µs per offer · lower is better")
    grouped_bars(ax, labels, scoring, max(map(max, scoring)) * 1.25, decimal)
    ax = panel(fig, 2, "Allocated memory", "Median KiB per offer · lower is better")
    grouped_bars(ax, labels, allocations, max(map(max, allocations)) * 1.25, decimal)
    fig.savefig(output, dpi=165, facecolor=BACKGROUND)
    plt.close(fig)


def read_builds(path):
    versions = json.loads(path.read_text(encoding="utf-8"))["versions"]
    return [versions[generation] for generation in GENERATIONS]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, default=HERE / "tactical-results.json")
    parser.add_argument("--rewards-results", type=Path, default=HERE / "reward-generations.json")
    parser.add_argument("--output", type=Path, default=ASSETS / "ultra-tactics-benchmarks.png")
    parser.add_argument("--rewards-output", type=Path, default=ASSETS / "ultra-reward-generations.png")
    args = parser.parse_args()
    render_tactics(read_builds(args.results), args.output)
    render_rewards(read_builds(args.rewards_results), args.rewards_output)


if __name__ == "__main__":
    main()
