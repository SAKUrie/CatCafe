# Metrics and assumptions

## Current baseline

- Board: 4 columns × 4 rows, 16 visible slots.
- Draw: without replacement; one copy cannot appear twice in a spin.
- Position: randomly assigned after drawing.
- Adjacency: all eight surrounding cells, including diagonals.
- Stable target: 16-20 cards.
- Pollution warning: 22+ cards.

## Metrics

### `single_core_appearance`

Probability that one specific copy appears in a spin. It is 100% at 16 cards or fewer, `16 / pool_size` above 16.

### `specific_pair_adjacent`

Probability that two specific copies both appear and land adjacent. On a 4×4 board, two random distinct cells are adjacent with probability `42 / 120 = 35%`.

This metric describes one named pair, such as one sharpener plus one specific pencil. It is not the chance that a sharpener touches any pencil among several copies.

### `consumer_hits_N_supplies`

Approximate probability that a consumer contacts at least one of N supply copies. It is designed for fast design comparison. Contacts are treated as independent, so use simulation before relying on small percentage differences.

### `expected_visible_N_copies`

Expected number of visible copies of a type when the pool contains N copies. This measures density, not adjacency.

### `count_build_income_N_copies`

Structural proxy for count-scaling symbols whose combined income grows approximately with the square of visible count. It uses `E[K²] = Var(K) + E[K]²` before the configured cap.

## What this harness does not model

- three-choice reward acquisition;
- skip and reroll behavior;
- symbol values or full rule text;
- generated symbols entering over time;
- removal, storage, consumption, transformation, or permanent upgrades;
- daily revenue targets and run loss rate;
- player strategy.

Use a turn-by-turn Monte Carlo harness when any of these decides the answer.

## Standard report checkpoints

Always compare:

| Pool | Meaning |
|---:|---|
| 16 | every copy appears; maximum consistency |
| 20 | intended mature-pool midpoint |
| 22 | pollution should become perceptible |

Keep assumptions with the output CSV so a later result is reproducible.
