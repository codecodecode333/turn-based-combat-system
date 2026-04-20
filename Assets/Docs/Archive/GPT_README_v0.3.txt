GPT_README.txt
(Isometric Tactical Combat Project — GPT Context File)

Version: Snapshot v0.3
Date: 2026-03-07

---

1. Project Overview

---

This project is an **Isometric Tactical Grid Roguelike**.

Core characteristics

* Isometric grid battlefield
* Turn-based tactical combat
* 3 player characters
* 4-6 enemy units
* Skill-based combat system
* Roguelike progression

Combat focuses on

* positioning
* skill usage
* tactical decision making

Design reference

GAME_DESIGN_DOCUMENT.txt

---

2. Core Combat Loop

---

Each unit turn:

Move
+
Use Skill

Turn Flow

Select Unit
→ Plan Move
→ Plan Skill
→ Confirm
→ Execute

UX follows a **planned action system**

Preview → Confirm → Execute

Reference

UX_RULES.txt

---

3. Core Runtime Systems (Code)

---

Important runtime scripts

BattleController
Main battle loop

* turn management
* skill execution
* AI turn

BattleInput
Player input

* click handling
* hover preview
* skill targeting

GridManager
Grid logic

* BFS movement
* tile occupancy
* grid ↔ world conversion
* pathfinding

Example

Reachable tiles are calculated via BFS.



Unit
Unit runtime data

Contains

HP
stats
statuses
skills
grid position

---

4. Skill System

---

Defined using

SkillData (ScriptableObject)

SkillData contains

skillName
animationTrigger
effects[]
targetMode
range
AOE radius

Targeting modes include

AutoNearestSingle
ClickSingle
ClickTileAOE
AllEnemiesInRange
AllEnemiesAnywhere

Example definition



Effects are implemented using

SkillEffect

---

5. Target Resolution

---

Targeting rules must be consistent between

Preview
Confirm
AI evaluation

Core rule

ResolveTargets()

must be shared by

Player
AI
Preview system

Design reference

SKILL_SYSTEM_DESIGN.txt

---

6. Grid System

---

GridManager handles

tile occupancy
movement
pathfinding
grid coordinate system

Movement

* BFS reachable tiles
* Manhattan distance
* 4-direction neighbors

GridManager also supports

isometric coordinate conversion

---

7. AI System

---

Enemy AI uses a **utility based planner**

Main script

AIPlanner

Configurable parameters

AIProfile

Example parameters

maxTilesToEvaluate
topK
weightDamage
weightKill
weightThreat
weightKeepRange

Example definition



AI evaluates

move tile
skill
target

based on expected utility.

Reference

AI_SYSTEM_DESIGN.txt

---

8. Status System

---

Status effects follow lifecycle

Apply
→ OnTurnStart Tick
→ Duration decrease
→ Remove

Stacking policy

StrongestOnly + DurationMax

Reference

STATUS_SYSTEM_DESIGN.txt

---

9. Tile System

---

Tiles may contain

passable
blocksLOS
heightLevel
hazardType

Height primarily affects

movement rules

Reference

TILE_SYSTEM_DESIGN.txt

---

10. Combat Rules

---

Damage formula

FinalDamage = BaseDamage + Attack − Defense

Minimum damage

1

Friendly fire

AOE affects both enemies and allies.

Reference

COMBAT_RULES.txt

---

11. Current Development Stage

---

Already implemented

Grid BFS movement
SkillTargetMode system
Hover AOE preview
Tile highlight overlays
Utility AI
SkillEffect system

Reference

GAME_DESIGN_DOCUMENT.txt

---

12. Next Development Focus

---

Current roadmap priority

1. Move Ghost + Confirm UX
2. PreviewPosition based skill preview
3. ActionQueue implementation

Reference

DEVELOPMENT_ROADMAP.txt

---

13. Important Design Principle

---

The following systems must always share the same logic

Preview
Player execution
AI decision

Violating this rule will cause

AI mismatch
Preview inconsistency
Combat bugs.

---

## End of File
