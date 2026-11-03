# Branding, Logo & Windows Icon Guide

Everything needed to ship FreeScreen Recorder as a branded, installable Windows program.
The build pipeline is `build.ps1` → self-contained publish → `FreeScreenRecorder.iss` (Inno Setup, project root) → `dist\*-Setup.exe`.

---

## 1. Product identity

| Field | Value | Where it appears |
| --- | --- | --- |
| Product name | `FreeScreen Recorder` | Window title, Start menu, installer, Add/Remove Programs |
| Technical name | `FreeScreenRecorder` | Namespace, assembly name, settings folder |
| Executable | `FreeScreenRecorder.exe` | Install folder, shortcuts |
| Publisher | `AppPublisher` *(replace before release)* | Installer, file properties, Add/Remove Programs |
| Product URL | `https://example.com` *(replace)* | Installer support/update links |
| Package identity | `62A2BE88-A8EF-4D3E-8641-E47E923AC7F3` | `Package.appxmanifest` (MSIX only) |
| Installer AppId | `B7E1C2A4-9F3D-4A6B-8E5C-2D7F1A9B0C33` | Inno Setup upgrade identity — never change after first release |
| Version | `1.0.0` | `FreeScreenRecorder.csproj` → `build.ps1` |
| Settings folder | `%LOCALAPPDATA%\FreeScreenRecorder` | User data (never uninstalled automatically) |
| Default recordings | `%USERPROFILE%\Videos\FreeScreenRecorder` | User recordings |

### Naming rules

- Keep **FreeScreen Recorder** (with a space) for humans, **FreeScreenRecorder** (PascalCase) for files, folders, namespaces, and URLs.
- Never rename `FreeScreenRecorder.exe`, the installer `AppId`, or the settings folder after a public release; upgrades depend on them.
- Use the same publisher string in the installer, the EXE version info, and the code-signing certificate subject.
- Semantic versioning: `MAJOR.MINOR.PATCH` for marketing; `MAJOR.MINOR.PATCH.0` for assembly/file versions.

---

## 2. Visual identity

### Logo concept

A single, recognizable mark that survives at 16 px:

- **Tile:** a rounded square app tile with a vertical dark gradient (`#1A2030` → `#090C13`) and a faint white edge.
- **Mark:** four capture-frame corner brackets (`#E9EEF7`) around a soft glow, a timer/recording arc, and a solid gradient core (mint → blue) at the centre.
- **Wordmark:** "FreeScreen" in semibold, "Recorder" in regular/light, separated by a space.
- **Master file:** 1024×1024 PNG plus SVG, supplied in `Branding\mine\` and mirrored to `Branding\source\`.
- **Clear space:** at least 12.5% of the tile's width on all sides.
- **Minimum sizes:** 16×16 px for the mark alone, 96 px wide for the wordmark lockup.
- **Backgrounds:** the tile is self-contained and works on light and dark surfaces. For single-colour use, redraw only the corner brackets in one colour; do not flatten the gradient core.
- **Do not:** recolour the gradient, stretch, rotate, add outlines or drop shadows, crop the brackets, or place the tile on a busy photo.

### Colour palette

| Token | Hex | Usage |
| --- | --- | --- |
| Brand background start | `#1A2030` | Tile gradient, splash, installer wizard |
| Brand background end | `#090C13` | Tile gradient, deep surfaces |
| Accent mint | `#2BF0C8` | Recording arc/glow, active accents |
| Accent blue | `#3D7BFF` | Recording core, links, focus |
| Foreground / brackets | `#E9EEF7` | Capture brackets, primary text on dark |
| Tile edge | `#FFFFFF` at 8% | Hairline around the rounded tile |
| Success | `#22C55E` | Saved / complete states |
| Warning | `#F59E0B` | Non-fatal warnings |
| Error | `#EF4444` | Errors and destructive actions |
| Surface (dark) | `#111827` | Cards, panels |
| Text primary (dark) | `#E9EEF7` | Body text on dark |
| Text secondary (dark) | `#9AA7BD` | Captions on dark |
| Background (light) | `#F8FAFC` | Light tiles, docs |
| Surface (light) | `#FFFFFF` | Cards |
| Text primary (light) | `#0F172A` | Body text on light |
| Border | `#E2E8F0` | Dividers, tile outlines |

The current WinUI UI uses the system accent colour and Mica. Applying these brand tokens is optional; if adopted, drive them through theme resources rather than hard-coded brushes.

### Typography

