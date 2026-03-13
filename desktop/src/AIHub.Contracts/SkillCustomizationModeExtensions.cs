namespace AIHub.Contracts;

public static class SkillCustomizationModeExtensions
{
    public static string ToDisplayName(this SkillCustomizationMode mode)
    {
        return mode switch
        {
            SkillCustomizationMode.Managed => "托管",
            SkillCustomizationMode.Overlay => "覆盖层",
            SkillCustomizationMode.Fork => "Fork",
            SkillCustomizationMode.Local => "本地",
            _ => "未知"
        };
    }

    public static string ToDescription(this SkillCustomizationMode mode)
    {
        return mode switch
        {
            SkillCustomizationMode.Managed => "完全跟随上游，适合没有做本地修改的 Skill。",
            SkillCustomizationMode.Overlay => "以来源内容为基础，但允许保留你的本地覆盖改动。",
            SkillCustomizationMode.Fork => "保留来源信息，但后续默认按分叉后的本地版本维护。",
            SkillCustomizationMode.Local => "完全本地维护，不依赖任何上游来源。",
            _ => "未知模式。"
        };
    }
}