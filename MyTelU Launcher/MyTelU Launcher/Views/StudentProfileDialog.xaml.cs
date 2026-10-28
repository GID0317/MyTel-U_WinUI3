using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;

namespace MyTelU_Launcher.Views;

public sealed partial class StudentProfileDialog : ContentDialog
{
    private readonly CancellationTokenSource _loadCancellation = new();

    public StudentProfileDialog()
    {
        InitializeComponent();
        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => _loadCancellation.Cancel();
    }

    private async Task LoadAsync()
    {
        var service = App.GetService<MobileAcademicService>();
        try
        {
            var cached = await Task.Run(service.GetCachedProfile);
            if (cached != null) Display(cached);
            Display(await service.GetProfileAsync(_loadCancellation.Token));
        }
        catch (OperationCanceledException) when (_loadCancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or InvalidOperationException or InvalidDataException or TaskCanceledException)
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Message = "Could not refresh your profile. Connect to the internet and sign in to iGracias again.";
            StatusBar.IsOpen = true;
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void Display(StudentProfile profile)
    {
        ProfilePhoto.Source = null;
        PhotoPlaceholder.Visibility = Visibility.Visible;
        if (Uri.TryCreate(profile.Photo, UriKind.Absolute, out var photo)
            && photo.Scheme == Uri.UriSchemeHttps
            && (photo.Host == "telkomuniversity.ac.id" || photo.Host.EndsWith(".telkomuniversity.ac.id", StringComparison.OrdinalIgnoreCase)))
        {
            var image = new BitmapImage(photo);
            image.ImageOpened += (_, _) => PhotoPlaceholder.Visibility = Visibility.Collapsed;
            ProfilePhoto.Source = image;
        }
        NameText.Text = profile.FullName;
        StudentIdText.Text = profile.StudentId;
        ProgramText.Text = profile.StudyProgram;
        FacultyText.Text = profile.Faculty;
        ClassText.Text = profile.Class;
        // The mobile API represents the entry academic year as YYZZ, e.g. 2324.
        CohortText.Text = profile.SchoolYear.Length == 4
            && int.TryParse(profile.SchoolYear[..2], out int startYear)
            && int.TryParse(profile.SchoolYear[2..], out int endYear) && endYear == startYear + 1
                ? (2000 + startYear).ToString() : profile.SchoolYear;
        CohortPanel.Visibility = string.IsNullOrWhiteSpace(CohortText.Text) ? Visibility.Collapsed : Visibility.Visible;
        StudentTypeText.Text = profile.StudentType.Equals("STUDENT", StringComparison.OrdinalIgnoreCase)
            ? "MAHASISWA" : profile.StudentType;
        StudentTypePanel.Visibility = string.IsNullOrWhiteSpace(StudentTypeText.Text) ? Visibility.Collapsed : Visibility.Visible;
        StatusBar.IsOpen = false;
    }
}
