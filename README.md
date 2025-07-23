# 🏴‍☠️ POTCO Unity Tools

A collection of two Unity Editor tools designed for working with assets from *Pirates of the Caribbean Online*:

- 🗺️ **World Data Importer**
- 🛠️ **Procedural Cave Generator**

> Built using **Unity 6000.1.11f1**

> ⚠️ This project uses world data and phase files from a publicly available **Pirates Online Rewritten** GitHub repository.

---

## 📦 Model Conversion Pipeline

- Models converted from `.bam` to `.egg` using **Panda3D’s `bam2egg` tool**
- Imported into Blender using the **egg importer plugin**
- Exported as **FBX** files for Unity

> ✅ All LODs and collision data removed via `egg-trans` to focus purely on visuals.

---

## 🧭 World Data Importer

### 📌 How to Use

1. In Unity, click on the **`POTCO`** menu at the top.
2. Select **`World Data Importer`**.
3. Click **`Select World .py File`**, then choose a world file to import.

> ⏱️ *Importing the largest island, **Padres Del Fuego**, takes around 30 seconds. Most others are near-instant.*

---

### 🐞 World Data Importer Known Issues

- Some objects import at a slight, noticeable **inclination** (possibly due to nested parent-child transforms)
- **Alpha textures** do not render properly
- Some models (like islands) have **broken textures**
- Inconsistent **FBX scale and rotation**

> 🔧 Importing is functional but not yet perfect.

---

## 🧠 Planned Tools

- 🔄 **World Data Exporter**
- 🎦 **Third/First person player controller**

---

## 🛠️ Procedural Cave Generator

### 📌 How to Use

1. In Unity, click on the **`POTCO`** menu at the top.
2. Select **`Procedural Cave Generator`**.
3. Configure your settings:

   - **Cave Length** – number of cave pieces to generate  
   - **Generation Delay** – controls how quickly each piece is spawned  
   - **Cap Open Ends** – automatically caps unconnected connectors with dead-end pieces  

4. Select which prefabs to include and assign their spawn weight.  
   > A **Padres-themed preset** is included for best results.

> ⚠️ Making cave pieces with 4 connectors too common may result in tangled or looping messes.

### 🐞 Procedural Generator Known Issues

- Caves might generate clipping over each other at times
---

## 🖼️ Procedural Cave Generation – Screenshots

![Cave Generation](https://github.com/user-attachments/assets/87328a7a-390d-454c-9fbe-ce272e14ff66)

---

## 🌍 World Data Importer – Screenshots/Videos

### 🍺 Tortuga Tavern

https://github.com/user-attachments/assets/c2c9f232-cbf9-42aa-8c1b-de6bef74e36a

### 🏠 Mansion Interior

<img width="2756" height="1190" alt="Mansion Interior" src="https://github.com/user-attachments/assets/0bc48425-a4c8-4776-bc17-4fcc4a55d750" />

---

### 🧱 Bilgewater Concept  
*(Original concept by the Pirates Online team – console notifies when a model fails to import)*

<img width="2763" height="1644" alt="Bilgewater Concept" src="https://github.com/user-attachments/assets/adaddf6f-5ec0-4484-8188-2a0e98b6a7d8" />

---

### 🏰 Kingshead (Broken Textures)

<img width="2768" height="1191" alt="Kingshead Broken Textures" src="https://github.com/user-attachments/assets/85458c8d-6f7d-48af-b757-96ed46af1cf0" />

---

<img width="2761" height="1129" alt="Additional Import" src="https://github.com/user-attachments/assets/0fb7e76d-fa1f-48d0-bb10-f96840525b09" />

---


