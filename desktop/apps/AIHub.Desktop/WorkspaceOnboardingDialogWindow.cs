using AIHub.Contracts;
using AIHub.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AIHub.Desktop;

public sealed class WorkspaceOnboardingDialogWindow : Window
{
    private static readonly WorkspaceImportOption[] ImportOptions =
    [
        new(WorkspaceImportTargetKind.AIHub, "导入到 AI-Hub"),
        new(WorkspaceImportTargetKind.Private, "导入到私人目录"),
        new(WorkspaceImportTargetKind.Ignore, "忽略")
    ];

    public WorkspaceOnboardingDialogWindow(WorkspaceOnboardingDialogRequest request)
    {
        Title = request.Title;
        Width = 920;
        Height = 720;
        MinWidth = 760;
        MinHeight = 560;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ResolveBrush("WindowBackgroundBrush", "#0B1020");

        var selectors = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
        var candidatePanel = new StackPanel { Spacing = 12 };

        foreach (var candidate in request.Preview.Candidates)
        {
            var options = ImportOptions
                .Select(option => option with { })
                .ToArray();
            var selected = options.First(option => option.Target == candidate.SuggestedTarget);
            var selector = new ComboBox
            {
                ItemsSource = options,
                SelectedItem = selected,
                MinWidth = 180
            };
            selectors[candidate.Id] = selector;

            candidatePanel.Children.Add(new Border
            {
                Background = ResolveBrush("SurfaceBrush", "#111A2E"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{GetResourceDisplayName(candidate.ResourceKind)} / {candidate.DisplayName}",
                            FontWeight = FontWeight.SemiBold,
                            FontSize = 15
                        },
                        new TextBlock
                        {
                            Text = "来源：" + candidate.SourcePath,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = ResolveBrush("TextSecondaryBrush", "#A8B3C7")
                        },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,*"),
                            ColumnSpacing = 12,
                            Children =
                            {
                                CreateTargetPreview("AI-Hub", candidate.CompanyDestinationPath, candidate.CompanyDestinationExists),
                                CreateTargetPreview("私人目录", candidate.PrivateDestinationPath, candidate.PrivateDestinationExists).WithGridColumn(1)
                            }
                        },
                        selector,
                        new TextBox
                        {
                            Text = string.IsNullOrWhiteSpace(candidate.SourceDetails) ? "无额外预览。" : candidate.SourceDetails,
                            IsReadOnly = true,
                            AcceptsReturn = true,
                            TextWrapping = TextWrapping.Wrap,
                            MinHeight = 88,
                            Background = ResolveBrush("SurfaceAltBrush", "#162238")
                        }
                    }
                }
            });
        }

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 96,
            Background = ResolveBrush("NeutralButtonBrush", "#18233A"),
            Foreground = ResolveBrush("TextPrimaryBrush", "#EAF0FF")
        };
        cancelButton.Click += (_, _) => Close(null);

        var confirmButton = new Button
        {
            Content = "开始导入",
            MinWidth = 120,
            Background = ResolveBrush("SuccessBrush", "#34D399"),
            Foreground = ResolveBrush("AccentForegroundBrush", "#08101E")
        };
        confirmButton.Click += (_, _) =>
        {
            var decisions = selectors
                .Select(item => new WorkspaceImportDecisionRecord(
                    item.Key,
                    (item.Value.SelectedItem as WorkspaceImportOption)?.Target ?? WorkspaceImportTargetKind.AIHub))
                .ToArray();
            Close(new WorkspaceOnboardingDialogResult(decisions));
        };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                RowSpacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = request.Title,
                        FontSize = 24,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ResolveBrush("TextPrimaryBrush", "#EAF0FF")
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 1,
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = request.Message,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = ResolveBrush("TextSecondaryBrush", "#A8B3C7")
                            },
                            new TextBlock
                            {
                                Text = request.Preview.Summary,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = ResolveBrush("WarningBrush", "#FBBF24")
                            }
                        }
                    },
                    new ScrollViewer
                    {
                        [Grid.RowProperty] = 2,
                        Content = candidatePanel,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 3,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 12,
                        Children =
                        {
                            cancelButton,
                            confirmButton
                        }
                    }
                }
            }
        };
    }

    private static Border CreateTargetPreview(string title, string path, bool exists)
    {
        return new Border
        {
            Background = ResolveBrush("SurfaceAltBrush", "#162238"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = path,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ResolveBrush("TextSecondaryBrush", "#A8B3C7")
                    },
                    new TextBlock
                    {
                        Text = exists ? "目标已存在，导入时会先备份。" : "目标为空，可直接导入。",
                        Foreground = ResolveBrush(exists ? "WarningBrush" : "SuccessBrush", exists ? "#FBBF24" : "#34D399")
                    }
                }
            }
        };
    }

    private static IBrush ResolveBrush(string key, string fallback)
    {
        if (Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallback));
    }
    private static string GetResourceDisplayName(WorkspaceOnboardingResourceKind resourceKind)
    {
        return resourceKind switch
        {
            WorkspaceOnboardingResourceKind.Skill => "Skills",
            WorkspaceOnboardingResourceKind.ClaudeCommand => "commands",
            WorkspaceOnboardingResourceKind.ClaudeAgent => "agents",
            WorkspaceOnboardingResourceKind.ClaudeSettings => "Claude settings",
            WorkspaceOnboardingResourceKind.McpServer => "MCP",
            _ => resourceKind.ToString()
        };
    }

    private sealed record WorkspaceImportOption(WorkspaceImportTargetKind Target, string Label)
    {
        public override string ToString() => Label;
    }
}

internal static class ControlGridExtensions
{
    public static T WithGridColumn<T>(this T control, int column)
        where T : Control
    {
        control.SetValue(Grid.ColumnProperty, column);
        return control;
    }
}
