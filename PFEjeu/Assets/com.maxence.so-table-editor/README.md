# SO Table Editor

Edit every ScriptableObject of one type in a single spreadsheet-like Unity window.
Made for game designers who balance dozens of enemies, weapons, towers or items and are tired of opening assets one by one.

![Unity 2021.3+](https://img.shields.io/badge/Unity-2021.3%2B-black) ![License MIT](https://img.shields.io/badge/license-MIT-blue)

## Features

- **One table per type**: every asset of the chosen ScriptableObject type, one row each.
- **Inline editing** with full Undo support.
- **Nested structs flattened** into columns (`Stats / Health`, `Stats / Speed`).
- **Sort** by any column (numbers numerically, text in natural order).
- **Filter** by name and by field values: `goblin hp>50 rank=elite`.
- **Multi-row editing**: select several rows, edit one cell, every selected row gets the value.
- **Hide / resize columns** (right-click on the header).
- **Create, duplicate, ping** assets without leaving the window.
- **CSV export** of what is currently visible.
- **Extensible**: plug your own cell drawers through a small public API.

## Installation

### Git URL (recommended)
1. Open `Window > Package Manager`.
2. Click `+` > `Add package from git URL...`.
3. Paste: `https://github.com/<your-account>/com.maxence.so-table-editor.git`

### Local
Copy the `com.maxence.so-table-editor` folder into your project's `Packages/` folder.

## Usage

1. Open `Tools > SO Table Editor`, or right-click any ScriptableObject asset > `Open in SO Table Editor`.
2. Pick a type in the toolbar dropdown.
3. Edit cells directly. Numbers and text are committed on **Enter** or when the field loses focus.

| Action | How |
| --- | --- |
| Select a row | Click its name |
| Multi-select | Ctrl/Cmd + click, Shift + click |
| Edit many rows at once | Select rows, then edit a cell of one of them |
| Ping / select in Project | Double-click the name |
| Row actions | Right-click the name |
| Sort | Click a column header |
| Hide columns | Right-click the header |
| Lists, arrays, big structs | Click the cell button to edit them in the Inspector |

### Filter syntax

Terms are separated by spaces and must **all** match.

| Query | Meaning |
| --- | --- |
| `goblin` | Asset name contains "goblin" |
| `hp>50` | Field `hp` greater than 50 |
| `speed<=2.5` | Field `speed` lower than or equal to 2.5 |
| `rank=boss` | Text/enum/object field contains "boss" |
| `rank=2` | Enum value equals 2 |
| `isFlying=true` | Boolean field is true |
| `loot!=0` | List `loot` is not empty |
| `stats.health>=100` | Field inside a nested struct |

Field names accept the code name (`maxHealth`, `_maxHealth`) or the displayed name without spaces (`MaxHealth`).

## Custom cell drawers

```csharp
using Maxence.SOTable.Editor;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PercentCellDrawerRegistration
{
    static PercentCellDrawerRegistration() => CellDrawerRegistry.Register(new PercentCellDrawer());
}

internal sealed class PercentCellDrawer : ICellDrawer
{
    public bool CanDraw(SerializedProperty property) =>
        property.propertyType == SerializedPropertyType.Float && property.name.EndsWith("Chance");

    public void Draw(Rect rect, SerializedProperty property) =>
        EditorGUI.Slider(rect, property, 0f, 1f, GUIContent.none);
}
```

Custom drawers are checked before the built-in ones, most recently registered first.

## Limitations (v0.1)

- Only assets under `Assets/` are listed (not those inside packages).
- Abstract base types cannot be picked (pick a concrete type and enable **Derived**).
- Lists and arrays are edited in the Inspector, not inline.
- `[Header]` / `[Space]` decorators are hidden inside cells on purpose.

## License

MIT, see [LICENSE.md](LICENSE.md).
