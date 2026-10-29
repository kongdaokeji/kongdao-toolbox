# Hardware component models

## CC BY 4.0 models

The following models are licensed under Creative Commons Attribution 4.0:
https://creativecommons.org/licenses/by/4.0/

| File | Original title | Author | Original source |
| --- | --- | --- | --- |
| cpu.glb | PC CPU processor | apleesee | https://sketchfab.com/3d-models/pc-cpu-processor-efb6b95b255c4a37a9661df178ea3bb3 |
| gpu.glb | GeForce RTX 3080 Graphics Card | _surovic_ | https://sketchfab.com/3d-models/geforce-rtx-3080-graphics-card-8b947ee1bf7a4e3d8ffa1c24893ac160 |
| ram.glb | Corsairn VENGEANCE RGB PRO | Nouraiz | https://sketchfab.com/3d-models/corsairn-vengeance-rgb-pro-9b163c4d5ad34edca58488500b5f4daf |
| storage.glb | M.2 NVME SSD Samsung 990 Pro 1TB | lime.ball.animations | https://sketchfab.com/3d-models/m2-nvme-ssd-samsung-990-pro-1tb-3d-model-41b7bfda7eab40f8b13330913fd66fc2 |
| motherboard.glb | Asus Strix b-550-f Gaming Motherboard Realistic | MUSHROOM_BUILDS | https://sketchfab.com/3d-models/asus-strix-b-550-f-gaming-motherboard-realistic-3eba5f45bed74fbeb2647de38047000f |

Redistributed GLBs obtained from https://github.com/Jacob-Baker-Git/CustomPc/tree/main/public/models
with per-model CC BY credits verified against the original Sketchfab listings.
Modifications: resized/reoriented for component display, adjusted material roughness and added a generic Intel mark,
removed animations, resized/compressed textures, quantized/compressed geometry.
The motherboard also has stray decoration removed and reduced polygon count.
These are illustrative models, not the detected device's exact physical appearance.
The focus panel links each author/source and the license.

## Original geometry

Laptop/desktop, wallpaper monitor, AMD CPU variant, network/audio boards, battery and BIOS/NPU illustrations are authored
in ../scene.js and ../components.js. Procedural component geometry is also used as a
fallback if a local asset cannot be decoded.

All assets load locally. No network access is required at runtime.
Three.js and Meshopt decoder licenses are in ../THREE-LICENSE.txt and ../MESHOPT-LICENSE.txt.