| Role | Font | Size / weight |
| --- | --- | --- |
| Display / app title | Segoe UI Variable Display | 28 px, Semibold |
| Headings | Segoe UI Variable Text | 20 px, Semibold |
| Body | Segoe UI Variable Text | 14 px, Regular |
| Captions | Segoe UI Variable Text | 12 px, Regular |
| Fallback | Segoe UI | same sizes |

---

## 3. Required icons — EXE and installer (must ship)

### 3.1 `Assets\AppIcon.ico` (multi-resolution Windows icon)

A single `.ico` containing every size below. Windows picks the best size for the shell, taskbar, Alt-Tab, file properties, and the installer.

| Size | Where it is used |
| --- | --- |
| 16×16 | Explorer small icons, title bar (100% DPI) |
| 20×20 | Explorer small icons at 125% DPI |
| 24×24 | Explorer small icons at 150% DPI |
| 32×32 | Taskbar, Alt-Tab, desktop shortcuts |
| 40×40 | Explorer at 200% DPI |
| 48×48 | Medium icons, Start menu |
| 64×64 | Large icons |
| 96×96 | Extra-large icons (150–200% DPI) |
| 128×128 | Extra-large icons at 200% DPI |
| 256×256 | Jumbo icons, file properties, Store |

Notes:

- 256 px is the maximum Windows will use; do not add larger entries.
- The 256×256 entry may be BMP or PNG-compressed inside the ICO; the supplied file uses BMP.
- Current `Assets\AppIcon.ico` contains **16, 24, 32, 48, 64, 128, 256** (59 KB) from `Branding\mine\icon.ico`. Add **20, 40, 96** only if you want the full DPI ladder.
- The file is referenced by `<ApplicationIcon>` in the csproj, the title-bar `ImageIconSource`, `SetupIconFile`, and `UninstallDisplayIcon`.

### 3.2 Inno Setup branding assets

| Asset | Format | Size | Required | Notes |
| --- | --- | --- | --- | --- |
| `SetupIconFile` | `.ico` | 16–256 multi-size | Yes | Reuse `Assets\AppIcon.ico` |
| `UninstallDisplayIcon` | `.exe` or `.ico` | n/a | Yes | Points at the installed EXE/icon |
| `WizardImage.bmp` | BMP/PNG, 24/32-bit | 164×314 (100%) | Optional | Left panel of the wizard; keep critical art inside the safe area |
| `WizardSmallImage.bmp` | BMP/PNG, 24/32-bit | 55×55 | Optional | Top-right corner of the wizard |
| `LicenseFile` | `.txt` / `.rtf` | n/a | Yes | Already wired to `LICENSE` |
| Version info | metadata | n/a | Yes | Set by `build.ps1` (`AppVersion`, `AppPublisher`) |

Place `WizardImage.bmp` and `WizardSmallImage.bmp` in the project root next to `FreeScreenRecorder.iss`. The script includes them automatically only when present.

### 3.3 File-property metadata (set in the csproj)

`Product`, `Description`, `Company`, `Copyright`, `FileVersion`, `AssemblyVersion` — visible in Explorer → Properties → Details and used by Add/Remove Programs. Replace the `AppPublisher` placeholders before release.

---

## 4. Optional MSIX / Microsoft Store icons

The project builds unpackaged (`WindowsPackageType=None`), so MSIX assets are not required for the Inno installer. Ship them only if you publish through the Microsoft Store or package as MSIX. All are PNG with transparency unless stated.

### 4.1 Tile and shell assets

Scale variants: **100%, 125%, 150%, 200%, 400%** (`scale-100` … `scale-400`).

| Asset | Base size | Variants (100/125/150/200/400) | Purpose |
| --- | --- | --- | --- |
| `Square44x44Logo.png` | 44×44 | 44, 55, 66, 88, 176 | Start menu, taskbar, Alt-Tab |
| `Square150x150Logo.png` | 150×150 | 150, 188, 225, 300, 600 | Medium Start tile |
| `Wide310x150Logo.png` | 310×150 | 310×150, 388×188, 465×225, 620×300, 1240×600 | Wide Start tile |
| `StoreLogo.png` | 50×50 | 50, 63, 75, 100, 200 | Store listing, package logo |
| `SplashScreen.png` | 620×300 | 620×300, 775×375, 930×450, 1240×600, 2480×1200 | Launch splash |
| `LockScreenLogo.png` | 24×24 | 24, 30, 36, 48, 96 | Lock screen / badge |
| `SmallTile.png` *(legacy)* | 71×71 | 71, 89, 107, 142, 284 | Small tile |
| `LargeTile.png` *(legacy)* | 310×310 | 310, 388, 465, 620, 1240 | Large tile |
| `BadgeLogo.png` *(optional)* | 24×24 | 24, 30, 36, 48, 96 | Monochrome badge |

