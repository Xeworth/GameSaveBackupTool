# Third-party components

Game Save Backup Tool (GSBT) is licensed under the [MIT License](LICENSE). The application also includes or depends on the components below.

## 7-Zip (`7z.dll`)

The WinUI edition ships a copy of **`7z.dll`** next to `gsbt.exe` (see `src-winui/native/win-x64/7z.dll`). Compression uses this native library at runtime; users do **not** need a separate 7-Zip installation.

- **Project:** [7-Zip](https://www.7-zip.org/)
- **License:** GNU LGPL (see [7-Zip license](https://www.7-zip.org/license.txt))
- **Notes:** 7-Zip is written by Igor Pavlov. The unRAR license restriction in the official 7-Zip distribution does not apply to the `7z.dll` / LZMA parts used for `.7z` creation.

## SharpSevenZip

Managed wrapper used by `GSBT.Core` to call `7z.dll`.

- **Project:** [SharpSevenZip](https://github.com/JeremyAnsel/SharpSevenZip) (NuGet package, currently v2.0.77 in this repo)
- **License:** [LGPL-3.0-or-later](https://www.gnu.org/licenses/lgpl-3.0.html)

## Inno Setup Dependency Installer

The Windows installer uses Inno Setup only (no third-party dependency downloader). Release publish is **self-contained** (.NET 8 + Windows App SDK in `publish\`).

Optional vendored script (not wired into `GSBT_Setup.iss` today): [InnoDependencyInstaller](https://github.com/DomGries/InnoDependencyInstaller) at `src-winui/installer/dependencies/CodeDependencies.iss`.

## Ludusavi manifest data

Save-location hints are derived from the community [**Ludusavi manifest**](https://github.com/mtkennerly/ludusavi-manifest). See [README.md](README.md#save-locations--thanks-to-ludusavi) for attribution.

---

*If you redistribute a build of GSBT, keep this file with the distribution and preserve the notices above.*
