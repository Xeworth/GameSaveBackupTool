# GSBT Basic — Python PyQt GUI

Desktop GUI edition (legacy Python stack). **Source will be added here** when imported from your maintained PyQt tree.

## Status

This folder is reserved in the monorepo. The deprecated root-level Python app was removed from [GameSaveBackupTool](https://github.com/Xeworth/GameSaveBackupTool); do not expect runnable code here until you copy the current PyQt project in.

## Planned layout (when imported)

```
src-pyqt/
  requirements.txt
  main.py
  core/
  ui/
  ...
```

## Run (after import)

```bat
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python main.py
```

For the active Windows-native edition, use [../src-winui/README.md](../src-winui/README.md).
