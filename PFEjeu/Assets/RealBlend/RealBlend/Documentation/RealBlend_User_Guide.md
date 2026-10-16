# RealBlend User Guide

RealBlend is a Unity editor tool for vertex painting, mesh sculpting, material blending, and creating paint-ready meshes. This guide starts with the shortest working path, then explains the advanced tools only when you need them.

## Start here

Open the main window from **Tools > RealBlend > Real Blend**.

Choose the path that matches what you already have:

- **Separate PBR textures:** open **Tools > RealBlend > Mask Packer**, choose **Production RealBlend (Texture Arrays)**, and follow "Create a Production material."
- **An existing Standard RealBlend material:** select the material and use **Assets > RealBlend > Convert Selected Material to Production**.
- **Only vertex colors or custom shader data:** use **Paint > Paint Workflow > Vertex Color**. A RealBlend material is not required, but the material on the mesh must display the vertex channels you intend to use.

### Render pipeline support

| Feature | Built-in | URP | HDRP |
| --- | --- | --- | --- |
| Paint, Create, and Sculpt tools | Yes | Yes | Yes |
| Standard three-layer RealBlend shader | Limited legacy shader | Yes | Yes |
| Production texture-array shader | No | Yes | Yes |
| Alpha Cutout shader | No | Yes | Yes |
| 5-Way Blend shader | No | No | Yes |
| HDRP Lit mask profile | No | No | Yes |

The editor tools write normal Unity mesh data, so painting, creation, and sculpting are not tied to a render pipeline. Shader availability is what differs.

### Sample content

The HDRP example scene is at `Assets/RealBlend/Demo/Scenes/HDRP_RealBlendSampleScene.unity`. For a URP project, import `Assets/RealBlend/URP_Demo_Import_Source.unitypackage` to add the URP sample material setup.

## Two-minute wall and painting quick start

1. Open **Tools > RealBlend > Real Blend**, then choose **Create (Beta)**.
2. Select **Wall**. Choose **Surface** for a simple paintable wall, or **Solid** when you need thickness for doorways, windows, or Boolean cuts.
3. Set the wall size, density, and **Default Material**, position its preview in the Scene view, then click **Generate Wall**.
4. Keep the generated wall selected and switch to **Paint**. Create-tab meshes already include the required **MeshCollider** and **VertexPaintStorage** component.
5. Set **Paint Workflow** to **LayeredBlend** and choose a layer, or choose **VertexColor** and select a color.
6. Click **START PAINTING**.
7. Paint with the left mouse button. Hold **Shift** while painting to erase.

To paint an existing mesh instead, select a GameObject with a **MeshFilter** and **MeshRenderer**, switch directly to **Paint**, then use **Fix: Add Mesh Collider** and **Add Data Storage Script** if those prompts appear.

### Useful Scene view shortcuts

| Action | Shortcut |
| --- | --- |
| Paint or sculpt | Left mouse drag |
| Erase paint | Shift + left mouse drag |
| Change paint or sculpt brush size | Ctrl/Cmd + mouse wheel |
| Change opacity or sculpt strength | Ctrl/Cmd + Shift + mouse wheel |
| Change circular sculpt brush size | `[` and `]` |

## Create a Production material - recommended

Production RealBlend is the recommended shader for new URP and HDRP materials. It stores texture sets in shared Albedo, Normal, and Mask texture arrays. This reduces texture and sampler bindings, allows a larger reusable texture library, and avoids several material/lightmap-baking issues that can occur with large conventional graphs.

### Prepare texture names

Keep every texture set in one project folder and use a consistent base name. For example:

- `Brick_BaseColor.png`
- `Brick_Normal.png`
- `Brick_AO.png`
- `Brick_Roughness.png`
- `Brick_Height.png`

Names such as Albedo or Diffuse, Normal, Metallic or Metalness, AO or Ambient Occlusion, Roughness, Smoothness or Gloss, and Height are detected automatically. When both Roughness and Gloss/Smoothness exist, the smoothness source is used directly and the roughness source is left untouched.

### Build the material

