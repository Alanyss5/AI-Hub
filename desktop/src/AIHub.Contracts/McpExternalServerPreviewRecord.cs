namespace AIHub.Contracts;

public sealed record McpExternalServerPreviewRecord(
    string Name,
    IReadOnlyList<McpExternalServerVariantRecord> Variants)
{
    public bool HasConflict => Variants
        .Select(variant => variant.Definition)
        .Distinct()
        .Skip(1)
        .Any();
}
