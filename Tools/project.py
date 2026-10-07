#!/usr/bin/env python3
"""Cross-platform build/tests/package/deploy. No game binaries in Git or ZIP."""
import argparse, hashlib, json, os, re, shutil, subprocess, sys, zipfile
from shaders import sources_digest
from localization import validate_localization
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
NAME='VolumetricExplosionFX'
VERSION='1.0.0'
BUILD=ROOT/'build'
def run(args):
    subprocess.run([str(x) for x in args],check=True,cwd=ROOT)
def digest():
    h=hashlib.sha256()
    inputs=list((ROOT/'Source').rglob('*.cs'))+list((ROOT/'Tests').glob('*.cs'))+list((ROOT/'Tests').glob('*.csproj'))+list((ROOT/'Tests').glob('test_*.py'))
    inputs+=list((ROOT/'GameData').rglob('*.cfg'))+[ROOT/'GameData'/NAME/'README.md',ROOT/'LICENSE',ROOT/'CREDITS.md',ROOT/'Tools/project.py']
    inputs+=[p for p in (ROOT/'RenderAssets').rglob('*') if p.is_file() and p.suffix!='.meta']+[ROOT/'Tools/shaders.py',ROOT/'Tools/localization.py']
    for p in sorted(inputs):
        h.update(p.relative_to(ROOT).as_posix().encode()); h.update(p.read_bytes())
    return h.hexdigest()
def configuration(args):
    p=ROOT/'config.local.json'
    cfg=json.loads(p.read_text()) if p.exists() else {}
    ksp=args.ksp_root or os.environ.get('KSP_ROOT') or cfg.get('kspRoot')
    if not ksp: raise RuntimeError('Set KSP_ROOT, --ksp-root, or ignored config.local.json. See config.example.json.')
    return Path(ksp).expanduser().resolve()
def managed(ksp):
    for p in [ksp/'KSP_x64_Data/Managed',ksp/'KSP.app/Contents/Resources/Data/Managed']:
        if (p/'Assembly-CSharp.dll').exists() and (p/'mscorlib.dll').exists(): return p
    raise RuntimeError('KSP 1.12.5 managed assemblies not found.')
def audit():
    entries=validate_localization(ROOT)
    errors=[]
    forbidden=[r'\.\s*(?:AddForce|AddTorque|AddExplosionForce|RequestResource|Explode|explode|Die|decouple)\s*\(',
      r'AddComponent\s*<\s*(?:Rigidbody|\w*Collider|Part|PartModule)\s*>',
      r'\b(?:crashTolerance|temperature|mass|buoyancy|amount|maxAmount)\s*[+*/-]?=(?!=)',
      r'\bUnityEngine\.Random\b',r'\bRandom\.(?:InitState|Range|value|insideUnitSphere)',r'HarmonyPatch']
    for p in (ROOT/'Source').rglob('*.cs'):
        source=re.sub(r'//[^\n]*|/\*.*?\*/','',p.read_text(),flags=re.S)
        for pattern in forbidden:
            if re.search(pattern,source): errors.append(p.relative_to(ROOT).as_posix()+': forbidden gameplay/global-random pattern')
        if '/Core/' in p.as_posix() and re.search(r'using (?:UnityEngine|VolumetricExplosionFX.Ksp)|\b(?:Part|Vessel|GameObject|Transform)\s+\w+',source):
            errors.append('Core must remain independent: '+p.name)
    for p in ROOT.rglob('*'):
        if not p.is_file() or any(s in p.relative_to(ROOT).parts for s in ['.git','build','dist','work','bin','obj','__pycache__']): continue
        if p.name=='config.local.json': continue
        if p.suffix.lower() in ['.dll','.exe','.pdb','.log','.zip','.unity3d']: errors.append('Unexpected distributable binary: '+p.name)
        try: content=p.read_text()
        except (UnicodeError,OSError): continue
        # Patterns assembled to avoid flagging the scanner's own implementation.
        private_path='/'+'Users/'
        if private_path in content or re.search(r'[A-Za-z]:\\(?:Users|Games)\\',content): errors.append('Machine path in '+p.name)
        if re.search(r'gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|'+'-----BEGIN '+'PRIVATE KEY',content): errors.append('Possible credential in '+p.name)
    if errors: raise RuntimeError('\n'.join(errors))
    print('Source boundary/privacy audit passed (heuristic; not proof of physics equivalence).')
    print('Localization catalogs validated:',entries,'entries across nine languages.')
