# Security and download verification

## Official downloads

Only download public launcher builds from this repository's **GitHub Releases** page or from the project website when it links directly to that Releases page.

Do not download repacked copies from random mirrors.

## Windows / browser warnings

A new or unsigned Windows executable can receive SmartScreen, browser, or antivirus reputation warnings even when the file is legitimate. The launcher also performs actions that security products inspect closely, such as downloading modding components and updating files inside the Dead Cells installation.

Do **not** disable Windows Defender or your antivirus just to run the launcher.

If a release receives a specific malware detection, report the exact detection name and release version so it can be investigated and, when appropriate, submitted to the security vendor as a false positive.

## Verify a release with SHA-256

Every automated GitHub Release contains `SHA256SUMS.txt`.

Windows PowerShell can calculate the downloaded launcher's hash with:

```powershell
Get-FileHash .\DeadCellsModLauncher.exe -Algorithm SHA256
```

The result should exactly match the `DeadCellsModLauncher.exe` entry in `SHA256SUMS.txt`.


## Code signing

The GitHub Actions workflow supports Authenticode signing automatically when the repository owner configures these GitHub Actions secrets:

- `WINDOWS_CERTIFICATE_BASE64`
- `WINDOWS_CERTIFICATE_PASSWORD`

The certificate must be a trusted Windows code-signing certificate in PFX format. Never commit the PFX file or its password to the repository.

## Reporting security problems

For a genuine security issue, avoid posting secrets, private credentials, or sensitive logs in a public issue. Contact the project maintainer privately when possible.
