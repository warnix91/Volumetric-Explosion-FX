#!/usr/bin/env python3
"""Build original target bundles in an ignored scratch Unity project."""
import argparse, hashlib, json, os, shutil, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]

def sources_digest():
    h=hashlib.sha256()
    for p in sorted((ROOT/'RenderAssets').rglob('*')):
        if p.is_file() and p.suffix!='.meta':
            h.update(p.relative_to(ROOT).as_posix().encode());h.update(p.read_bytes())
    h.update(Path(__file__).read_bytes())
    h.update((ROOT/'Tools'/'firesim.py').read_bytes())   # the baked fireball simulation is part of the bundle
    return h.hexdigest()

def ensure_firesim():
    """Bake the fireball simulation (numpy, ~2 min) when missing or when Tools/firesim.py changed."""
    script=ROOT/'Tools'/'firesim.py'
    out=ROOT/'work'/'firesim'
    stamp=out/'stamp.txt'
    digest=hashlib.sha256(script.read_bytes()).hexdigest()
    ready=all((out/('fire_'+v+'.bin')).exists() for v in ('ground','ground2','ground3','ground4','air','air2','nuke','nuke2','nukeair')) and stamp.exists() and stamp.read_text().strip()==digest
    if ready: return
    print('Baking the fireball simulation (Tools/firesim.py, a couple of minutes)...')
    subprocess.run([sys.executable,str(script),'--bake'],check=True,cwd=ROOT)
    stamp.write_text(digest+chr(10))

def replace_file(source,target):
    if os.path.lexists(target): os.remove(target)
    return shutil.copyfile(source,target)