def build(args):
    audit(); data=managed(configuration(args)); out=BUILD/'GameData'/NAME/'Plugins';out.mkdir(parents=True,exist_ok=True)
    sdk=subprocess.check_output(['dotnet','--list-sdks'],text=True).strip().splitlines()[-1]
    version,base=sdk.split(' ',1); csc=Path(base.strip('[]'))/version/'Roslyn/bincore/csc.dll'
    refs=[data/x for x in ['mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll']]
    refs+=sorted(data.glob('UnityEngine*.dll'))
    sources=sorted((ROOT/'Source').rglob('*.cs'))
    args_csc=['dotnet',csc,'/noconfig','/nostdlib+','/langversion:7.3','/target:library','/optimize+', '/deterministic+', '/warnaserror+', '/out:'+str(out/(NAME+'.dll'))]
    args_csc+=['/reference:'+str(p) for p in refs if p.exists()]
    run(args_csc+sources)
    for p in (ROOT/'GameData'/NAME).rglob('*'):
        if p.is_file():
            target=BUILD/'GameData'/NAME/p.relative_to(ROOT/'GameData'/NAME);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,target)
    for filename in ['LICENSE','CREDITS.md']:
        shutil.copy2(ROOT/filename,BUILD/'GameData'/NAME/filename)
    bundles=[]
    asset_dir=BUILD/'GameData'/NAME/'Assets'
    if asset_dir.exists():
        for old in asset_dir.glob('vefx-*.unity3d'): old.unlink()
    for platform in ['windows','mac']:
        bundle=verified_bundle(platform)
        if bundle:
            asset_dir.mkdir(exist_ok=True);shutil.copy2(bundle,asset_dir/bundle.name);bundles.append(platform)
    (BUILD/'build-receipt.json').write_text(json.dumps({'source_sha256':digest(),'version':VERSION,'actual_ksp_assembly_build':True,'shader_bundles':bundles},indent=2)+'\n')
    print('Built plugin against actual KSP assemblies. No dependency DLLs copied.')
def test(args):
    audit()
    run([sys.executable,'-m','unittest','discover','-s','Tests','-p','test_*.py'])
    # No NuGet packages or mock Unity runtime. Core source compiled and exercised on .NET.
    run(['dotnet','run','--project','Tests/VolumetricExplosionFX.Tests.csproj','--configuration','Release'])
    BUILD.mkdir(exist_ok=True)
    (BUILD/'tests-receipt.json').write_text(json.dumps({'source_sha256':digest(),'core_tests_passed':True},indent=2)+'\n')
def validate_tree(folder):
    expected={'Plugins/'+NAME+'.dll','Settings.cfg','README.md','LICENSE','CREDITS.md'}
    for source in (ROOT/'GameData'/NAME/'Localization').glob('*.cfg'):
        relative='Localization/'+source.name
        if not (folder/relative).is_file() or (folder/relative).read_bytes()!=source.read_bytes():
            raise RuntimeError('Packaged localization differs: '+source.name)
        expected.add(relative)
    built=json.loads((BUILD/'build-receipt.json').read_text()).get('shader_bundles',[])
    for platform in ['windows','mac']:
        packaged=folder/'Assets'/('vefx-'+platform+'.unity3d')
        if platform in built:
            original=verified_bundle(platform)
            if not original or not packaged.exists() or packaged.read_bytes()!=original.read_bytes(): raise RuntimeError('Shader bundle does not match validated build.')
            expected.add('Assets/'+packaged.name)
    actual={p.relative_to(folder).as_posix() for p in folder.rglob('*') if p.is_file()}
    if actual!=expected: raise RuntimeError('Invalid GameData contents: '+repr(actual.symmetric_difference(expected)))
    dll=folder/'Plugins'/(NAME+'.dll')
    if dll.read_bytes()[:2]!=b'MZ' or dll.stat().st_size<10000: raise RuntimeError('Plugin is not a valid compiled PE assembly.')
    for p in folder.rglob('*'):
        if p.is_file() and p.suffix not in ['.dll','.unity3d'] and ('/'+'Users/') in p.read_text(): raise RuntimeError('Personal path in package')

def verified_bundle(platform):
    output=BUILD/'shader-bundles'/platform
    receipt=output/'receipt.json'
    if not receipt.exists(): return None
    data=json.loads(receipt.read_text());bundle=output/('vefx-'+platform+'.unity3d')
    if data.get('source_sha256')!=sources_digest() or data.get('unity')!='2019.4.18f1' or data.get('target')!=platform or not data.get('bundle_built'):
        raise RuntimeError('Rebuild current '+platform+' shader sources before packaging.')
    if bundle.read_bytes()[:7]!=b'UnityFS' or hashlib.sha256(bundle.read_bytes()).hexdigest()!=data.get('sha256'):
        raise RuntimeError('Shader bundle checksum/format failed.')
    return bundle
