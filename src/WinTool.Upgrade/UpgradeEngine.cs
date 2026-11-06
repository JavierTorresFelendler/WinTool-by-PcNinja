using Microsoft.Win32;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Security.Cryptography;
using WinTool.Core.Json;

namespace WinTool.Upgrade;

public sealed class UpgradeEngine
{
    public const string DownloadEndpoint = DownloadSourcePolicy.OemPage;
    public const string PcNinjaBrowserFallbackPage = "https://iso.pcninja.pro";
    private const string AutomaticDownloadProviderPage =
        "https://technionmail-my.sharepoint.com/:u:/g/personal/techsup_technion_ac_il/IQB4hCE2DRrGRqQuQYDafLdfAYjrr8R8MAqD4efXPsI-epA?e=aqZwxY";
    public const string MicrosoftDownloadPage = "https://www.microsoft.com/software-download/windows11";
    public const long MinimumIsoBytes = 3L * 1024 * 1024 * 1024;
    public const long MinimumSystemDriveFreeBytes = 20L * 1024 * 1024 * 1024;
    private const long UnknownDownloadReserveBytes = 12L * 1024 * 1024 * 1024;
    private const long DownloadSafetyMarginBytes = 1L * 1024 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly IsoValidationSession _isoValidation;
    private readonly Func<DownloadSystem> _readDownloadSystem;

    public UpgradeEngine() : this(DownloadSystem.Read, CreateDownloadHttp()) { }

    internal UpgradeEngine(Func<DownloadSystem> readDownloadSystem, HttpClient http)
    {
        _readDownloadSystem = readDownloadSystem;
        _http = http;
        _isoValidation = new(IsoFileProof.TryOpen, ResolveIsoMountAsync, HashIsoStreamAsync,
            (root, token, progress) => ValidateMountedSourceAsync(root, token, progress: progress),
            (root, token) => Task.Run(() => ValidateMountedSource(root, removeSelectedAnswerFiles: false), token));
    }