1. Open **Tools > RealBlend > Mask Packer**.
2. Set **Target Shader** to **Production RealBlend (Texture Arrays)**.
3. Set **Source Folder** to the folder containing your texture sets.
4. Enable **Include Subfolders** only when you want to process them. Sets from different folders are never mixed.
5. Leave **Create Materials** enabled.
6. In **Production Material Texture Sets**, choose the ordered **Base**, **Layer 1**, and **Layer 2** sets.
7. Click **Pack Source Textures + Build Production Material**. If **Create New Production Material** is enabled, the button says **Pack Source Textures + Create New Production Material**.
8. Assign the generated material to your mesh.

RealBlend chooses the Production HDRP or URP graph for the active pipeline. Every detected set receives a stable slice in the shared folder library, so more than one material can reuse the same arrays without changing existing slice identities.

Keep each Production texture library in its own folder. Array memory grows with texture resolution and slice count; the Mask Packer estimates large writes before creating them.

### Use already-packed RealBlend masks

If a project already contains masks packed as R=Metallic, G=AO, B=Height, A=Smoothness, select the folder and use **Build Production Material from Packed Masks**. Enable **Create New Production Material** when you want another material that reuses the same compatible array library.

Explicit `RealBlendMaskMap` or `RealBlendMask` names are accepted. HDRP-labelled Mask Maps and Detail Masks are excluded. A generic `MaskMap` or `Mask` name requires confirmation because its channel layout is ambiguous.

## Convert a Standard material to Production

The fastest method is:

1. Select one or more saved Standard RealBlend materials in the Project window.
2. Choose **Assets > RealBlend > Convert Selected Material to Production**.
3. Assign the generated `_Production` material where needed.

You can also open the Mask Packer, drop one material into **Standard Material Conversion > Standard Material**, and click **Convert to Production Material**.

Conversion creates a new Production material and a dedicated three-slice texture-array library beside the source. It copies supported layer settings and material flags. The source material and source textures are not changed.

Automatic conversion supports saved three-layer Standard RealBlend HDRP or URP materials for the active pipeline. Alpha Cutout, 5-Way, Built-in, custom shaders, and materials that are already Production are not converted automatically.

Missing Albedo, Normal, or Mask inputs use safe white, flat-normal, or white-mask fallbacks. Review Console notes after conversion when a fallback was needed.

## Mask Packer reference

The Mask Packer has three common uses:

- **Automatic Folder Conversion:** detects consistently named texture sets, packs masks, and can create materials or Production arrays.
- **Manual Packing:** packs one mask from fields you assign yourself.
- **Standard Material Conversion:** migrates an existing three-layer Standard material to Production.

### Correct mask channel layouts

| Profile | Red | Green | Blue | Alpha |
| --- | --- | --- | --- | --- |
| RealBlend and Production | Metallic | Ambient Occlusion | Height | Smoothness |
| HDRP Lit | Metallic | Ambient Occlusion | Detail Mask | Smoothness |

When **Input Is Smoothness** is off, a Roughness texture is inverted into Smoothness. When it is on, Smoothness or Gloss is copied directly.

Missing inputs use the values under **Defaults for Missing Inputs**. **Output Width** and **Output Height** set to 0 use the largest assigned input. **Preserve Source Resolution** temporarily bypasses lower importer limits while packing. Leave **Compress Output** off when exact mask values matter.

### Automatic folder conversion

1. Choose the correct **Target Shader** profile.
2. Select **Source Folder** or click **Use Project Selection**.
3. Decide whether to include subfolders and overwrite existing generated masks.
4. Enable **Create Materials** when you want setup completed for you.
5. Review the detected sets and warnings.
6. Run the action button.

Automatic conversion skips ambiguous sets and does not combine similarly named files from different folders.

### Delete source channel textures

Enable **Delete Source Channel Textures** only when you no longer need the separate Metallic, AO, Roughness/Smoothness, and Height/Detail inputs.

Cleanup happens only after the packed mask and any requested material or arrays are created successfully. Base Color, Normal, Emissive, generated packed masks, and skipped inputs are kept. Deleted files go to the operating system Recycle Bin/Trash and can often be recovered there, but deletion is not a Unity Undo operation. Building from existing packed masks never deletes source files.

## Painting

### Choose a paint workflow

**LayeredBlend** paints shader layer weights. The default RealBlend mapping is:

| Palette entry | Vertex data | Purpose |
| --- | --- | --- |
| Base | Remaining weight | The material visible where no blend layer wins |
| Layer 1 | Green | First blended texture set |
| Layer 2 | Blue | Second blended texture set |
| Wetness | Alpha | Wetness amount |
| Variation | Red | A/B texture variation amount |

**VertexColor** paints a direct RGBA value. Use it for custom shaders, masks, tint data, or any workflow that reads standard Unity mesh vertex colors. **Channels** can limit painting to selected RGBA components. **Show Color Overlay** previews the data even if the assigned material does not show it. Saved color palettes can be reused from the palette controls.

Vertex alpha and brush opacity are different things. Vertex alpha is one stored data channel. **Opacity** controls how strongly a brush stroke moves any selected target channel toward its target value.

### Brush settings

- **Lock Selection:** prevents an accidental object switch while painting.
- **Size:** world-space brush radius.
- **Opacity:** strength of the paint operation.
- **Hardness:** softness of the brush edge.
- **Mouse Sensitivity:** how strongly repeated drag events build toward the target in Continuous mode.
- **Brush Mask (Red):** uses the red channel of a readable Texture2D as stamp coverage. Rotation and inversion appear when a mask is assigned.
- **Show Preview:** previews the result under the cursor.

If a brush mask is not readable, click **Enable Read/Write for Brush Mask**.

### Continuous opacity or Limit Opacity Per Stroke

Open **Advanced Brush Settings** and choose the behavior you want:

- **Limit Opacity Per Stroke off - Continuous:** Opacity is applied again on drag events. Scrubbing over one place can quickly build toward the target. Lower **Mouse Sensitivity** for slower buildup.
- **Limit Opacity Per Stroke on - Capped:** one click-and-drag can contribute at most the selected Opacity. At 0.5, a single stroke reaches no more than half of the remaining blend. Release and paint again to build farther. Brush falloff and masks can reduce the result.

Flood operations fill the target directly and do not use brush Opacity.

### Procedural Dirt / Grime

Use the **Procedural Dirt / Grime** foldout to cover large areas quickly.

- **FullSurface:** distributes the active layer or color across the surface.
- **EdgeBand:** concentrates it near borders. **Use UVs for Edge** is useful on curved objects and pipes.
- **Coverage, Edge Softness, Opacity, and Edge Width:** control the result.
- **Randomize Pattern, Preview, Apply, and Clear Preview:** let you test a pattern before committing it.

### Paint Target for multi-material meshes

- **Automatic:** follows the visible submesh triangle under the mouse, updates the palette from that submesh material, and does not paint through the wall.
- **Selected Submesh:** locks painting to one selected submesh and material section.

RealBlend preserves submesh divisions so a mesh with multiple materials keeps its material-to-face assignment after painting and prefab creation.

### Advanced Layer Settings

The default layer labels, channels, overlay modes, and shader texture property names match the supplied RealBlend shaders. Change these only for an intentional custom shader mapping. With the HDRP 5-Way shader, Green, Blue, Alpha, and Red are used as blend layers 1 through 4, so the usual Wetness and Variation meanings are repurposed.

## Production RealBlend shader

Production RealBlend is supplied for HDRP and URP. It provides:

- Three ordered material layers selected from shared Albedo, Normal, and Mask texture arrays.
- The normal Green/Blue layer blend, Red variation, and Alpha wetness workflow.
- UV and triplanar modes through a shader keyword.
- An optional **Wetness Mask**. Its red channel limits painted wetness, which is useful for leaks, stains, and damp patches.
- POM/parallax controls using the packed height channel.

To use POM, increase **Height Amplitude** and **Height Steps**, then adjust them to suit the material.

## Sculpt and cut

Choose **Sculpt (Beta)** in the Real Blend window. Sculpting modifies actual mesh geometry and normally needs enough nearby vertices for a smooth result.

| Tool | Use |
| --- | --- |
| Pull (Move) | Pull vertices along the surface direction |
| Push | Push vertices inward |
| Smooth | Relax uneven geometry |
| Revert Pose | Move vertices toward the stored base pose |
| Refine (Add Detail) | Split local topology to add sculpting detail |
| Remove | Delete existing front-facing faces under a circle or square brush |
| Nudge (Drag) | Slide points along the hit plane |
| Flatten Height | Pull vertices toward a local X, Y, or Z height |
| Edit Elements | Move, rotate, or scale a selected vertex, edge, or face |
| Boolean Cut | Subtract one closed mesh from one or more target meshes |

