# First-person preview

The **First Person** view renders one empty arm and its sleeve from the selected player skin. It supports Steve, Alex, their legacy Minimal variants, and textures at higher resolutions. The compact control group contains the hand selector, with right hand as the default. The hand preference is saved in `settings.ini`; the view mode itself is session-only.

This is a static skin preview, with no held item, swinging, walking bob, or world scene. Minecraft Java renders the hand with a fixed 70° projection independently of world FOV. The preview uses that projection directly and has no FOV control or saved world FOV preference.

## Pose and references

The pose, projection and lighting were checked against the supplied Minecraft 26.3 source tree and the official [Minecraft Java 26.3 client](https://piston-data.mojang.com/v1/objects/e877b6a07acd633fb3bb475002175cec036e7b87/client.jar), SHA-1 `e877b6a07acd633fb3bb475002175cec036e7b87`, including `FirstPersonHandsAndItemsRenderer.renderPlayerArm`, `AvatarRenderer.renderHand`, `PlayerModel.createMesh`, `Camera.calculateHudFov` and `GameRenderer.render3dHud`. Swing, equip progress and camera sway are zero in this static preview. Underwater and death-animation FOV effects are not simulated.

`FirstPersonPreview.Pose` applies the matrix chain in OpenTK row-vector order, retaining the idle arm roll of ±0.1 radians. All translations are converted to the editor's units of 16 pixels per block. Both Steve and Alex use the modern arm pivot at `(∓5, 2, 0)` pixels; the bundled Alex model's historical 2.5-pixel height is overridden only in this preview. The narrower Alex box still comes from its model data. Sleeves share the corrected arm pivot, removing the historical right-sleeve export's extra Z offset. The left arm uses its own UVs, with a mirrored pose rather than a mirrored texture.

## Editor integration

The view uses an independent model clone. Only part-visibility choices are shared with the regular preview; document geometry, UVs, texture dimensions, model metadata, pixels and history are unchanged by view or hand switches. Rendering and picking filter to the selected arm and sleeve before handling visibility/ghost mode, so the rest of the body and accessories cannot appear or intercept strokes. In Skin + Cape mode, the skin remains visible when Edit Cape is selected, but cannot receive cape edits.

The first-person camera is at the origin, looking along negative Z, with a 0.05-block near plane. Bounds and centers follow the same mesh transforms used for rendering, sorting, lighting and picking. Mesh lighting accounts for the first-person Y-up coordinates. The sleeve always uses its ordinary flat overlay mesh, with its normal transparency and part visibility; first-person items neither build nor pick raised skin-layer geometry. This does not change the saved 3D Skin Layers setting or the geometry used on returning to the regular preview. Existing render-resolution and antialiasing settings apply normally.

Lighting assumes a level camera facing north or south with the selected Overworld light level. In-game colors can vary with camera orientation and world lighting. In First Person, the compact Brightness group remains visible but disabled whenever 3D Skin Layers or Minecraft Lighting is off. Its layout stays the same across both toggles, so the viewport dimensions, hand projection and picking positions remain unchanged; only lighting changes. Other views retain their usual brightness-control visibility.

Camera drags, the wheel and Reset Camera leave the first-person projection and stored orbit and texture-sheet cameras unchanged. Switching to an unsupported model exits First Person and disables its controls until a compatible player skin is selected. The hand group wraps with the tool-options area and uses the English fallback catalog when a translation is unavailable.
