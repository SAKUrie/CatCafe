#!/usr/bin/env python3
"""Cat-cafe structural balance harness.

Produces a CSV for pool dilution, adjacency combos, supply-consumer contact,
and quantity-build density. Uses only the Python standard library.
"""

from __future__ import annotations

import argparse
import csv
import math
from pathlib import Path


def appearance_probability(pool_size: int, board_slots: int) -> float:
    return min(1.0, board_slots / pool_size)


def adjacency_probability(board_width: int, board_height: int) -> float:
    """Probability that two distinct random cells are 8-neighbor adjacent."""
    slots = board_width * board_height
    if slots < 2:
        return 0.0
    horizontal = board_height * (board_width - 1)
    vertical = board_width * (board_height - 1)
    diagonal = 2 * (board_width - 1) * (board_height - 1)
    return (horizontal + vertical + diagonal) / math.comb(slots, 2)


def specific_pair_adjacent_probability(
    pool_size: int, board_slots: int, pair_adjacency: float
) -> float:
    if pool_size <= board_slots:
        return pair_adjacency
    both_appear = (board_slots / pool_size) * ((board_slots - 1) / (pool_size - 1))
    return both_appear * pair_adjacency


def expected_visible(copies: int, pool_size: int, board_slots: int) -> float:
    return copies * appearance_probability(pool_size, board_slots)


def consumer_hit_probability(
    supply_copies: int, pool_size: int, board_slots: int, pair_adjacency: float
) -> float:
    """Fast approximation: at least one supply both appears and contacts consumer."""
    supply_contact = pair_adjacency * appearance_probability(pool_size, board_slots)
    return 1 - (1 - supply_contact) ** supply_copies


def hypergeom_variance(copies: int, pool_size: int, board_slots: int) -> float:
    if pool_size <= board_slots or pool_size <= 1:
        return 0.0
    probability = copies / pool_size
    return (
        board_slots
        * probability
        * (1 - probability)
        * ((pool_size - board_slots) / (pool_size - 1))
    )


def expected_square_count_income(
    copies: int, pool_size: int, board_slots: int, cap: int
) -> float:
    """E[K^2] before cap; conservative approximation when the mean reaches cap."""
    mean = min(cap, expected_visible(copies, pool_size, board_slots))
    if mean >= cap:
        return float(cap * cap)
    return hypergeom_variance(copies, pool_size, board_slots) + mean * mean


def parse_int_list(raw: str, label: str) -> list[int]:
    try:
        values = sorted({int(part.strip()) for part in raw.split(",") if part.strip()})
    except ValueError as error:
        raise argparse.ArgumentTypeError(f"{label} must be comma-separated integers") from error
    if not values or any(value <= 0 for value in values):
        raise argparse.ArgumentTypeError(f"{label} must contain positive integers")
    return values


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Analyze 4x4 cat-cafe pool dilution and synergy probabilities."
    )
    parser.add_argument("--width", type=int, default=4, help="board width; default: 4")
    parser.add_argument("--height", type=int, default=4, help="board height; default: 4")
    parser.add_argument("--min-pool", type=int, default=4, help="first pool size")
    parser.add_argument("--max-pool", type=int, default=30, help="last pool size")
    parser.add_argument("--copies", default="4,6", help="same-type copy counts")
    parser.add_argument("--supplies", default="2,4", help="supply copy counts")
    parser.add_argument("--count-cap", type=int, default=6, help="quantity-build cap")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("outputs") / "cat-cafe-probability-baseline.csv",
        help="CSV output path",
    )
    return parser


def main() -> int:
    args = build_parser().parse_args()
    board_slots = args.width * args.height
    if args.width <= 0 or args.height <= 0:
        raise SystemExit("board dimensions must be positive")
    if args.min_pool <= 0 or args.max_pool < args.min_pool:
        raise SystemExit("pool range is invalid")
    if args.min_pool < 1:
        raise SystemExit("min-pool must be positive")

    copies = parse_int_list(args.copies, "copies")
    supplies = parse_int_list(args.supplies, "supplies")
    pair_adjacency = adjacency_probability(args.width, args.height)

    columns = ["pool_size", "single_core_appearance", "specific_pair_adjacent"]
    columns += [f"consumer_hits_{count}_supplies" for count in supplies]
    for count in copies:
        columns += [
            f"expected_visible_{count}_copies",
            f"count_build_income_{count}_copies",
        ]

    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=columns)
        writer.writeheader()
        for pool_size in range(args.min_pool, args.max_pool + 1):
            row: dict[str, str | int] = {
                "pool_size": pool_size,
                "single_core_appearance": f"{appearance_probability(pool_size, board_slots):.4f}",
                "specific_pair_adjacent": f"{specific_pair_adjacent_probability(pool_size, board_slots, pair_adjacency):.4f}",
            }
            for count in supplies:
                row[f"consumer_hits_{count}_supplies"] = (
                    f"{consumer_hit_probability(count, pool_size, board_slots, pair_adjacency):.4f}"
                )
            for count in copies:
                row[f"expected_visible_{count}_copies"] = (
                    f"{expected_visible(count, pool_size, board_slots):.3f}"
                )
                row[f"count_build_income_{count}_copies"] = (
                    f"{expected_square_count_income(count, pool_size, board_slots, args.count_cap):.3f}"
                )
            writer.writerow(row)

    print(f"board={args.width}x{args.height} slots={board_slots}")
    print(f"8-neighbor pair adjacency={pair_adjacency:.4f}")
    for checkpoint in (16, 20, 22):
        if args.min_pool <= checkpoint <= args.max_pool:
            core = appearance_probability(checkpoint, board_slots)
            pair = specific_pair_adjacent_probability(checkpoint, board_slots, pair_adjacency)
            print(
                f"pool={checkpoint}: core={core:.1%}, specific-adjacent-pair={pair:.1%}"
            )
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
