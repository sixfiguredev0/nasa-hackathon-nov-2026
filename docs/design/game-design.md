# Game design (draft)

Status: **draft**. Nothing here is final. Change it through a pull request and note why.

## Pitch

The player runs a small outpost on the Moon or Mars, one sol (day) at a time. Every sol they split limited power and make one or two choices. Real solar activity can bring radiation storms. Good planning keeps the crew safe; every choice costs something.

## Core resources (max three)

| Resource | What it means to a kid | Main trade-off |
|---|---|---|
| Power | Electricity from solar panels | Split between life support, comms, science |
| Oxygen / life support | Air the crew breathes | Cutting power today lowers oxygen tomorrow |
| Shielding / radiation dose | Protection from the Sun's dangerous particles | Reinforcing costs power; skipping risks crew health |

Food and communication delay are candidates for later, not the first prototype.

## Core loop

1. Start of sol: see meters and a forecast.
2. Choose: allocate power, place or upgrade a module.
3. Preview: each choice shows its cost in one sentence before confirming.
4. Resolve: events happen (some driven by real NASA data).
5. Review: short cause-and-effect summary.

Turn-based sols, not real-time timers.

## Two lenses, one game

- **Surface (ages 7 to 9):** big icon meters, tap and drag, one-line previews, optional voice-over.
- **Depth (ages 10 to 12, older siblings):** opt-in Mission Planner panel with numbers, forecasts, data sources.

## Critical thinking hooks

- Predict before a storm resolves ("What will happen?").
- Mission review instead of game over: show the chain of decisions.
- "Why?" buttons with short true explanations.

## Stretch goals

- Sibling co-op: younger player runs the outpost, older player is Mission Control with delayed messages.
- Food production as a fourth system.
- Multiple sites (Moon south pole, Mars).

## Open questions

- Engine: see `docs/decisions/0001-engine.md`.
- Art style and camera (top-down, isometric, 2.5D).
- Moon, Mars, or both for the prototype.