def main():
    p=argparse.ArgumentParser();p.add_argument('--target',choices=['windows','mac'],required=True)
    p.add_argument('--unity-editor')
    p.add_argument('--preview',nargs='?',const='all',help='render look-development sheets into ignored work/preview (optional scenario filter)')
    p.add_argument('--frame',help='close-up frames, e.g. "960x540;0.5,1.5;0.6" (size; ages; camera distance factor)')
    p.add_argument('--perf',action='store_true',help='measure GPU time per frame at 2560x1440 with the camera close (ignored work/preview/perf.txt)')
    p.add_argument('--wreck',nargs='?',const='all',help='render torn-part debris, residue and smoke timelines into ignored work/preview (optional scenario filter)')
    p.add_argument('--ui',action='store_true',help='render every page of the settings window into ignored work/preview')
    p.add_argument('--scorch',action='store_true',help='render a sheet of ground marks seen from above into ignored work/preview')
    p.add_argument('--half',nargs='?',const='2',help='render volumes through the runtime reduced-resolution path (divisor 2 or 3)')
    p.add_argument('--dusk',action='store_true',help='preview at dusk to judge the light cast by explosions')
    p.add_argument('--night',action='store_true',help='preview at night (no sun) to judge fires lighting the smoke')
    p.add_argument('--lamp',action='store_true',help='add a floodlight aimed at the smoke column (wreck bench)')
    p.add_argument('--look',help='preview look profile: "classic" (1.0) or "default", plus overrides, e.g. "default;turbulence=1.4"')
    a=p.parse_args()
    local=ROOT/'config.local.json'
    cfg=json.loads(local.read_text()) if local.exists() else {}
    editor=a.unity_editor or os.environ.get('UNITY_EDITOR') or cfg.get('unityEditor')
    if not editor or not Path(editor).is_file(): raise RuntimeError('Set UNITY_EDITOR, --unity-editor or unityEditor in config.local.json.')
    ensure_firesim()
    scratch=ROOT/'work'/'unity-volumes';scratch.mkdir(parents=True,exist_ok=True)
    for folder in ['Assets','ProjectSettings','Packages']:
        # Fresh copies, content only: overwriting a file macOS flagged as compressed can leave it truncated.
        shutil.copytree(ROOT/'RenderAssets'/folder,scratch/folder,dirs_exist_ok=True,copy_function=replace_file)
    # Editor-only copy of the pure core and the KSP-free Unity helpers, so the preview bench uses the
    # runtime evolution, fracture, mesh and particle code.
    core=scratch/'Assets'/'Editor'/'PreviewCore'
    if core.exists(): shutil.rmtree(core)
    core.mkdir(parents=True)
    for source in list((ROOT/'Source'/'Core').glob('*.cs'))+list((ROOT/'Source'/'Unity').glob('*.cs')): shutil.copy2(source,core/source.name)
    if not a.ui:
        for unused in [scratch/'Assets'/'Editor'/'PreviewUi.cs',core/'SettingsPanel.cs',core/'SettingsModel.cs']:
            if unused.exists(): unused.unlink()
        manifest=scratch/'Packages'/'manifest.json'
        packages=json.loads(manifest.read_text())
        packages['dependencies'].pop('com.unity.ugui',None)
        manifest.write_text(json.dumps(packages)+'\n')
    if a.perf:
        out=ROOT/'work'/'preview';out.mkdir(parents=True,exist_ok=True)
        env=os.environ.copy();env['VEFX_PREVIEW_OUTPUT']=str(out)
        if a.look: env['VEFX_PREVIEW_LOOK']=a.look
        if a.half: env['VEFX_PREVIEW_HALF']=a.half
        log=ROOT/'work'/'unity-perf.log'
        subprocess.run([editor,'-batchmode','-projectPath',str(scratch),'-executeMethod','PreviewVolumes.Perf',
            '-logFile',str(log)],env=env,check=True,cwd=ROOT)
        print((out/'perf.txt').read_text())
        return
    if a.scorch:
        out=ROOT/'work'/'preview';out.mkdir(parents=True,exist_ok=True)
        env=os.environ.copy();env['VEFX_PREVIEW_OUTPUT']=str(out)
        log=ROOT/'work'/'unity-scorch.log'
        subprocess.run([editor,'-batchmode','-projectPath',str(scratch),'-executeMethod','PreviewScorch.Render',
            '-logFile',str(log)],env=env,check=True,cwd=ROOT)
        print('Ground marks sheet in ignored work/preview (editor render, not a KSP test).')
        return
    if a.ui:
        out=ROOT/'work'/'preview';out.mkdir(parents=True,exist_ok=True)
        env=os.environ.copy();env['VEFX_PREVIEW_OUTPUT']=str(out)
        log=ROOT/'work'/'unity-ui.log'
        subprocess.run([editor,'-batchmode','-projectPath',str(scratch),'-executeMethod','PreviewUi.Render',
            '-logFile',str(log)],env=env,check=True,cwd=ROOT)
        print('Settings window pages in ignored work/preview (editor render, not a KSP test).')
        return
    if a.wreck:
        out=ROOT/'work'/'preview';out.mkdir(parents=True,exist_ok=True)
        env=os.environ.copy();env['VEFX_PREVIEW_OUTPUT']=str(out)
        if a.wreck!='all': env['VEFX_PREVIEW_ONLY']=a.wreck
        if a.dusk: env['VEFX_PREVIEW_DUSK']='1'
        if a.night: env['VEFX_PREVIEW_DUSK']='2'
        if a.lamp: env['VEFX_PREVIEW_LAMP']='1'
        if a.look: env['VEFX_PREVIEW_LOOK']=a.look
        if a.half: env['VEFX_PREVIEW_HALF']=a.half
        if a.frame: env['VEFX_PREVIEW_FRAME']=a.frame
        log=ROOT/'work'/'unity-wreck.log'
        subprocess.run([editor,'-batchmode','-projectPath',str(scratch),'-executeMethod','PreviewWreck.Render',
            '-logFile',str(log)],env=env,check=True,cwd=ROOT)
        print('Wreck sheets in ignored work/preview (editor look-development, not a KSP test).')
        return
    if a.preview:
        out=ROOT/'work'/'preview';out.mkdir(parents=True,exist_ok=True)
        env=os.environ.copy();env['VEFX_PREVIEW_OUTPUT']=str(out)
        if a.preview!='all': env['VEFX_PREVIEW_ONLY']=a.preview
        if a.look: env['VEFX_PREVIEW_LOOK']=a.look
        if a.dusk: env['VEFX_PREVIEW_DUSK']='1'
        if a.night: env['VEFX_PREVIEW_DUSK']='2'
        if a.lamp: env['VEFX_PREVIEW_LAMP']='1'
        if a.half: env['VEFX_PREVIEW_HALF']=a.half
        if a.frame: env['VEFX_PREVIEW_FRAME']=a.frame
        log=ROOT/'work'/'unity-preview.log'
        subprocess.run([editor,'-batchmode','-projectPath',str(scratch),'-executeMethod','PreviewVolumes.Render',
            '-logFile',str(log)],env=env,check=True,cwd=ROOT)
        print('Preview sheets in ignored work/preview (editor look-development, not a KSP test).')
        return
    output=ROOT/'build'/'shader-bundles'/a.target;output.mkdir(parents=True,exist_ok=True)
    receipt=output/'receipt.json'
    if receipt.exists(): receipt.unlink()  # Failed builds must not leave a success receipt.
    validation=output/'unity-validation.json'
    if validation.exists(): validation.unlink()
    env=os.environ.copy();env['VEFX_BUNDLE_TARGET']=a.target;env['VEFX_BUNDLE_OUTPUT']=str(output)
    log=ROOT/'work'/('unity-'+a.target+'.log')
    # Do not pass -nographics: Unity can omit shader variants on that path.
    subprocess.run([editor,'-batchmode','-projectPath',str(scratch),'-executeMethod','BuildVolumes.Build',
        '-logFile',str(log)]+(['-force-d3d11'] if os.name=='nt' and a.target=='windows' else ['-force-glcore']),env=env,check=True,cwd=ROOT)
    if not validation.exists(): raise RuntimeError('Unity did not produce validation; inspect ignored work/Unity log (license or compilation failure).')
    result=json.loads(validation.read_text())
    bundle=output/('vefx-'+a.target+'.unity3d')
    if not result.get('bundle_built') or bundle.read_bytes()[:7]!=b'UnityFS': raise RuntimeError('Invalid bundle build.')
    result.update(source_sha256=sources_digest(),sha256=hashlib.sha256(bundle.read_bytes()).hexdigest())
    receipt.write_text(json.dumps(result,indent=2)+'\n')
    print('Verified original shader bundle:',a.target,'host render probe:',result['host_render_probe'])

if __name__=='__main__': main()
