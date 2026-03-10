using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Views;

public sealed partial class OpenCommunityToolsPage : Page
{
    public OpenCommunityToolsViewModel ViewModel
    {
        get;
    }

    public OpenCommunityToolsPage()
    {
        ViewModel = App.GetService<OpenCommunityToolsViewModel>();
        InitializeComponent();
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // Handle error
                Debug.WriteLine($"Error opening URL: {ex.Message}");
            }
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Manage Tools",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        };

        // Apply dynamic accent colors to fix ContentDialog button hover states
        var accentService = App.GetService<MyTelU_Launcher.Services.AccentColorService>();
        accentService?.ApplyToContentDialog(dialog);

        var scrollViewer = new ScrollViewer
        {
            Height = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var mainStack = new StackPanel
        {
            Spacing = 20
        };

        var toolsSection = CreateEditableSection("Tools", ViewModel.Tools);
        mainStack.Children.Add(toolsSection);

        var communityToolsSection = CreateEditableSection("Community Tools", ViewModel.CommunityTools);
        mainStack.Children.Add(communityToolsSection);

        scrollViewer.Content = mainStack;
        dialog.Content = scrollViewer;

        await dialog.ShowAsync();
    }

    private StackPanel CreateEditableSection(string title, ObservableCollection<ToolItem> collection)
    {
        var section = new StackPanel
        {
            Spacing = 8
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        section.Children.Add(titleBlock);

        var itemsList = new ItemsControl
        {
            ItemsSource = collection
        };

        var itemTemplate = new DataTemplate();
        itemsList.ItemTemplate = itemTemplate;

        foreach (var tool in collection)
        {
            var itemCard = CreateToolItemCard(tool, collection);
            section.Children.Add(itemCard);
        }

        var addButton = new Button
        {
            Content = "Add Tool",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0)
        };
        addButton.Click += (s, e) => AddToolButton_Click(title, collection);
        section.Children.Add(addButton);

        return section;
    }

    private Border CreateToolItemCard(ToolItem tool, ObservableCollection<ToolItem> collection)
    {
        var card = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Tool Info
        var infoStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };
        Grid.SetColumn(infoStack, 0);

        var iconBorder = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentAAFillColorDefaultBrush"],
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6)
        };
        var icon = new FontIcon
        {
            Glyph = tool.Icon,
            FontSize = 16
        };
        iconBorder.Child = icon;
        infoStack.Children.Add(iconBorder);

        var nameBlock = new TextBlock
        {
            Text = tool.Name,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };
        infoStack.Children.Add(nameBlock);

        grid.Children.Add(infoStack);

        // Action Buttons
        var actionsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };
        Grid.SetColumn(actionsStack, 1);

        // Move Up Button
        var moveUpButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE70E", FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0)
        };
        ToolTipService.SetToolTip(moveUpButton, "Move up");
        moveUpButton.Click += (s, e) =>
        {
            ViewModel.MoveToolUp(tool, collection);
            RefreshDialog();
        };
        actionsStack.Children.Add(moveUpButton);

        // Move Down Button
        var moveDownButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE70D", FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0)
        };
        ToolTipService.SetToolTip(moveDownButton, "Move down");
        moveDownButton.Click += (s, e) =>
        {
            ViewModel.MoveToolDown(tool, collection);
            RefreshDialog();
        };
        actionsStack.Children.Add(moveDownButton);

        // Remove Button
        var removeButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        ToolTipService.SetToolTip(removeButton, "Remove");
        removeButton.Click += (s, e) =>
        {
            ViewModel.RemoveTool(tool, collection);
            RefreshDialog();
        };
        actionsStack.Children.Add(removeButton);

        grid.Children.Add(actionsStack);
        card.Child = grid;

        return card;
    }

    private async void AddToolButton_Click(string category, ObservableCollection<ToolItem> collection)
    {
        var dialog = new ContentDialog
        {
            Title = "Add New Tool",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        // Apply dynamic accent colors to fix ContentDialog button hover states
        var accentService = App.GetService<MyTelU_Launcher.Services.AccentColorService>();
        accentService?.ApplyToContentDialog(dialog);

        var stack = new StackPanel { Spacing = 12 };

        var nameBox = new TextBox
        {
            Header = "Tool Name",
            PlaceholderText = "Enter tool name"
        };
        stack.Children.Add(nameBox);

        var urlBox = new TextBox
        {
            Header = "URL",
            PlaceholderText = "https://example.com"
        };
        stack.Children.Add(urlBox);

        var iconBox = new TextBox
        {
            Header = "Icon (Unicode)",
            PlaceholderText = "\uE8F1",
            Text = "\uE8F1"
        };
        stack.Children.Add(iconBox);

        dialog.Content = stack;

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            var newTool = new ToolItem
            {
                Name = nameBox.Text,
                Url = urlBox.Text,
                Icon = iconBox.Text,
                Category = category
            };

            collection.Add(newTool);
            RefreshDialog();
        }
    }

    private void RefreshDialog()
    {
        // Close and reopen dialog to refresh the list
        // This is a simple approach; in production, you'd use proper data binding
    }
}
