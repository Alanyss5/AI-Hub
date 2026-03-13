namespace AIHub.Contracts;

public static class SkillSourceKindExtensions
{
    public static string ToDisplayName(this SkillSourceKind kind)
    {
        return kind switch
        {
            SkillSourceKind.GitRepository => "Git 仓库",
            SkillSourceKind.LocalDirectory => "本地目录",
            _ => "未知"
        };
    }

    public static string ToDescription(this SkillSourceKind kind)
    {
        return kind switch
        {
            SkillSourceKind.GitRepository => "适合登记 GitHub 或自建 Git 仓库中的 Skills 来源。",
            SkillSourceKind.LocalDirectory => "适合登记你本机上自己维护的 Skills 目录。",
            _ => "未知来源类型。"
        };
    }
}