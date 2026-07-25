using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace DcmmInstaller;

public partial class MainWindow : Window
{
    // ---- Configuration: point these at the repo the installer serves ----
    private const string RepoOwner = "vaiserYT";
    private const string RepoName  = "DeadCellsMultiplayerMod";
    private const string GitHubRepoUrl = "https://github.com/vaiserYT/DeadCellsMultiplayerMod";
    private const string ModFolderName = "DeadCellsMultiplayerMod";
    private const int DeadCellsSteamAppId = 588650;
    private const string PlayShortcutName = "Play Dead Cells Multiplayer";
    private const string VanillaModeMarkerName = ".dcmm-vanilla-mode";
    private const string InstalledBuildMarkerName = ".dcmm-installed-build.json";

    // DCCM (Core Modding API)
    private const string DccmRepoOwner = "dead-cells-core-modding";
    private const string DccmRepoName  = "core";
    private const string DccmDocsUrl = "https://dead-cells-core-modding.github.io/docs/docs/tutorial/install-core/";

    // Community
    private const string DiscordUrl = "https://discord.gg/rEAzpe7wyb";

    private static readonly HttpClient Http = CreateHttp();

    private string? _gameRoot;      // ...\steamapps\common\Dead Cells
    private string? _coremodRoot;   // ...\Dead Cells\coremod
    private string? _modsRoot;      // ...\coremod\mods
    private string? _latestVersion;
    private string? _latestZipUrl;
    private string? _latestAssetName;
    private long? _latestAssetId;
    private long? _latestAssetSize;
    private string? _latestAssetCreatedAt;
    private string? _latestAssetUpdatedAt;
    private string? _latestAssetDigest;
    private string? _latestBuildFingerprint;
    private bool _updateAvailable;
    private string? _dccmLatestVersion;
    private string? _dccmZipUrl;
    private string? _dccmSteamShellUrl;
    private string? _dccmSteamShellAssetName;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient();
        h.DefaultRequestHeaders.UserAgent.ParseAdd("DeadCellsMultiplayerMod-Installer/1.0");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        h.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        h.Timeout = TimeSpan.FromSeconds(60);
        return h;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        DetectEverything();
        RefreshInstalledVersion();
        await CheckLatestAsync();
        await CheckDccmLatestAsync();
    }

    private void Discord_Click(object sender, RoutedEventArgs e) => OpenUrl(DiscordUrl);
    private void GitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(GitHubRepoUrl);

    private void PopulateNews(string? title, string? version, string? body)
    {
        Dispatcher.Invoke(() =>
        {
            var heading = !string.IsNullOrWhiteSpace(title) ? title!
                        : !string.IsNullOrWhiteSpace(version) ? $"Version {version}"
                        : "Latest release";
            NewsHeading.Text = heading;
            NewsBody.Text = string.IsNullOrWhiteSpace(body)
                ? "No patch notes were provided for this release."
                : CleanMarkdown(body!);
        });
    }

    // Light markdown -> readable plain text for the news panel. Not a full parser; just enough
    // to make GitHub release notes pleasant to read in a TextBlock.
    private static string CleanMarkdown(string md)
    {
        var lines = md.Replace("\r\n", "\n").Split('\n');
        var sb = new System.Text.StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { sb.AppendLine(); continue; }

            // Headings: strip leading #, keep the text as its own line.
            int h = 0; while (h < line.Length && line[h] == '#') h++;
            if (h > 0) line = line[h..].TrimStart();

            // Bullets: normalize -, *, + to a bullet dot.
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "))
            {
                var indent = line.Length - trimmed.Length;
                line = new string(' ', indent) + "\u2022 " + trimmed[2..];
            }

            // Inline emphasis/backticks: drop the markers.
            line = line.Replace("**", "").Replace("`", "").Replace("__", "");
            sb.AppendLine(line);
        }
        return sb.ToString().Trim();
    }

    // ---------------------------------------------------------------- detection

    private void DetectEverything()
    {
        _gameRoot = TryFindGameRoot();
        if (_gameRoot != null)
        {
            _coremodRoot = Path.Combine(_gameRoot, "coremod");
            _modsRoot = Path.Combine(_coremodRoot, "mods");
            PathText.Text = Path.Combine(_modsRoot, ModFolderName);
            PathHint.Text = "Auto-detected. Click Change if your install is elsewhere.";
        }
        else
        {
            PathText.Text = "Dead Cells not found automatically.";
            PathHint.Text = "Click Change and pick your Dead Cells folder (the one containing deadcells.exe or the coremod folder).";
        }
        RefreshDccmStatus();
        UpdateInstallButtonState();
    }

    private void RefreshDccmStatus()
    {
        var dccmPresent = DccmInstalled();
        var steam = IsSteamInstall();
        var vanillaMode = IsVanillaMode();
        if (dccmPresent)
        {
            DccmIcon.Text = vanillaMode ? "\u23F8" : "\u2714";
            DccmIcon.Foreground = vanillaMode
                ? (Brush)FindResource("SubtleTextBrush")
                : (Brush)FindResource("GoodBrush");
            var installedV = ReadDccmInstalledVersion();
            var mode = vanillaMode
                ? "Disabled — full vanilla mode"
                : steam
                    ? (SteamShellReady() ? "Steam hosting + LAN / port forwarding" : "Steam install; Steam shell will be repaired on Install/Play")
                    : "LAN / port-forwarding mode";
            if (installedV != null)
                DccmStatus.Text = vanillaMode
                    ? $"Installed ({installedV}), but currently disabled. Dead Cells will launch without DCCM or the co-op mod."
                    : $"Installed ({installedV}). {mode}. The launcher will repair the mod loader automatically when needed.";
            else
                DccmStatus.Text = vanillaMode
                    ? "Installed, but currently disabled. Dead Cells will launch in full vanilla mode."
                    : $"Installed. {mode}. The launcher will repair the mod loader automatically when needed.";
            DccmStatus.Foreground = (Brush)FindResource("SubtleTextBrush");
            DccmGuideBtn.Visibility = Visibility.Visible;
            if (DccmInstallBtn != null) DccmInstallBtn.Content = "Repair / Update DCCM";
        }
        else
        {
            DccmIcon.Text = "\u26A0";
            DccmIcon.Foreground = (Brush)FindResource("BadBrush");
            DccmStatus.Text = _gameRoot == null
                ? "Dead Cells has not been selected yet."
                : "Not installed yet. Clicking Install will download DCCM and the required launcher automatically.";
            DccmStatus.Foreground = _gameRoot == null
                ? (Brush)FindResource("SubtleTextBrush")
                : (Brush)FindResource("BadBrush");
            DccmGuideBtn.Visibility = Visibility.Visible;
            if (DccmInstallBtn != null) DccmInstallBtn.Content = "Install / Repair DCCM";
        }
        if (DccmInstallBtn != null) DccmInstallBtn.IsEnabled = _gameRoot != null && !_busy;
    }

    private string? TryFindGameRoot()
    {
        // Steam first, because it is the most common install and we can identify it reliably.
        foreach (var lib in EnumerateSteamLibraries())
        {
            var candidate = Path.Combine(lib, "steamapps", "common", "Dead Cells");
            if (LooksLikeGameRoot(candidate))
                return candidate;
        }

        // Then try common standalone / GOG-style locations. We deliberately avoid a full-drive
        // recursive scan: it would make launcher startup slow and can trip antivirus software.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var root = drive.RootDirectory.FullName;
            var candidates = new[]
            {
                Path.Combine(root, "GOG Games", "Dead Cells"),
                Path.Combine(root, "Games", "Dead Cells"),
                Path.Combine(root, "Program Files", "GOG Galaxy", "Games", "Dead Cells"),
                Path.Combine(root, "Program Files (x86)", "GOG Galaxy", "Games", "Dead Cells")
            };
            foreach (var candidate in candidates)
                if (LooksLikeGameRoot(candidate))
                    return candidate;
        }
        return null;
    }

    private bool IsSteamInstall()
    {
        if (_gameRoot == null) return false;
        var game = Path.GetFullPath(_gameRoot).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var lib in EnumerateSteamLibraries())
        {
            var candidate = Path.GetFullPath(Path.Combine(lib, "steamapps", "common", "Dead Cells"))
                .TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(game, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return game.Contains($"{Path.DirectorySeparatorChar}steamapps{Path.DirectorySeparatorChar}common{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeGameRoot(string dir)
    {
        if (!Directory.Exists(dir)) return false;
        return File.Exists(Path.Combine(dir, "deadcells.exe"))
            || File.Exists(Path.Combine(dir, "deadcells_gl.exe"))
            || Directory.Exists(Path.Combine(dir, "coremod"));
    }

    private static IEnumerable<string> EnumerateSteamLibraries()
    {
        var roots = new List<string>();
        string? steam = ReadSteamPathFromRegistry();
        if (steam != null) roots.Add(steam);

        // Common defaults as a fallback.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam"));
            roots.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !seen.Add(root)) continue;
            yield return root;

            // Parse libraryfolders.vdf for additional library drives.
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var extra in ParseLibraryFolders(vdf))
                    if (seen.Add(extra)) yield return extra;
            }
        }
    }

    private static string? ReadSteamPathFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string is string p ? p.Replace('/', '\\') : null;
        }
        catch { return null; }
    }

    private static IEnumerable<string> ParseLibraryFolders(string vdfPath)
    {
        // Lightweight: pull every "path" "<value>" pair. Avoids a VDF dependency.
        string text;
        try { text = File.ReadAllText(vdfPath); } catch { yield break; }
        int i = 0;
        while (true)
        {
            int key = text.IndexOf("\"path\"", i, StringComparison.OrdinalIgnoreCase);
            if (key < 0) yield break;
            int firstQuote = text.IndexOf('"', key + 6);
            if (firstQuote < 0) yield break;
            int secondQuote = text.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0) yield break;
            var value = text.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Replace("\\\\", "\\");
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
            i = secondQuote + 1;
        }
    }

    // ---------------------------------------------------------------- versions

    private string? ActiveModPath()
        => _modsRoot == null ? null : Path.Combine(_modsRoot, ModFolderName);

    private string? DisabledModPath()
        => _modsRoot == null ? null : Path.Combine(_modsRoot, ModFolderName + ".disabled");

    private string? VanillaModeMarkerPath()
        => _gameRoot == null ? null : Path.Combine(_gameRoot, VanillaModeMarkerName);

    private bool IsVanillaMode()
    {
        var marker = VanillaModeMarkerPath();
        return marker != null && File.Exists(marker);
    }

    private void RefreshInstalledVersion()
    {
        var installed = ReadInstalledVersion();
        InstalledVer.Text = installed == null
            ? "not installed"
            : IsVanillaMode() ? installed + " (disabled)" : installed;
    }

    private string? ReadInstalledVersion()
    {
        if (_modsRoot == null) return null;
        foreach (var folder in new[] { ActiveModPath(), DisabledModPath() })
        {
            if (folder == null) continue;
            var info = Path.Combine(folder, "modinfo.json");
            if (!File.Exists(info)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(info));
                return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            }
            catch { }
        }
        return null;
    }

    private string? ReadInstalledBuildFingerprint()
    {
        if (_modsRoot == null) return null;
        foreach (var folder in new[] { ActiveModPath(), DisabledModPath() })
        {
            if (folder == null) continue;
            var marker = Path.Combine(folder, InstalledBuildMarkerName);
            if (!File.Exists(marker)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(marker));
                return doc.RootElement.TryGetProperty("fingerprint", out var f) ? f.GetString() : null;
            }
            catch { }
        }
        return null;
    }

    private static string BuildAssetFingerprint(string? name, long? id, long? size, string? createdAt, string? updatedAt, string? digest)
    {
        // GitHub creates a new asset id when an asset is deleted/re-uploaded. updated_at and
        // digest cover in-place metadata/content changes when available. Hash the combined
        // metadata so the marker stays compact and does not depend on the visible mod version.
        var canonical = string.Join("|", new[]
        {
            name ?? "",
            id?.ToString() ?? "",
            size?.ToString() ?? "",
            createdAt ?? "",
            updatedAt ?? "",
            digest ?? ""
        });
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    private void WriteInstalledBuildMarker(string modFolder)
    {
        if (string.IsNullOrWhiteSpace(_latestBuildFingerprint)) return;
        var marker = Path.Combine(modFolder, InstalledBuildMarkerName);
        var json = JsonSerializer.Serialize(new
        {
            version = _latestVersion,
            asset_name = _latestAssetName,
            asset_id = _latestAssetId,
            asset_size = _latestAssetSize,
            asset_created_at = _latestAssetCreatedAt,
            asset_updated_at = _latestAssetUpdatedAt,
            asset_digest = _latestAssetDigest,
            fingerprint = _latestBuildFingerprint,
            installed_at_utc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(marker, json);
    }

    private bool RecalculateUpdateAvailable()
    {
        var installed = ReadInstalledVersion();
        if (installed == null || _latestVersion == null)
        {
            _updateAvailable = false;
            return false;
        }

        if (!VersionsEqual(installed, _latestVersion))
        {
            _updateAvailable = true;
            return true;
        }

        // Same visible version: compare the exact GitHub release asset build. Existing installs
        // from older launcher versions have no marker, so they receive one refresh to establish
        // a baseline. After that, replacing/re-uploading the same 0.4.1 ZIP is detected too.
        if (!string.IsNullOrWhiteSpace(_latestBuildFingerprint))
        {
            var installedFingerprint = ReadInstalledBuildFingerprint();
            _updateAvailable = string.IsNullOrWhiteSpace(installedFingerprint)
                || !string.Equals(installedFingerprint, _latestBuildFingerprint, StringComparison.OrdinalIgnoreCase);
            return _updateAvailable;
        }

        _updateAvailable = false;
        return false;
    }

    private void RefreshUpdateIndicator()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateBadge.Visibility = _updateAvailable ? Visibility.Visible : Visibility.Collapsed;
            UpdateBadge.Text = _updateAvailable ? "●  New update available" : "";
        });
    }

    private async Task CheckLatestAsync()
    {
        try
        {
            SetStatus("Checking for the latest release...");
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                LatestVer.Text = "unavailable";
                SetStatus($"Could not reach GitHub ({(int)resp.StatusCode}). You can still install if files are cached, or try again later.");
                return;
            }
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            _latestVersion = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            LatestVer.Text = _latestVersion ?? "unknown";

            // News: show the latest release's title + notes.
            var relTitle = root.TryGetProperty("name", out var nm) ? nm.GetString() : null;
            var relBody  = root.TryGetProperty("body", out var bd) ? bd.GetString() : null;
            PopulateNews(relTitle, _latestVersion, relBody);

            // Find the mod ZIP asset and remember its GitHub identity. The visible mod version
            // is not enough: maintainers may replace the ZIP while keeping the same tag/version.
            _latestZipUrl = null;
            _latestAssetName = null;
            _latestAssetId = null;
            _latestAssetSize = null;
            _latestAssetCreatedAt = null;
            _latestAssetUpdatedAt = null;
            _latestAssetDigest = null;
            _latestBuildFingerprint = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                    _latestAssetName = name;
                    _latestZipUrl = a.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                    _latestAssetId = a.TryGetProperty("id", out var aid) && aid.TryGetInt64(out var id) ? id : null;
                    _latestAssetSize = a.TryGetProperty("size", out var asz) && asz.TryGetInt64(out var size) ? size : null;
                    _latestAssetCreatedAt = a.TryGetProperty("created_at", out var aca) ? aca.GetString() : null;
                    _latestAssetUpdatedAt = a.TryGetProperty("updated_at", out var aua) ? aua.GetString() : null;
                    _latestAssetDigest = a.TryGetProperty("digest", out var adg) && adg.ValueKind == JsonValueKind.String ? adg.GetString() : null;
                    _latestBuildFingerprint = BuildAssetFingerprint(
                        _latestAssetName, _latestAssetId, _latestAssetSize, _latestAssetCreatedAt, _latestAssetUpdatedAt, _latestAssetDigest);
                    break;
                }
            }

            var installed = ReadInstalledVersion();
            var updateAvailable = RecalculateUpdateAvailable();
            if (installed == null)
            {
                LatestVer.Text = _latestVersion ?? "unknown";
                SetStatus("Ready to install.");
            }
            else if (updateAvailable && _latestVersion != null && !VersionsEqual(installed, _latestVersion))
            {
                LatestVer.Text = _latestVersion;
                SetStatus($"Update available: {installed} \u2192 {_latestVersion}.");
            }
            else if (updateAvailable)
            {
                LatestVer.Text = (_latestVersion ?? installed) + " (new build)";
                SetStatus($"New build available for {installed}. The GitHub package changed even though the version number stayed the same.");
            }
            else
            {
                LatestVer.Text = _latestVersion ?? "unknown";
                SetStatus("You have the latest build.");
            }

            RefreshUpdateIndicator();
            UpdateInstallButtonState();

            // Auto-update on launch. If it fails, RunInstallAsync leaves _updateAvailable true,
            // so the badge remains visible and the user can press Update Now manually.
            if (updateAvailable && AutoUpdateChk.IsChecked == true && _latestZipUrl != null && !_busy)
            {
                var sameVersionBuild = _latestVersion != null && VersionsEqual(installed!, _latestVersion);
                SetStatus(sameVersionBuild
                    ? $"Auto-updating {_latestVersion} to the newest build..."
                    : $"Auto-updating to {_latestVersion}...");
                await RunInstallAsync();
            }
        }
        catch (Exception ex)
        {
            LatestVer.Text = "unavailable";
            SetStatus("Could not check for updates: " + ex.Message);
        }
    }

    private static bool VersionsEqual(string a, string b)
        => string.Equals(a.TrimStart('v'), b.TrimStart('v'), StringComparison.OrdinalIgnoreCase);

    private void VerifyDownloadedAssetDigest(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(_latestAssetDigest)) return;
        const string prefix = "sha256:";
        if (!_latestAssetDigest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        var expected = _latestAssetDigest[prefix.Length..].Trim();
        using var stream = File.OpenRead(zipPath);
        using var sha = SHA256.Create();
        var actual = Convert.ToHexString(sha.ComputeHash(stream));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded multiplayer package did not match GitHub's SHA-256 digest. Nothing was installed; try the update again.");
    }

    // ---------------------------------------------------------------- install

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_modsRoot == null || _gameRoot == null)
        {
            SetStatus("Pick your Dead Cells folder first (Change...).");
            return;
        }
        if (_latestZipUrl == null)
        {
            SetStatus("No downloadable mod release found yet. Make sure a release has been published on GitHub.");
            return;
        }

        await RunInstallAsync();
    }

    private async Task RunInstallAsync()
    {
        SetBusy(true);
        string? tempZip = null;
        string? tempExtract = null;
        try
        {
            // One-click setup: the mod install owns the prerequisite chain. A fresh machine only
            // needs to select the Dead Cells folder and press Install.
            await EnsureDccmReadyAsync(forceCoreInstall: false);

            SetStatus("Downloading Dead Cells Multiplayer...");
            SetProgress(0.42);
            tempZip = Path.Combine(Path.GetTempPath(), $"dcmm_{Guid.NewGuid():N}.zip");
            await DownloadFileAsync(_latestZipUrl!, tempZip, 0.42, 0.72);
            VerifyDownloadedAssetDigest(tempZip);

            SetStatus("Extracting multiplayer mod...");
            SetProgress(0.74);
            tempExtract = Path.Combine(Path.GetTempPath(), $"dcmm_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExtract);
            ZipFile.ExtractToDirectory(tempZip, tempExtract);

            var payload = FindModFolderInExtract(tempExtract);
            if (payload == null)
                throw new InvalidOperationException("The downloaded release didn't contain modinfo.json. The release asset may be malformed.");

            var target = IsVanillaMode() ? DisabledModPath()! : ActiveModPath()!;
            var otherTarget = IsVanillaMode() ? ActiveModPath() : DisabledModPath();
            SetStatus(IsVanillaMode() ? "Updating multiplayer mod (kept disabled)..." : "Installing multiplayer mod...");
            SetProgress(0.86);

            Directory.CreateDirectory(_modsRoot!);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            if (otherTarget != null && Directory.Exists(otherTarget))
                Directory.Delete(otherTarget, recursive: true);
            CopyDirectory(payload, target);
            WriteInstalledBuildMarker(target);
            _updateAvailable = false;
            RefreshUpdateIndicator();

            SetProgress(1.0);
            RefreshInstalledVersion();
            RefreshDccmStatus();
            var launchMode = IsVanillaMode()
                ? "currently disabled — vanilla mode"
                : IsSteamInstall()
                    ? (SteamShellReady() ? "Steam hosting + LAN/port forwarding" : "LAN / port forwarding fallback")
                    : "LAN / port forwarding";
            SetStatus($"Ready. Installed {ReadInstalledVersion() ?? "?"}. Launch mode: {launchMode}.");

            Dispatcher.Invoke(() =>
            {
                if (!DesktopShortcutExists())
                {
                    var ask = MessageBox.Show(
                        "Add a \"Play Dead Cells Multiplayer\" shortcut to your desktop?\n\nThe shortcut uses the correct modded launcher for this game install.",
                        "Desktop shortcut", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (ask == MessageBoxResult.Yes)
                        CreateDesktopShortcut();
                }
                PlayBtn.IsEnabled = CanLaunchMod();
            });
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Access denied writing to the game folder. Run the launcher as administrator, then try again.");
        }
        catch (Exception ex)
        {
            SetStatus("Install failed: " + ex.Message);
        }
        finally
        {
            TryDelete(tempZip);
            TryDeleteDir(tempExtract);
            SetBusy(false);
            UpdateInstallButtonState();
        }
    }

    private static string? FindModFolderInExtract(string extractRoot)
    {
        // Prefer an exact folder that contains modinfo.json.
        foreach (var dir in Directory.EnumerateDirectories(extractRoot, "*", SearchOption.AllDirectories))
            if (File.Exists(Path.Combine(dir, "modinfo.json")))
                return dir;
        // Or the extract root itself if the files sit at top level.
        if (File.Exists(Path.Combine(extractRoot, "modinfo.json")))
            return extractRoot;
        return null;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    // ---------------------------------------------------------------- co-op / vanilla toggle

    private async void ToggleCoop_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _gameRoot == null || _modsRoot == null) return;

        SetBusy(true);
        try
        {
            if (IsVanillaMode())
                await EnableCoopModeAsync();
            else
                DisableCoopMode();

            RefreshInstalledVersion();
            RefreshDccmStatus();
            UpdateInstallButtonState();
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Access denied while switching modes. Run the launcher as administrator, then try again.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not switch game mode: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            RefreshInstalledVersion();
            RefreshDccmStatus();
            UpdateInstallButtonState();
        }
    }

    private void DisableCoopMode()
    {
        if (_gameRoot == null || _modsRoot == null)
            throw new InvalidOperationException("Dead Cells folder is not selected.");

        SetStatus("Switching Dead Cells back to vanilla...");

        if (IsSteamInstall() && !RestoreVanillaSteamLauncher())
            throw new InvalidOperationException(
                "Could not remove the DCCM Steam shell. Try Steam > Properties > Installed Files > Verify integrity, then disable co-op again.");

        var marker = VanillaModeMarkerPath();
        if (marker == null) throw new InvalidOperationException("Could not create the vanilla-mode marker.");
        File.WriteAllText(marker, "vanilla");
        TryDelete(SteamShellMarkerPath());

        var active = ActiveModPath();
        var disabled = DisabledModPath();
        if (active != null && disabled != null && Directory.Exists(active))
        {
            if (Directory.Exists(disabled))
                Directory.Delete(disabled, recursive: true);
            Directory.Move(active, disabled);
        }

        SetProgress(1.0);
        SetStatus(IsSteamInstall()
            ? "Co-op + DCCM disabled. Steam's Dead Cells entry point is now vanilla, and Play Vanilla will not start Core Modding."
            : "Co-op + DCCM disabled. Dead Cells will launch directly in vanilla mode; Core Modding is completely bypassed.");
    }

    private async Task EnableCoopModeAsync()
    {
        if (_gameRoot == null || _modsRoot == null)
            throw new InvalidOperationException("Dead Cells folder is not selected.");

        SetStatus("Enabling Dead Cells Multiplayer...");

        var active = ActiveModPath();
        var disabled = DisabledModPath();
        if (active != null && disabled != null && Directory.Exists(disabled))
        {
            if (Directory.Exists(active))
                Directory.Delete(active, recursive: true);
            Directory.Move(disabled, active);
        }

        var marker = VanillaModeMarkerPath();
        TryDelete(marker);

        // Re-enable/repair DCCM and, on Steam, put the DCCM Steam shell back in place.
        await EnsureDccmReadyAsync(forceCoreInstall: false);

        if (ReadInstalledVersion() == null)
            SetStatus("Co-op mode enabled, but the multiplayer mod is not installed yet. Press Install.");
        else if (IsSteamInstall() && SteamShellReady())
            SetStatus("Co-op enabled. Steam hosting + LAN / port forwarding are ready.");
        else
            SetStatus("Co-op enabled. LAN / port-forwarding mode is ready.");
    }

    private bool RestoreVanillaSteamLauncher()
    {
        if (!IsSteamInstall() || _gameRoot == null) return true;

        var gameExe = Path.Combine(_gameRoot, "deadcells.exe");
        var backup = Path.Combine(_gameRoot, "deadcells.launcher-backup.exe");
        var vanillaRuntime = Path.Combine(_gameRoot, "deadcells_gl.exe");

        // IMPORTANT: do not decide that an executable is vanilla merely because it differs
        // from our *current* cached DCCM shell. An older/newer DCCM shell has a different hash
        // and that was the bug that allowed "Dead Cells with Core Modding" to keep launching.
        if (File.Exists(gameExe) && !IsLikelyDccmSteamShell(gameExe))
            return true;

        // Prefer the original launcher backup, but only when it is positively NOT a DCCM shell.
        if (File.Exists(backup) && !IsLikelyDccmSteamShell(backup))
        {
            File.Copy(backup, gameExe, overwrite: true);
            return File.Exists(gameExe) && !IsLikelyDccmSteamShell(gameExe);
        }

        // Recovery path for installs where an earlier launcher version accidentally backed up
        // a DCCM shell. deadcells_gl.exe is the actual vanilla Dead Cells game executable.
        // Copying that executable to Steam's deadcells.exe entry point means Steam's Play button
        // starts the vanilla game directly and never enters DCCM. Enable Co-op later restores the
        // cached DCCM shell again.
        if (File.Exists(vanillaRuntime) && !IsLikelyDccmSteamShell(vanillaRuntime))
        {
            File.Copy(vanillaRuntime, gameExe, overwrite: true);

            // Repair a poisoned/missing backup as well, so future toggles have a known vanilla copy.
            if (!File.Exists(backup) || IsLikelyDccmSteamShell(backup))
                File.Copy(vanillaRuntime, backup, overwrite: true);

            return File.Exists(gameExe) && !IsLikelyDccmSteamShell(gameExe);
        }

        return false;
    }

    private bool IsLikelyDccmSteamShell(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;

            // Exact current-shell match is the strongest signal.
            var cache = SteamShellCachePath();
            if (cache != null && File.Exists(cache) && FilesEqual(path, cache))
                return true;

            // DCCM's Steam shell is a .NET single-file app built from the SteamStartShell project.
            // File metadata catches most builds without reading the whole file.
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var metadata = string.Join(" ", new[]
            {
                info.ProductName, info.FileDescription, info.InternalName,
                info.OriginalFilename, info.CompanyName
            }.Where(v => !string.IsNullOrWhiteSpace(v)));

            if (metadata.Contains("SteamStartShell", StringComparison.OrdinalIgnoreCase) ||
                metadata.Contains("Core Modding", StringComparison.OrdinalIgnoreCase) ||
                metadata.Contains("DCCM", StringComparison.OrdinalIgnoreCase))
                return true;

            // Older DCCM shell builds may not expose useful version metadata. Scan for a few
            // project-specific strings embedded in the self-contained executable.
            var file = new FileInfo(path);
            if (file.Length > 0 && file.Length <= 128L * 1024 * 1024)
            {
                var bytes = File.ReadAllBytes(path);
                foreach (var marker in new[] { "SteamStartShell", "DeadCellsModding", "DCCM_SHOULD_WAIT_FOR_DEBUGGER" })
                {
                    var needle = System.Text.Encoding.UTF8.GetBytes(marker);
                    if (bytes.AsSpan().IndexOf(needle) >= 0)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    // ---------------------------------------------------------------- DCCM

    private string? DccmLauncherPath()
        => _coremodRoot == null ? null : Path.Combine(_coremodRoot, "core", "host", "startup", "DeadCellsModding.exe");

    private string? SteamShellCachePath()
        => _coremodRoot == null ? null : Path.Combine(_coremodRoot, "launcher", "steam", "deadcells.exe");

    private string? SteamShellMarkerPath()
        => _coremodRoot == null ? null : Path.Combine(_coremodRoot, ".dcmm-steam-shell-version");

    private bool DccmInstalled()
    {
        var launcher = DccmLauncherPath();
        return _coremodRoot != null
            && Directory.Exists(Path.Combine(_coremodRoot, "core"))
            && launcher != null
            && File.Exists(launcher);
    }

    private string? ReadDccmInstalledVersion()
    {
        if (_coremodRoot == null) return null;
        var candidates = new[]
        {
            Path.Combine(_coremodRoot, "core", "version.txt"),
            Path.Combine(_coremodRoot, ".dcmm-dccm-version")
        };
        foreach (var versionFile in candidates)
        {
            try
            {
                if (File.Exists(versionFile))
                {
                    var value = File.ReadAllText(versionFile).Trim();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            catch { }
        }
        return null;
    }

    private async Task CheckDccmLatestAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{DccmRepoOwner}/{DccmRepoName}/releases/latest";
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            _dccmLatestVersion = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            _dccmZipUrl = null;
            _dccmSteamShellUrl = null;
            _dccmSteamShellAssetName = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                var coreCandidates = new List<(int score, string url)>();
                foreach (var a in assets.EnumerateArray())
                {
                    var originalName = a.GetProperty("name").GetString() ?? "";
                    var name = originalName.ToLowerInvariant();
                    var downloadUrl = a.GetProperty("browser_download_url").GetString();
                    if (string.IsNullOrWhiteSpace(downloadUrl)) continue;

                    // Current DCCM releases publish the Steam bootstrapper as a standalone
                    // asset named exactly "deadcells.exe". Keep broader matching too so a future
                    // release can rename it without immediately breaking this launcher.
                    var exactSteamShell = string.Equals(originalName, "deadcells.exe", StringComparison.OrdinalIgnoreCase);
                    var namedSteamShell = (name.EndsWith(".exe") || name.EndsWith(".zip"))
                        && name.Contains("steam")
                        && (name.Contains("shell") || name.Contains("start") || name.Contains("deadcells"));
                    if (exactSteamShell || namedSteamShell)
                    {
                        _dccmSteamShellUrl ??= downloadUrl;
                        _dccmSteamShellAssetName ??= originalName;
                    }

                    if (!name.EndsWith(".zip")) continue;
                    var score = 0;
                    if (name.Contains("win-x64") || name.Contains("windows") || name.Contains("win64")) score += 120;
                    else if (name.Contains("win")) score += 80;
                    // For players, DCCM's no-MDK package is the smaller runtime-only archive.
                    if (name.Contains("no-mdk")) score += 150;
                    if (name.Contains("core")) score += 60;
                    if (name.Contains("release")) score += 10;
                    if (name.Contains("linux") || name.Contains("osx") || name.Contains("mac")) score -= 250;
                    if ((name.Contains("mdk") && !name.Contains("no-mdk")) || name.Contains("symbol") || name.Contains("source")) score -= 180;
                    if (name.Contains("steam") && name.Contains("shell")) score -= 120;
                    coreCandidates.Add((score, downloadUrl));
                }

                if (coreCandidates.Count > 0)
                    _dccmZipUrl = coreCandidates.OrderByDescending(x => x.score).First().url;
            }
            RefreshDccmStatus();
        }
        catch
        {
            // Non-fatal. Existing installs can still launch without a network check.
        }
    }

    private async void DccmInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_gameRoot == null) { SetStatus("Pick your Dead Cells folder first (Change...)."); return; }

        SetBusy(true);
        try
        {
            if (_dccmZipUrl == null)
                await CheckDccmLatestAsync();
            await EnsureDccmReadyAsync(forceCoreInstall: true);
            SetProgress(1.0);
            RefreshDccmStatus();
            if (IsVanillaMode())
                SetStatus("DCCM repaired and kept disabled. Dead Cells remains in full vanilla mode.");
            else if (IsSteamInstall() && SteamShellReady())
                SetStatus("DCCM repaired. Steam mod-loader shell is installed; Steam hosting + LAN are ready.");
            else if (IsSteamInstall())
                SetStatus("DCCM repaired, but the Steam shell was unavailable. LAN / port-forwarding fallback is ready.");
            else
                SetStatus("DCCM repaired. Non-Steam launch is ready for LAN / port forwarding.");
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Access denied while installing DCCM. Run the launcher as administrator, then try again.");
        }
        catch (Exception ex)
        {
            SetStatus("DCCM setup failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            UpdateInstallButtonState();
        }
    }

    private async Task EnsureDccmReadyAsync(bool forceCoreInstall)
    {
        if (_gameRoot == null || _coremodRoot == null)
            throw new InvalidOperationException("Dead Cells folder is not selected.");

        if (forceCoreInstall || !DccmInstalled())
        {
            if (_dccmZipUrl == null)
                await CheckDccmLatestAsync();
            if (_dccmZipUrl == null)
                throw new InvalidOperationException("Could not find a Windows DCCM release download on GitHub.");
            await InstallDccmCoreFilesAsync();
        }
        else
        {
            SetStatus("DCCM detected. Checking the mod loader...");
            SetProgress(0.16);
        }

        var launcher = DccmLauncherPath();
        if (launcher == null || !File.Exists(launcher))
            throw new InvalidOperationException("DCCM core is present, but DeadCellsModding.exe is missing. Use Repair / Update DCCM.");

        if (IsSteamInstall() && !IsVanillaMode())
        {
            var steamReady = await EnsureSteamShellInstalledAsync(extractedDccmRoot: null);
            if (!steamReady)
                SetStatus("DCCM is installed, but the Steam shell could not be found. The launcher will still use LAN / port-forwarding mode.");
        }
        SetProgress(0.38);
        RefreshDccmStatus();
    }

    private async Task InstallDccmCoreFilesAsync()
    {
        string? zip = null, extract = null;
        try
        {
            SetStatus("Downloading DCCM core...");
            SetProgress(0.03);
            zip = Path.Combine(Path.GetTempPath(), $"dccm_{Guid.NewGuid():N}.zip");
            await DownloadFileAsync(_dccmZipUrl!, zip, 0.03, 0.22);

            SetStatus("Extracting DCCM...");
            SetProgress(0.24);
            extract = Path.Combine(Path.GetTempPath(), $"dccm_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extract);
            ZipFile.ExtractToDirectory(zip, extract);

            var coremodSource = LocateCoremodPayload(extract);
            SetStatus("Installing DCCM core and mod launcher...");
            SetProgress(0.30);
            Directory.CreateDirectory(_coremodRoot!);
            CopyDirectory(coremodSource.path, _coremodRoot!);

            var launcher = DccmLauncherPath();
            if (launcher == null || !File.Exists(launcher))
                throw new InvalidOperationException("The DCCM archive did not contain core/host/startup/DeadCellsModding.exe.");

            if (!string.IsNullOrWhiteSpace(_dccmLatestVersion))
            {
                try { File.WriteAllText(Path.Combine(_coremodRoot!, ".dcmm-dccm-version"), _dccmLatestVersion!); }
                catch { }
            }

            // Cache the Steam shell from the same release while the archive is still available.
            // This gives us a known-good copy we can reapply if a Dead Cells update overwrites it.
            var shell = FindSteamShellCandidate(extract);
            if (shell != null)
                CacheSteamShell(shell);

            if (IsSteamInstall() && !IsVanillaMode())
            {
                // Pair a freshly installed core with the matching standalone Steam shell asset.
                // This intentionally refreshes the cached shell during DCCM repair/update.
                if (_dccmSteamShellUrl != null)
                    await DownloadAndCacheSteamShellAsync();
                await EnsureSteamShellInstalledAsync(extract);
            }

            SetProgress(0.38);
        }
        finally
        {
            TryDelete(zip);
            TryDeleteDir(extract);
        }
    }

    private static (string path, bool isCoremodItself) LocateCoremodPayload(string extractRoot)
    {
        foreach (var dir in Directory.EnumerateDirectories(extractRoot, "coremod", SearchOption.AllDirectories))
            return (dir, true);
        if (Directory.Exists(Path.Combine(extractRoot, "core")))
            return (extractRoot, true);
        var subs = Directory.GetDirectories(extractRoot);
        if (subs.Length == 1 && Directory.Exists(Path.Combine(subs[0], "core")))
            return (subs[0], true);
        return (extractRoot, true);
    }

    private string? FindSteamShellCandidate(string searchRoot)
    {
        if (!Directory.Exists(searchRoot)) return null;
        try
        {
            var candidates = Directory.EnumerateFiles(searchRoot, "*.exe", SearchOption.AllDirectories)
                .Select(path =>
                {
                    var lower = path.Replace('\\', '/').ToLowerInvariant();
                    var file = Path.GetFileName(path).ToLowerInvariant();
                    var score = 0;
                    if (file == "deadcells.exe") score += 80;
                    if (file.Contains("steamstartshell")) score += 220;
                    if (lower.Contains("/steam/")) score += 160;
                    if (lower.Contains("host/startup")) score += 50;
                    if (lower.Contains("gog")) score -= 200;
                    if (file == "deadcellsmodding.exe") score -= 300;
                    return (path, score);
                })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ToList();
            return candidates.Count == 0 ? null : candidates[0].path;
        }
        catch { return null; }
    }

    private void CacheSteamShell(string shellPath)
    {
        var cache = SteamShellCachePath();
        if (cache == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.Copy(shellPath, cache, overwrite: true);
    }

    private async Task<bool> EnsureSteamShellInstalledAsync(string? extractedDccmRoot)
    {
        if (!IsSteamInstall() || _gameRoot == null || _coremodRoot == null) return false;

        var cache = SteamShellCachePath();
        if (cache == null) return false;

        if (!File.Exists(cache) && extractedDccmRoot != null)
        {
            var candidate = FindSteamShellCandidate(extractedDccmRoot);
            if (candidate != null) CacheSteamShell(candidate);
        }
        if (!File.Exists(cache))
        {
            var candidate = FindSteamShellCandidate(_coremodRoot);
            if (candidate != null) CacheSteamShell(candidate);
        }
        if (!File.Exists(cache) && _dccmSteamShellUrl == null)
            await CheckDccmLatestAsync();
        if (!File.Exists(cache) && _dccmSteamShellUrl != null)
            await DownloadAndCacheSteamShellAsync();
        if (!File.Exists(cache))
            return false;

        var gameExe = Path.Combine(_gameRoot, "deadcells.exe");
        var backup = Path.Combine(_gameRoot, "deadcells.launcher-backup.exe");

        // Only preserve a launcher as the vanilla backup when it is actually non-DCCM.
        // This also repairs backups poisoned by older launcher builds.
        if (File.Exists(gameExe) && !IsLikelyDccmSteamShell(gameExe) &&
            (!File.Exists(backup) || IsLikelyDccmSteamShell(backup)))
            File.Copy(gameExe, backup, overwrite: true);

        if (!File.Exists(gameExe) || !FilesEqual(gameExe, cache))
        {
            SetStatus("Installing DCCM Steam launcher (deadcells.exe)...");
            File.Copy(cache, gameExe, overwrite: true);
        }

        var marker = SteamShellMarkerPath();
        if (marker != null)
        {
            try { File.WriteAllText(marker, _dccmLatestVersion ?? "installed"); }
            catch { }
        }
        return File.Exists(gameExe) && FilesEqual(gameExe, cache);
    }

    private async Task DownloadAndCacheSteamShellAsync()
    {
        if (_dccmSteamShellUrl == null) return;
        string? temp = null, extract = null;
        try
        {
            var assetName = _dccmSteamShellAssetName ?? "steam-shell";
            var isZip = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || _dccmSteamShellUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            temp = Path.Combine(Path.GetTempPath(), $"dccm_steam_{Guid.NewGuid():N}" + (isZip ? ".zip" : ".exe"));
            SetStatus("Downloading DCCM Steam launcher...");
            await DownloadFileAsync(_dccmSteamShellUrl, temp, 0.30, 0.35);

            if (!isZip)
            {
                CacheSteamShell(temp);
                return;
            }

            extract = Path.Combine(Path.GetTempPath(), $"dccm_steam_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extract);
            ZipFile.ExtractToDirectory(temp, extract);
            var candidate = FindSteamShellCandidate(extract);
            if (candidate != null) CacheSteamShell(candidate);
        }
        finally
        {
            TryDelete(temp);
            TryDeleteDir(extract);
        }
    }

    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
            using var sha = SHA256.Create();
            using var sa = File.OpenRead(a);
            var ha = sha.ComputeHash(sa);
            using var sb = File.OpenRead(b);
            var hb = sha.ComputeHash(sb);
            return ha.SequenceEqual(hb);
        }
        catch { return false; }
    }

    private async Task DownloadFileAsync(string url, string destination, double progressStart, double progressEnd)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(destination);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n));
            read += n;
            if (total > 0)
            {
                var t = read / (double)total;
                SetProgress(progressStart + (progressEnd - progressStart) * t);
            }
        }
    }

    // ---------------------------------------------------------------- launch & shortcut

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _gameRoot == null) return;

        if (IsVanillaMode())
        {
            LaunchVanillaGame();
            return;
        }

        SetBusy(true);
        try
        {
            await EnsureDccmReadyAsync(forceCoreInstall: false);
        }
        catch (Exception ex)
        {
            SetStatus("Could not prepare the mod loader: " + ex.Message);
            return;
        }
        finally
        {
            SetBusy(false);
            UpdateInstallButtonState();
        }
        LaunchGame();
    }

    private bool CanLaunchVanilla()
    {
        if (_gameRoot == null) return false;

        // deadcells_gl.exe is the actual vanilla game executable and is the preferred
        // guaranteed-no-DCCM path. A non-DCCM deadcells.exe is also acceptable.
        var gl = Path.Combine(_gameRoot, "deadcells_gl.exe");
        if (File.Exists(gl) && !IsLikelyDccmSteamShell(gl)) return true;

        var exe = Path.Combine(_gameRoot, "deadcells.exe");
        return File.Exists(exe) && !IsLikelyDccmSteamShell(exe);
    }

    private void LaunchVanillaGame()
    {
        if (_gameRoot == null) return;
        try
        {
            // On Steam, first remove the DCCM shell from Steam's normal entry point as well.
            // This means pressing Play in Steam after disabling co-op also stays vanilla.
            if (IsSteamInstall() && !RestoreVanillaSteamLauncher())
                throw new InvalidOperationException(
                    "Could not replace the DCCM Steam shell with a vanilla Dead Cells executable. Try Steam > Properties > Installed Files > Verify integrity, then disable co-op again.");

            var gameEntry = Path.Combine(_gameRoot, "deadcells.exe");
            if (IsSteamInstall() && File.Exists(gameEntry) && !IsLikelyDccmSteamShell(gameEntry))
            {
                // Now that Steam's configured entry point is positively verified as non-DCCM,
                // use Steam normally so overlay/playtime/Steam initialization are preserved.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    $"steam://rungameid/{DeadCellsSteamAppId}") { UseShellExecute = true });
                SetStatus("Launching FULL VANILLA Dead Cells through Steam. DCCM is not being started.");
                return;
            }

            // Standalone installs (and Steam recovery fallback) launch the real game binary
            // directly. Prefer deadcells_gl.exe specifically because DCCM's Steam shell is
            // installed as deadcells.exe.
            var exe = new[]
            {
                Path.Combine(_gameRoot, "deadcells_gl.exe"),
                Path.Combine(_gameRoot, "deadcells.exe")
            }.FirstOrDefault(x => File.Exists(x) && !IsLikelyDccmSteamShell(x));
            if (exe == null)
                throw new FileNotFoundException("Could not find a vanilla Dead Cells executable that is not DCCM.");

            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = _gameRoot
            };
            if (IsSteamInstall())
            {
                psi.Environment["SteamAppId"] = DeadCellsSteamAppId.ToString();
                psi.Environment["SteamGameId"] = DeadCellsSteamAppId.ToString();
            }
            System.Diagnostics.Process.Start(psi);
            SetStatus("Launching FULL VANILLA Dead Cells directly. DCCM and the co-op mod are completely bypassed.");
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't launch vanilla Dead Cells: " + ex.Message);
        }
    }

    private bool SteamShellReady()
    {
        if (!IsSteamInstall() || _gameRoot == null) return false;
        var cache = SteamShellCachePath();
        var gameExe = Path.Combine(_gameRoot, "deadcells.exe");
        return cache != null && File.Exists(cache) && File.Exists(gameExe) && FilesEqual(cache, gameExe);
    }

    private bool CanLaunchMod()
    {
        var launcher = DccmLauncherPath();
        var active = ActiveModPath();
        return !IsVanillaMode()
            && active != null && Directory.Exists(active)
            && ReadInstalledVersion() != null
            && launcher != null && File.Exists(launcher);
    }

    private void LaunchGame()
    {
        if (_gameRoot == null) return;
        try
        {
            if (IsSteamInstall() && SteamShellReady())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    $"steam://rungameid/{DeadCellsSteamAppId}") { UseShellExecute = true });
                SetStatus("Launching through Steam with the DCCM deadcells.exe shell. Steam hosting + LAN are enabled.");
                return;
            }

            var launcher = DccmLauncherPath();
            if (launcher == null || !File.Exists(launcher))
                throw new FileNotFoundException("DeadCellsModding.exe was not found.", launcher);

            var psi = new System.Diagnostics.ProcessStartInfo(launcher)
            {
                UseShellExecute = true,
                WorkingDirectory = _gameRoot
            };
            System.Diagnostics.Process.Start(psi);
            SetStatus(IsSteamInstall()
                ? "Launching DCCM directly. Steam shell was unavailable, so use LAN / port forwarding for this session."
                : "Launching DCCM directly. LAN / port-forwarding mode enabled.");
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't launch Dead Cells: " + ex.Message);
        }
    }

    private static string DesktopDir()
        => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    private string LnkShortcutPath() => Path.Combine(DesktopDir(), PlayShortcutName + ".lnk");
    private string UrlShortcutPath() => Path.Combine(DesktopDir(), PlayShortcutName + ".url");

    private bool DesktopShortcutExists()
        => File.Exists(LnkShortcutPath()) || File.Exists(UrlShortcutPath());

    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        if (CreateDesktopShortcut())
            SetStatus("Desktop shortcut created.");
    }

    private bool CreateDesktopShortcut()
    {
        try
        {
            TryDelete(LnkShortcutPath());
            string? url;
            if (IsVanillaMode())
            {
                url = IsSteamInstall()
                    ? $"steam://rungameid/{DeadCellsSteamAppId}"
                    : new[] { Path.Combine(_gameRoot!, "deadcells.exe"), Path.Combine(_gameRoot!, "deadcells_gl.exe") }
                        .FirstOrDefault(File.Exists) is string vanillaExe
                        ? new Uri(vanillaExe).AbsoluteUri
                        : null;
            }
            else
            {
                url = IsSteamInstall() && SteamShellReady()
                    ? $"steam://rungameid/{DeadCellsSteamAppId}"
                    : DccmLauncherPath() is string exe && File.Exists(exe)
                        ? new Uri(exe).AbsoluteUri
                        : null;
            }
            if (url == null)
                throw new InvalidOperationException("Install DCCM before creating the play shortcut.");

            File.WriteAllText(UrlShortcutPath(),
                "[InternetShortcut]\r\n" +
                $"URL={url}\r\n" +
                "IconIndex=0\r\n");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("Couldn't create shortcut: " + ex.Message);
            return false;
        }
    }


    // ---------------------------------------------------------------- ui glue

    private void UpdateInstallButtonState()
    {
        var installed = ReadInstalledVersion();
        RecalculateUpdateAvailable();
        RefreshUpdateIndicator();
        if (installed == null)
        {
            InstallBtn.Content = "Install";
        }
        else if (_updateAvailable)
        {
            InstallBtn.Content = "Update Now";
        }
        else
        {
            InstallBtn.Content = "Reinstall";
        }
        var vanillaMode = IsVanillaMode();
        CoopToggleBtn.Content = vanillaMode ? "Enable Co-op Mod" : "Disable Co-op Mod";
        CoopToggleBtn.IsEnabled = (installed != null || DccmInstalled()) && _gameRoot != null && !_busy;
        InstallBtn.IsEnabled = _modsRoot != null && !_busy && _latestZipUrl != null;
        PlayText.Text = vanillaMode ? "Play Vanilla" : "Play";
        PlayBtn.IsEnabled = (vanillaMode ? CanLaunchVanilla() : CanLaunchMod()) && !_busy;
        ShortcutBtn.IsEnabled = !_busy;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select your Dead Cells folder",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;

        var picked = dlg.FolderName;
        // Accept either the game root or a coremod/mods folder and normalize.
        _gameRoot = NormalizeToGameRoot(picked);
        if (_gameRoot == null)
        {
            // Even without exe, allow a folder that has (or will have) coremod.
            _gameRoot = picked;
        }
        _coremodRoot = Path.Combine(_gameRoot, "coremod");
        _modsRoot = Path.Combine(_coremodRoot, "mods");
        PathText.Text = Path.Combine(_modsRoot, ModFolderName);
        PathHint.Text = "Manual selection.";
        RefreshDccmStatus();
        RefreshInstalledVersion();
        UpdateInstallButtonState();
    }

    private static string? NormalizeToGameRoot(string picked)
    {
        if (LooksLikeGameRoot(picked)) return picked;
        // If they picked coremod or coremod\mods, walk up.
        var name = Path.GetFileName(picked.TrimEnd('\\'));
        if (string.Equals(name, "mods", StringComparison.OrdinalIgnoreCase))
        {
            var up = Path.GetDirectoryName(Path.GetDirectoryName(picked));
            if (up != null && LooksLikeGameRoot(up)) return up;
        }
        if (string.Equals(name, "coremod", StringComparison.OrdinalIgnoreCase))
        {
            var up = Path.GetDirectoryName(picked);
            if (up != null && LooksLikeGameRoot(up)) return up;
        }
        return null;
    }

    private void DccmGuide_Click(object sender, RoutedEventArgs e) => OpenUrl(DccmDocsUrl);

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private void SetStatus(string s) => Dispatcher.Invoke(() => StatusLine.Text = s);

    private void SetProgress(double frac)
    {
        frac = Math.Clamp(frac, 0, 1);
        Dispatcher.Invoke(() =>
        {
            var track = ((FrameworkElement)ProgressFill.Parent).ActualWidth;
            ProgressFill.Width = track * frac;
        });
    }

    private void SetBusy(bool b)
    {
        _busy = b;
        Dispatcher.Invoke(() =>
        {
            InstallBtn.IsEnabled = !b && _modsRoot != null && _latestZipUrl != null;
            CoopToggleBtn.IsEnabled = !b && (ReadInstalledVersion() != null || DccmInstalled()) && _gameRoot != null;
            BrowseBtn.IsEnabled = !b;
            PlayBtn.IsEnabled = !b && (IsVanillaMode() ? CanLaunchVanilla() : CanLaunchMod());
            ShortcutBtn.IsEnabled = !b;
        });
    }

    private static void TryDelete(string? path)
    {
        try { if (path != null && File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string? path)
    {
        try { if (path != null && Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    // Title bar
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
