using System;
using Microsoft.UI.Xaml;
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
        var tool = GetToolFromSender(sender);
        
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

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        var tool = GetToolFromSender(sender);
        if (tool == null)
        {
            return;
        }

        Hide();

        var category = ViewModel.Tools.Contains(tool) ? "Tools" : "Community Tools";
        var updatedTool = await ShowToolEditorDialogAsync("Edit Tool", "Save", tool, category);

        if (updatedTool != null)
        {
            await ViewModel.UpdateToolAsync(tool, updatedTool);
        }

        await ShowAsync();
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        var tool = GetToolFromSender(sender);
        if (tool == null)
        {
            return;
        }

        if (ViewModel.Tools.Contains(tool))
        {
            ViewModel.DuplicateTool(tool, ViewModel.Tools);
        }
        else if (ViewModel.CommunityTools.Contains(tool))
        {
            ViewModel.DuplicateTool(tool, ViewModel.CommunityTools);
        }
    }

    private void MenuFlyout_Opening(object sender, object e)
    {
        if (sender is MenuFlyout flyout)
        {
            var moveUpItem = flyout.Items
                .OfType<MenuFlyoutItem>()
                .FirstOrDefault(item => string.Equals(item.Text, "Move up", StringComparison.Ordinal));
            var moveDownItem = flyout.Items
                .OfType<MenuFlyoutItem>()
                .FirstOrDefault(item => string.Equals(item.Text, "Move down", StringComparison.Ordinal));
            var tool = flyout.Items
                .OfType<MenuFlyoutItem>()
                .Select(item => item.Tag as ToolItem)
                .FirstOrDefault(item => item != null);

            if (tool != null)
            {
                if (ViewModel.Tools.Contains(tool))
                {
                    if (moveUpItem != null)
                    {
                        moveUpItem.IsEnabled = ViewModel.CanMoveUp(tool, ViewModel.Tools);
                    }

                    if (moveDownItem != null)
                    {
                        moveDownItem.IsEnabled = ViewModel.CanMoveDown(tool, ViewModel.Tools);
                    }
                }
                else if (ViewModel.CommunityTools.Contains(tool))
                {
                    if (moveUpItem != null)
                    {
                        moveUpItem.IsEnabled = ViewModel.CanMoveUp(tool, ViewModel.CommunityTools);
                    }

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

        var newTool = await ShowToolEditorDialogAsync("Add New Tool", "Add", null, "Tools");

        if (newTool != null)
        {
            ViewModel.AddTool(newTool, newTool.Category);
        }

        // Reopen this same dialog
        await sender.ShowAsync();
    }

    private ToolItem? GetToolFromSender(object sender)
    {
        if (sender is Button button)
        {
            return button.Tag as ToolItem;
        }

        if (sender is MenuFlyoutItem menuItem)
        {
            return menuItem.Tag as ToolItem;
        }

        return null;
    }

    private async Task<ToolItem?> ShowToolEditorDialogAsync(string title, string primaryButtonText, ToolItem? existingTool, string defaultCategory)
    {
        var editorDialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot
        };

        var accentService = App.GetService<MyTelU_Launcher.Services.AccentColorService>();
        accentService?.ApplyToContentDialog(editorDialog);

        var stack = new StackPanel { Spacing = 12 };

        var categoryCombo = new ComboBox
        {
            Header = "Category",
            PlaceholderText = "Select category",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        categoryCombo.Items.Add("Tools");
        categoryCombo.Items.Add("Community Tools");
        categoryCombo.SelectedItem = defaultCategory;
        stack.Children.Add(categoryCombo);

        var nameBox = new TextBox
        {
            Header = "Tool Name",
            PlaceholderText = "Enter tool name",
            Text = existingTool?.Name ?? string.Empty
        };
        stack.Children.Add(nameBox);

        var urlBox = new TextBox
        {
            Header = "URL",
            PlaceholderText = "https://example.com",
            Text = existingTool?.Url ?? string.Empty
        };
        stack.Children.Add(urlBox);

        var iconBox = new TextBox
        {
            Header = "Icon (Emoji)",
            PlaceholderText = "🔥",
            Text = string.IsNullOrWhiteSpace(existingTool?.Icon) ? "🔥" : existingTool.Icon
        };
        stack.Children.Add(iconBox);

        editorDialog.Content = stack;

        void UpdatePrimaryButtonState(object? _, object __)
        {
            editorDialog.IsPrimaryButtonEnabled =
                !string.IsNullOrWhiteSpace(nameBox.Text) &&
                !string.IsNullOrWhiteSpace(urlBox.Text);
        }

        nameBox.TextChanged += UpdatePrimaryButtonState;
        urlBox.TextChanged += UpdatePrimaryButtonState;
        UpdatePrimaryButtonState(null, EventArgs.Empty);

        var result = await editorDialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return new ToolItem
        {
            Name = nameBox.Text.Trim(),
            Url = urlBox.Text.Trim(),
            Icon = string.IsNullOrWhiteSpace(iconBox.Text) ? "🔥" : iconBox.Text.Trim(),
            Category = categoryCombo.SelectedItem?.ToString() == "Community Tools" ? "Community" : "Tools"
        };
    }
}
