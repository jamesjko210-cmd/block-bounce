# Block Bounce — how to run

## Just press Play
1. Open the project in Unity 6 (`6000.4.8f1`).
2. Open any scene (the default `Assets/Scenes/SampleScene.unity` is fine).
3. Press **▶ Play**.

The game spawns itself automatically — no GameObjects to wire up, no prefabs,
no art assets to assign. A script (`BlockBounceGame`) creates the camera setup,
generates all sprites at runtime, and runs the game.

## Controls
- **← / →** move piece
- **↑** rotate
- **↓** soft drop
- **Space** hard drop
- **P** pause
- When you earn balls: pick **Aim & Shoot** (move mouse, click to fire) or
  **Random Spray** (auto-launch)

## How the code is organised
| File | What it is | Unity needed? |
|------|-----------|---------------|
| `BBCore.cs` | The whole game engine — levels, pieces, row clears, ball physics, scoring. Pure C#. | No — it's testable on its own |
| `BlockBounceGame.cs` | The Unity layer — drawing, input, HUD. | Yes |

Keeping the engine Unity-free is deliberate: it means the core game logic can be
covered by **automated tests** later (the "Function" success criterion in the
project plan), independent of the rendering.

## Known MVP limitations (intentional, for later polish)
- HUD/menus use Unity's quick IMGUI (`OnGUI`) — functional but plain. Upgrading to
  uGUI/TextMeshPro is a good next step for the "Aesthetics" criterion.
- Blocks are drawn as numbered colored squares (matching the design), not the
  Blender FBX models — the game destroys *individual cells* by HP, so per-cell
  squares are what the mechanic needs. The FBX pieces are kept for menu/decor use.
- Ball speed is per-frame (like the original prototype), so it assumes ~60fps.

## Two deliberate changes from the Claude Design prototype
1. **No login/email screen** — the project's accessibility goal (UN SDG 10) says
   no accounts. Only a local player name is kept, for the leaderboard.
2. **Local leaderboard** — your name + best score (saved with `PlayerPrefs`) sit
   among the demo names. No server, no sign-in.
