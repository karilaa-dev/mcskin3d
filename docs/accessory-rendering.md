# Attaching cape and elytra previews

The combined workspace uses Minecraft's standing cape and elytra transforms. Model coordinates are pixels, with Y pointing down and positive Z behind the player. The torso extends from Z=-2 to Z=2, and its upper edge is Y=0 for Steve, Alex, and the legacy variants.

Both accessory layers attach at (0, 0, 2): Minecraft's 0.125-block layer translation multiplied by 16 pixels per block. Do not add further spacing for the skin's jacket. The previous preview used 2.5 for capes and 3.5 for elytra, producing a visible gap.

The cape turns 180 degrees around Y, then tilts 6 degrees around X from its top edge. Its original local cuboid and UVs remain intact. Elytra use mirrored pivots at X=+5 and X=-5, Y=0; the resting pitch/roll are 15/-15 degrees and the full spread pitch/roll are 20/-90 degrees, with the other wing's roll mirrored. OpenTK row-vector matrices apply pitch, roll, then the pivot translation. Spread represents the wing pose on a standing preview, without rotating the player into a flying animation.

The wing slider interpolates pitch from 15 to 20 degrees and roll magnitude from 15 to 90 degrees. Its 0% and 100% positions exactly retain the original Folded and Spread transforms. Intermediate changes update existing mesh matrices and bounds without reloading textures, changing the camera, or recording history.

The historical bundled elytra export names Minecraft's left wing `RightArm` and starts its box at X=-14. Vanilla uses X=-10. A local +4 translation corrects this only in the independent preview; the original model, inflated box dimensions, mirrored UVs, and texture metadata are preserved. Every transformed vertex contributes to the same bounds used by picking and transparent-face sorting.

Sources checked September 17, 2026:

- [Minecraft Cape layer](https://github.com/Marcelektro/MavenMCP-1.8.9/blob/master/src/main/java/net/minecraft/client/renderer/entity/layers/LayerCape.java): attachment offset, resting tilt, and turn.
- [Minecraft Elytra layer](https://github.com/KealJones/mc-1.12.2-source_files/blob/master/src/minecraft/net/minecraft/client/renderer/entity/layers/LayerElytra.java): attachment offset.
- [Minecraft Elytra model](https://github.com/KealJones/mc-1.12.2-source_files/blob/master/src/minecraft/net/minecraft/client/model/ModelElytra.java): wing boxes, pivots, and pose angles.
- [skinview3d model implementation](https://github.com/bs-community/skinview3d/blob/master/src/model.ts): independent renderer using the same two-pixel attachment distance and inflated wing dimensions (with its own Y-up coordinate system).

Smoke checks compare posed vertices against scalar reference transforms and verify attachment pivots, UV preservation, occlusion, and scaled picking. Geometry contact sheets provide front, side, and back projections; the graphics integration suite covers real viewport strokes and framebuffer captures when a display is available.
