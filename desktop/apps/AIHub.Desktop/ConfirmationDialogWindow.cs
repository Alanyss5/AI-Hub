using AIHub.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AIHub.Desktop;

public sealed class ConfirmationDialogWindow : Window
{
    public ConfirmationDialogWindow(ConfirmationRequest request)
    {
        Title = request.Title;
        Width = 560;
        Height = 420;
        MinWidth = 480;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ResolveBrush("WindowBackgroundBrush", "#0B1020");

        var detailBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(request.Details) ? request.Message : request.Details,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 180,
            Background = ResolveBrush("SurfaceBrush", "#111A2E")
        };

        var cancelButton = new Button
        {
            Content = request.CancelText,
            MinWidth = 96,
            Background = ResolveBrush("NeutralButtonBrush", "#18233A"),
            Foreground = ResolveBrush("TextPrimaryBrush", "#EAF0FF")
        };
        cancelButton.Click += (_, _) => Close(false);

        var confirmButton = new Button
        {
            Content = request.ConfirmText,
            MinWidth = 120,
            Background = ResolveBrush(request.IsDangerous ? "DangerBrush" : "SuccessBrush", request.IsDangerous ? "#F87171" : "#34D399"),
            Foreground = ResolveBrush("AccentForegroundBrush", "#08101E")
        };
        confirmButton.Click += (_, _) => Close(true);

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
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ResolveBrush("TextPrimaryBrush", "#EAF0FF")
                    },
                    new TextBlock
                    {
                        [Grid.RowProperty] = 1,
                        Text = request.Message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ResolveBrush("TextSecondaryBrush", "#A8B3C7")
                    },
                    new Border
                    {
                        [Grid.RowProperty] = 2,
                        Background = ResolveBrush("SurfaceBrush", "#111A2E"),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(12),
                        Child = detailBox
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

    private static IBrush ResolveBrush(string key, string fallback)
    {
        if (Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallback));
    }
}
