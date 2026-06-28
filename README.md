# Pirates of the Caribbean Online - Unity Toolkit

> A Unity toolkit for exploring, importing, editing, and creating with Pirates of the Caribbean Online assets.

<p align="center">
Got questions or want to share what you are building?
</p>

<p align="center">
  <a href="https://discord.gg/Dr6YR8HkPJ" target="_blank">
    <img alt="Join our Discord"
      src="https://img.shields.io/badge/Join%20our%20Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white">
  </a>
</p>

---

## Important First Import Note

Unity may take a long time to process the project the first time it opens. This toolkit includes thousands of original POTCO model and texture files, so Unity needs time to build its local cache.

If it looks like Unity is frozen, give it time. The first import is by far the slowest one.

---

## What Is This?

The POTCO Unity Toolkit brings Pirates of the Caribbean Online content into Unity so the community can explore old assets, rebuild scenes, create custom worlds, test gameplay ideas, and make new experiences using familiar POTCO visuals.

It is built for creativity and preservation. You can import original world data, place props visually, build ships, preview effects, create NPCs, edit item cards, work with POTCO-style inventory visuals, and export custom scene data.

This is not a finished game or a one-click remake. It is a community toolkit for experimenting, learning, building, and making new POTCO-inspired content.

---

## Features

### World Creation Tools

- **World Data Importer** - Import original POTCO world data into Unity scenes.
- **World Data Exporter** - Export Unity scenes back into POTCO-style world data.
- **Object Browser** - Browse POTCO props visually and place them into your scene.
- **Quick Place Tool** - Save favorite props into quick slots for faster building.
- **Surface Placement** - Drop props onto terrain, floors, docks, rocks, and other surfaces.
- **Group Tools** - Save multiple placed objects as reusable groups.
- **Procedural Cave Generator** - Generate cave layouts using connector-based cave pieces.

### Asset Tools

- **EGG Importer Manager** - Import Panda3D `.egg` models used by POTCO.
- **RGB Texture Importer** - Import old `.rgb` texture files with transparency support.
- **Prefab Creator** - Convert imported `.egg` assets into Unity prefabs.
- **Thumbnail Cache** - Generate and reuse preview thumbnails for the object browser.

### POTCO Gameplay And Visual Systems

- **Ship Builder** - Build and customize POTCO-style ships.
- **Custom NPC Creator** - Create and preview pirate/NPC characters.
- **Character Database** - Browse character-related data such as clothing, palettes, body shapes, and NPCs.
- **Item Card Editor** - View, edit, duplicate, and preview POTCO item cards.
- **Inventory And Chest UI** - Recreated POTCO-style inventory and sea chest visuals.
- **Weapon And Combat Systems** - Runtime support for weapons, HUD behavior, projectiles, and combat targets.
- **Sky And Time Of Day** - POTCO-inspired sky, moon, fog, lighting, and day/night controls.
- **Ocean System** - POTCO-style ocean color, waves, island water maps, and reflections.
- **Effect Previewer** - Spawn and preview many POTCO-inspired visual effects.
- **NPC, Creature, And Enemy Support** - Import and preview world NPCs, creatures, enemies, and animations.

---

## Requirements

- Unity `6000.1.11f1` is recommended.
- Universal Render Pipeline is used by the project.
- A decent amount of disk space and patience for the first import.

Most work is done directly inside the Unity Editor from the `POTCO` menu.

---

## Quick Start

1. Clone or download the project.
2. Open the project root in Unity Hub.
3. Use Unity `6000.1.11f1` if possible.
4. Wait for Unity to finish importing everything.
5. Open the Unity menu bar and look under `POTCO`.

Main tools are available from:

```text
POTCO > World Data > Importer
POTCO > World Data > Exporter
POTCO > EGG Importer Manager
POTCO > Level Editor > Object Browser
POTCO > Level Editor > Quick Place Tool
POTCO > Level Editor > Procedural Cave Generator
POTCO > Ship Builder
POTCO > Characters > Custom NPC Creator
POTCO > Item Card Editor
POTCO > Create Sky
POTCO > Effect Previewer
POTCO > Extras > Debug Controls
```

---

## Tool Overview

### World Data Importer

Import original POTCO world-data `.py` files into Unity. The importer can create the scene hierarchy, place models, apply colors, add lights, bring in collision objects, spawn supported NPCs, and optionally add export metadata.

Good for:

- Rebuilding old POTCO islands or interiors.
- Studying how original worlds were assembled.
- Using old worlds as a starting point for new custom scenes.

### World Data Exporter

Export objects from a Unity scene back into POTCO-style world data. This is useful if you are building a custom scene in Unity and want to save it in a format closer to the original POTCO data structure.

Good for:

- Sharing custom layouts.
- Round-tripping imported scenes.
- Exporting selected objects or full scenes.

