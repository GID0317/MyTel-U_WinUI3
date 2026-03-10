using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MyTelU_Launcher.ViewModels;

namespace MyTelU_Launcher.Views;

public sealed partial class ToolsFlyoutContent : UserControl
{
    public OpenCommunityToolsViewModel ViewModel { get; }

    public event EventHandler<string>? ToolInvoked;
    public event EventHandler? EditRequested;

    public ToolsFlyoutContent()
    {
        ViewModel = App.GetService<OpenCommunityToolsViewModel>();
        InitializeComponent();
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url)
        {
            ToolInvoked?.Invoke(this, url);
        }
    }

    private void EditToolsButton_Click(object sender, RoutedEventArgs e)
    {
        EditRequested?.Invoke(this, EventArgs.Empty);
    }
}