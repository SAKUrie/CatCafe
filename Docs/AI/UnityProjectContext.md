# Unity Project Context

## Current gameplay entry

- Project root: `D:\思想家计划`
- Active scene: `Assets/Scenes/CatCafeDemo.unity`
- Reference: `C:\Users\陈卓\Desktop\cat-cafe-demo-share\cat-cafe-demo.html`
- Runtime entry: `Assets/Scripts/CatCafe/CatCafeGameController.cs`
- Artwork: `Assets/Resources/CatCafe/*.png`

## Port boundary

The Cat Cafe scene is a direct Unity C# port of the reference HTML script. The previous CatCafe catalog/simulation/presentation framework was removed. The single controller owns the HTML-equivalent data tables, state machine, board resolution and runtime uGUI.

## Gameplay flow

1. Start with three cats and three guests in the pool.
2. `营业` shuffles up to sixteen pool elements onto a 4x4 board.
3. Resolve guests, cats, food, drinks, staff and special elements using HTML adjacency rules.
4. Consume milk when adjacent to a cat, then resolve nursery breeding.
5. Choose one reward, optionally reroll or skip, and add a selected element to the pool/board.
6. At stage boundaries, pay the target, gain tokens and choose an operating item; failed stages can use the emergency thermos once.
7. After the third stage, enter the HTML free-经营 loop.

The port also includes the HTML item effects: cat apron, lucky paw, stamp card, snack shelf, quiet bell, matching cushions, recycling bin, panoramic window, double tray, emergency thermos, house special, reservation book and golden register.

## Unity constraints

- The canvas and overlays are created at runtime, so the scene only needs the camera, EventSystem and `CatCafeGameController` root component.
- The runtime UI uses a 1600x900 16:9 reference canvas. The HUD spans the viewport while the main machine stays centered at 790x690, containing the framed 540px 4x4 board and aligned primary/secondary controls. Choice and item dialogs use a 600px panel with a centered 360px card row (two-thirds of the 540px board), fixed 112px cards and a 330px action row; pool content uses the standard dialog width contract.
- The imported reference PNGs are loaded through `Resources/CatCafe` and use point-filtered sprite import settings.
- The former BoardCombat / puppet prototype, its demo scenes, configs, assets and WebPrototype have been removed; CatCafeDemo is the active gameplay entry.
- Unity MCP was not callable in this desktop surface. Package resolution currently reports `The "path" argument must be of type string. Received undefined`, so final Editor/Play Mode validation remains pending until package resolution is healthy.
