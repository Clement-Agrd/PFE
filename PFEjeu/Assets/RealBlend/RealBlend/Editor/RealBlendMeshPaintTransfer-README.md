# Update Mesh and Transfer Paint

An optional RealBlend editor tool for transferring painted vertex colors onto a revised Blender/FBX mesh. It runs only when you open it and create an updated copy. It adds no model-import callbacks and does not change ordinary painting, sculpting, shader, or ProBuilder behavior.

## Using the tool

See the [usage example](https://drive.google.com/file/d/1gd83T5ocgQ1maOtPFNSNLnbU2ksAD-9r/view?usp=sharing), also available from the tool's **Example usage** button.

1. In Edit Mode, keep the original painted object in your Unity scene. Finish or apply any active paint preview before transferring.
2. Export the revised model from Blender and let Unity import it. Keep the same mesh-local origin, orientation, and scale. Do not manually replace the mesh on the painted object first.
3. Open **Tools > RealBlend > Update Mesh and Transfer Paint**.
4. Choose the original painted scene object and the updated **Mesh** asset. Expand the FBX/model in the Project browser to select its mesh. For a room containing several mesh objects, transfer one supported mesh object at a time.
5. Build the preview. Review the original and transferred vertex colors and the counts of matched, projected, unmatched, and ambiguous vertices. The channel controls include alpha, which carries wetness or a blend layer depending on your shader.
6. Leave projection off to transfer only vertices at matching positions. Enable projection when you want new or moved vertices to inherit nearby surface paint. Keep its distance small enough to avoid borrowing paint from another part of the room.
7. Create the updated painted copy. The generated mesh is saved as a new asset under `Assets/RealBlendTransferredMeshes`. The original object and its painted mesh are retained. The window offers an option to hide the original to avoid overlapping geometry.
8. Inspect the result before replacing the original in your workflow. Rebuild baked lighting for changed geometry. Existing script references and other systems that point to the original object are not automatically redirected.

If Unity reports that a mesh is not readable, enable **Read/Write** on that model's import settings and reimport it. The tool does not change import settings automatically.

## What transfers

All four vertex-color channels transfer together. Vertex numbering and vertex count can change. Unique matching positions retain their original color values. UVs, normals, and material-slot membership help distinguish split vertices that occupy the same position; unresolved conflicting colors retain the updated mesh's original colors instead of choosing arbitrarily.

Optional surface projection interpolates the old triangle's colors, subject to a maximum distance and normal-angle check. It is an approximation for edited geometry. Unmatched vertices keep the updated mesh's vertex colors, or clear RGBA when it has none. Ambiguous vertices are included in the unmatched count.

The preview uses an isolated copy of committed paint, and does not change the scene. Changing source paint, mesh data, or transfer settings requires a new preview before creating a copy.

Colliders that use the original render mesh follow the updated mesh on the copy. Separate custom collision meshes remain as configured and should be reviewed for geometry changes.

## Scope and Undo

This initial tool supports static MeshFilter/MeshRenderer objects. It does not update a model automatically on import, transfer an entire multi-mesh hierarchy in one operation, or edit ProBuilder-owned, skinned, animated blend-shape, or LOD-managed meshes. The window explains unsupported selections before creating anything. Keep material-slot order consistent between exports.

Creating the scene copy and hiding the original are undoable together. **Undo retains the generated mesh asset**, so it can still be inspected or reused. Remove unused generated assets manually when no objects use them. Importing the add-on or merely opening its window changes no existing painted objects.

## Installation

Import the supplied `.unitypackage` into a project that already has RealBlend installed. The add-on consists only of three new editor scripts, its editor preview shader, and this guide. It does not replace RealBlend's existing runtime or editor scripts, materials, or rendering shaders.
