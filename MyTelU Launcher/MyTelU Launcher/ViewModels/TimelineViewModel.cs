using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTelU_Launcher.Contracts.Services;
using MyTelU_Launcher.Contracts.ViewModels;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;

namespace MyTelU_Launcher.ViewModels;

public sealed partial class TimelineViewModel(MobileAcademicService mobile, IBrowserLoginService login,
    INavigationService navigation) : ObservableObject, INavigationAware
{
    private CancellationTokenSource? _activePage;
    private CancellationTokenSource? _loadCancellation;
    private string _search = "";

    [ObservableProperty]
    public partial ObservableCollection<TimelinePost> Posts { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPage))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool NeedsLogin { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(PagerIndex))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    public partial int CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanPage))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    public partial int TotalPages { get; set; }

    public bool HasError => ErrorMessage.Length != 0;
    public int PagerIndex => Math.Max(0, CurrentPage - 1);
    public bool CanPage => !IsLoading && TotalPages > 0;
    public string PageLabel => CurrentPage == 0 ? "" : $"Page {CurrentPage} of {TotalPages}";
    private bool CanRefresh => !IsLoading;
    private bool CanGoBack => !IsLoading && CurrentPage > 1;
    private bool CanGoForward => !IsLoading && CurrentPage < TotalPages;

    public void OnNavigatedTo(object parameter)
    {
        _activePage = new CancellationTokenSource();
        IsLoading = false;
        _ = LoadAsync(1);
    }

    public void OnNavigatedFrom()
    {
        _loadCancellation?.Cancel();
        _activePage?.Cancel();
        _activePage?.Dispose();
        _activePage = null;
        Posts.Clear();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => LoadAsync(Math.Max(1, CurrentPage));

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousPageAsync() => LoadAsync(CurrentPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextPageAsync() => LoadAsync(CurrentPage + 1);

    [RelayCommand]
    private void OpenSchedule() => navigation.NavigateTo(typeof(ScheduleViewModel).FullName!);

    public Task SearchAsync(string search)
    {
        _search = search.Trim();
        return LoadAsync(1);
    }

    public Task GoToPageAsync(int page)
        => page > 0 && page <= TotalPages && page != CurrentPage ? LoadAsync(page) : Task.CompletedTask;

    public async Task ToggleLikeAsync(TimelinePost post)
    {
        if (_activePage == null || IsLoading || !Posts.Contains(post)) return;
        var ct = _activePage.Token;
        IsLoading = true;
        try
        {
            bool liked = !post.IsLiked;
            await mobile.SetTimelineLikeAsync(post.Id, liked, ct);
            ct.ThrowIfCancellationRequested();
            post.ApplyLike(liked);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { if (!ct.IsCancellationRequested) IsLoading = false; }
    }

    private async Task LoadAsync(int page)
    {
        if (_activePage == null) return;
        _loadCancellation?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_activePage.Token);
        _loadCancellation = request;
        var ct = request.Token;
        IsLoading = true;
        ErrorMessage = "";
        IsEmpty = false;
        try
        {
            NeedsLogin = !login.HasSavedCredentials;
            if (NeedsLogin)
            {
                Posts = [];
                CurrentPage = TotalPages = 0;
                return;
            }

            if (!InternetConnection.IsAvailable)
            {
                ErrorMessage = "Timeline needs an internet connection.";
                return;
            }

            var result = await mobile.GetTimelineAsync(page, ct, _search);
            ct.ThrowIfCancellationRequested();
            CurrentPage = result.CurrentPage;
            TotalPages = result.TotalPages;
            Posts = new(result.Posts);
            IsEmpty = Posts.Count == 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException
            or IOException or OperationCanceledException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) return;
            ErrorMessage = ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                ? "MyTelU could not verify your account. Sign in to iGracias again."
                : "Check your connection and try your search or page selection again.";
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, request))
            {
                _loadCancellation = null;
                IsLoading = false;
            }
            request.Dispose();
        }
    }
}
