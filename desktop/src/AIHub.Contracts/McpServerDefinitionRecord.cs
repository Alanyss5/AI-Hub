namespace AIHub.Contracts;

public sealed record McpServerDefinitionRecord(
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    public string CommandLine => Arguments.Count == 0
        ? Command
        : Command + " " + string.Join(" ", Arguments);
}
