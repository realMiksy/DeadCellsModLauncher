# Upload this repository to GitHub

## 1. Create the repository

Create a new **Public** GitHub repository named:

`DeadCellsModLauncher`

Recommended description:

> One-click installer, updater and launcher for the Dead Cells Multiplayer Mod — automatic DCCM setup, Steam/LAN support, build-aware updates and one-click vanilla mode.

Do **not** add a README, .gitignore, or license when creating it, because this upload already contains the repository files.

## 2. Upload these files

Extract `DeadCellsModLauncher_GitHub_UPLOAD_READY.zip`.

On the empty GitHub repository page choose:

**Add file → Upload files**

Drag **all files and folders inside the extracted folder** into GitHub and commit them to `main`.

Important: upload the contents, not the ZIP itself.

## 3. Make the website public

Open:

**Settings → Pages**

Set:

- Source: `Deploy from a branch`
- Branch: `main`
- Folder: `/docs`

Press **Save**.

GitHub will then show the public Pages URL.

## 4. Automatic launcher builds

This repository already contains:

`.github/workflows/build-launcher.yml`

Every push to `main` creates a downloadable Actions build artifact.

To create a public launcher Release, create/push a tag such as:

`v1.0.0`

GitHub Actions will build the Windows single-file launcher and attach:

`DeadCellsModLauncher.exe`

to the Release automatically.

## 5. Repository topics

Suggested topics:

`dead-cells` `multiplayer` `coop` `modding` `launcher` `dccm` `steam` `lan` `windows` `game-modding`

## 6. One URL to change after the repository exists

The launcher currently uses the official multiplayer-mod repository for mod updates, which should remain:

`vaiserYT/DeadCellsMultiplayerMod`

The launcher's **GitHub** button also currently opens that repository. Once your launcher repository exists, you can change only the UI button destination by separating it from the mod-update repository URL.

The mod updater itself should continue checking the multiplayer-mod repository unless you intentionally move the mod releases elsewhere.