### 4.2 Square44x44 target-size variants

Windows also requests exact pixel sizes independent of DPI. Provide `targetsize-XX` variants:

`16, 24, 32, 40, 48, 60, 64, 72, 80, 96, 256`

Plus alternate forms required by the manifest:

| Asset | Size | Purpose |
| --- | --- | --- |
| `Square44x44Logo.targetsize-24_altform-unplated.png` | 24×24 | Taskbar, no plate |
| `Square44x44Logo.targetsize-48_altform-lightunplated.png` | 48×48 | Light taskbar, no plate |
| `Square44x44Logo.altform-unplated.png` | 44×44 | Unplated default |
| `Square44x44Logo.altform-lightunplated.png` | 44×44 | Light unplated default |

### 4.3 Microsoft Store listing artwork

| Asset | Size | Required |
| --- | --- | --- |
| App icon | 300×300 PNG | Yes |
| Screenshots | 1366×768 or 1920×1080 (16:9), 1–10 images | Yes |
| Store logo | 50×50 PNG | Yes |
| Promotional / hero art | 1920×1080 PNG | Optional |
| Poster art | 720×1080 PNG | Optional |
| Box art | 1080×1080 PNG | Optional |
| Trailer | MP4/MOV, 1080p, ≤ 60 s | Optional |

---

## 5. Optional web and social assets

| Asset | Size | Notes |
| --- | --- | --- |
| Favicon | 16×16, 32×32, 48×48 | `.ico` plus PNG |
| Apple touch icon | 180×180 | PNG, opaque background |
| Android / PWA | 192×192, 512×512 | Include a maskable 512×512 |
| Open Graph image | 1200×630 | Title + mark, safe margins |
| Twitter/X card | 1200×675 | Same art as OG |
| GitHub social preview | 1280×640 | Repository settings |

---

## 6. Current asset audit

| File | Dimensions | Status |
| --- | --- | --- |
| `Assets\AppIcon.ico` | 16, 24, 32, 48, 64, 128, 256 | Good; 20, 40, 96 optional |
| `Assets\StoreLogo.png` | 50×50 | Present |
| `Assets\Square44x44Logo.scale-200.png` | 88×88 | Present; other scales missing |
| `Assets\Square44x44Logo.targetsize-24_altform-unplated.png` | 24×24 | Present |
| `Assets\Square44x44Logo.targetsize-48_altform-lightunplated.png` | 48×48 | Present |
| `Assets\Square150x150Logo.scale-200.png` | 300×300 | Present; other scales missing |
| `Assets\Wide310x150Logo.scale-200.png` | 620×300 | Present; other scales missing |
| `Assets\SplashScreen.scale-200.png` | 1240×600 | Present; other scales missing |
| `Assets\LockScreenLogo.scale-200.png` | 48×48 | Present; other scales missing |

All existing tile PNGs and `AppIcon.ico` now use the supplied master logo. Missing only matters for MSIX/Store packaging: the base `scale-100` files and the remaining scale/target-size variants. The EXE and Inno installer need only `AppIcon.ico`.

---

## 7. Source files and naming

Keep editable sources separate from exported runtime assets:

```
Branding/
  mine/                 original supplied assets (logo.svg, icon-1024.png, icon-256.png, icon.ico, github-banner.png)
  source/
    logo-master.svg     copy of mine/logo.svg
    logo-master-1024.png copy of mine/icon-1024.png
  exports/
    appicon/            appicon-16.png ... appicon-256.png
    AppIcon.ico
    installer/          WizardImage.bmp, WizardSmallImage.bmp (copy to project root)
    msix/               Square44x44Logo.scale-*.png, ...
    store/              300x300.png, screenshots/
```

Runtime assets still live in `FreeScreen-Recorder\Assets\` because the csproj and manifest reference that path. Copy the final exports there.

Naming convention: lowercase kebab-case for sources, exact Microsoft asset names for MSIX files, `appicon-<size>.png` for intermediate PNGs.

---

## 8. Generating the assets

### Multi-resolution ICO (ImageMagick)

```powershell
magick Branding/source/logo-master-1024.png `
  -define icon:auto-resize=256,128,96,64,48,40,32,24,20,16 `
  FreeScreen-Recorder/Assets/AppIcon.ico
