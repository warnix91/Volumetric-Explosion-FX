# Compatibility

Game version: KSP 1.12.5.

| Platform | Status |
|---|---|
| Windows | Tested in KSP 1.12.5. |
| macOS | Not validated for this release; fixes and testing deferred. |
| Linux | Not tested. |

No other mods are required. Other explosion mods may produce overlapping effects.

Quality defaults to Ultra. Pick the Performance quick setup, or select High, Medium or Low, if effects reduce the frame
rate. Performance depends on resolution, craft size, camera distance and other mods.

## Mods

The Compatibility page of the settings window shows which of these are installed.

| Mod | With VEFX |
|---|---|
| BDArmory, BDArmory Extended, Kerbal Powers Armory | Their explosions are drawn by VEFX, scaled by each warhead. VEFX only reads them: damage, sound and physics are unchanged. Small cannon shells keep BDArmory's own effects. |
| EVE | Explosions are hidden behind a cloud layer lying between the camera and them. |
| Waterfall, contrails | Engine flames and trails stay in front of explosion clouds and do not show through them from behind. |
| Parallax Continued | Wreckage and blasts settle on the ground, not on the trees and rocks. |
| Scatterer, Deferred, TUFX, Kopernicus, Firefly, SmokeScreen, Volumetric Ground FX | Work alongside VEFX. |

## Reporting a problem

Include the mod and KSP versions, operating system, CPU/GPU, resolution and quality
preset. Describe how to reproduce the problem and attach a screenshot or video.
Include the relevant `[VEFX]` lines from KSP.log; remove personal information.
