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
        Background = new SolidColorBrush(Color.Parse("#F5F1E8"));

        var detailBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(request.Details) ? request.Message : request.Details,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 180,
            Background = new SolidColorBrush(Color.Parse("#FFFDF8"))
        };

        var cancelButton = new Button
        {
            Content = request.CancelText,
            MinWidth = 96,
            Background = new SolidColorBrush(Color.Parse("#D9E2EC")),
            Foreground = new SolidColorBrush(Color.Parse("#102A43"))
        };
        cancelButton.Click += (_, _) => Close(false);

        var confirmButton = new Button
        {
            Content = request.ConfirmText,
            MinWidth = 120,
            Background = new SolidColorBrush(Color.Parse(request.IsDangerous ? "#B44D12" : "#2F855A")),
            Foreground = Brushes.White
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
                        Foreground = new SolidColorBrush(Color.Parse("#102A43"))
                    },
                    new TextBlock
                    {
                        [Grid.RowProperty] = 1,
                        Text = request.Message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#486581"))
                    },
                    new Border
                    {
                        [Grid.RowProperty] = 2,
                        Background = new SolidColorBrush(Color.Parse("#FFFDF8")),
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
}
