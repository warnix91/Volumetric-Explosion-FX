# Changelog

## 1.1.0 — Visual Overhaul — 2026-10-19

Almost everything you see has been redone: fireballs, smoke, shock waves, ground fires, wreckage, water impacts, nuclear
detonations and the settings window. VEFX stays visual only: damage, physics and sound are untouched.

### Explosions
- Fireballs come from a baked fluid simulation and take one of several shapes: a mushroom on its stem, a squat ragged
  cloud, a column of fire, a chaotic ball; in the air a ball or a rising ring. No two explosions are alike.
- The impact shapes the blast: a fast, shallow crash draws the fireball out along its path, a steep one spreads it, a slow
  one rises as a ball, more fuel makes it broader. Blasts landing in a fireball merge into it.
- Tanks let go according to how full they were: a full tank boils into a huge fireball, a half-empty one jets flame
  sideways, a big stage goes off in steps, a nearly empty one flashes into a pale vapour cloud.
- Propellants show: hydrogen burns with a nearly invisible flame in white vapour, hypergolics leave orange-brown billows,
  monopropellant a pale yellow cloud, solid fuel a white flame, ore a cloud of rock dust; batteries crackle with arcs.
- Real fire: bright yellow pockets and dark sooty lanes; the flames go out patch by patch.
- More brutal: a brighter flash, a fireball that slams out, a harder shock wave, debris thrown faster.
- The blast wave bends the air as a spreading ring, as in 1.0, and a thin ring of dust runs over the ground at the
  speed of sound. The camera shake arrives with it, where the camera is when the wave reaches it.
- Every ground mark is its own: an uneven fuel burn with singed edges, or a warhead's crater with a sunlit rim and earth
  thrown out in rays; a grazing crash leaves a gouge along its path. Marks blend into the ground and follow its colour;
  rock and concrete only take soot.
- Photorealistic smoke: sunlight traced through the cloud, soft shadows on the ground, lit by fires and floodlights at
  night.
- Dynamic lighting: explosions light the ground and nearby craft, the colour following the fire's temperature.
- Dust takes the colour of the ground (sand, snow, rock, Duna's red soil); on airless worlds the ejecta fly in clean
  ballistic rays.
- Collapsing buildings raise a rolling cloud of concrete dust and rubble; fuel stores go up in flames.

### After the blast
- Ground fires take hold after the fireball as several low flames. Fuel thrown along a crash path burns in scattered
  pools that join up and leave a burnt scar; secondary fireballs roll along the path.
- Small flames of several kinds, secondary explosions in burning wrecks, grass fires creeping outward, heat shimmer over
  hot wrecks.
- Smoke drifts with the wind in natural wisps; a crash rises as one cloud, the fire's smoke drawn up into it.
- Wreckage is cut from the destroyed part's own model, soot-stained and heat-tinted, and bounces, slides and settles on
  the slope. Burning fuel flung out arcs up with thin smoke trails; a warhead's steel fragments leave none.

### Nuclear detonations (rebuilt from scratch)
- Their own renderer: a white-hot fireball boiling into a rolling toroidal cap with a ring of fire under it, on a dust
  stem with a rolling skirt at its foot.
- Real proportions for the yield: the cloud climbs for minutes (about 3.5 km for 1.5 kt, 7 km for 20 kt, 20 km for a
  megaton), drifts and shears in the wind, then breaks up into wisps.
- Ground, low-air, high-air (ring cloud), sea (spray column and base surge), vacuum and thin-air bursts each look their
  part.
- A blinding flash anchored in the world lights the land for kilometres. The blast wave races out at the speed of sound
  as a ring of bent air and raises an uneven, billowing wall of dust that follows the terrain, arriving with a short
  camera jolt and BDArmory's boom.
- Condensation: a Wilson cloud, rings round the stem that form and evaporate, a collar under the cap, a pileus, an ice
  cap at megaton yields.
- At night the heart glows for tens of seconds and the cloud keeps a red-violet afterglow. Lightning flashes inside the
  cloud, the cloud casts its shadow on the land, and it is drawn out to 60 km.

### Water
- Water impacts are volumetric: a quick burst of spray in ragged sheaves and a fading patch of whitewater; a grazing
  impact throws the spray forward; a warhead bursting at the sea throws up a broad plume of spray taller than its
  fireball.
- Underwater blasts lift a white dome that bursts into plumes; burning fuel spreads in a slick on the water.

### Combat mods (BDArmory and packs built on it)
- BDArmory explosions are drawn by VEFX, also for BDArmory Extended and Kerbal Powers Armory, read only: no change to
  damage, sound or physics. Each warhead has its own look, scaled by its charge: missile, bomb, continuous rod,
  thermobaric, incendiary, nuclear, EMP. Small cannon shells keep BDArmory's own effects.

### Settings window
- Redesigned, with smooth animations: five complete styles (Glass, Neon, Terminal, Paper, Mission), three layouts (Open,
  Panel, Compact), see-through, with opacity and size settings.
- A Compatibility page shows which known mods are installed and how they work with VEFX.
- Photo mode (Alt+F7) slows down or freezes the explosions while the game runs; quick setups (Cinematic, Balanced,
  Performance); a camera shake option; an advanced look editor with 13 dials per kind of explosion.

### Compatibility
- EVE: explosions hide behind cloud layers.
- Waterfall engine flames and contrails stay in front of explosion clouds and no longer show through them from behind.
- Parallax Continued: wreckage and blasts no longer float on trees and rocks.

### Performance
- Volume resolution option (Full, 1/2, 1/3) and optional adaptive performance.
- No stall on the first explosion of a flight; effects set up faster and are spread over several frames.

### Fixes
- Explosions drawn under the ground; a square around the nuclear flash; missing or flat explosions in big crashes and
  battles; two mushrooms from one nuclear missile; a crash cloud rising as two clouds; engine flames showing through
  clouds; wreckage floating on slopes or in trees; long "spaghetti" wreck pieces; a crash's explosions vanishing when
  switching vessels or opening the map.

The 1.0 look remains available exactly as it was ("Compare with 1.0" in the look editor).

## 1.0.0

- Fireballs, smoke, visual debris, ground fires and water impacts.
- Effects respond to vessel resources, motion and environment.
- Adjustable quality and an explosion preview from the toolbar.
- Visual effects only, with no required mods.
- Interface follows KSP's language, with all nine built-in languages supported.
- Ultra is the default preset; saved player settings are retained.
- Initial release validated on Windows; macOS validation is deferred.
