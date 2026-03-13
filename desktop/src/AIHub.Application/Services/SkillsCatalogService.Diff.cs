using System.Text;

namespace AIHub.Application.Services;

public sealed partial class SkillsCatalogService
{
    private static string BuildDiffPreviewDetails(
        SkillInstallContext context,
        string? blockedReason,
        IReadOnlyList<string> sourceAdded,
        IReadOnlyList<string> sourceChanged,
        IReadOnlyList<string> sourceRemoved,
        IReadOnlyList<string> localAdded,
        IReadOnlyList<string> localChanged,
        IReadOnlyList<string> localRemoved)
    {
        var builder = new StringBuilder();
        var hasUpdate = sourceAdded.Count > 0 || sourceChanged.Count > 0 || sourceRemoved.Count > 0;
        builder.AppendLine(BuildUpdateDetails(context, hasUpdate, blockedReason));
        builder.AppendLine();
        AppendDiffSection(builder, "来源相对当前安装", sourceAdded, sourceChanged, sourceRemoved);
        builder.AppendLine();

        if (context.State.BaselineFiles.Count == 0)
        {
            builder.AppendLine("当前安装相对本地基线：尚未建立本地基线。");
        }
        else
        {
            AppendDiffSection(builder, "当前安装相对本地基线", localAdded, localChanged, localRemoved);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendDiffSection(
        StringBuilder builder,
        string title,
        IReadOnlyList<string> added,
        IReadOnlyList<string> changed,
        IReadOnlyList<string> removed)
    {
        builder.AppendLine(title + "：");

        if (added.Count == 0 && changed.Count == 0 && removed.Count == 0)
        {
            builder.AppendLine("- 无差异");
            return;
        }

        foreach (var item in added)
        {
            builder.AppendLine("+ 新增: " + item);
        }

        foreach (var item in changed)
        {
            builder.AppendLine("~ 修改: " + item);
        }

        foreach (var item in removed)
        {
            builder.AppendLine("- 删除: " + item);
        }
    }
}
