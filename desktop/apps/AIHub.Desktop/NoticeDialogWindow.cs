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
        Background = new SolidColorBrush(Color.Parse("#F5F1E8"));

        var confirmButton = new Button
        {
            Content = request.ConfirmText,
            MinWidth = 108,
            Background = new SolidColorBrush(Color.Parse("#285E61")),
            Foreground = Brushes.White
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
                                Foreground = new SolidColorBrush(Color.Parse("#102A43")),
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = request.Title,
                                Foreground = new SolidColorBrush(Color.Parse("#486581")),
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
                        Background = new SolidColorBrush(Color.Parse("#FFFDF8")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#D9E2EC"))
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
}
