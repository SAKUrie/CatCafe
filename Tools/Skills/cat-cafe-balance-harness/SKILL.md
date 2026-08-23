---
name: cat-cafe-balance-harness
description: Analyze the cat-cafe 4x4 symbol-pool structure with a dependency-free Python harness. Use when evaluating pool dilution, core appearance rate, pair adjacency, supply-consumer hit rate, same-type copy density, or when comparing a proposed deck size against the stable 16-20 and polluted 22+ pool targets.
---

# Cat Cafe Balance Harness

Use the bundled Python script to turn a proposed pool size and copy count into reproducible structural-probability data. Treat this as a first-pass design harness: it measures whether a combo can appear, not the complete economy, reward draft, or player decision policy.

## Workflow

1. Identify the design question before changing numbers:
   - Does a key symbol appear often enough?
   - How quickly does a two-card adjacency combo decay as the pool grows?
   - How many supply copies are needed for a consumer to find one?
   - Does a same-type quantity build still function above 20 cards?
2. Read [references/metrics.md](references/metrics.md) when interpreting or presenting the metrics.
3. Run the harness from the skill directory:

```powershell
python scripts/cat_cafe_math_harness.py
```

4. For a focused range or alternate board, pass explicit arguments:

```powershell
python scripts/cat_cafe_math_harness.py --min-pool 12 --max-pool 26 --copies 3,4,6 --supplies 2,4 --output outputs/my-test.csv
```

5. Compare at least these checkpoints in any conclusion: 16 cards, 20 cards, and 22 cards. Explain the design implication in plain language, not only percentages.
6. If a proposal changes symbol effects, reward drafting, removals, generators, or income targets, state that this structural harness is insufficient and recommend a second-stage turn-by-turn Monte Carlo simulation.

## Interpretation Rules

- Use the exact 4x4 eight-neighbor adjacency probability unless the game rule changes.
- Treat `consumer_hit_*` as an analytic approximation for quick iteration, not an exact dependent-event result.
- Do not claim that a high appearance rate guarantees a viable build; value per trigger and acquisition probability are outside this harness.
- Do not tune runtime values directly in generated Unity JSON. Record recommended changes in the source design table first.
- Keep the original design target visible: 16-20 cards should feel stable; 22+ cards should show perceptible pollution pressure.

## Deliverable Format

When reporting results, include:

- question tested;
- parameters and assumptions;
- 16/20/22-card comparison;
- one-sentence conclusion;
- recommended next experiment;
- generated CSV path.

Example conclusion: “At 16 cards the core always appears; at 20 it appears in 80% of spins; at 22 it falls to 72.7%, so adding two low-value generators beyond 20 creates visible but not catastrophic dilution.”

## Scope

This Skill packages the current cat-cafe Python probability harness. The older V2/V3 TypeScript combat-turn harnesses are historical and are intentionally excluded.
