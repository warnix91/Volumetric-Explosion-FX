#!/usr/bin/env python3
"""Install the built mod into the local KSP test copy (kspRoot in config.local.json).

Refuses while KSP is running, backs the previous copy up into <kspRoot>/_mods_desactives,
keeps PluginData (the player's settings), replaces only this mod's folder and checks the result.
Never touches other mods or saves.
"""
import json, filecmp, os, shutil, subprocess, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NAME = 'VolumetricExplosionFX'


def ksp_running():
    if os.name != 'nt': return False
    out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq KSP_x64.exe', '/NH'], capture_output=True, text=True).stdout
    return 'KSP_x64.exe' in out


def same_tree(a, b, skip=('PluginData',)):
    cmp = filecmp.dircmp(a, b, ignore=list(skip))
    if cmp.left_only or cmp.right_only or cmp.diff_files or cmp.funny_files: return False
    _, mismatch, errors = filecmp.cmpfiles(a, b, cmp.common_files, shallow=False)
    if mismatch or errors: return False
    return all(same_tree(os.path.join(a, d), os.path.join(b, d), ()) for d in cmp.common_dirs)


def main():
    cfg = json.loads((ROOT / 'config.local.json').read_text())
    ksp = Path(cfg['kspRoot'])
    src = ROOT / 'build' / 'GameData' / NAME
    dest = ksp / 'GameData' / NAME
    if not (src / 'Plugins' / (NAME + '.dll')).exists(): sys.exit('ERROR: no build found; run Tools/project.py build first.')
    if not (ksp / 'GameData').is_dir(): sys.exit('ERROR: kspRoot has no GameData: ' + str(ksp))
    if ksp_running(): sys.exit('ERROR: KSP is running; close it before installing.')
    label = sys.argv[1] if len(sys.argv) > 1 else 'dev'
    backup = None
    if dest.exists():
        backup = ksp / '_mods_desactives' / ('%s_avant_%s_%s' % (NAME, label, time.strftime('%Y%m%d_%H%M%S')))
        backup.parent.mkdir(exist_ok=True)
        shutil.copytree(dest, backup)
        # Replace item by item (the folder itself may be held open by Windows Explorer); keep PluginData.
        for item in dest.iterdir():
            if item.name == 'PluginData': continue
            shutil.rmtree(item) if item.is_dir() else item.unlink()
    else:
        dest.mkdir(parents=True)
    for item in src.iterdir():
        target = dest / item.name
        shutil.copytree(item, target) if item.is_dir() else shutil.copy2(item, target)
    if not same_tree(str(src), str(dest)): sys.exit('ERROR: installed copy differs from the build.')
    print('Installed', NAME, 'in', dest)
    print('Backup:', backup if backup else 'none (fresh install)')
    print('PluginData kept:', (dest / 'PluginData').exists())


if __name__ == '__main__':
    main()
