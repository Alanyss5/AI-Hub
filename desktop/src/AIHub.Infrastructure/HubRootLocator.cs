using System.Text.Json;
using AIHub.Application.Abstractions;
using AIHub.Contracts;
using AIHub.Core;

namespace AIHub.Infrastructure;

public sealed class HubRootLocator : IHubRootLocator
{
    private readonly string _stateFilePath;
    private string? _preferredRoot;

    public HubRootLocator(string? preferredRoot = null)
    {
        _stateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIHub",
            "desktop-state.json");

        _preferredRoot = LoadPersistedRoot();

        if (!string.IsNullOrWhiteSpace(preferredRoot))
        {
            SetPreferredRoot(preferredRoot);
        }
    }

    public void SetPreferredRoot(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            _preferredRoot = null;
            PersistPreferredRoot();
            return;
        }

        try
        {
            _preferredRoot = Path.GetFullPath(rootPath.Trim());
        }
        catch
        {
            _preferredRoot = rootPath.Trim();
        }

        PersistPreferredRoot();
    }

    public string? GetPreferredRoot()
    {
        return _preferredRoot;
    }

    public Task<HubRootResolution> EvaluateAsync(string candidatePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvaluateCandidate(candidatePath, "手动指定"));
    }

    public Task<HubRootResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<(string Path, string Source)>();

        if (!string.IsNullOrWhiteSpace(_preferredRoot))
        {
            candidates.Add((_preferredRoot, "桌面设置"));
        }

        var environmentRoot = Environment.GetEnvironmentVariable("AI_HUB_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentRoot))
        {
            candidates.Add((environmentRoot, "环境变量 AI_HUB_ROOT"));
        }

        foreach (var path in EnumerateParents(AppContext.BaseDirectory))
        {
            candidates.Add((path, "可执行文件目录向上探测"));
        }

        foreach (var candidate in candidates.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var resolution = EvaluateCandidate(candidate.Path, candidate.Source);
            if (resolution.IsValid)
            {
                return Task.FromResult(resolution);
            }
        }

        return Task.FromResult(new HubRootResolution(
            RootPath: null,
            IsValid: false,
            Source: "未找到",
            Errors: new[] { "未找到有效的 AI-Hub 根目录。请设置 AI_HUB_ROOT 或在应用中手动选择目录。" }));
    }

    private string? LoadPersistedRoot()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<DesktopState>(json);
            return string.IsNullOrWhiteSpace(state?.PreferredRoot) ? null : state.PreferredRoot;
        }
        catch
        {
            return null;
        }
    }

    private void PersistPreferredRoot()
    {
        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new DesktopState
            {
                PreferredRoot = _preferredRoot
            }, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
        }
    }

    private static IEnumerable<string> EnumerateParents(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static HubRootResolution EvaluateCandidate(string candidatePath, string source)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return new HubRootResolution(null, false, source, new[] { "候选目录为空。" });
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(candidatePath.Trim());
        }
        catch (Exception exception)
        {
            return new HubRootResolution(candidatePath, false, source, new[] { "目录格式无效：" + exception.Message });
        }

        if (!Directory.Exists(normalizedPath))
        {
            return new HubRootResolution(normalizedPath, false, source, new[] { "目录不存在：" + normalizedPath });
        }

        var hasHubMarker = File.Exists(Path.Combine(normalizedPath, HubLayout.HubMarkerFileName));
        var topLevelDirectories = Directory
            .GetDirectories(normalizedPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

        var validation = HubValidationRules.Validate(hasHubMarker, topLevelDirectories);
        return new HubRootResolution(normalizedPath, validation.IsValid, source, validation.Errors);
    }

    private sealed class DesktopState
    {
        public string? PreferredRoot { get; set; }
    }
}