### Object Browser

A visual prop browser for the POTCO asset library. Search for models, browse categories, favorite objects, preview thumbnails, and place props directly into your scene.

Good for:

- Building custom islands.
- Decorating interiors.
- Finding models without digging through folders.
- Creating reusable object groups.

### EGG Importer Manager

Controls how POTCO `.egg` files are imported. You can import selected files, import everything, skip certain folders, choose LOD behavior, skip animations or collision files, and view import statistics.

Good for:

- Preparing assets before scene building.
- Avoiding unnecessary startup imports.
- Reimporting selected models after changing settings.

### Ship Builder

Build POTCO-style ships using available ship parts and presets. You can customize ship layout and appearance, then preview or place the result in a scene.

Good for:

- Creating custom ships.
- Testing ship parts.
- Setting up ship gameplay experiments.

### Custom NPC Creator

Create and preview pirate/NPC characters using POTCO-style character data. Adjust body, face, clothing, hair, morphs, and related options with live preview support.

Good for:

- Making custom NPCs.
- Previewing old NPC data.
- Testing character clothing and colors.

### Item Card Editor

Browse and edit POTCO item data with a live item-card preview. You can search by class, duplicate items, create new entries, preview icons/models, and add items to a play-mode inventory.

Good for:

- Editing weapons, clothing, jewelry, charms, tattoos, and consumables.
- Previewing item cards.
- Testing inventory visuals.

### Sky, Ocean, And Effects

The toolkit includes recreated POTCO-style environmental visuals:

- Day, dusk, night, stars, swamp, invasion, Halloween, and overcast sky presets.
- Moon phase and overlay controls.
- Fog and lighting changes.
- Ocean color and wave behavior.
- Many previewable effects such as fire, explosions, smoke, cannon effects, dark magic effects, and more.

---

## Basic Usage

### Import A World

1. Open `POTCO > World Data > Importer`.
2. Select a world-data `.py` file.
3. Choose whether to import extra options like colors, collisions, lighting, NPCs, or ObjectList data.
4. Click `Build Scene`.
5. Wait for the scene to finish generating.

### Build A Custom Scene

1. Open `POTCO > Level Editor > Object Browser`.
2. Search or browse for props.
3. Enable surface placement if you want props to snap onto scene geometry.
4. Place objects into the scene.
5. Save useful object combinations as groups if needed.

### Export A Scene

1. Make sure important objects have ObjectList data.
2. Open `POTCO > World Data > Exporter`.
3. Choose whether to export the full scene or selected objects.
4. Pick an output `.py` file.
5. Click `Export World Data`.

### Reimport EGG Files

1. Open `POTCO > EGG Importer Manager`.
2. Adjust import settings if needed.
3. Select `.egg` files in the Project window.
4. Use `POTCO > EGG Importer > Reimport Selected EGG`.

---

## Screenshots

### Level Editor In Action

https://github.com/user-attachments/assets/9ebcb461-3981-4f06-8db6-6236e9a3b744

### Cave Generation In Action

https://github.com/user-attachments/assets/39e63113-1cf6-457f-9515-f33dab15f0b0

### World Importer In Action

https://github.com/user-attachments/assets/b4187ae0-391a-410d-99f8-5706204fa792

### Custom Scenes

<img width="1607" height="1091" alt="Custom POTCO scene" src="https://github.com/user-attachments/assets/6a8616a4-4b24-401c-964a-fefe9b388d31" />

### POTCO Tortuga Tavern

<img width="3829" height="1890" alt="POTCO Tortuga Tavern" src="https://github.com/user-attachments/assets/e0e57ff1-6b74-4bf9-a862-c4084f12340f" />

### Tool Windows

<img width="3143" height="1356" alt="POTCO Unity Toolkit windows" src="https://github.com/user-attachments/assets/58e3240e-da14-47e3-8cd0-c2b454ea57b9" />

---

## Known Issues

- The first Unity import can take a very long time.
- Some `.egg` files may still need special handling.
- Very large world imports can make Unity appear frozen while objects are being created.
- Some manually placed objects may need ObjectList data refreshed before export.
- Procedural cave generation can occasionally create overlapping pieces.
- Some visual systems are still being improved and may not perfectly match the original game yet.

---

## Educational Use

This project is intended for educational, archival, research, and community creativity purposes.

Pirates of the Caribbean Online and the original game assets belong to their respective rights holders. This toolkit is not affiliated with or endorsed by Disney, the original POTCO team, or any related rights holders.

Please be respectful with how you use and share original assets.

---

## Set Sail

The goal of this toolkit is simple: make it easier for the POTCO community to explore, create, experiment, and share new ideas inside Unity.

Import old worlds, build new ones, preview assets, create ships, customize NPCs, test gameplay, and make something fun.