def require_distribution_bundle(platform, require_host_probe=True):
    bundle=verified_bundle(platform)
    if bundle is None:
        raise RuntimeError('Missing '+platform+' shaders. Build them with Tools/shaders.py before creating a distribution package.')
    receipt=json.loads((BUILD/'shader-bundles'/platform/'receipt.json').read_text())
    api='Direct3D11' if platform=='windows' else 'OpenGLCore'
    if require_host_probe and (not receipt.get('host_render_probe') or receipt.get('graphics_api')!=api):
        raise RuntimeError('Verify '+platform+' shaders on a '+api+' host before distribution.')

def package(args):
    audit()
    private_test=getattr(args,'test_package',False)
    platform=getattr(args,'platform','windows')
    candidate=getattr(args,'candidate',False)
    if private_test and candidate: raise RuntimeError('Choose candidate or private test packaging.')
    if not private_test:
        require_distribution_bundle(platform, require_host_probe=not candidate)
    for name,key in [('build-receipt.json','actual_ksp_assembly_build'),('tests-receipt.json','core_tests_passed')]:
        receipt=json.loads((BUILD/name).read_text())
        if receipt.get('source_sha256')!=digest() or not receipt.get(key): raise RuntimeError('Rebuild/retest current source before packaging.')
    folder=BUILD/'GameData'/NAME;validate_tree(folder)
    bundled=json.loads((BUILD/'build-receipt.json').read_text())['shader_bundles']
    if not private_test and platform not in bundled:
        raise RuntimeError('Rebuild the plugin after compiling the '+platform+' shaders.')
    suffix=('-'+platform+'-private-test') if private_test else ('-'+platform+'-candidate' if candidate else ('-mac' if platform=='mac' else ''))
    dest=ROOT/'dist';dest.mkdir(exist_ok=True);out=dest/(NAME+'-'+VERSION+suffix+'.zip')
    with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(folder.rglob('*')):
            if p.is_file(): z.write(p,p.relative_to(BUILD).as_posix())
    with zipfile.ZipFile(out) as z:
        if z.testzip() is not None: raise RuntimeError('ZIP checksum validation failed')
    (dest/'package-validation.json').write_text(json.dumps({'package':out.name,'sha256':hashlib.sha256(out.read_bytes()).hexdigest(),'source_sha256':digest(),
      'package_verified':True,'package_kind':'private-test' if private_test else 'distribution-candidate','target':platform,'shader_bundles':bundled,'release_ready':False,
      'target_host_probe_verified':False if private_test else json.loads((BUILD/'shader-bundles'/platform/'receipt.json').read_text()).get('host_render_probe',False),
      'loaded_in_ksp':False,'visually_tested_in_ksp':False,'performance_tested':False},indent=2)+'\n')
    print('Validated '+('private test' if private_test else 'distribution candidate')+' package:',out.name)
def deploy(args):
    # Deliberate opt-in; never overwrite other mods or saves. Backup only our old DLL/config.
    ksp=configuration(args);managed(ksp);src=BUILD/'GameData'/NAME;validate_tree(src)
    dest=ksp/'GameData'/NAME
    if dest.exists(): raise RuntimeError('Existing project mod folder found; preserve/review it before a new deployment.')
    shutil.copytree(src,dest);validate_tree(dest)
    print('Installed project test folder only. KSP must be restarted; no saves touched.')
def collect(args):
    ksp=configuration(args);source=ksp/'KSP.log';out=ROOT/'work'/'ksp-summary.txt';out.parent.mkdir(exist_ok=True)
    if not source.exists(): raise RuntimeError('KSP.log missing.')
    # Extract only project lines and redact paths. Raw logs remain on the local machine.
    lines=[line for line in source.read_text(errors='replace').splitlines() if '[VEFX]' in line or '['+NAME+']' in line]
    content='\n'.join(lines)
    content=re.sub(r'/(?:Users|home)/[^\s]+','<local path>',content)
    content=re.sub(r'[A-Za-z]:\\[^\s]+','<local path>',content)
    out.write_text(content+'\n');print('Project-only redacted log summary saved in ignored work/.')
def main():
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['build','test','audit','package','deploy','collect']);parser.add_argument('--ksp-root')
    mode=parser.add_mutually_exclusive_group()
    mode.add_argument('--test-package',action='store_true',help='Explicit private test ZIP; may omit volumetric shaders.')
    mode.add_argument('--candidate',action='store_true',help='Complete private candidate with current shaders; target-host and KSP acceptance may be pending.')
    parser.add_argument('--platform',choices=['windows','mac'],default='windows')
    args=parser.parse_args()
    try: {'build':build,'test':test,'audit':lambda _:audit(),'package':package,'deploy':deploy,'collect':collect}[args.action](args)
    except (RuntimeError,subprocess.CalledProcessError,FileNotFoundError) as e:
        print('ERROR:', 'Tool failed; see diagnostics above.' if isinstance(e,subprocess.CalledProcessError) else e,file=sys.stderr);return 1
    return 0
if __name__=='__main__': sys.exit(main())
