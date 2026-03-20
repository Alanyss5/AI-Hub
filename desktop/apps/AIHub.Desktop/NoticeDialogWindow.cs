using AIHub.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AIHub.Desktop;

public sealed class NoticeDialogWindow : Window
{
    public NoticeDialogWindow(NoticeDialogRequest request)
    {
        Title = request.Title;
        Width = 700;
        Height = 420;
        MinWidth = 560;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ResolveBrush("WindowBackgroundBrush", "#0B1020");

        var confirmButton = new Button
        {
            Content = request.ConfirmText,
            MinWidth = 108,
            Background = ResolveBrush("InfoBrush", "#38BDF8"),
            Foreground = ResolveBrush("AccentForegroundBrush", "#08101E")
        };
        confirmButton.Click += (_, _) => Close();

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 14,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = request.Message,
                                FontSize = 20,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = ResolveBrush("TextPrimaryBrush", "#EAF0FF"),
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = request.Title,
                                Foreground = ResolveBrush("TextSecondaryBrush", "#A8B3C7"),
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    },
                    new TextBox
                    {
                        [Grid.RowProperty] = 1,
                        Text = request.Details,
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        Background = ResolveBrush("SurfaceBrush", "#111A2E"),
                        BorderBrush = ResolveBrush("OutlineBrush", "#32415E")
                    },
                    new StackPanel
                    {
                        [Grid.RowProperty] = 2,
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
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
