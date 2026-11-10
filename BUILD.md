# Building Sumthing 1.0.0

Sumthing is a C# / WPF app on .NET 8. The build produces one self-contained `Sumthing.exe`, so people who install it do **not** need .NET, Python or Node. The build is followed by a normal Windows setup program made with Inno Setup.

## What you need (one-time, on the Windows PC that builds it)

Open **PowerShell** and run:

```powershell
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
```

Close and reopen PowerShell afterwards so the new commands are found. Check with `dotnet --version` (it should print `8.0.x`).

## Build everything (exe + installer)

From the `Sumthing` folder (the one with `build.ps1` in it):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

When it finishes, the `dist` folder has:

| File | What it is |
|---|---|
| `dist\Sumthing.exe` | The app. It runs on its own, with no install needed. |
| `dist\Sumthing-1.0.0-win-x64.zip` | The portable download (just the exe, zipped) |
| `dist\Sumthing-1.0.0-Setup.exe` | The installer to give to people |

## Or build the two steps by hand

**1. The exe**

```powershell
dotnet publish src\Sumthing\Sumthing.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\publish
```

**2. The installer** (needs step 1 first)

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\Sumthing.iss
```

If you installed Inno Setup for your user only, the compiler is at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` instead.

## Testing

`Sumthing.exe --self-test result.txt screenshots` runs Sumthing's built-in test. It checks the calculator engine (sums, percent, memory, errors, the tape), Type-a-sum, the unit and currency maths and every everyday tool, then clicks through the real window in the Light, Dark and Classic looks and saves a screenshot of every mode. It writes PASS/FAIL lines to `result.txt` and screenshots to `screenshots`. It uses a temporary settings folder, so your own settings aren't touched.

GitHub runs this on every push, along with an installer test: install to Program Files, check the Start Menu shortcut, uninstall entry and icon, start the app, then uninstall and check nothing is left behind.

## Releasing a new version

1. Change `<Version>` (and `<InformationalVersion>`, `<AssemblyVersion>`, `<FileVersion>`) in `src\Sumthing\Sumthing.csproj`, and `MyAppVersion` in `installer\Sumthing.iss`, e.g. to `1.0.1`.
2. Add a `## 1.0.1 (<date>)` section at the top of `CHANGELOG.md`. The release notes and Settings › About › What's new come from it, and the release stops if it's missing.
3. Push to `main`.

GitHub then builds and tests it. If every test passes and there's no `v1.0.1` release yet, it publishes Release v1.0.1 with the Setup .exe and portable zip (pushing a tag like `v1.0.1` does the same). The Bytehawk Studio Launcher picks up the new version from there.

## What the installer does

- Installs to `C:\Program Files\Sumthing`
- Adds a Start Menu shortcut, plus a Desktop shortcut if you tick the box
- Adds an uninstaller under **Settings › Apps › Installed apps** (named "Sumthing 1.0.0")
- Offers "Install for me only" on the first page, which needs no administrator rights
- Uninstalling removes the program and its shortcuts. It doesn't touch your settings, history or tape in `%APPDATA%\Sumthing`.

## Icon files

`assets\sumthing.ico` contains sizes from 16 to 256 px, and `assets\sumthing-256.png` is the 256 px picture (the Bytehawk Studio Launcher shows it). The icon is built into Sumthing.exe, so the exe, the Start Menu shortcut and the installer all show the same icon.

## Building in the cloud instead

`.github/workflows/build.yml` builds and tests everything on GitHub's Windows servers on every push. Download the results from the run's **Artifacts**, or from **Releases** for tagged versions.

## Where Sumthing keeps things

| What | Where |
|---|---|
| Settings, history, memory, tape and the Type-a-sum sheet | `%APPDATA%\Sumthing\settings.json` |
| Last exchange rates (for offline use) | `%APPDATA%\Sumthing\currency.json` |
| Error log (only if something crashes) | `%APPDATA%\Sumthing\error.log` |
