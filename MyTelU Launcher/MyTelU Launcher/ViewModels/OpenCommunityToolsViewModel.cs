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
    private static readonly IReadOnlyList<ToolItem> DefaultTools =
    [
        new ToolItem { Name = "SEE Lab", Icon = "🤖", Url = "https://see.labs.telkomuniversity.ac.id/home", Category = "Tools" },
        new ToolItem { Name = "Lab Fisika Dasar", Icon = "🚀", Url = "https://labfisdas-telu.com/", Category = "Tools" },
        new ToolItem { Name = "SEA Lab", Icon = "💻", Url = "https://sealab-telu.com/", Category = "Tools" },
        new ToolItem { Name = "Evconn Lab", Icon = "🛜", Url = "https://labevconn.com/", Category = "Tools" },
        new ToolItem { Name = "SECULAB", Icon = "🔐", Url = "https://seculab-telu.cloud/", Category = "Tools" },
        new ToolItem { Name = "KEPOKAPE", Icon = "👷", Url = "https://kepokape.id/", Category = "Tools" }
    ];
    private static readonly IReadOnlyList<ToolItem> DefaultCommunityTools =
    [
        new ToolItem { Name = "Jeey college files", Icon = "😂", Url = "https://ac.jeyy.xyz/files/", Category = "Community" },
        new ToolItem { Name = "Regresi Linear", Icon = "🧮", Url = "https://regresi.msatrio.com/", Category = "Community" }
    ];

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
        var deletedTools = await _localSettingsService.ReadSettingAsync<List<string>>(DeletedToolsSettingsKey) ?? [];
        var deletedToolsSet = new HashSet<string>(deletedTools, StringComparer.OrdinalIgnoreCase);
        var hasToolChanges = false;
        var hasCommunityToolChanges = false;

        // Unsubscribe existing event handlers if they exist to prevent memory leaks on reload
        Tools.CollectionChanged -= Tools_CollectionChanged;
        CommunityTools.CollectionChanged -= CommunityTools_CollectionChanged;

        // Clear existing lists to avoid duplication if called multiple times (e.g. during reset)
        Tools.Clear();
        CommunityTools.Clear();

        if (savedTools != null && savedTools.Count > 0)
        {
            var savedToolUrls = new HashSet<string>(savedTools.Select(tool => tool.Url), StringComparer.OrdinalIgnoreCase);

            // Add existing saved tools
            foreach (var tool in savedTools)
            {
                Tools.Add(tool);
            }

            // Check for any NEW default tools that aren't in the saved list AND haven't been deleted
            for (int defIndex = 0; defIndex < DefaultTools.Count; defIndex++)
            {
                var defTool = DefaultTools[defIndex];
                if (!savedToolUrls.Contains(defTool.Url) && !deletedToolsSet.Contains(defTool.Url))
                {
                    // Insert at correct position based on defaultTools order
                    int insertAt = 0;
                    for (int i = 0; i < defIndex; i++)
                    {
                        if (savedToolUrls.Contains(DefaultTools[i].Url))
                            insertAt++;
                    }
                    Tools.Insert(insertAt, defTool);
                    savedToolUrls.Add(defTool.Url);
                    hasToolChanges = true;
                }
            }
        }
        else
        {
            // First run or no saved tools
            // Load defaults unless they were explicitly deleted (edge case where user deletes all but data cleared?)
            // Actually, if savedTools is null/empty, we should respect deletedTools if present (unlikely on fresh install, but possible on clear)
            foreach (var tool in DefaultTools)
            {
                if (!deletedToolsSet.Contains(tool.Url))
                {
                    Tools.Add(tool);
                }
            }
        }

        if (savedCommunityTools != null && savedCommunityTools.Count > 0)
        {
            var savedCommunityToolUrls = new HashSet<string>(savedCommunityTools.Select(tool => tool.Url), StringComparer.OrdinalIgnoreCase);

            foreach (var tool in savedCommunityTools)
            {
                CommunityTools.Add(tool);
            }

            // Check for any NEW community tools that aren't in the saved list AND haven't been deleted
            foreach (var defTool in DefaultCommunityTools)
            {
                if (!savedCommunityToolUrls.Contains(defTool.Url) && !deletedToolsSet.Contains(defTool.Url))
                {
                    CommunityTools.Add(defTool);
                    savedCommunityToolUrls.Add(defTool.Url);
                    hasCommunityToolChanges = true;
                }
            }
        }
        else
        {
            foreach (var tool in DefaultCommunityTools)
            {
                if (!deletedToolsSet.Contains(tool.Url))
                {
                    CommunityTools.Add(tool);
                }
            }
        }

        if (hasToolChanges)
        {
            SaveToolsAsync();
        }

        if (hasCommunityToolChanges)
        {
            SaveCommunityToolsAsync();
        }

        // Subscribe to collection changes to save automatically when reordered
        Tools.CollectionChanged += Tools_CollectionChanged;
        CommunityTools.CollectionChanged += CommunityTools_CollectionChanged;
    }

    private void Tools_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => SaveToolsAsync();
    
    private void CommunityTools_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => SaveCommunityToolsAsync();

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
        bool isDefault = DefaultTools.Any(t => t.Url == tool.Url) || DefaultCommunityTools.Any(t => t.Url == tool.Url);

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

    public async Task UpdateToolAsync(ToolItem originalTool, ToolItem updatedTool)
    {
        var originalCollection = Tools.Contains(originalTool) ? Tools : CommunityTools.Contains(originalTool) ? CommunityTools : null;
        if (originalCollection == null)
        {
            return;
        }

        var targetCollection = updatedTool.Category == "Tools" ? Tools : CommunityTools;
        var originalIndex = originalCollection.IndexOf(originalTool);

        var shouldMarkOriginalDefaultAsDeleted = IsDefaultTool(originalTool) &&
            (!string.Equals(originalTool.Url, updatedTool.Url, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(originalTool.Category, updatedTool.Category, StringComparison.OrdinalIgnoreCase));

        if (shouldMarkOriginalDefaultAsDeleted)
        {
            await EnsureDeletedDefaultToolAsync(originalTool.Url);
        }

        if (ReferenceEquals(originalCollection, targetCollection))
        {
            if (originalIndex >= 0)
            {
                originalCollection[originalIndex] = updatedTool;
            }
            return;
        }

        if (originalIndex >= 0)
        {
            originalCollection.RemoveAt(originalIndex);
        }

        targetCollection.Add(updatedTool);
    }

    public void DuplicateTool(ToolItem tool, ObservableCollection<ToolItem> collection)
    {
        var index = collection.IndexOf(tool);
        if (index < 0)
        {
            return;
        }

        collection.Insert(index + 1, new ToolItem
        {
            Name = tool.Name,
            Icon = tool.Icon,
            Url = tool.Url,
            Category = tool.Category
        });
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

    private static bool IsDefaultTool(ToolItem tool)
    {
        return DefaultTools.Any(t => t.Url == tool.Url) || DefaultCommunityTools.Any(t => t.Url == tool.Url);
    }

    private async Task EnsureDeletedDefaultToolAsync(string toolUrl)
    {
        var deletedTools = await _localSettingsService.ReadSettingAsync<List<string>>(DeletedToolsSettingsKey) ?? [];
        if (deletedTools.Contains(toolUrl))
        {
            return;
        }

        deletedTools.Add(toolUrl);
        await _localSettingsService.SaveSettingAsync(DeletedToolsSettingsKey, deletedTools);
    }
}