Refine changes topology. In refined areas, Revert Pose cannot fully reconstruct the original layout. Coincident seam vertices move together in Edit Elements to help prevent cracks.

### Remove or Boolean Cut?

Use **Remove** when you want a fast, topology-based deletion from the mesh that is already there. Use **Boolean Cut** when the opening must follow the precise volume of another object.

### Remove

- **Square:** removes every front-facing face the square brush touches. This makes door and window selections less awkward than requiring every face vertex to lie inside the brush.
- **Circle:** removes selected faces. Enable **Round Existing Rim** to move an existing closed boundary toward the circular brush. It does not insert new vertices or triangles, so denser surrounding topology makes a rounder result.
- **Cut Through Solid Walls:** with a Square brush on a generated **RealBlend Solid Wall**, removes aligned front and back grid cells and joins the opening with reveal faces. Other meshes keep normal surface Remove behavior.
- **Separate Interior Submesh:** places solid-wall reveal faces in their own submesh. Assign **Interior Material**, or leave it empty to copy material slot 0 initially.

Each committed Remove stroke creates one Undo step.

### Boolean Cut

1. Select **Boolean Cut** in Sculpt.
2. Assign a **Cutter Mesh**, or select it in the scene and click **Use Selected as Cutter**.
3. Add one or more **Target Meshes**. **Auto Add Intersecting Targets** finds nearby meshes where the cutter removes real volume.
4. Enable **Leave Cut Open** when you want an open boundary instead of cutter-derived interior faces.
5. If the cut is closed, optionally enable **Separate Interior Submesh**, assign an **Interior Material**, and choose a **Cut Vertex Color**.
6. Choose whether to **Hide Cutter After Cut**.
7. Click **Apply Boolean Cut**, or **Apply Boolean Cut to N Targets**.

Multi-target cuts are all-or-nothing: if one target fails validation, none are changed. A successful batch creates one Undo step and leaves the original mesh assets unchanged.

#### Boolean advanced settings

- **Seam Tolerance:** welding tolerance for nearby cut vertices. Start with the default. Extremely small values can create excessive work without improving the result.
- **Per-Mesh Triangle Limit:** safety limit for input complexity.
- **Resource Budget:** choose **Standard** for simpler cuts, **Expanded** for most complex cuts, or **High** when an operation needs additional time or memory.

For the most reliable result, use readable triangle meshes that are closed, have real overlapping volume, and avoid extremely thin or self-intersecting surfaces.

### Topology management

**Revert Topology & Keep Paint** restores the original triangles and projects current colors onto them. **Recalculate Normals** refreshes shading after geometry changes. Check **Mesh Statistics** before a very large operation.

## Create paintable meshes

Choose **Create (Beta)**. Position the Scene view cursor where the preview should appear, adjust the settings, and click **Generate**.

| Mode | What it creates |
| --- | --- |
| Floor | A paint-ready XZ grid |
| Wall | A Surface wall, a closed Solid wall with thickness, or a custom profile wall |
| Curved Wall | An arc with adjustable curvature, completeness, and pivot location |
| Box | An inward- or outward-facing box with optional top, bottom, and bevels |
| Spline | A tunnel along a Unity SplineContainer, with profiles, presets, caps, and an optional separate floor |

**Unity Splines is optional.** Only the Spline tunnel creation mode requires the Splines package. If it is not installed, that mode shows an **Open Splines in Package Manager** button; painting, sculpting, and the other creation modes remain available. Already generated tunnel meshes do not require Splines to render or edit in RealBlend.

Shared settings include **Density (Verts/m)**, **Default Material**, UV tile size and rotation, raycast layers, preview controls, and increment snapping. Higher density gives painting and sculpting more detail but increases triangle count. **Smart Preview** keeps large previews responsive.

For doorway and window thickness cuts, create a rectangular Wall with **Wall Geometry > Solid**, set **Thickness (Z)**, then use Square Remove with **Cut Through Solid Walls** or use Boolean Cut.

