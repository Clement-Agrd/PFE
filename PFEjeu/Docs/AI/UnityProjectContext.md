# Unity project context

Analyzed 2026-10-14. Project: `PFEjeu`; Git revision unavailable in the restricted shell.

## Confirmed environment

- Unity 6000.6.0f1, URP 17.6.0, Input System 1.20.0; legacy input and Input System are both enabled.
- Feature folders under `Assets/Scripts`, prefabs under `Assets/Prefabs`, data under `Assets/ScriptableObjects`, editor tools under `Assets/Editor` and feature `Editor` folders.
- Reviewed features compile into Assembly-CSharp. No first-party assembly definitions were found under Assets/Scripts.
- Runtime UI uses uGUI with TMP. PauseUiKit supplies common visual widgets. GameFreeze owns pause/cursor requests and supports multiple owners.
- Build Settings list MainMenu, Max and SampleScene. ClemA is a development scene and was not added to Build Settings by this task. Startup behavior beyond those settings is unverified.
- No networking dependency is present in the inspected manifest. Broader networking code was not audited.

## Relevant architecture

- `Core.InteractionSystem.Interactor` finds nearby InteractableBase components and invokes their interaction on E.
- `Core.Village.VillageManager` checks town-hall prerequisites and charges HDV storage before calling Building.SetLevel.
- `Core.Village.UI.BuildingPanelUI` receives village selection events; ShowStandalone also owns pause/cursor requests for use outside the village view.
- `VillageTableTrigger` opens VillageViewController. The view freezes player controls, suspends the configured camera driver, selects Buildings by raycast, and restores controls on exit.
- Barracks handles troops, soldier levels and flag placement, using SOBarracks. BarracksVillageUpgrade connects Building.OnLevelChanged to Barracks.SetLevel.
- BarracksConfirmationUI uses PauseUiKit to offer movement, improvement and cancel when an upgrade bridge is configured; its original yes/no mode remains available without that bridge.
- `heros.cs` is a basic MonoBehaviour with Tank, Degats and Invocateur classes, levels clamped to 1–5, change/destruction events, and one assigned barracks per hero. HeroRoster on ClemA's Player owns recruited heroes and starts empty. F8 recruits a random class and level 1–5 during Play Mode; recruited objects are children of Player and are not startup assets.
- Barracks.AssignHero claims/releases ownership and recalculates existing and new troops from the base SOBarracks level data. Four TypeCaserne values are Standard, Tank, Degats and Invocateur. Removing or disabling the hero restores standard stats.
- SOBarracks has configurable profiles: at hero level 1, Tank gives +35% life/defense and -20% damage; Degats gives +35% damage and -20% life/defense; Invocateur gives ceil(base soldier count × 25%) extra soldiers and -10% life/defense/damage. Higher hero levels improve those profiles and respawn time. Baseline assets are not multiplied cumulatively.
- The interaction menu includes Choisir un héros and shows effective troop stats. Its scrollable picker uses the player's HeroRoster, not a preset hero array, and validates roster ownership before assignment. Existing wounds are preserved by Health's normalized-health behavior. Hero switches do not bypass a pending respawn, and surplus troops are disabled before destruction.

## Scene integration in ClemA

- Prefabs_Barracks retains its existing components and now has Building, BarracksVillageUpgrade and BuildingHighlight.
- VillageSystems contains Village_Manager, HDV_Storage and VillageView.
- Village contains Proto_Table_ and the town hall copied from Max.
- VillageCanvas contains Max's BuildingPanel, BuildingPanelUI, InteractPrompt and VillageResourceBar. All player/camera/system references point to ClemA.
- Only the required VillageManager, VillageViewController, VillageInputReader and HDVInventoryLink components were retained on the copied manager. Max's unrelated wave/exploration/teleport systems were not copied.
- `Assets/ScriptableObjects/Village/Caserne.asset` has three levels and optional configurable costs: 10 Bois for level 2, 20 Bois for level 3. It has no visual prefabs; Barracks continues to own its models and troop stats.
- The original Max storage starting resources and town-hall level were retained. Upgrade the town hall first to satisfy level prerequisites.
- Canvas/InventoryUI is copied from Max, with its player, inventory, equipment and village references remapped to ClemA. InventoryUI remains the controller on Canvas. Its four tabs are Inventaire, Village, Carte and Héros. HeroInventoryUI displays recruited heroes, classes, levels and assignments. Tab opens/closes the inventory; Escape closes it. InventoryTabController returns to Inventaire on opening.
- MinimapSystem was copied for the Carte page, retargeted to ClemA's Player and configured to derive bounds from this scene. Three unused duplicate MapContent copies were removed from the imported hierarchy. Max's inventory source scene was not saved during this operation.
- The copied optional ExpeditionStatusUI has no exploration manager in ClemA and correctly displays no expeditions. Exploration gameplay was not imported as part of the inventory task.

## Conventions and tooling

