# Optional Windows code signing

The launcher builds without a signing certificate, but trusted Authenticode signing is strongly recommended for public releases.

The included GitHub Actions workflow will sign `DeadCellsModLauncher.exe` automatically when two repository secrets are present.

## 1. Obtain a trusted code-signing certificate

Use a legitimate Windows code-signing provider or another trusted signing service. The certificate must be usable for Authenticode signing.

Export it as a password-protected `.pfx` file if your provider allows PFX-based signing.

## 2. Convert the PFX to Base64 locally

In PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\to\codesigning.pfx")) | Set-Content codesigning-base64.txt
```

Do not upload either file to the repository.

## 3. Add GitHub Actions secrets

In the repository open:

**Settings → Secrets and variables → Actions → New repository secret**

Create:

### `WINDOWS_CERTIFICATE_BASE64`

Paste the entire Base64 text from `codesigning-base64.txt`.

### `WINDOWS_CERTIFICATE_PASSWORD`

Enter the PFX password.

## 4. Build a release

Create a tag such as `v1.0.1` and push it. The workflow will:

1. build the launcher;
2. sign `DeadCellsModLauncher.exe`;
3. verify the signature;
4. package the complete self-contained build;
5. create SHA-256 checksums;
6. attach the signed `DeadCellsModLauncher.exe` and checksum file to the GitHub Release.

## Important

Never commit your certificate, password, or Base64 certificate text to Git.

If you use a cloud signing service instead of a PFX certificate, replace the optional signing step in `.github/workflows/build-launcher.yml` with that provider's official signing action or CLI.
