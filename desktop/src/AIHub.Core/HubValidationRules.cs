namespace AIHub.Core;

public static class HubValidationRules
{
    public static HubValidationResult Validate(bool hasHubMarker, IReadOnlyCollection<string> topLevelDirectories)
    {
        var errors = new List<string>();

        if (!hasHubMarker)
        {
            errors.Add("缺少 hub.json 标记文件。");
        }

        foreach (var requiredDirectory in HubLayout.RequiredDirectories)
        {
            if (!topLevelDirectories.Contains(requiredDirectory, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"缺少必需目录：{requiredDirectory}");
            }
        }

        return new HubValidationResult(errors.Count == 0, errors);
    }
}
