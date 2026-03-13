using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class McpExternalServerImportItem : ObservableObject
{
    private bool _isSelected = true;
    private McpExternalServerVariantOption? _selectedVariantOption;

    public McpExternalServerImportItem(McpExternalServerPreviewRecord preview)
    {
        Preview = preview;
        VariantOptions = preview.Variants
            .Select(variant => new McpExternalServerVariantOption(variant))
            .ToArray();
        _selectedVariantOption = VariantOptions.FirstOrDefault();
    }

    public McpExternalServerPreviewRecord Preview { get; }

    public IReadOnlyList<McpExternalServerVariantOption> VariantOptions { get; }

    public string Name => Preview.Name;

    public string ConflictDisplay => Preview.HasConflict ? "存在跨客户端冲突" : "定义一致";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public McpExternalServerVariantOption? SelectedVariantOption
    {
        get => _selectedVariantOption;
        set => SetProperty(ref _selectedVariantOption, value);
    }

    public McpExternalServerImportDecision? BuildDecision()
    {
        if (!IsSelected || SelectedVariantOption is null)
        {
            return null;
        }

        return new McpExternalServerImportDecision(Name, SelectedVariantOption.Variant.Client);
    }
}
