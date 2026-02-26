using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Helpers;

namespace MyTelU_Launcher.ViewModels;

public partial class OpenCommunityToolsViewModel : ObservableRecipient
{
    private readonly ILocalSettingsService _localSettingsService;
    private const string ToolsSettingsKey = "SavedTools";
    private const string CommunityToolsSettingsKey = "SavedCommunityTools";
    private const string DeletedToolsSettingsKey = "DeletedDefaultTools";

    public ObservableCollection<ToolItem> Tools { get; } = new();
    public ObservableCollection<ToolItem> CommunityTools { get; } = new();

    public OpenCommunityToolsViewModel(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _ = LoadToolsAsync();

        // Register for reset messages
        WeakReferenceMessenger.Default.Register<ToolsResetMessage>(this, (r, m) =>
        {
            _ = ResetToolsAsync();
        });
    }

    private async Task ResetToolsAsync()
    {
        // Clear saved settings
        await _localSettingsService.SaveSettingAsync<List<ToolItem>>(ToolsSettingsKey, null);
        await _localSettingsService.SaveSettingAsync<List<ToolItem>>(CommunityToolsSettingsKey, null);
        await _localSettingsService.SaveSettingAsync<List<string>>(DeletedToolsSettingsKey, null);

        // Reload (which will load defaults since settings are null)
        await LoadToolsAsync();
    }

    private async Task LoadToolsAsync()
    {
        var savedTools = await _localSettingsService.ReadSettingAsync<List<ToolItem>>(ToolsSettingsKey);
        var savedCommunityTools = await _localSettingsService.ReadSettingAsync<List<ToolItem>>(CommunityToolsSettingsKey);
        var deletedTools = await _localSettingsService.ReadSettingAsync<List<string>>(DeletedToolsSettingsKey) ?? new List<string>();

        // Load default tools lists (what should be there)
        var defaultTools = GetDefaultTools();
        var defaultCommunityTools = GetDefaultCommunityTools();

        // Clear existing lists to avoid duplication if called multiple times (e.g. during reset)
        Tools.Clear();
        CommunityTools.Clear();

        if (savedTools != null && savedTools.Count > 0)
        {
            // Add existing saved tools
            foreach (var tool in savedTools)
            {
                Tools.Add(tool);
            }

            // Check for any NEW default tools that aren't in the saved list AND haven't been deleted
            foreach (var defTool in defaultTools)
            {
                if (!Tools.Any(t => t.Url == defTool.Url) && !deletedTools.Contains(defTool.Url))
                {
                    Tools.Add(defTool);
                }
            }
        }
        else
        {
            // First run or no saved tools
            // Load defaults unless they were explicitly deleted (edge case where user deletes all but data cleared?)
            // Actually, if savedTools is null/empty, we should respect deletedTools if present (unlikely on fresh install, but possible on clear)
            foreach (var tool in defaultTools)
            {
                if (!deletedTools.Contains(tool.Url))
                {
                    Tools.Add(tool);
                }
            }
        }

        if (savedCommunityTools != null && savedCommunityTools.Count > 0)
        {
            foreach (var tool in savedCommunityTools)
            {
                CommunityTools.Add(tool);
            }

            // Check for any NEW community tools that aren't in the saved list AND haven't been deleted
            foreach (var defTool in defaultCommunityTools)
            {
                if (!CommunityTools.Any(t => t.Url == defTool.Url) && !deletedTools.Contains(defTool.Url))
                {
                    CommunityTools.Add(defTool);
                }
            }
        }
        else
        {
            foreach (var tool in defaultCommunityTools)
            {
                if (!deletedTools.Contains(tool.Url))
                {
                    CommunityTools.Add(tool);
                }
            }
        }

        // Save immediately to persist any new defaults added
        SaveToolsAsync();
        SaveCommunityToolsAsync();

        // Subscribe to collection changes to save automatically when reordered
        Tools.CollectionChanged += (s, e) => SaveToolsAsync();
        CommunityTools.CollectionChanged += (s, e) => SaveCommunityToolsAsync();
    }

    private List<ToolItem> GetDefaultTools()
    {
        return new List<ToolItem>
        {
            new ToolItem { Name = "Lab Fisika Dasar", Icon = "\U0001F680", Url = "https://labfisdas-telu.com/", Category = "Tools" },
            new ToolItem { Name = "SEA Lab", Icon = "💻", Url = "https://sealab-telu.com/", Category = "Tools" }
        };
    }

    private List<ToolItem> GetDefaultCommunityTools()
    {
        return new List<ToolItem>
        {
            new ToolItem { Name = "Jeey college files", Icon = "😂", Url = "https://ac.jeyy.xyz/files/", Category = "Community" },
            new ToolItem { Name = "Regresi Linear", Icon = "🧮", Url = "https://regresi.msatrio.com/", Category = "Community" }
        };
    }

    private async void SaveToolsAsync()
    {
        await _localSettingsService.SaveSettingAsync(ToolsSettingsKey, Tools.ToList());
    }

    private async void SaveCommunityToolsAsync()
    {
        await _localSettingsService.SaveSettingAsync(CommunityToolsSettingsKey, CommunityTools.ToList());
    }

    public void MoveToolUp(ToolItem tool, ObservableCollection<ToolItem> collection)
    {
        var index = collection.IndexOf(tool);
        if (index > 0)
        {
            collection.Move(index, index - 1);
        }
    }

    public void MoveToolDown(ToolItem tool, ObservableCollection<ToolItem> collection)
    {
        var index = collection.IndexOf(tool);
        if (index < collection.Count - 1)
        {
            collection.Move(index, index + 1);
        }
    }

    public async void RemoveTool(ToolItem tool, ObservableCollection<ToolItem> collection)
    {
        collection.Remove(tool);

        // Check if the removed tool was a default one
        var defaultTools = GetDefaultTools();
        var defaultCommunityTools = GetDefaultCommunityTools();

        bool isDefault = defaultTools.Any(t => t.Url == tool.Url) || defaultCommunityTools.Any(t => t.Url == tool.Url);

        if (isDefault)
        {
            var deletedTools = await _localSettingsService.ReadSettingAsync<List<string>>(DeletedToolsSettingsKey) ?? new List<string>();
            if (!deletedTools.Contains(tool.Url))
            {
                deletedTools.Add(tool.Url);
                await _localSettingsService.SaveSettingAsync(DeletedToolsSettingsKey, deletedTools);
            }
        }
    }

    public void AddTool(ToolItem tool, string category)
    {
        if (category == "Tools")
        {
            Tools.Add(tool);
        }
        else
        {
            CommunityTools.Add(tool);
        }
    }

    public bool CanMoveUp(ToolItem tool, ObservableCollection<ToolItem> collection)
    {
        var index = collection.IndexOf(tool);
        return index > 0;
    }

    public bool CanMoveDown(ToolItem tool, ObservableCollection<ToolItem> collection)
    {
        var index = collection.IndexOf(tool);
        return index >= 0 && index < collection.Count - 1;
    }
}
