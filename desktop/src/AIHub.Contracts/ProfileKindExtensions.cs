namespace AIHub.Contracts;

public static class ProfileKindExtensions
{
    public static string ToStorageValue(this ProfileKind profile)
    {
        return profile switch
        {
            ProfileKind.Global => "global",
            ProfileKind.Frontend => "frontend",
            ProfileKind.Backend => "backend",
            _ => "global"
        };
    }

    public static string ToDisplayName(this ProfileKind profile)
    {
        return profile switch
        {
            ProfileKind.Global => "全局",
            ProfileKind.Frontend => "前端",
            ProfileKind.Backend => "后端",
            _ => "未知"
        };
    }

    public static bool TryParse(string? rawValue, out ProfileKind profile)
    {
        profile = ProfileKind.Global;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        switch (rawValue.Trim().ToLowerInvariant())
        {
            case "global":
                profile = ProfileKind.Global;
                return true;
            case "frontend":
                profile = ProfileKind.Frontend;
                return true;
            case "backend":
                profile = ProfileKind.Backend;
                return true;
            default:
                return false;
        }
    }
}