- Feature namespaces, private serialized fields, braces on separate lines, balanced event subscriptions and direct component references.
- Unity Pipeline 0.8.0-exp.1 is installed. Live HTTP status, eval, console, recompile, scene and capture commands work with the project's local bearer token; never save that token in documentation.
- Automatic CLI process discovery is unavailable in this sandbox. Read the project descriptor and use authenticated local commands instead.
- Unity Test Framework is installed; no relevant automated test assembly was found. This feature was validated with live Play Mode smoke checks.

## Validation / limits

- Compilation passed. Live checks covered direct opening, table view/selection, prerequisite and insufficient-stock rejection, cost deductions, levels 2/3, soldier configuration, max level, move/cancel, close, and camera/control restoration.
- Hero validation passed 93 live stat checks across all barracks levels, hero classes, hero levels and returns to standard. Additional checks covered real UI assignment/removal, level clamps, exclusive ownership, disable cleanup, wounds, pending respawns, newly respawned troop stats, upgrades with a hero and flag placement.
- Inventory/recruitment checks passed: startup zero heroes, all four tab buttons, keyboard F8 recruitment while paused, random class and level ranges (all classes and levels observed), roster-only selection, assignment/release status refresh, outsider rejection, hero destruction cleanup, 25-hero page/picker scrolling, last-hero selection, map rendering, keyboard Tab/Escape and pause/cursor release.
- Max had pre-existing Tower.Update null-reference errors in the captured baseline. No new error was recorded during ClemA validation.
- Main evidence: VillageManager.cs, Building.cs, BuildingPanelUI.cs, VillageViewController.cs, VillageTableTrigger.cs, Barracks.cs, BarracksConfirmationUI.cs, BarracksVillageUpgrade.cs, GameFreeze.cs, ProjectVersion.txt, manifest.json, EditorBuildSettings.asset and both scenes in the Editor.

## Barracks comparison UI

- BarracksStatsPreview is a shared uGUI view with 10 rows: soldier count, respawn delay and all eight StatTypes (life, stamina, physical/magic damage, physical/magic defense, movement speed, attack speed). It compares current/future/delta without changing gameplay data. Numerical increases are green, decreases red, unchanged gray.
- Hero picker has comparison on the left and recruited hero choices on the right. Selecting only previews; Affecter ce héros / Retirer le héros confirms. Returning cancels an unconfirmed choice. Current hero/standard selection cannot be redundantly applied.
- BuildingPanelUI shifts the barracks purchase panel left and shows next-level troop comparison on the right, retaining the current hero. Maximum level shows identical values and hides upgrade action. Other buildings retain the original panel layout.
- Live checks passed preview isolation, exact Tank and Invocateur values, color directions, all stat rows, confirmation/cancel, actual upgrade stats matching all previewed stat values, real final-level upgrade, original layout restoration and pause/cursor release. The prerequisite check passed in the initial run; rerunning after changing town-hall level correctly no longer blocked that upgrade.
- Barracks.RefreshHeroStats only spawns newly added capacity; existing empty slots retain respawn timing. An expired empty slot with an assigned hero was tested during Play Mode shutdown: no cleanup error or new console error. Compilation passed; final editor state is ClemA, level 1, zero heroes, no assigned hero, out of Play Mode.
- Layout correction: barracks upgrade panel now retains the original town-hall right-side anchors/pivot/position. Troop comparison sits to its left with a 32-unit gap. Live checks verified identical panel position/right edge, 10 stat rows within screen and no overlap; compilation passed and screenshot verified.

## Captain wave interaction (2026-10-16)

- NPC_Capitaine copied from Max to ClemA at (8,1,8.25), near starting Player. DialoguePanel and CaptainPrompt copied to VillageCanvas; DialoguePanelUI links retargeted to ClemA VillageInputReader and cloned text objects. Max closed without saving.
- WaveGiverNPC references existing WaveSystem WaveSpawner; TowerDefenseLevel left null to use its direct StartNextWave path (no teleport/extra wave system). Existing debug button remains wired.
- Repaired existing WaveSpawner zones[0] null reference to ClemA Spawn_01 (id 1), matching both spawn entries in the wave asset.
- Live checks: range dialogue opens, ready choice, decline without wave, accept closes dialogue and starts existing wave with Enemy_Test/Enemy_Combo appearing; talking during active wave displays combat message. Exactly one captain and one spawner; serialized references verified. No new scripts required. Source scene backup before changes in Codex work/ClemA-before-captain.unity (does not include previously unsaved editor changes).
- Validation limitation: after additive source-scene inspection, URP raised ZBinningJob/RenderGraph errors and subsequent Blitter/ProbeReferenceVolume errors in engine rendering code. Gameplay checks still passed. No rendering settings modified; requested script reload to clear editor state.
- Follow-up: script reload cleared the transient URP state. Re-entered Play Mode for final check: zero console errors, compilation healthy. Stopped Play Mode and saved ClemA.
