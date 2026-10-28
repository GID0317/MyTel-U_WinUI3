using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MyTelU_Launcher.Controls;

public sealed partial class AccountMenuFlyoutHeader : MenuFlyoutItem
{
    public static readonly DependencyProperty DisplayNameProperty = DependencyProperty.Register(
        nameof(DisplayName), typeof(string), typeof(AccountMenuFlyoutHeader), new PropertyMetadata("Student profile"));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(AccountMenuFlyoutHeader), new PropertyMetadata("Not signed in"));

    public static readonly DependencyProperty ProfilePictureProperty = DependencyProperty.Register(
        nameof(ProfilePicture), typeof(ImageSource), typeof(AccountMenuFlyoutHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(AccountMenuFlyoutHeader), new PropertyMetadata(false));

    public AccountMenuFlyoutHeader()
    {
        IsTabStop = false;
        AllowFocusOnInteraction = false;
    }

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public ImageSource? ProfilePicture
    {
        get => (ImageSource?)GetValue(ProfilePictureProperty);
        set => SetValue(ProfilePictureProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }
}