    private static HttpClient CreateDownloadHttp()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"PcNinja-Win11-Upgrade-Assistant/{AppUpdateIdentity.Version}");
        return http;
    }

    public string GetOperatingSystemDescription()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var name = key?.GetValue("ProductName")?.ToString() ?? "Windows";
        var display = key?.GetValue("DisplayVersion")?.ToString();
        var editionId = key?.GetValue("EditionID")?.ToString();
        var buildText = key?.GetValue("CurrentBuildNumber")?.ToString();
        var build = int.TryParse(buildText, out var parsedBuild) ? parsedBuild : Environment.OSVersion.Version.Build;
        return FormatOperatingSystemDescription(name, display, build, Environment.Is64BitOperatingSystem, editionId);
    }

    public static string FormatOperatingSystemDescription(string productName, string? displayVersion,
        int build, bool is64Bit, string? editionId = null)
    {
        if (productName.Contains("Server", StringComparison.OrdinalIgnoreCase))
        {
            var serverName = productName.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)
                ? productName[10..].Trim()
                : productName.Trim();
            var serverVersion = string.IsNullOrWhiteSpace(displayVersion) ? "" : $" {displayVersion.Trim()}";
            return $"{serverName}{serverVersion} (build {build}, {(is64Bit ? "x64" : "x86")})";
        }
        var family = build >= 22000 ? "Windows 11" : "Windows 10";
        var edition = ExtractEdition(productName);
        if (string.IsNullOrWhiteSpace(edition)) edition = FormatEditionId(editionId);
        var version = string.IsNullOrWhiteSpace(displayVersion) ? "" : $" {displayVersion.Trim()}";
        var architecture = is64Bit ? "x64" : "x86";
        return $"{family}{(string.IsNullOrWhiteSpace(edition) ? "" : $" {edition}")}{version} (build {build}, {architecture})";
    }

    public void ValidateSupportedComputer() => ManualUpgradePolicy.EnsureAvailable(DownloadSystem.Read());

    public IReadOnlyList<string> FindLocalIsos()
    {
        try
        {
            return Directory.EnumerateFiles(AppContext.BaseDirectory, "*.iso", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.Length >= MinimumIsoBytes)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => file.FullName)
                .ToArray();
        }
        catch (Exception ex)
        {
            AppLog.Error($"ISO scan failed: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<MountedSource> FindMountedSources()
    {
        var sources = new List<MountedSource>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var setup = Path.Combine(drive.RootDirectory.FullName, "setup.exe");
                if (File.Exists(setup)) sources.Add(new(drive.RootDirectory.FullName, setup));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { AppLog.Info($"Source scan skipped {drive.Name}: {ex.Message}"); }
        }
        return sources;
    }

    public IReadOnlyList<StorageLocation> FindDownloadLocations()
    {
        var locations = new List<StorageLocation>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                if (!IsEligibleDownloadLocation(drive.DriveFormat, drive.AvailableFreeSpace)) continue;
                if (DownloadVolume.IsReserved(drive.RootDirectory.FullName)) continue;
                locations.Add(new StorageLocation(drive.RootDirectory.FullName, drive.VolumeLabel,
                    drive.DriveType, drive.DriveFormat, drive.AvailableFreeSpace));
            }
            catch (Exception ex)
            {
                AppLog.Error($"Storage scan skipped {drive.Name}: {ex.Message}");
            }
        }

        return locations.OrderBy(location => location.DriveType == DriveType.Fixed ? 0 : 1)
            .ThenBy(location => location.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public StorageCheck CheckSystemDriveSpace()
    {
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            ?? throw new InvalidOperationException("The Windows system drive could not be identified.");
        var drive = new DriveInfo(systemRoot);
        var valid = drive.IsReady && drive.AvailableFreeSpace >= MinimumSystemDriveFreeBytes;
        var message = SystemDriveSpaceMessage(systemRoot, drive.AvailableFreeSpace);
        return new StorageCheck(valid, drive.RootDirectory.FullName, drive.DriveFormat,
            drive.AvailableFreeSpace, MinimumSystemDriveFreeBytes, message);
    }

    internal static string SystemDriveSpaceMessage(string root, long availableBytes) =>
        availableBytes >= MinimumSystemDriveFreeBytes
            ? $"{FormatBytes(availableBytes)} free on the Windows drive ({root})."
            : $"Not enough space on the Windows drive ({root}). " +
              $"Available: {FormatBytes(availableBytes)}. Required: {FormatBytes(MinimumSystemDriveFreeBytes)}. " +
              $"Free another {FormatBytes(MinimumSystemDriveFreeBytes - availableBytes)}, then Refresh. " +
              "Moving the ISO to another drive does not free Windows upgrade workspace.";

    public StorageCheck CheckDownloadDestination(string destinationDirectory, long? expectedBytes = null)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new InvalidOperationException("Choose a download destination.");

        var fullDirectory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDirectory);
        var root = Path.GetPathRoot(fullDirectory)
            ?? throw new InvalidOperationException("The selected download drive could not be identified.");
        var drive = new DriveInfo(root);
        if (!drive.IsReady) throw new InvalidOperationException($"Drive {root} is not ready.");
        if (DownloadVolume.IsReserved(root))
            throw new InvalidOperationException("Choose a data drive instead of a hidden, recovery or system partition.");
        if (!IsSupportedDownloadFileSystem(drive.DriveFormat))
            throw new InvalidOperationException("Choose an NTFS or exFAT drive for the ISO download.");

        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        var required = RequiredDownloadBytes(expectedBytes, string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase));
        var valid = drive.AvailableFreeSpace >= required;
        var message = valid
            ? $"{FormatBytes(drive.AvailableFreeSpace)} free on {root}."
            : $"The selected drive needs at least {FormatBytes(required)} free. " +
              $"Only {FormatBytes(drive.AvailableFreeSpace)} is available.";
        if (valid)
        {
            var probe = Path.Combine(fullDirectory, $".pcninja-write-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(probe, "write test");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"The selected download folder is not writable: {fullDirectory}", ex);
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            }
        }
        return new StorageCheck(valid, root, drive.DriveFormat, drive.AvailableFreeSpace, required, message);
    }

    public static bool IsSupportedDownloadFileSystem(string format) =>
        format.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || format.Equals("exFAT", StringComparison.OrdinalIgnoreCase);

    public static bool IsEligibleDownloadLocation(string format, long availableBytes) =>
        IsSupportedDownloadFileSystem(format) && availableBytes >= MinimumIsoBytes + DownloadSafetyMarginBytes;

    public static long RequiredDownloadBytes(long? remainingBytes, bool windowsDrive) =>
        checked((remainingBytes ?? UnknownDownloadReserveBytes) + DownloadSafetyMarginBytes +
            (windowsDrive ? MinimumSystemDriveFreeBytes : 0));

    public static string? ConfiguredIsoSha256 => Environment.GetEnvironmentVariable("PCNINJA_ISO_SHA256") is { Length: > 0 } hash
        ? NormalizeSha256(hash) : null;

    public static string NormalizeSha256(string hash)
    {
        hash = hash.Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.");
        return hash.ToUpperInvariant();
    }

    public static async Task<string> VerifyIsoHashAsync(string path, string? expected, CancellationToken token)
    {
        expected = expected is null ? null : NormalizeSha256(expected);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await HashIsoStreamAsync(stream, token, null).ConfigureAwait(false);
        if (expected is not null && !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ISO checksum does not match. Download a fresh copy before upgrading.");
        AppLog.Info($"ISO SHA256: {actual}. Trusted expected hash supplied: {expected is not null}.");
        return actual;
    }

    internal static async Task<string> HashIsoStreamAsync(Stream stream, CancellationToken token,
        IProgress<string>? progress)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long readTotal = 0;
        var lastPercent = -1;
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            hash.AppendData(buffer, 0, read);
            readTotal += read;
            var percent = stream.Length == 0 ? 100 : (int)(readTotal * 100 / stream.Length);
            if (percent != lastPercent)
            {
                progress?.Report($"Checking the ISO checksum... {percent}%");
                lastPercent = percent;
            }
        }
        token.ThrowIfCancellationRequested();
        return Compat.ToHexString(hash.GetHashAndReset());
    }

    public async Task<IsoPreparationResult> PrepareIsoAsync(string path, string? expectedSha256,
        CancellationToken token, IProgress<string>? progress = null)
    {
        try { ValidateIsoFile(path); }
        catch { _isoValidation.Invalidate(); throw; }
        using var operation = AppOperationGate.EnterWindows();
        return await _isoValidation.PrepareAsync(path, expectedSha256, progress, token).ConfigureAwait(false);
    }

    public void InvalidateIsoValidation() => _isoValidation.Invalidate();

    public async Task<DownloadRecommendation> GetDownloadRecommendationAsync(CancellationToken token)
    {
        // Catalog requests must not inherit the download provider's cookies or
        // follow a changed catalog URL to another host before it is checked.
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var catalog = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return await IsoDownloadCatalog.ResolveAsync(catalog, DownloadSystem.Read(), token);
    }

    public async Task<string> GetManualServerCommandAsync(MountedSource selectedSource, int imageIndex,
        CancellationToken token)
    {
        var system = DownloadSystem.Read();
        if (!system.IsServer) throw new InvalidOperationException("Manual Server guidance is only available on Windows Server.");
        var source = await ValidateMountedSourceAsync(selectedSource.Root, token, removeSelectedAnswerFiles: false);
        if (!string.Equals(source.ImagePath, selectedSource.ImagePath, StringComparison.OrdinalIgnoreCase) ||
            !(source.Images ?? []).SequenceEqual(selectedSource.Images ?? []))
            throw new InvalidOperationException("Installation media changed. Validate and select the image again.");
        var image = (source.Images ?? []).SingleOrDefault(item => item.Index == imageIndex)
            ?? throw new InvalidOperationException("Select a validated Windows Server image first.");
        var command = ManualServerUpgrade.BuildCommand(source, image, system);
        InstallationSafety.EnsureSafe(source.Root, removeSelectedAnswerFiles: false);
        token.ThrowIfCancellationRequested();
        return command;
    }

    public async Task<DownloadResult> DownloadIsoAsync(string destinationDirectory,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken, string? expectedSha256 = null)
    {
        var system = _readDownloadSystem();
        AutomaticDownloadPolicy.EnsureAvailable(system);
        var configuredSource = DownloadSourcePolicy.GetAutomaticSource(system)!;
        using var operation = AppOperationGate.EnterWindows();
        InstallationSafety.EnsureSafe(AppContext.BaseDirectory, removeSelectedAnswerFiles: false);
        MountedSource? validatedSource = null;
        var result = await DownloadWithTransportFallbackAsync(endpoint => IsoTransfer.DownloadAsync(_http, endpoint, Path.GetFullPath(destinationDirectory),
            remaining =>
            {
                var storage = CheckDownloadDestination(destinationDirectory, remaining);
                if (!storage.IsValid) throw new InvalidOperationException(storage.Message);
            }, progress, cancellationToken, expectedSha256 ?? ConfiguredIsoSha256,
            validateMedia: async (path, token) =>
            {
                var source = await MountAndValidateAsync(path, token, new InlineProgress<string>(stage =>
                    progress?.Report(new(path, 0, null, null, stage))));
                InstallationImages.EnsureAutomaticWindows11Source(source);
                validatedSource = source;
            }, validateExistingIso: async (path, expected, token) =>
            {
                var prepared = await PrepareIsoAsync(path, expected, token, new InlineProgress<string>(stage =>
                    progress?.Report(new(path, 0, null, null, stage))));
                InstallationImages.EnsureAutomaticWindows11Source(prepared.Source);
                validatedSource = prepared.Source;
                return prepared.Sha256;
            }), new Uri(configuredSource.BrowserPage));
        // Reuse only this operation's exact completed validation, never a path/time-based cache.
        return result with { ValidatedSource = validatedSource
            ?? throw new InvalidOperationException("The downloaded ISO did not complete media validation.") };
    }

    internal static async Task<DownloadResult> DownloadWithTransportFallbackAsync(Func<Uri, Task<DownloadResult>> transfer,
        Uri? endpoint = null)
    {
        endpoint ??= new Uri(DownloadEndpoint);
        try { return await transfer(endpoint); }
        catch (DownloadTransferException ex) when (ex.InnerException is HttpRequestException &&
            endpoint.Host.Equals(new Uri(DownloadEndpoint).Host, StringComparison.OrdinalIgnoreCase))
        {
            // The short-link host may require TLS 1.3. Windows 10 can securely use the
            // provider route directly with OS-default TLS and normal certificate checks.
            AppLog.Info($"Short-link transport unavailable; trying the provider directly. {ex.InnerException.Message}");
            return await transfer(new Uri(AutomaticDownloadProviderPage + "&download=1"));
        }
    }

    public void ValidateIsoFile(string isoPath)
    {
        if (string.IsNullOrWhiteSpace(isoPath) || !File.Exists(isoPath))
            throw new FileNotFoundException("The selected ISO file was not found.", isoPath);
        if (!string.Equals(System.IO.Path.GetExtension(isoPath), ".iso", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected file is not an ISO image.");
        var size = new FileInfo(isoPath).Length;
        if (size < MinimumIsoBytes)
            throw new InvalidOperationException($"The selected ISO is unexpectedly small ({FormatBytes(size)}).");
    }

    public async Task<MountedSource> MountAndValidateAsync(string isoPath, CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        isoPath = Path.GetFullPath(isoPath);
        ValidateIsoFile(isoPath);
        using var operation = AppOperationGate.EnterWindows();
        var mount = await ResolveIsoMountAsync(isoPath, true, cancellationToken)
            ?? throw new InvalidOperationException("The selected ISO's mounted drive could not be identified.");
        return await ValidateMountedSourceAsync(mount.Root, cancellationToken, progress: progress);
    }

    private static async Task<IsoMountIdentity?> ResolveIsoMountAsync(string isoPath, bool attachIfMissing,
        CancellationToken cancellationToken)
    {
        isoPath = Path.GetFullPath(isoPath);
        var json = (await RunPowerShellAsync(BuildIsoMountCommand(isoPath, attachIfMissing), cancellationToken)).Trim();
        var mount = JsonData.Deserialize<IsoMountIdentity>(json);
        if (mount is null) return null;
        if (!string.Equals(Path.GetFullPath(mount.ImagePath), isoPath, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(mount.Root))
            throw new InvalidOperationException("Windows returned a different ISO mount. Refresh and try again.");
        if (attachIfMissing) AppLog.Info(mount.MountedNow
            ? $"Mounted exact ISO {isoPath} at {mount.Root}."
            : $"Using already mounted exact ISO {isoPath} at {mount.Root}.");
        return mount;
    }

    internal static string BuildIsoMountCommand(string isoPath, bool attachIfMissing)
    {
        isoPath = Path.GetFullPath(isoPath);
        var escaped = isoPath.Replace("'", "''");
        return "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$p='" + escaped + "';" +
            "$d=Get-DiskImage -ImagePath $p -ErrorAction SilentlyContinue;" +
            "$mounted=$false;if(-not $d -or -not $d.Attached){" +
            (attachIfMissing ? "Mount-DiskImage -ImagePath $p -ErrorAction Stop|Out-Null;$mounted=$true;"
                : "[Console]::Out.Write('null');exit 0;") + "};" +
            "$until=[DateTime]::UtcNow.AddSeconds(" + (attachIfMissing ? "30" : "0") + ");$v=$null;" +
            "do{$d=Get-DiskImage -ImagePath $p -ErrorAction Stop;" +
            "$v=$d|Get-Volume -ErrorAction SilentlyContinue|Where-Object DriveLetter|Select-Object -First 1;" +
            "if(-not $v -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 500}}while(-not $v -and [DateTime]::UtcNow -lt $until);" +
            "if(-not $v){" + (attachIfMissing ? "throw 'PCNINJA_ISO_VOLUME_TIMEOUT'" : "[Console]::Out.Write('null');exit 0") + "};" +
            "[Console]::Out.Write((@{ImagePath=[string]$d.ImagePath;DevicePath=[string]$d.DevicePath;" +
            "VolumeId=[string]$v.UniqueId;Root=($v.DriveLetter+':\\');MountedNow=$mounted}|ConvertTo-Json -Compress))";
    }

    public MountedSource ValidateMountedSource(string root, bool removeSelectedAnswerFiles = true)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("No mounted installation source was selected.");
        root = Path.GetFullPath(root.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"The installation source folder was not found: {root}");
        var setup = System.IO.Path.Combine(root, "setup.exe");
        if (!File.Exists(setup))
            throw new InvalidOperationException($"setup.exe was not found in {root}.");
        if (!AuthenticodeVerifier.IsTrustedMicrosoftFile(setup, out var signer))
            throw new InvalidOperationException("setup.exe does not have a valid Microsoft digital signature.");
        InstallationSafety.EnsureSafe(root, removeSelectedAnswerFiles);
        AppLog.Info($"Validated installation source {root}; signer: {signer}.");
        return new MountedSource(root, setup);
    }

    public async Task<MountedSource> ValidateMountedSourceAsync(string root, CancellationToken cancellationToken,
        bool removeSelectedAnswerFiles = true, IProgress<string>? progress = null)
    {
        using var operation = AppOperationGate.EnterWindows();
        progress?.Report("Checking Microsoft signature and media safety...");
        var source = await Task.Run(() => ValidateMountedSource(root, removeSelectedAnswerFiles), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var sourcesDirectory = Path.Combine(source.Root, "sources");
        var installImage = new[] { "install.wim", "install.esd", "install.swm" }
            .Select(name => Path.Combine(sourcesDirectory, name))
            .FirstOrDefault(File.Exists);
        if (installImage is null)
            throw new InvalidOperationException(
                "The installation source does not contain sources\\install.wim, install.esd, or install.swm.");

        AppLog.Info($"Validating Windows image metadata with DISM: {installImage}.");
        var dism = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "dism.exe");
        var result = await RunProcessAsync(dism, $"/English /Get-WimInfo /WimFile:\"{installImage}\"",
            cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Windows could not validate the installation image (DISM exit code {result.ExitCode}). " +
                "The ISO may be incomplete or damaged.");
        var images = new List<InstallationImage>();
        var indexes = InstallationImages.ParseIndexes(result.Output).ToArray();
        foreach (var index in indexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Reading Windows image {images.Count + 1} of {indexes.Length}...");
            var details = await RunProcessAsync(dism,
                $"/English /Get-WimInfo /WimFile:\"{installImage}\" /Index:{index}", cancellationToken);
            if (details.ExitCode != 0)
                throw new InvalidOperationException($"Windows could not read image {index} (DISM exit code {details.ExitCode}).");
            images.Add(InstallationImages.ParseDetail(details.Output, index));
        }
        if (images.Count == 0) throw new InvalidOperationException("No installation images were found in the selected source.");
        AppLog.Info($"DISM read {images.Count} installation images from {installImage}.");
        return source with { ImagePath = installImage, Images = images };
    }

    public async Task<UpgradeResult> StartUpgradeAsync(MountedSource selectedSource, int imageIndex,
        bool allowAutomaticRestart, CancellationToken cancellationToken, Action? setupStarting = null, bool quiet = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = AppOperationGate.EnterWindows();
        Directory.CreateDirectory(AppLog.Folder);
        using var setupLock = new FileStream(Path.Combine(AppLog.Folder, "Setup-Launch.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        ValidateSupportedComputer();
        var storage = CheckSystemDriveSpace();
        if (!storage.IsValid) throw new InvalidOperationException(storage.Message);
        var source = await ValidateMountedSourceAsync(selectedSource.Root, cancellationToken, removeSelectedAnswerFiles: false);
        if (!string.Equals(source.ImagePath, selectedSource.ImagePath, StringComparison.OrdinalIgnoreCase) ||
            !(source.Images ?? []).SequenceEqual(selectedSource.Images ?? []))
            throw new InvalidOperationException("Installation media changed. Validate and select the image again.");
        var current = CurrentWindows.Read();
        var image = InstallationImages.Select(source, imageIndex, current);
        AppLog.Info($"Selected Setup profile: {InstallationImages.GetSetupProfile(image)}; " +
            $"image {image.Index}; image metadata version {image.Version}; current build {current.Build}.");
        var arguments = BuildSetupArguments(source, image, allowAutomaticRestart, quiet);
        var setupPath = source.SetupPath;
        InstallationSafety.EnsureSafe(source.Root, removeSelectedAnswerFiles: false);
        var attempt = UpgradeHistory.Begin(source, image);
        cancellationToken.ThrowIfCancellationRequested();
        setupStarting?.Invoke();

        AppLog.Info($"Starting Windows Setup. Automatic restart: {allowAutomaticRestart}. Quiet: {quiet}. Command: setup.exe {arguments}");
        var startInfo = new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = arguments,
            UseShellExecute = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(setupPath)!
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows Setup could not be started.");
        // Once Setup starts, let Windows finish; cancelling the assistant cannot stop an upgrade safely.
        await process.WaitForExitAsync(CancellationToken.None);
        AppLog.Info($"Windows Setup exit code: {process.ExitCode}.");
        UpgradeHistory.RecordExit(attempt, process.ExitCode);
        return new UpgradeResult(process.ExitCode, setupPath);
    }

    internal static string BuildSetupArguments(MountedSource source, InstallationImage image, bool automaticRestart, bool quiet = false)
    {
        if (source.ImagePath is null || !(source.Images ?? []).Contains(image))
            throw new InvalidOperationException("The selected image is not bound to the validated source.");
        var command = InstallationImages.GetSetupProfile(image) switch
        {
            WindowsSetupProfile.Windows10 => "/auto upgrade /dynamicupdate disable /compat ignorewarning",
            WindowsSetupProfile.Windows11 => "/product server /auto upgrade /eula accept /dynamicupdate disable /compat ignorewarning",
            _ => throw new InvalidOperationException("No supported Setup command profile was found for the selected image.")
        };
        return command +
            $" /imageindex {image.Index} /installfrom \"{source.ImagePath}\"" + (automaticRestart ? "" : " /noreboot") +
            (quiet ? " /quiet" : "");
    }

    public static void OpenMicrosoftDownloadPage() => Process.Start(new ProcessStartInfo
    {
        FileName = MicrosoftDownloadPage,
        UseShellExecute = true
    });

    public static void OpenPcNinjaDownloadPage() => Process.Start(new ProcessStartInfo
    {
        // Manual language/version selection is separate from the internal automatic route.
        FileName = DownloadSourcePolicy.GetPcNinjaBrowserPage(DownloadSystem.Read()),
        UseShellExecute = true
    });

    internal static async Task<string> RunPowerShellAsync(string command, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = new UTF8Encoding(false),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows could not mount the ISO.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(process, "PowerShell ISO operation");
            throw;
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var failure = new InvalidOperationException(PowerShellDiagnostics.MountFailureMessage(error),
                new InvalidOperationException($"PowerShell exit code {process.ExitCode}. Raw stderr:\n{error}"));
            AppLog.Error($"ISO mount helper failed: {failure}");
            throw failure;
        }
        return output;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(fileName)}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(process, Path.GetFileName(fileName));
            throw;
        }
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static void TryTerminateProcess(Process process, string description)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not stop {description} after cancellation: {ex.Message}");
        }
    }

    internal static string GetRemoteFileName(HttpResponseMessage response)
    {
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fileName.Trim().Trim('"');
            var encodingSeparator = fileName.IndexOf("''", StringComparison.Ordinal);
            if (encodingSeparator >= 0) fileName = fileName[(encodingSeparator + 2)..];
            fileName = Uri.UnescapeDataString(fileName);
        }
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = Uri.UnescapeDataString(Path.GetFileName(response.RequestMessage?.RequestUri?.LocalPath) ?? "");
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
            fileName != Path.GetFileName(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new DownloadTransferException("The service did not provide a safe original ISO filename. Download in your browser and select the exact file.");
        return fileName;
    }

    private static string ExtractEdition(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return "";
        var value = productName.Trim();
        if (value.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)) value = value[10..].Trim();
        foreach (var prefix in new[] { "Windows 11", "Windows 10", "Windows" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return value[prefix.Length..].Trim();
        }
        return value;
    }

    private static string FormatEditionId(string? editionId) => editionId?.ToLowerInvariant() switch
    {
        "professional" => "Pro",
        "professionaln" => "Pro N",
        "core" => "Home",
        "coren" => "Home N",
        "coresinglelanguage" => "Home Single Language",
        "education" => "Education",
        "educationn" => "Education N",
        "professionalworkstation" => "Pro for Workstations",
        "professionalworkstationn" => "Pro N for Workstations",
        _ => editionId ?? ""
    };

    public static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:N2} GB";

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public static class AuthenticodeVerifier
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool IsTrustedMicrosoftFile(string filePath, out string signer)
    {
        signer = "Unknown";
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            signer = certificate.Subject;
            if (!certificate.Subject.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch { return false; }

        var fileInfo = new WinTrustFileInfo
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = filePath,
            FileHandle = IntPtr.Zero,
            KnownSubject = IntPtr.Zero
        };
        var fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        var dataPointer = IntPtr.Zero;
        var actionPointer = IntPtr.Zero;
        var fileInfoWritten = false;
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            fileInfoWritten = true;
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                PolicyCallbackData = IntPtr.Zero,
                SipClientData = IntPtr.Zero,
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfoPointer = fileInfoPointer,
                StateAction = 0,
                StateData = IntPtr.Zero,
                UrlReference = IntPtr.Zero,
                ProviderFlags = 0,
                UiContext = 0
            };
            dataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPointer, false);
            actionPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(ActionGenericVerifyV2, actionPointer, false);
            return WinVerifyTrust(IntPtr.Zero, actionPointer, dataPointer) == 0;
        }
        finally
        {
            if (actionPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(actionPointer);
            if (dataPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(dataPointer);
            if (fileInfoWritten) Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeCoTaskMem(fileInfoPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, IntPtr actionId, IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfoPointer;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }
}
