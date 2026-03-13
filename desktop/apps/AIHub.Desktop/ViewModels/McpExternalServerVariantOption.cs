using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class McpExternalServerVariantOption
{
    public McpExternalServerVariantOption(McpExternalServerVariantRecord variant)
    {
        Variant = variant;
        DisplayName = $"{variant.Client} / {variant.Definition.CommandLine}";
    }

    public McpExternalServerVariantRecord Variant { get; }

    public string DisplayName { get; }
}