## Saving, prefabs, and baking

**VertexPaintStorage** stores vertex colors, positions, UV channels, normals, tangents, topology, and submesh index buffers. Prefabs keep painted data and multi-material submesh ordering without requiring **Bake to Asset**.

To create a prefab, paint or sculpt the object, finish the current stroke, then drag the object from the Hierarchy into the Project window.

### Bake to Asset - optional

Use **Bake to Asset (Permanent)** when you want a standalone mesh asset, generated secondary lightmap UVs, or a final GameObject that no longer needs VertexPaintStorage.

1. Select the GameObject.
2. In **VertexPaintStorage > Mesh Management**, choose the **Bake Folder** if needed.
3. Click **Bake to Asset (Permanent)**.
4. Choose whether to remove VertexPaintStorage after the bake.

The default folder is `Assets/BakedVertexPaintMeshes`. Baking creates a new uniquely named mesh asset and assigns it to the MeshFilter and MeshCollider. It does not overwrite the original imported mesh.

**Force Rebuild Mesh** reconstructs the editable mesh from stored data when a manual refresh is needed.

### Legacy prefab repair note

If a previously saved prefab has collapsed submeshes, restore its original multi-submesh mesh, paint once, then save or bake the prefab again.

## ProBuilder workflow

When a ProBuilderMesh component is attached, ProBuilder owns topology and its compiled MeshFilter output.

- Vertex painting is supported. RealBlend sends completed paint to ProBuilder automatically, and completed ProBuilder edits are captured back into VertexPaintStorage.
- Use **Sync From ProBuilder** to rebuild ProBuilder's compiled mesh and adopt its current geometry and colors as RealBlend state.
- Use **Send Paint to ProBuilder** to map stored RealBlend colors onto ProBuilder without replacing its topology.
- RealBlend Sculpt, Remove, Refine, Edit Elements, storage-side topology rebuild, and Bake to Asset are disabled while ProBuilder owns the mesh. Finalize or remove the ProBuilder component before topology-changing RealBlend work or a permanent bake.

## Shader guide

| Shader | Pipeline | Best use |
| --- | --- | --- |
| RealBlend Production | URP and HDRP | Recommended three-layer texture-array workflow, wetness, variation, UV/triplanar, optional POM |
| RealBlend Variation | URP and HDRP | Compatible Standard three-layer workflow with separate textures per layer |
| RealBlend Alpha Cutout | URP and HDRP | Three-layer blend where painted Layer 2 can clip the surface |
| RealBlend 5-Way Blend | HDRP | Five texture layers; uses all four RGBA channels as blend weights |
| RealBlend Built-In Variation | Built-in | Legacy basic blending with fewer features and no Production arrays |

### Alpha Cutout shader

The HDRP and URP Alpha Cutout graphs use the alpha channel of the **Layer 2 Albedo** texture where Layer 2 is painted. **Layer2 Alpha Clip Threshold** controls the cutoff. Use it for broken plaster, torn surfaces, foliage-like holes, grime breakup, or similar painted reveal effects.

### Custom shaders and Shader Graph

RealBlend writes ordinary Unity mesh vertex colors. Any shader can read `COLOR`/Vertex Color RGBA and decide how to use those values.

There is no packaged plug-and-play Shader Graph Sub Graph that can be dropped into every custom graph. The supplied Shader Graphs and custom-function include files are working references that can be inspected or adapted. Keep the default RealBlend mapping only when it matches your shader: Green=Layer 1, Blue=Layer 2, Alpha=Wetness, and Red=Variation.

## Troubleshooting

### I cannot paint

- Confirm a MeshFilter and readable mesh are present.
- Add a MeshCollider, or click **Fix: Add Mesh Collider**.
- Add VertexPaintStorage, or click **Add Data Storage Script**.
- Click **START PAINTING** and confirm the object is included in **Paint Raycast Layers**.
- If **Paint Target** is locked to a submesh, confirm the cursor is on that section.

### Opacity 0.1 seems to reach 1 too quickly

Continuous mode reapplies Opacity during mouse-drag events. Lower **Mouse Sensitivity**, or enable **Advanced Brush Settings > Limit Opacity Per Stroke** so one click-and-drag cannot exceed the selected opacity contribution.

