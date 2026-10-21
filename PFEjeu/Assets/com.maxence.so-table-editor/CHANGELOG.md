# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-19

### Added
- `Tools > SO Table Editor` window listing every asset of a ScriptableObject type as a table.
- Type picker with search, limited to types that have at least one asset under `Assets/`.
- `Assets > Open in SO Table Editor` context menu on any ScriptableObject asset.
- Inline editing for ints, floats, strings, bools, enums, colors, vectors, object references, curves and gradients.
- Nested serializable structs/classes flattened into columns (two levels deep).
- Column sorting (natural order for text), resizing and hiding (right-click on the header).
- Filter bar: name terms plus field conditions such as `hp>50`, `rank=boss`, `isFlying=true`.
- Multi-row editing: Ctrl/Cmd or Shift click names, then edit one cell to apply the value to every selected row (single undo step).
- Row context menu: ping, reveal in file browser, duplicate.
- One-click asset creation for the selected type.
- CSV export of the visible rows and columns.
- Public `ICellDrawer` / `CellDrawerRegistry` API for custom cell drawers.
