using System;
using Microsoft.UI.Xaml.Controls;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Views;

public sealed partial class ManageToolsDialog : ContentDialog
{
    public OpenCommunityToolsViewModel ViewModel { get; }

    public ManageToolsDialog()
    {
        ViewModel = App.GetService<OpenCommunityToolsViewModel>();
        this.InitializeComponent();
        
        // Apply dynamic accent colors to fix ContentDialog button hover states
        var accentService = App.GetService<MyTelU_Launcher.Services.AccentColorService>();
        accentService?.ApplyToContentDialog(this);
    }

    private void MoveUpButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ToolItem? tool = null;
        
        if (sender is Button button)
        {
            tool = button.Tag as ToolItem;
        }
        else if (sender is MenuFlyoutItem menuItem)
        {
            tool = menuItem.Tag as ToolItem;
        }
        
        if (tool != null)
        {
            // Determine which collection this tool belongs to
            if (ViewModel.Tools.Contains(tool))
            {
                ViewModel.MoveToolUp(tool, ViewModel.Tools);
            }
            else if (ViewModel.CommunityTools.Contains(tool))
            {
                ViewModel.MoveToolUp(tool, ViewModel.CommunityTools);
            }
        }
    }

    private void MoveDownButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ToolItem? tool = null;
        
        if (sender is Button button)
        {
            tool = button.Tag as ToolItem;
        }
        else if (sender is MenuFlyoutItem menuItem)
        {
            tool = menuItem.Tag as ToolItem;
        }
        
        if (tool != null)
        {
            // Determine which collection this tool belongs to
            if (ViewModel.Tools.Contains(tool))
            {
                ViewModel.MoveToolDown(tool, ViewModel.Tools);
            }
            else if (ViewModel.CommunityTools.Contains(tool))
            {
                ViewModel.MoveToolDown(tool, ViewModel.CommunityTools);
            }
        }
    }

    private void RemoveButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ToolItem? tool = null;
        
        if (sender is Button button)
        {
            tool = button.Tag as ToolItem;
        }
        else if (sender is MenuFlyoutItem menuItem)
        {
            tool = menuItem.Tag as ToolItem;
        }
        
        if (tool != null)
        {
            // Determine which collection this tool belongs to
            if (ViewModel.Tools.Contains(tool))
            {
                ViewModel.RemoveTool(tool, ViewModel.Tools);
            }
            else if (ViewModel.CommunityTools.Contains(tool))
            {
                ViewModel.RemoveTool(tool, ViewModel.CommunityTools);
            }
        }
    }

    private void MenuFlyout_Opening(object sender, object e)
    {
        if (sender is MenuFlyout flyout && flyout.Items.Count >= 2)
        {
            // Get the move up and move down items (first two items in the flyout)
            var moveUpItem = flyout.Items[0] as MenuFlyoutItem;
            var moveDownItem = flyout.Items[1] as MenuFlyoutItem;

            if (moveUpItem?.Tag is ToolItem tool)
            {
                // Determine which collection this tool belongs to
                if (ViewModel.Tools.Contains(tool))
                {
                    moveUpItem.IsEnabled = ViewModel.CanMoveUp(tool, ViewModel.Tools);
                    if (moveDownItem != null)
                    {
                        moveDownItem.IsEnabled = ViewModel.CanMoveDown(tool, ViewModel.Tools);
                    }
                }
                else if (ViewModel.CommunityTools.Contains(tool))
                {
                    moveUpItem.IsEnabled = ViewModel.CanMoveUp(tool, ViewModel.CommunityTools);
                    if (moveDownItem != null)
                    {
                        moveDownItem.IsEnabled = ViewModel.CanMoveDown(tool, ViewModel.CommunityTools);
                    }
                }
            }
        }
    }

    private async void BrowseButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Defer the dialog close so we can show another dialog
        args.Cancel = true;
        
        // Close this dialog temporarily
        sender.Hide();

        var addDialog = new ContentDialog
        {
            Title = "Add New Tool",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        // Apply dynamic accent colors to fix ContentDialog button hover states
        var accentService = App.GetService<MyTelU_Launcher.Services.AccentColorService>();
        accentService?.ApplyToContentDialog(addDialog);

        var stack = new StackPanel { Spacing = 12 };

        var categoryCombo = new ComboBox
        {
            Header = "Category",
            PlaceholderText = "Select category",
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch
        };
        categoryCombo.Items.Add("Tools");
        categoryCombo.Items.Add("Community Tools");
        categoryCombo.SelectedIndex = 0;
        stack.Children.Add(categoryCombo);

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
            Header = "Icon (Emoji)",
            PlaceholderText = "🔥",
            Text = "🔥"
        };
        stack.Children.Add(iconBox);

        addDialog.Content = stack;

        var result = await addDialog.ShowAsync();

        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            var newTool = new ToolItem
            {
                Name = nameBox.Text,
                Url = urlBox.Text,
                Icon = iconBox.Text,
                Category = categoryCombo.SelectedItem?.ToString() ?? "Tools"
            };

            ViewModel.AddTool(newTool, newTool.Category);
        }

        // Reopen this same dialog
        await sender.ShowAsync();
    }
}
