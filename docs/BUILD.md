# Building VEFX

Requirements: Python 3, .NET 8 for core tests, licensed local KSP 1.12.5 files and
Unity 2019.4.18f1 with the target platform module for shader bundles.

Copy config.example.json to config.local.json and set your local kspRoot and
unityEditor paths. This file is ignored. KSP_ROOT and UNITY_EDITOR can be used instead.

```sh
python Tools/project.py test
python Tools/shaders.py --target windows
python Tools/project.py build
python Tools/project.py package
```

Use python3 where needed on macOS. Shell and PowerShell wrappers are in Tools/.
Builds are written to build/ and ZIPs to dist/. Shader bundles must be rebuilt when
their sources change. KSP and Unity binaries are local references and are not redistributed.

The package command currently creates a prototype ZIP and permits absent shader bundles.
A Windows volumetric release must include the current Windows bundle. Build receipts,
core tests and Unity rendering do not establish in-game performance or visual acceptance.
