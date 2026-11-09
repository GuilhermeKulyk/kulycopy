# Publishing KulyCopy

How releases are built and how to distribute them through GitHub Releases, winget and the Microsoft Store.

## 1. Build the artifacts

```powershell
.\build.ps1          # dist\publish\kulycopy.exe, portable zip, KulyCopy-Setup-<ver>.exe
.\build.ps1 -Msix    # also KulyCopy-<ver>-win-x64.msix (unsigned)
```

Only the .NET 8 SDK is required. The Inno Setup compiler is downloaded from NuGet on first use; the MSIX
step needs the Windows 10/11 SDK (`makeappx.exe`), which GitHub's `windows-latest` runners already have.

| Artifact | Audience | Notes |
| --- | --- | --- |
| `KulyCopy-Setup-<ver>.exe` | most users | Per-user install, no admin rights, Start Menu shortcut, optional "start with Windows", uninstaller. Silent: `/VERYSILENT /NORESTART` (add `/TASKS=` to skip autostart). |
| `KulyCopy-<ver>-portable-win-x64.zip` | USB stick, no install | Contains an empty `portable` marker; settings live in `data\` next to the exe. |
| `KulyCopy-<ver>-win-x64.msix` | Microsoft Store, enterprise sideload | Unsigned by design; the Store signs it. Sign yourself for sideloading. |

## 2. GitHub release

The workflow in `.github/workflows/build.yml` builds, tests and packages on every push. Pushing a tag
creates a GitHub Release with the three artifacts attached and auto-generated notes:

```powershell
# bump <Version> in src/KulyCopy/KulyCopy.csproj and src/KulyCopy.Core/KulyCopy.Core.csproj, update CHANGELOG.md, then:
git tag v0.1.0
git push origin v0.1.0
```

### SmartScreen

An unsigned installer shows the "Windows protected your PC" dialog until it builds reputation.
Options, in order of cost: live with it (users click *More info › Run anyway*), an OV code-signing
certificate (hardware token required since 2023), an EV certificate (immediate reputation), or
[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/) (monthly fee, no token, works in CI).
Signing is done with `signtool sign /fd SHA256 /tr <timestamp-url> /td SHA256 ...` on the Setup exe and
on `kulycopy.exe` inside `dist\publish` *before* compiling the installer.

## 3. winget (free, recommended)

winget installs apps straight from GitHub Releases, so it only needs a manifest in
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).

1. Publish a GitHub release (step 2) so the installer has a permanent URL.
2. Install the tool: `winget install Microsoft.WingetCreate`.
3. Create the manifest interactively and open the pull request:
   ```powershell
   wingetcreate new https://github.com/GuilhermeKulyk/kulycopy/releases/download/v0.1.0/KulyCopy-Setup-0.1.0.exe
   ```
   Package identifier `GuilhermeKulyk.KulyCopy`, installer type `inno`, scope `user`, moniker `kulycopy`.
4. Later versions: `wingetcreate update GuilhermeKulyk.KulyCopy -u <new url> -v <version> --submit`.

After the PR is merged (usually 1–3 days) anyone can run `winget install kulycopy`.

## 4. Microsoft Store

The Store takes the MSIX package. Steps, all done once unless noted:

1. **Partner Center account** at <https://partner.microsoft.com/dashboard> — "Windows & Xbox" developer
   program. An individual account costs a one-time fee (about US$19); it asks for identity verification
   and a payout/tax profile only if the app is paid. KulyCopy is free, so pricing is simply "Free".
2. **Create the app**: *Apps and games › New product › MSIX or PWA app*, reserve the name **KulyCopy**.
3. **Product identity**: open *Product management › Product identity* and copy
   *Package/Identity/Name*, *Package/Identity/Publisher* and *Package/Properties/PublisherDisplayName*.
4. **Build the package with those values** (every release):
   ```powershell
   .\build.ps1 -Msix -MsixIdentityName "12345YourName.KulyCopy" `
       -MsixPublisher "CN=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" `
       -MsixPublisherDisplayName "Your Name"
   ```
   Do not sign it: Partner Center signs Store packages. Bump the version for each submission; the
   Store requires it to increase (four-part, last digit `0`).
5. **Submission**: *Start your submission*.
   - *Packages*: upload `dist\KulyCopy-<ver>-win-x64.msix`. Device family: Windows 10/11 desktop.
   - *Properties*: category *Productivity*. Declare the `runFullTrust` capability reason
     ("desktop clipboard manager; needs to read the clipboard and register global shortcuts").
   - *Age ratings*: IARC questionnaire, no sensitive content.
   - *Store listing* (en-US and pt-BR): description, 3+ screenshots (1366×768 or larger),
     the `packaging\msix\Assets\StoreLogo*.png` logos, **privacy policy URL** (required because the
     app processes clipboard data) — use the GitHub page of [PRIVACY.md](../PRIVACY.md),
     support URL (GitHub issues), and the MIT license text.
   - *Pricing and availability*: Free, all markets.
6. **Certification** takes about 24–72 hours. Common rejections: missing privacy policy, screenshots
   with another app's name, app crashing on first start. After approval the listing goes live and
   updates are new submissions with a higher version.

### Store build notes

- Autostart: the registry *Run* key does not work inside MSIX. The manifest declares a
  `windows.startupTask` (off by default); users enable it in *Settings › Apps › Startup*.
  KulyCopy detects the packaged mode and shows a hint instead of the checkbox.
- The `kulycopy` command-line alias is declared through `appExecutionAlias`, so the CLI works from
  any terminal for Store installs too.
- Settings and history are stored in the package's virtualized `%APPDATA%`; uninstalling from the Store
  removes them (Windows behavior, not ours).

### Testing the MSIX locally

```powershell
# once: a self-signed certificate whose subject equals the manifest Publisher
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=KulyCopy Dev" -KeyUsage DigitalSignature `
    -FriendlyName "KulyCopy Dev" -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$pwd = ConvertTo-SecureString -String "dev" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath dev.pfx -Password $pwd
# trust it (admin): Import-PfxCertificate -FilePath dev.pfx -CertStoreLocation Cert:\LocalMachine\Root -Password $pwd

.\build.ps1 -Msix -MsixPublisher "CN=KulyCopy Dev" -SignCertificate .\dev.pfx -SignPassword dev
Add-AppxPackage .\dist\KulyCopy-0.1.0-win-x64.msix
```

Never commit `dev.pfx` (the `.gitignore` already excludes `*.pfx`).
