# 0001: Game engine and web delivery

- Status: Proposed
- Date: 2026-11-14
- Deciders: team

## Context

The game must run in a browser with no download or account, for kids 7 to 12, likely on school Chromebooks, tablets and phones. The build window is the hackathon weekend. The team leans towards Unity.

## Options

1. **Unity (WebGL / Web export).** Pros: team familiarity, strong 2D/2.5D tooling, editor for designers. Cons: larger builds and slower loading, weaker mobile browser support, slower iteration and harder merges with several agents editing scenes.
2. **Lightweight web stack (Phaser, PixiJS, or TypeScript + canvas).** Pros: loads in seconds, runs on any device, plain text files merge cleanly, fast iteration. Cons: less editor tooling, team may be less familiar.
3. **Godot (Web export).** Middle ground. Smaller builds than Unity; mobile web support should be checked.

## Test before deciding

Build a one-sprite web export with the leading option and open it on a mid-range phone and a Chromebook. Target: playable in under about 10 seconds.

## Decision

Open.

## Consequences

To be filled in once accepted. Until then, keep engine-specific files inside `game/`.
