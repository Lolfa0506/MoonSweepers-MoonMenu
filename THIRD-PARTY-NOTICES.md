# Third-party components in the release zip

The release zip bundles these unmodified binaries so MoonMenu runs without installing anything else.
They were taken from the official BepInEx 6 Bleeding Edge build #793
(`BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793+dbc82f0.zip`, <https://builds.bepinex.dev/projects/bepinex_be>).

| File in the zip | Component | License | Source |
|---|---|---|---|
| `version.dll` (shipped as `winhttp.dll` by BepInEx, renamed) | Unity Doorstop 4.6.0 | LGPL-2.1 – `licenses/UnityDoorstop-LICENSE.txt` | <https://github.com/NeighTools/UnityDoorstop> |
| `.doorstop_version` | Unity Doorstop | LGPL-2.1 | as above |
| `dotnet\*` | .NET 6 runtime (CoreCLR 6.0.7) | MIT – `licenses/dotnet-LICENSE.txt`, `licenses/dotnet-THIRD-PARTY-NOTICES.txt` | <https://github.com/dotnet/runtime> |
| `MoonMenu\dobby.dll` | Dobby (hook library) | Apache-2.0 – `licenses/Dobby-LICENSE.txt` | <https://github.com/jmpews/Dobby> |

Unity Doorstop is used as a separate, unmodified library (LGPL-2.1). You can replace `version.dll` with
any compatible Doorstop 4.x build. Its source code is available at the link above.

MoonMenu's own code is MIT licensed (see `LICENSE`).
