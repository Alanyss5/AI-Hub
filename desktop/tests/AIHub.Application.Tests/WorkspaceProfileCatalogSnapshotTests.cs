using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Tests;

public sealed class WorkspaceProfileCatalogSnapshotTests
{
    [Fact]
    public void UsageSummary_Uses_Localized_Labels_And_Includes_Command_Agent_Counts()
    {
        var descriptor = new WorkspaceProfileDescriptor(
            new WorkspaceProfileRecord
            {
                Id = "data-ops",
                DisplayName = "Data Ops",
                SortOrder = 1,
                IsBuiltin = false,
                IsDeletable = true
            },
            ProjectCount: 1,
            SkillSourceCount: 2,
            SkillInstallCount: 3,
            SkillStateCount: 4,
            SkillDirectoryCount: 5,
            McpServerCount: 6,
            SettingsCount: 7,
            CommandAssetCount: 8,
            AgentAssetCount: 9);

        Assert.Equal("\u9879\u76ee 1 / \u6765\u6e90 2 / \u5b89\u88c5 3 / \u72b6\u6001 4 / \u76ee\u5f55 5 / MCP 6 / \u8bbe\u7f6e 7 / commands 8 / agents 9", descriptor.UsageSummary);
    }
}
