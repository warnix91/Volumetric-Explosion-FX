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
