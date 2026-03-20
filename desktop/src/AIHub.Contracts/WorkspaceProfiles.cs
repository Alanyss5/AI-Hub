using System.Text.RegularExpressions;

namespace AIHub.Contracts;

public static partial class WorkspaceProfiles
{
    public const string GlobalId = "global";
    public const string FrontendId = "frontend";
    public const string BackendId = "backend";

    public const string GlobalDisplayName = "全局";
    public const string FrontendDisplayName = "前端";
    public const string BackendDisplayName = "后端";

    public static IReadOnlyList<WorkspaceProfileRecord> CreateDefaultCatalog()
    {
        return
        [
            new WorkspaceProfileRecord
            {
                Id = GlobalId,
                DisplayName = GlobalDisplayName,
                IsBuiltin = true,
                IsDeletable = false,
                SortOrder = 0
            },
            new WorkspaceProfileRecord
            {
                Id = FrontendId,
                DisplayName = FrontendDisplayName,
                IsBuiltin = true,
                IsDeletable = true,
                SortOrder = 1
            },
            new WorkspaceProfileRecord
            {
                Id = BackendId,
                DisplayName = BackendDisplayName,
                IsBuiltin = true,
                IsDeletable = true,
                SortOrder = 2
            }
        ];
    }

    public static string NormalizeId(string? rawProfileId)
    {
        if (string.IsNullOrWhiteSpace(rawProfileId))
        {
            return GlobalId;
        }

        var trimmed = rawProfileId.Trim();
        var lowered = trimmed.ToLowerInvariant();
        switch (lowered)
        {
            case "global":
            case "全局":
                return GlobalId;
            case "frontend":
            case "前端":
                return FrontendId;
            case "backend":
            case "后端":
                return BackendId;
        }

        var normalized = ProfileSlugRegex()
            .Replace(lowered, "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(normalized) ? GlobalId : normalized;
    }

    public static string NormalizeDisplayName(string? rawDisplayName, string profileId)
    {
        if (!string.IsNullOrWhiteSpace(rawDisplayName))
        {
            return rawDisplayName.Trim();
        }

        return ToDisplayName(profileId);
    }

    public static string ToDisplayName(string? profileId)
    {
        return NormalizeId(profileId) switch
        {
            GlobalId => GlobalDisplayName,
            FrontendId => FrontendDisplayName,
            BackendId => BackendDisplayName,
            var customId => customId
        };
    }

    public static bool IsGlobal(string? profileId)
    {
        return string.Equals(NormalizeId(profileId), GlobalId, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex ProfileSlugRegex();
}
