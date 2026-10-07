# Building VEFX

Requires Python 3, .NET 8, KSP 1.12.5 and Unity 2019.4.18f1 with Windows Build Support.

Copy `config.example.json` to `config.local.json` and set `kspRoot` and `unityEditor`.
The local configuration is ignored by Git.

```sh
python3 Tools/project.py test
python3 Tools/shaders.py --target windows
python3 Tools/project.py build
python3 Tools/project.py package
```

Build output: `build/`. Package: `dist/VolumetricExplosionFX-1.0.0.zip`.
Use `python` if that is the Python 3 command on your system.

The distribution package requires a current shader bundle and a successful
Direct3D 11 render probe on Windows. Missing shaders stop packaging.
For a complete cross-built private candidate awaiting target-host and KSP checks,
use `package --candidate --platform windows`. Its archive is named
`VolumetricExplosionFX-1.0.0-windows-candidate.zip`; shaders remain mandatory.

For a local fallback test only, use `package --test-package`; its ZIP is marked
`private-test` and is not a release package.

Mac support remains experimental. Its build uses OpenGLCore to match KSP Mac:

```sh
python3 Tools/shaders.py --target mac
python3 Tools/project.py build
python3 Tools/project.py package --platform mac
```

A complete package still needs visual and performance checks in KSP before release.
