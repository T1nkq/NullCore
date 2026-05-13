using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Voidstrap;

public sealed class GithubUpdateRelease
{
    public string TagName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
    public IReadOnlyList<GithubUpdateAsset> Assets { get; init; } = Array.Empty<GithubUpdateAsset>();
}

public sealed class GithubUpdateAsset
{
    public string Name { get; init; } = string.Empty;
    public string BrowserDownloadUrl { get; init; } = string.Empty;
    public long Size { get; init; }
}

public static class GithubUpdater
{
    private static readonly HttpClient http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", $"{App.ProjectName}-Updater" } }
    };

    public static async Task<GithubUpdateRelease?> GetLatestReleaseAsync()
    {
        try
        {
            return await GetReleaseAsync($"https://api.github.com/repos/{App.ProjectRepository}/releases/latest");
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine("GitHubUpdater", $"Failed to get latest release: {ex}");
            return null;
        }
    }

    public static async Task<string?> GetLatestVersionTagAsync()
    {
        var release = await GetLatestReleaseAsync();
        return release?.TagName;
    }

    public static bool IsNewerVersion(string latestTag, string currentVersion)
    {
        string latest = latestTag.TrimStart('v', 'V');
        string current = currentVersion.TrimStart('v', 'V');

        if (Version.TryParse(latest, out var latestVersion) &&
            Version.TryParse(current, out var currentParsed))
        {
            return latestVersion > currentParsed;
        }

        return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
    }

    public static async Task<bool> DownloadAndInstallUpdate(string tag)
    {
        try
        {
            var release = string.IsNullOrWhiteSpace(tag)
                ? await GetLatestReleaseAsync()
                : await GetReleaseByTagAsync(tag);

            if (release is null)
                return false;

            var asset = SelectUpdateAsset(release);
            if (asset is null)
            {
                App.Logger.WriteLine("GitHubUpdater", "No valid .exe or .zip asset found.");
                return false;
            }

            if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return await StageExeUpdate(asset);

            if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return await StageZipUpdate(asset);

            return false;
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine("GitHubUpdater", $"Update failed: {ex}");
            return false;
        }
    }

    private static async Task<GithubUpdateRelease?> GetReleaseByTagAsync(string tag)
    {
        try
        {
            string escapedTag = Uri.EscapeDataString(tag);
            return await GetReleaseAsync($"https://api.github.com/repos/{App.ProjectRepository}/releases/tags/{escapedTag}");
        }
        catch (Exception ex)
        {
            App.Logger.WriteLine("GitHubUpdater", $"Failed to get release by tag '{tag}': {ex}");
            return null;
        }
    }

    private static async Task<GithubUpdateRelease> GetReleaseAsync(string url)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        List<GithubUpdateAsset> assets = new();
        if (root.TryGetProperty("assets", out var assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetElement in assetsElement.EnumerateArray())
            {
                long size = 0;
                if (assetElement.TryGetProperty("size", out var sizeElement))
                    sizeElement.TryGetInt64(out size);

                assets.Add(new GithubUpdateAsset
                {
                    Name = GetString(assetElement, "name"),
                    BrowserDownloadUrl = GetString(assetElement, "browser_download_url"),
                    Size = size
                });
            }
        }

        return new GithubUpdateRelease
        {
            TagName = GetString(root, "tag_name"),
            Name = GetString(root, "name"),
            Body = GetString(root, "body"),
            HtmlUrl = GetString(root, "html_url"),
            Assets = assets
        };
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind != JsonValueKind.Null
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static GithubUpdateAsset? SelectUpdateAsset(GithubUpdateRelease release)
    {
        var assets = release.Assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            .ToList();

        string projectName = App.ProjectName.Replace("-QA", string.Empty, StringComparison.OrdinalIgnoreCase);

        bool IsPreferred(GithubUpdateAsset asset) =>
            asset.Name.Contains(projectName, StringComparison.OrdinalIgnoreCase) ||
            asset.Name.Contains("NullCore", StringComparison.OrdinalIgnoreCase);

        return assets.FirstOrDefault(asset =>
                   asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && IsPreferred(asset))
               ?? assets.FirstOrDefault(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(asset =>
                   asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && IsPreferred(asset))
               ?? assets.FirstOrDefault(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> StageExeUpdate(GithubUpdateAsset asset)
    {
        string tempDir = CreateUpdateTempDirectory();
        string exePath = Path.Combine(tempDir, SanitizeFileName(asset.Name, $"{App.ProjectName}.exe"));
        await DownloadFileAsync(asset.BrowserDownloadUrl, exePath);

        string currentExe = Environment.ProcessPath ?? throw new InvalidOperationException("Current executable path is unavailable.");
        string scriptPath = Path.Combine(tempDir, "ApplyUpdate.ps1");

        await File.WriteAllTextAsync(
            scriptPath,
            BuildExeUpdateScript(exePath, currentExe),
            Encoding.UTF8);

        return StartUpdaterScript(scriptPath);
    }

    private static async Task<bool> StageZipUpdate(GithubUpdateAsset asset)
    {
        string tempDir = CreateUpdateTempDirectory();
        string zipPath = Path.Combine(tempDir, SanitizeFileName(asset.Name, "update.zip"));
        string extractPath = Path.Combine(tempDir, "Extracted");

        await DownloadFileAsync(asset.BrowserDownloadUrl, zipPath);
        ZipFile.ExtractToDirectory(zipPath, extractPath, true);

        string currentDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string mainExe = Environment.ProcessPath ?? Path.Combine(currentDir, $"{App.ProjectName}.exe");
        string scriptPath = Path.Combine(tempDir, "ApplyUpdate.ps1");

        await File.WriteAllTextAsync(
            scriptPath,
            BuildZipUpdateScript(extractPath, currentDir, mainExe),
            Encoding.UTF8);

        return StartUpdaterScript(scriptPath);
    }

    private static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(destinationPath);
        await input.CopyToAsync(output);
    }

    private static string CreateUpdateTempDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"{App.ProjectName}_Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static string SanitizeFileName(string fileName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = fallback;

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return fileName;
    }

    private static string BuildExeUpdateScript(string sourceExe, string targetExe)
    {
        int processId = Process.GetCurrentProcess().Id;

        return string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"$pidToWait = {processId}",
            $"$source = {ToPowerShellString(sourceExe)}",
            $"$target = {ToPowerShellString(targetExe)}",
            "try { Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue } catch {}",
            "Start-Sleep -Milliseconds 500",
            "for ($i = 0; $i -lt 40; $i++) {",
            "    try {",
            "        Copy-Item -LiteralPath $source -Destination $target -Force",
            "        break",
            "    } catch {",
            "        if ($i -eq 39) { throw }",
            "        Start-Sleep -Milliseconds 500",
            "    }",
            "}",
            "Start-Process -FilePath $target"
        });
    }

    private static string BuildZipUpdateScript(string sourceDir, string targetDir, string mainExe)
    {
        int processId = Process.GetCurrentProcess().Id;

        return string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"$pidToWait = {processId}",
            $"$sourceDir = {ToPowerShellString(sourceDir)}",
            $"$targetDir = {ToPowerShellString(targetDir)}",
            $"$mainExe = {ToPowerShellString(mainExe)}",
            "try { Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue } catch {}",
            "Start-Sleep -Milliseconds 500",
            "$sourcePrefix = $sourceDir.TrimEnd('\\') + '\\'",
            "Get-ChildItem -LiteralPath $sourceDir -Recurse -File | ForEach-Object {",
            "    $relative = $_.FullName.Substring($sourcePrefix.Length)",
            "    $destination = Join-Path $targetDir $relative",
            "    $destinationDir = Split-Path -Parent $destination",
            "    if ($destinationDir) { New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null }",
            "    for ($i = 0; $i -lt 40; $i++) {",
            "        try {",
            "            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force",
            "            break",
            "        } catch {",
            "            if ($i -eq 39) { throw }",
            "            Start-Sleep -Milliseconds 500",
            "        }",
            "    }",
            "}",
            "Start-Process -FilePath $mainExe"
        });
    }

    private static string ToPowerShellString(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    private static bool StartUpdaterScript(string scriptPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(scriptPath)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        return true;
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