### Vertex alpha looks different from brush opacity

This is expected: vertex alpha is stored mesh data, while brush Opacity controls how quickly the selected channel moves toward its target.

### The Mask Packer does not detect my textures

- Put each related set in the selected Assets folder.
- Use one consistent base name and recognized suffixes such as BaseColor, Albedo, Normal, Metallic, AO, Roughness, Smoothness/Gloss, and Height.
- Enable **Include Subfolders** only when needed, then use **Refresh**.
- Resolve duplicate or ambiguous candidates shown in the warnings.

### Roughness or smoothness looks inverted

For manual packing, turn **Input Is Smoothness** on for Smoothness/Gloss and off for Roughness. Automatic packing uses Smoothness/Gloss directly when both kinds exist.

### Height or POM looks flat

Height is stored in the blue channel of RealBlend and Production masks. If POM appears flat, increase **Height Amplitude** and **Height Steps**.

### A circle Remove result looks blocky

**Round Existing Rim** moves existing boundary vertices and adds no topology. Refine the surrounding mesh before removal, generate the mesh at a higher density, or use a smooth circular Boolean cutter for a precise result.

### Remove does not cut through a wall

Use a Square brush on a generated **RealBlend Solid Wall** and enable **Cut Through Solid Walls**. Thickness cutting is available only for Square Remove on generated Solid Walls.

### Boolean Cut fails or takes too long

- Confirm both target and cutter are readable triangle meshes with closed volume and real overlap.
- Use separate, non-parented GameObjects with nonzero scale. A Collider by itself is not a Boolean operand; the object needs a readable MeshFilter mesh.
- Return **Seam Tolerance** to its default before trying very small values.
- Check **Per-Mesh Triangle Limit**.
- Move from **Standard** to **Expanded** or **High** only when the mesh needs more bounded time or memory.
- Simplify self-intersections, extremely thin pieces, or excessive input topology.

### Sculpt controls are disabled on a ProBuilder mesh

ProBuilder owns the topology. Vertex paint is supported, but finalize or remove ProBuilder before RealBlend topology changes or baking.

### Undo changed data but the view looks stale

Finish the active stroke before undoing. If the view remains stale, use **Force Rebuild Mesh**.

## Feature overview

- **Automatic Mask Packing:** detects texture sets in folders, packs masks correctly, and can create configured materials.
- **Production RealBlend Shader:** HDRP and URP texture-array shaders with reusable libraries and optional wetness masks.
- **Standard-to-Production Conversion:** creates a Production material from a supported three-layer Standard material.
- **Wetness Mask:** limits painted wetness for leaks, stains, and damp patches.
- **Delete Source Channel Textures:** optionally cleans up source channels after packing.
- **Boolean Cut:** supports open cuts, interior submeshes and materials, multiple targets, and atomic Undo.
- **Remove:** supports touched-face square selection, circle rim rounding, and thickness cuts through generated solid walls.
- **Undo:** records paint and sculpt work at stroke level.
- **Alpha Cutout Shader:** HDRP and URP variants for painted Layer 2 cutout effects.
- **Limit Opacity Per Stroke:** caps each click-and-drag at the chosen opacity.
- **Multi-material and prefab preservation:** keeps submesh divisions and material order.
- **ProBuilder Mesh Syncing:** shares vertex paint while respecting ProBuilder topology ownership.
- **Paint Target:** automatic or selected-submesh painting for multi-material meshes.
- **Create tools:** Floor, Surface/Solid/Custom Wall, Curved Wall, Box, and Spline tunnel modes.

## Quick reference

- Main window: **Tools > RealBlend > Real Blend**
- Mask Packer: **Tools > RealBlend > Mask Packer**
- Convert selected Standard material: **Assets > RealBlend > Convert Selected Material to Production**
- Paint: left mouse drag
- Erase: Shift + left mouse drag
- Brush size: Ctrl/Cmd + mouse wheel
- Opacity/strength: Ctrl/Cmd + Shift + mouse wheel
- Default bake folder: `Assets/BakedVertexPaintMeshes`
- RealBlend/Production mask: R Metallic, G AO, B Height, A Smoothness