```

`Branding\mine\icon.ico` is already copied to `Assets\AppIcon.ico` and used by the build. Regenerate only if you add sizes or change the master.

### PNG size ladder

```powershell
$sizes = 16,20,24,32,40,48,64,96,128,256
New-Item -ItemType Directory -Force Branding/exports/appicon | Out-Null
foreach ($s in $sizes) {
  magick Branding/source/logo-master-1024.png -resize "${s}x${s}" "Branding/exports/appicon/appicon-$s.png"
}
```

### Installer wizard images

```powershell
magick Branding/source/logo-master-1024.png `
  -resize 164x314^ -gravity center -background "#1A2030" -extent 164x314 `
  -type TrueColor FreeScreen-Recorder/WizardImage.bmp

magick Branding/source/logo-master-1024.png `
  -resize 55x55 -background none -gravity center -extent 55x55 `
  -type TrueColorAlpha FreeScreen-Recorder/WizardSmallImage.bmp
```

### MSIX scale variants

```powershell
$assets = @{
  'Square44x44Logo'   = @(44, 55, 66, 88, 176)
  'Square150x150Logo' = @(150, 188, 225, 300, 600)
  'StoreLogo'         = @(50, 63, 75, 100, 200)
  'LockScreenLogo'    = @(24, 30, 36, 48, 96)
}
$scales = 100,125,150,200,400
foreach ($name in $assets.Keys) {
  for ($i = 0; $i -lt $scales.Count; $i++) {
    $size = $assets[$name][$i]
    magick Branding/source/logo-master-1024.png -resize "${size}x${size}" `
      "FreeScreen-Recorder/Assets/$name.scale-$($scales[$i]).png"
  }
}
```

Opaque tiles (Start, Store) should use a solid brand background; shell icons should keep transparency.

---

## 9. Code signing and SmartScreen

Unsigned installers trigger Windows SmartScreen and browser warnings. For public distribution:

1. Obtain an Authenticode code-signing certificate (OV or, preferably, EV).
2. Sign both the app EXE and the Setup EXE **after** building and before publishing.
3. Timestamp every signature so it stays valid after the certificate expires.
4. Build reputation over time; EV certificates and the Microsoft Store avoid most warnings immediately.

`build.ps1` signs both outputs when a thumbprint is supplied:

```powershell
.\build.ps1 -SignCertificateThumbprint <SHA1_THUMBPRINT>
```

Manual equivalent:

```powershell
signtool sign /sha1 <THUMBPRINT> /fd SHA256 `
  /tr http://timestamp.digicert.com /td SHA256 `
  dist\FreeScreenRecorder-1.0.0-win-x64-Setup.exe
```

For MSIX/Store, the manifest `Publisher` (`CN=AppPublisher`) **must** exactly match the certificate subject and the Partner Center identity.

---

## 10. Release checklist

- [ ] Product name, publisher, URL, and copyright are final (no `AppPublisher` / `example.com`).
- [ ] `Version`, `FileVersion`, `AssemblyVersion` bumped in the csproj; `AppId` unchanged.
- [ ] `Assets\AppIcon.ico` contains 16, 20, 24, 32, 40, 48, 64, 96, 128, 256.
- [ ] Icon is legible at 16 px and 32 px on light and dark backgrounds.
- [ ] Title-bar icon and EXE icon match.
- [ ] Inno `WizardImage.bmp` / `WizardSmallImage.bmp` exported (if used).
- [ ] License text included and accurate.
- [ ] `.\build.ps1` completes; portable ZIP and `*-Setup.exe` produced in `dist\`.
- [ ] Installer runs on a clean Windows 10 22H2 and Windows 11 VM.
- [ ] App launches, records, and uninstalls without leftovers.
- [ ] Both EXEs signed and timestamped; signature verifies with `signtool verify /pa`.
- [ ] Screenshots and Store art ready (only if publishing to the Store).

---

## 11. Quick reference — what to hand a designer

| Deliverable | Spec |
| --- | --- |
| Master logo | SVG + 1024×1024 PNG (supplied in `Branding\mine\`) |
| README banner | 1280×480 PNG (supplied in `Branding\mine\`) |
| Monochrome logo | Optional single-colour bracket mark for one-colour printing |
| Windows icon | `.ico`, 16/24/32/48/64/128/256 supplied; 20/40/96 optional |
| Installer art | 164×314 BMP wizard image, 55×55 BMP small image |
| App tiles (MSIX) | Square44, Square150, Wide310×150, StoreLogo, Splash, LockScreen at all scales |
| Store listing | 300×300 icon, 1366×768 or 1920×1080 screenshots |
| Palette | Dark tile `#1A2030` → `#090C13`, mint `#2BF0C8`, blue `#3D7BFF`, brackets `#E9EEF7` |
| Type | Segoe UI Variable, with Segoe UI fallback |
