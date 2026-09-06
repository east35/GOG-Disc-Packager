using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GogDisc.Core;

public sealed record GogDownloadEstimate(long DownloadBytes, long InstalledBytes);
public sealed record GogAccountFile(string Id, string Name, string Downlink, long Size, bool IsExtra);
/// <summary><paramref name="Message"/> is gogdl's raw line — diagnostic only, never surface it to a user.</summary>
public sealed record GogProcessProgress(string Message, double? Percent = null, TimeSpan? Remaining = null);
public sealed record GogCatalogProduct(string ProductId, string Slug, string Title, string ProductType)
{
    public override string ToString() => $"{Title} ({ProductType})";
}

public sealed class GogCatalogClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<GogCatalogProduct>> SearchAsync(string titleOrUrl, CancellationToken cancellationToken = default)
    {
        var input = titleOrUrl.Trim();
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Enter a game title or GOG store URL.");
        var requestedSlug = ExtractSlug(input);
        var query = requestedSlug?.Replace('_', ' ') ?? input;
        if (input.All(char.IsDigit))
        {
            using var exact = await Http.GetAsync($"https://api.gog.com/products/{input}?locale=en-US", cancellationToken);
            exact.EnsureSuccessStatusCode();
            using var product = JsonDocument.Parse(await exact.Content.ReadAsStreamAsync(cancellationToken));
            return [ReadProduct(product.RootElement)];
        }
        var uri = "https://catalog.gog.com/v1/catalog?limit=20&page=1&productType=in%3Agame%2Cpack%2Cdlc" +
                  "&countryCode=US&locale=en-US&currencyCode=USD&query=" + Uri.EscapeDataString(query);
        using var response = await Http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var products = document.RootElement.GetProperty("products").EnumerateArray().Select(ReadProduct).ToList();
        if (requestedSlug is not null)
        {
            var exact = products.FirstOrDefault(product => product.Slug.Equals(requestedSlug, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return [exact];
        }
        return products;
    }

    private static GogCatalogProduct ReadProduct(JsonElement product) => new(
        product.GetProperty("id").ToString(),
        product.GetProperty("slug").GetString() ?? "",
        product.GetProperty("title").GetString() ?? "Untitled GOG product",
        product.TryGetProperty("productType", out var type) ? type.GetString() ?? "game" : "game");

    private static string? ExtractSlug(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            !(uri.Host.Equals("gog.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".gog.com", StringComparison.OrdinalIgnoreCase))) return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var game = Array.FindIndex(segments, segment => segment.Equals("game", StringComparison.OrdinalIgnoreCase));
        if (game < 0 || game + 1 >= segments.Length) throw new ArgumentException("That GOG URL does not identify a store game page.");
        return Uri.UnescapeDataString(segments[game + 1]).Trim();
    }
}

public static class GogAuthentication
{
    public const string ClientId = "46899977096215655";
    public const string RedirectUri = "https://embed.gog.com/on_login_success?origin=client";
    public static string LoginUrl => "https://auth.gog.com/auth?client_id=" + ClientId +
        "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) + "&response_type=code&layout=client2";

    public static bool HasCredentials()
    {
        try
        {
            if (!File.Exists(AppPaths.GogAuth)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.GogAuth));
            return document.RootElement.EnumerateObject().Any(item =>
                item.Value.TryGetProperty("refresh_token", out var token) && !string.IsNullOrWhiteSpace(token.GetString()));
        }
        catch { return false; }
    }

    public static string ExtractAuthorizationCode(string value)
    {
        value = value.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals("code", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[1]);
        }
        throw new InvalidDataException("The pasted GOG sign-in URL does not contain an authorization code.");
    }
}

public sealed class GogDlRuntime
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Heroic-Games-Launcher/heroic-gogdl/releases/latest";
    private static readonly HttpClient Http = CreateHttp();
    public string ExecutablePath => Path.Combine(AppPaths.GogRuntime, "gogdl.exe");

    public async Task<string> EnsureCurrentAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.GogRuntime);
        HttpResponseMessage response;
        try { response = await Http.GetAsync(LatestReleaseApi, cancellationToken); }
        catch (HttpRequestException) when (File.Exists(ExecutablePath)) { return ExecutablePath; }
        using (response)
        {
        if (!response.IsSuccessStatusCode && File.Exists(ExecutablePath)) return ExecutablePath;
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var tag = release.RootElement.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("gogdl release has no version.");
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
        var asset = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(item =>
            string.Equals(item.GetProperty("name").GetString(), $"gogdl_windows_{architecture}.exe", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind == JsonValueKind.Undefined) throw new PlatformNotSupportedException("No compatible gogdl Windows release was found.");
        var versionPath = Path.Combine(AppPaths.GogRuntime, "version.txt");
        if (File.Exists(ExecutablePath) && File.Exists(versionPath) && File.ReadAllText(versionPath).Trim() == tag) return ExecutablePath;

        var uri = asset.GetProperty("browser_download_url").GetString()!;
        var partial = ExecutablePath + ".partial";
        using (var download = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            download.EnsureSuccessStatusCode();
            await using var input = await download.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            await input.CopyToAsync(output, cancellationToken);
        }
        File.Move(partial, ExecutablePath, true);
        File.WriteAllText(versionPath, tag);
        return ExecutablePath;
        }
    }

    public async Task AuthenticateAsync(string authorizationCode, CancellationToken cancellationToken) =>
        await RunAsync(["auth", "--code", GogAuthentication.ExtractAuthorizationCode(authorizationCode)], null, cancellationToken);

    /// <summary>Renews the stored access token, which GOG expires after an hour.</summary>
    public async Task RefreshAuthenticationAsync(CancellationToken cancellationToken) =>
        await RunAsync(["auth"], null, cancellationToken);

    public async Task<GogDownloadEstimate> GetEstimateAsync(GogKeyProduct product, CancellationToken cancellationToken)
    {
        // Ask for DLC metadata whenever this disc carries add-ons, so their sizes are available to sum.
        var dlcArguments = product.IncludedDlcs.Count == 0
            ? new[] { "--skip-dlcs" }
            : ["--dlcs", string.Join(",", product.IncludedDlcs)];
        var output = await RunAsync(["info", product.DownloadProductId, "--platform", product.Platform,
            "--lang", product.Language, .. dlcArguments], null, cancellationToken);
        return ParseDownloadEstimate(output, product);
    }

    /// <summary>Lists the DLCs the signed-in account owns for a base game.</summary>
    public async Task<IReadOnlyList<GogDlc>> GetOwnedDlcsAsync(GogKeyProduct product, CancellationToken cancellationToken)
    {
        var output = await RunAsync(["info", product.ProductId, "--platform", product.Platform,
            "--lang", product.Language, "--with-dlcs"], null, cancellationToken);
        using var json = ParseLastJson(output);
        if (!json.RootElement.TryGetProperty("dlcs", out var dlcs) || dlcs.ValueKind != JsonValueKind.Array) return [];
        return dlcs.EnumerateArray()
            .Select(item => new GogDlc(
                item.TryGetProperty("id", out var id) ? id.ToString() : "",
                item.TryGetProperty("title", out var title) ? title.GetString() ?? "" : ""))
            .Where(dlc => !string.IsNullOrWhiteSpace(dlc.ProductId))
            .ToList();
    }

    /// <summary>gogdl's download SYNCS a folder to a target state — anything outside that state is DELETED.
    /// Never pass --dlc-only: it makes the base game "extra", and gogdl removes the entire installation.
    ///
    /// --with-dlcs and --dlcs are separate switches: --dlcs only NARROWS the set, while --with-dlcs is what
    /// enables DLC at all (gogdl returns no owned DLCs without it). Passing --dlcs alone silently installs none.</summary>
    private static string[] DlcArguments(GogKeyProduct product) => product.IncludedDlcs.Count == 0
        ? ["--skip-dlcs"]
        : ["--with-dlcs", "--dlcs", string.Join(",", product.IncludedDlcs)];

    public static GogDownloadEstimate ParseDownloadEstimate(string output, string language)
    {
        using var json = ParseLastJson(output);
        return ReadSizes(json.RootElement, language);
    }

    /// <summary>gogdl reports the base game's size at the top level whatever the DLC flags say, and each add-on's
    /// size under "dlcs". Summing the right parts keeps the free-space check honest for every disc layout.</summary>
    public static GogDownloadEstimate ParseDownloadEstimate(string output, GogKeyProduct product)
    {
        using var json = ParseLastJson(output);
        var root = json.RootElement;
        var total = product.DiscRole == KeyDiscRole.Dlc
            ? new GogDownloadEstimate(0, 0)
            : ReadSizes(root, product.Language);
        if (product.IncludedDlcs.Count == 0 ||
            !root.TryGetProperty("dlcs", out var dlcs) || dlcs.ValueKind != JsonValueKind.Array) return total;
        foreach (var dlc in dlcs.EnumerateArray())
        {
            var id = dlc.TryGetProperty("id", out var value) ? value.ToString() : "";
            if (!product.IncludedDlcs.Contains(id)) continue;
            var size = ReadSizes(dlc, product.Language);
            total = new GogDownloadEstimate(total.DownloadBytes + size.DownloadBytes,
                total.InstalledBytes + size.InstalledBytes);
        }
        return total;
    }

    private static GogDownloadEstimate ReadSizes(JsonElement root, string language)
    {
        if (root.TryGetProperty("size", out var sizes) && sizes.ValueKind == JsonValueKind.Object)
        {
            var common = sizes.TryGetProperty("*", out var all) ? ReadSizeGroup(all) : new GogDownloadEstimate(0, 0);
            var languageGroup = sizes.EnumerateObject().FirstOrDefault(item =>
                !string.IsNullOrEmpty(item.Name) && item.Name != "*" && item.Name.StartsWith(language, StringComparison.OrdinalIgnoreCase));
            var localized = languageGroup.Value.ValueKind == JsonValueKind.Object ? ReadSizeGroup(languageGroup.Value) : new GogDownloadEstimate(0, 0);
            return new GogDownloadEstimate(common.DownloadBytes + localized.DownloadBytes, common.InstalledBytes + localized.InstalledBytes);
        }
        return new GogDownloadEstimate(FindLong(root, "download_size", "downloadSize", "download"),
            FindLong(root, "disk_size", "install_size", "installSize", "disk"));
    }

    private static GogDownloadEstimate ReadSizeGroup(JsonElement group) => new(
        FindLong(group, "download_size", "downloadSize"), FindLong(group, "disk_size", "install_size", "installSize"));

    public async Task InstallAsync(GogKeyProduct product, string finalGameDirectory,
        IProgress<GogProcessProgress>? progress, CancellationToken cancellationToken) =>
        _ = await RunAsync(["download", product.DownloadProductId, "--path", finalGameDirectory, "--platform", product.Platform,
            "--lang", product.Language, .. DlcArguments(product)], progress, cancellationToken);

    public async Task RepairAsync(GogKeyProduct product, string finalGameDirectory,
        IProgress<GogProcessProgress>? progress, CancellationToken cancellationToken) =>
        _ = await RunAsync(["repair", product.DownloadProductId, "--path", finalGameDirectory, "--platform", product.Platform,
            "--lang", product.Language, .. DlcArguments(product)], progress, cancellationToken);

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, IProgress<GogProcessProgress>? progress, CancellationToken cancellationToken)
    {
        var executable = await EnsureCurrentAsync(cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.GogAuth)!);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--auth-config-path");
        start.ArgumentList.Add(AppPaths.GogAuth);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the GOG download runtime.");
        var lines = new List<string>();
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lines.Add(line);
                progress?.Report(new GogProcessProgress(line, ParsePercent(line), ParseRemaining(line)));
            }
        }
        var stdout = ReadAsync(process.StandardOutput);
        var stderr = ReadAsync(process.StandardError);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException("GOG download failed. " + string.Join(" ", lines.TakeLast(4)));
        return string.Join(Environment.NewLine, lines);
    }

    private static JsonDocument ParseLastJson(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Reverse())
            try { return JsonDocument.Parse(line); } catch (JsonException) { }
        throw new InvalidDataException("The GOG runtime did not return usable metadata.");
    }

    private static long FindLong(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && TryReadBytes(value, out var number)) return number;
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var property in root.EnumerateObject())
            {
                var found = FindLong(property.Value, names);
                if (found > 0) return found;
            }
        if (root.ValueKind == JsonValueKind.Array)
            foreach (var item in root.EnumerateArray())
            {
                var found = FindLong(item, names);
                if (found > 0) return found;
            }
        return 0;
    }

    public static bool TryReadBytes(JsonElement value, out long number)
    {
        number = 0;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out number)) return true;
            if (value.TryGetDouble(out var floating)) { number = checked((long)floating); return true; }
            return false;
        }
        if (value.ValueKind != JsonValueKind.String) return false;
        var text = value.GetString()?.Trim() ?? "";
        if (long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number)) return true;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var amount)) return false;
        var multiplier = parts[1].ToUpperInvariant() switch
        {
            "B" => 1d, "KB" => 1_000d, "KIB" => 1024d, "MB" => 1_000_000d, "MIB" => 1024d * 1024,
            "GB" => 1_000_000_000d, "GIB" => 1024d * 1024 * 1024, _ => 0d
        };
        if (multiplier == 0) return false;
        number = checked((long)(amount * multiplier));
        return true;
    }

    /// <summary>Reads gogdl's "ETA: 1:23:45" (or "0:04:12") when it reports one.</summary>
    private static TimeSpan? ParseRemaining(string line)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line,
            @"ETA[:=]?\s*(?:(\d+):)?(\d{1,2}):(\d{2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        return new TimeSpan(hours, int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
    }

    private static double? ParsePercent(string line)
    {
        var marker = line.IndexOf('%');
        if (marker < 1) return null;
        var start = marker - 1;
        while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.')) start--;
        return double.TryParse(line[(start + 1)..marker], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GOG-Disc-Packager/2.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

public static class GogGalaxyDetection
{
    public static string? FindInstallation()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "GalaxyClient.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy", "GalaxyClient.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}

/// <summary>Searches the signed-in account's own library. The public catalog lists bundle SKUs that carry no
/// build, and hides some base games entirely, so owned products are the reliable source for packaging.</summary>
public sealed class GogLibraryClient
{
    public async Task<IReadOnlyList<GogCatalogProduct>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        await new GogDlRuntime().RefreshAuthenticationAsync(cancellationToken);
        var token = GogAccountDownloads.ReadAccessToken();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GOG-Disc-Packager/2.0");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.GetAsync(
            "https://embed.gog.com/account/getFilteredProducts?mediaType=1&search=" + Uri.EscapeDataString(query),
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("The GOG sign-in could not be renewed. Sign in to GOG again.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("products", out var products)) return [];
        return products.EnumerateArray()
            .Select(item => new GogCatalogProduct(
                item.GetProperty("id").ToString(),
                item.TryGetProperty("slug", out var slug) ? slug.GetString() ?? "" : "",
                item.TryGetProperty("title", out var title) ? title.GetString() ?? "Untitled GOG product" : "Untitled GOG product",
                "game"))
            .ToList();
    }
}

/// <summary>What a GOG product can actually deliver. A store SKU may support neither path — bundle
/// ("pack") SKUs carry no build and no installers, so media built from one is unusable.</summary>
public sealed record GogKeyAvailability(bool DirectDownload, int InstallerFiles, int Extras, string? Failure)
{
    public bool OfflineBackup => InstallerFiles > 0;
    public bool AnyInstallPath => DirectDownload || OfflineBackup;

    public static async Task<GogKeyAvailability> ProbeAsync(GogKeyProduct product, CancellationToken cancellationToken)
    {
        var direct = false;
        string? failure = null;
        try { await new GogDlRuntime().GetEstimateAsync(product, cancellationToken); direct = true; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { failure = ex.Message; }

        var client = await GogAccountDownloads.CreateAsync(cancellationToken);
        var files = await client.GetFilesAsync(product, cancellationToken);
        return new GogKeyAvailability(direct, files.Count(file => !file.IsExtra), files.Count(file => file.IsExtra), failure);
    }
}

public sealed class GogAccountDownloads
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private GogAccountDownloads(string accessToken)
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GOG-Disc-Packager/2.0");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    /// <summary>Renews the hour-long access token through gogdl, which owns the credential file.</summary>
    public static async Task<GogAccountDownloads> CreateAsync(CancellationToken cancellationToken)
    {
        await new GogDlRuntime().RefreshAuthenticationAsync(cancellationToken);
        return new GogAccountDownloads(ReadAccessToken());
    }

    public async Task<IReadOnlyList<GogAccountFile>> GetFilesAsync(GogKeyProduct product, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"https://api.gog.com/products/{product.ProductId}?locale=en-US&expand=downloads", cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("The GOG sign-in could not be renewed. Sign in to GOG again.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"The signed-in GOG account does not own {product.Title}.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var files = new List<GogAccountFile>();
        if (!document.RootElement.TryGetProperty("downloads", out var downloads)) return files;
        if (downloads.TryGetProperty("installers", out var installers))
        {
            foreach (var group in installers.EnumerateArray())
            {
                var os = GetString(group, "os") ?? "";
                var language = GetString(group, "language") ?? "";
                if (!os.Equals(product.Platform, StringComparison.OrdinalIgnoreCase) ||
                    !(language.Equals(product.Language, StringComparison.OrdinalIgnoreCase) || language.StartsWith(product.Language, StringComparison.OrdinalIgnoreCase))) continue;
                AddGroupFiles(group, files, false);
            }
        }
        if (downloads.TryGetProperty("bonus_content", out var bonus))
            foreach (var group in bonus.EnumerateArray()) AddGroupFiles(group, files, true);
        return files;
    }

    /// <summary>Resolves the real on-disk name GOG serves for <paramref name="destination"/> without downloading it.</summary>
    public async Task<string> ResolveDestinationAsync(GogAccountFile file, string destination, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Path.GetExtension(destination))) return destination;
        return ApplyRemoteName(destination, await ResolveDownlinkAsync(file.Downlink, cancellationToken));
    }

    private static string ApplyRemoteName(string destination, string actualUrl)
    {
        if (!string.IsNullOrWhiteSpace(Path.GetExtension(destination))) return destination;
        var remoteName = Uri.UnescapeDataString(Path.GetFileName(new Uri(actualUrl).AbsolutePath));
        return string.IsNullOrWhiteSpace(remoteName) ? destination
            : Path.Combine(Path.GetDirectoryName(destination)!, PackageBuilder.SanitizeFileName(remoteName));
    }

    public async Task<string> DownloadAsync(GogAccountFile file, string destination,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var actualUrl = await ResolveDownlinkAsync(file.Downlink, cancellationToken);
        destination = ApplyRemoteName(destination, actualUrl);
        if (File.Exists(destination) && (file.Size <= 0 || new FileInfo(destination).Length == file.Size)) return destination;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".partial";
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, actualUrl);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (existing > 0 && response.StatusCode == HttpStatusCode.OK) { File.Delete(partial); existing = 0; }
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(partial, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        var buffer = new byte[1024 * 1024];
        long total = existing;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            progress?.Report(total);
        }
        output.Close();
        File.Move(partial, destination, true);
        return destination;
    }

    private static void AddGroupFiles(JsonElement group, List<GogAccountFile> files, bool extra)
    {
        if (!group.TryGetProperty("files", out var groupFiles) || groupFiles.ValueKind != JsonValueKind.Array) return;
        var groupName = GetString(group, "name") ?? (extra ? "GOG extra" : "GOG installer");
        foreach (var file in groupFiles.EnumerateArray())
        {
            var downlink = GetString(file, "downlink");
            if (string.IsNullOrWhiteSpace(downlink)) continue;
            var id = file.TryGetProperty("id", out var idValue) ? idValue.ToString() : downlink;
            files.Add(new GogAccountFile(id, groupName, downlink, GetLong(file, "size", "total_size", "totalSize"), extra));
        }
    }

    private async Task<string> ResolveDownlinkAsync(string downlink, CancellationToken cancellationToken)
    {
        var uri = downlink.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? downlink : "https://www.gog.com" + downlink;
        using var response = await _http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        foreach (var name in new[] { "downlink", "url", "link" })
            if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString()!;
        throw new InvalidDataException("GOG did not provide a download URL.");
    }

    private static void Collect(JsonElement element, List<GogAccountFile> files, bool extras, string language)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("extras", out var extrasElement)) Collect(extrasElement, files, true, language);
            if (element.TryGetProperty("downloads", out var downloads)) Collect(downloads, files, extras, language);
            if (element.TryGetProperty("installers", out var installers)) Collect(installers, files, false, language);
            if (element.TryGetProperty("manualUrl", out var link) && link.ValueKind == JsonValueKind.String)
            {
                var name = GetString(element, "name", "title", "fileName") ?? (extras ? "GOG extra" : "GOG installer");
                var lang = GetString(element, "language", "language_code");
                if (extras || string.IsNullOrWhiteSpace(lang) || lang.Equals(language, StringComparison.OrdinalIgnoreCase) || lang.Equals("English", StringComparison.OrdinalIgnoreCase))
                    files.Add(new GogAccountFile(GetString(element, "id") ?? link.GetString()!, name, link.GetString()!, GetLong(element, "size", "totalSize", "total_size"), extras));
            }
            foreach (var property in element.EnumerateObject())
                if (property.Name is not ("extras" or "downloads" or "installers" or "manualUrl")) Collect(property.Value, files, extras, language);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Collect(item, files, extras, language);
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names) if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
        return null;
    }

    private static long GetLong(JsonElement element, params string[] names)
    {
        foreach (var name in names) if (element.TryGetProperty(name, out var value) && GogDlRuntime.TryReadBytes(value, out var number)) return number;
        return 0;
    }

    internal static string ReadAccessToken()
    {
        if (!File.Exists(AppPaths.GogAuth)) throw new UnauthorizedAccessException("Sign in to GOG first.");
        using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.GogAuth));
        foreach (var account in document.RootElement.EnumerateObject())
            if (account.Value.TryGetProperty("access_token", out var token) && !string.IsNullOrWhiteSpace(token.GetString())) return token.GetString()!;
        throw new UnauthorizedAccessException("The saved GOG sign-in has expired. Sign in again.");
    }
}
