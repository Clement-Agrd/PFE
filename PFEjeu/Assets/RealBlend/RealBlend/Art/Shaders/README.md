# RealBlend shader catalog

RealBlend keeps its established shader assets at their original paths so material GUIDs,
`Shader.Find` names, Asset Store upgrades, and customer editor scripts remain compatible.
Do not rename or move those files in an update.

## Recommended for new materials

- `RealBlendVariationHDRP.shadergraph` — three layers, variation, and wetness
- `RealBlendVariationURP.shadergraph` — three layers, variation, and wetness
- `RealBlend5WayBlendHDRP.shadergraph` — five layers
- `RealBlend5WayBlendURP.shadergraph` — five layers

The standard Variation and 5-Way pairs use **four material sampler states**: one shared across
all Albedo textures, one across all Normal textures, one across all Mask textures, and the
existing Linear Repeat sampler for POM. Each of the three surface samplers inherits the
corresponding **Base texture's** filtering, wrapping, anisotropy, and sampler mip bias. Use
matching sampling settings within each texture group to preserve the previous appearance;
the other layers follow the Base texture's sampler settings.

The Variation pair retains all nine layer textures; the 5-Way pair retains all fifteen.
Resolutions, UVs, layer blending, normal processing, and POM calculations are preserved.
The sampler changes reduce material bindings from ten to four for Variation and sixteen to
four for 5-Way, not the number of texture reads. Lighting, cookies, shadows, and other
render-pipeline features can consume additional bindings. Existing HDRP 5-Way materials
receive the sampler fix automatically without conversion.

In 5-Way, painted G/B/A/R control Layers 1/2/3/4, with Base underneath. Alpha is a blend layer
in this shader; the three-layer Variation pair retains its existing variation and wetness channels.

### Standard Variation wetness mask

Both standard Variation shaders expose an optional **Wetness Mask** (`_WetnessMask`). Its red
channel multiplies painted vertex-alpha wetness: white preserves it, black suppresses it, and
gray attenuates it. The mask defaults to white, so existing materials and unassigned masks
keep the previous wetness behaviour. Existing wetness properties and RGB paint channels are
unchanged. Wetness Tiling and Wetness Offset apply to mesh UV0, with defaults `(1,1)` and `(0,0)`.
Use a linear/data texture for predictable mask values.

The wetness mask reuses the existing Linear Repeat sampler, retaining four material sampler
states. It adds one texture read; its own texture filter/wrap settings do not change that sampler.

### Mask Packer setup

Open **Tools > RealBlend > Mask Packer**. New windows default to standard **RealBlend**. Use the
visible **3 Layers / 5 Layers** buttons; the active render pipeline automatically chooses the
HDRP or URP shader. The detected pipeline appears below the buttons.

**Follow Project Folder** is enabled by default and follows the currently open Project browser
folder, even when a different asset remains selected. Assigning a Source Folder or using
**Choose...** pins that folder by turning following off. **Use Open Project Folder** resumes
following. Enable **Include Subfolders** when needed. Turn following off before browsing to a
different folder to assign individual textures, so the source set and layer choices stay in place.

The expanded texture picker shows larger previews and direct **Albedo / Normal** assignment
fields. Automatic detection understands common texture suffixes and uses existing RealBlend
material bindings to associate renamed packed masks with their textures. Dedicated wetness
masks are excluded. Ambiguous matches remain unresolved until assigned explicitly. Manual
assignments survive rescans and window reloads; **Use Automatic Matches** clears them.

With **Create Materials**
enabled, assign texture sets to the Base and Layer slots using the preview picker or dropdowns;
the arrows change their order. A set can be intentionally reused in multiple slots. Only selected
source sets are packed for standard RealBlend materials. Use **Pack Selected Sets + Create
3-Layer Material** or **Pack Selected Sets + Create 5-Layer Material** to pack masks and assign
each layer's Albedo, Normal, and Mask textures automatically. Already-packed masks can use
**Create Material from Selected Packed Masks** instead.

The packed layout is R=Metallic, G=AO, B=Height, A=Smoothness. Generic packed-mask names require
layout confirmation; HDRP Lit Mask Maps use a different blue channel. **Always Create New Asset**
preserves existing materials by giving each result a unique name. When disabled, only generated
materials with the matching shader can be updated; user-owned collisions are left unchanged.

## Production (deprecated)

- `Optimized/HDRP/RealBlendProductionHDRP.shadergraph`
- `Optimized/URP/RealBlendProductionURP.shadergraph`

Production remains available for existing projects, with its asset paths, GUIDs, shader names,
and properties preserved. The Mask Packer profile and conversion menu are marked **(deprecated)**.
Use standard RealBlend or 5-Way for new materials.

The Production pair uses Albedo, Normal, and packed Mask texture arrays. It keeps RealBlend
vertex-layer blending and parallax/POM controls, offers a local UV/triplanar shader keyword,
and multiplies painted vertex-alpha wetness by an optional wetness-mask texture. Use the Mask
Packer's **Production RealBlend (deprecated)** profile to build compatible arrays and a
material without overwriting user-owned assets. To migrate an existing three-layer Standard
RealBlend material, assign it under **Production Conversion (deprecated)** in the Mask Packer or use
**Assets > RealBlend > Convert Selected Material to Production (deprecated)**. Conversion creates a dedicated
array library beside the material, preserves its shared layer settings, and leaves the source
material and textures unchanged. Existing projects can use **Create New Production Material from
Packed Masks (deprecated)** or **Build Production Material from Packed Masks (deprecated)** when
their masks are already packed as R=Metallic, G=AO, B=Height, A=Smoothness; the
original channel textures are not required. Generic `MaskMap`/`Mask` names require explicit
layout confirmation, while HDRP-labelled Mask Maps and Detail Masks are excluded.

Texture arrays reduce texture/sampler bindings; they do not turn a smooth three-layer blend
into one sample. UV mode samples three slices from each array, plus the optional wetness mask,
and POM adds height raymarch samples. Triplanar mode samples three projections per layer. The
generated arrays use mipmapped RGBA32 storage, so memory still scales with resolution and slice
count; the Mask Packer estimates large libraries before writing them. Slice identities are
stored with the shared library so adding a set or switching HDRP/URP cannot reorder existing
material indices. Optional source cleanup removes only the separate Metallic/AO/surface/height
channel textures after the packed mask and folder-level array build both succeed. Packed masks,
Base Color, Normal, and Emissive textures are always retained for safe rebuilds and later appends.
For an established manifest slice, rebuilds fail closed if its Base Color or Normal is missing or
ambiguous, preventing an existing slice from being silently replaced by white/flat fallbacks.
If a new slice is intentionally created with one of those fallbacks, add its matching Base Color and
Normal textures before the next rebuild or append; the folder update will otherwise stop safely.

The deprecated conversion section is at the bottom of the Mask Packer. Manual channel packing
is available under **Manual Mask Packing**, and legacy profiles remain under **Other Mask Profiles**.

## Other supported compatibility shaders

- `RealBlendBuiltInVariation.shader`
- `RealBlendAlphaCutOutHDRP.shadergraph`
- `RealBlendAlphaCutOutURP.shadergraph`

These are compatibility assets. Their paths, GUIDs, shader names, includes, and public properties
are preserved.

## Internal tooling

- `VertexColorPreview.shader` is the Scene-view vertex-color overlay.
- `MaskPacking/Editor/MaskPacker.shader` and `TextureArrayPacker.shader` are editor-only packing
  helpers and are not material shaders.
