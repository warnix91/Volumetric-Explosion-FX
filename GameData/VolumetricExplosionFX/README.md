# Volumetric Explosion FX — 0.9.0

Visual explosion effects for KSP 1.12.5. Created and maintained by Warnix.

## Installation

Close KSP before installing or removing the mod. Copy VolumetricExplosionFX into
GameData. If upgrading from ProjectDestructionFX, preserve your settings profile
and remove the old folder first; the two versions must not be installed together.

## Settings

The flame toolbar button opens settings in flight and at the Space Center.
Changes are saved to PluginData/settings.cfg and override Settings.cfg.
Escape or the close button closes the window. Lower quality if effects affect frame time.

The test preview changes visuals only. "Gros crash" chains eight preview blasts.
After an episode of effects, KSP.log records a [VEFX] Performance line with frame
times and peak effect load. Alt+F8 toggles diagnostic counters in flight.

Stock replacement hides only matching explosion renderers and lights; their sound
keeps playing. State changed by VEFX is restored when its replacement ends.
VEFX adds no damage, forces, physical debris or persistent save data.

Remove GameData/VolumetricExplosionFX to uninstall. Mac support is postponed;
there is no automatic Mac disable. Missing volume bundles retain particle effects.

MIT licensed. Copyright (c) 2026 Warnix.
