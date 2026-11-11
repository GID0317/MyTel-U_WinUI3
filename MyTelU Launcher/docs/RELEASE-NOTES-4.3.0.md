![BannerRelease](https://github.com/user-attachments/assets/3247bb09-2cbf-44e6-abba-a6c83bc3af86)

# UPDATE 4.3.0

MyTel-U 4.3.0 brings the Telutizen Timeline to Windows, adds QR attendance, and introduces a Week view for your class schedule. This update also makes previously loaded academic data available sooner when you're offline and reduces the memory Timeline uses while displaying posts and images.

---

## API and Framework Updates

- Updated **Windows App SDK to 2.6.6-experimental**, retaining the native Schedule TableView and adding the native pager for Timeline.
- Updated the **.NET 10 LTS runtime to 10.0.15**, the **.NET SDK to 10.0.404**, and **Windows SDK Build Tools to 10.0.28000.2957**.
- Added **ZXing.Net 0.16.11** for QR decoding. The main launcher remains a Native AOT application.

## Changes and Improvements

[Community Timeline]
- **New** Browse the **Telutizen Timeline** under **Community**. Search posts, move between pages with the native pager, and see author photos, pinned badges, and inline images in the feed.
- Open a post to read the full discussion, follow links, like or unlike it, or leave a comment. Double-click an attachment in the post dialog to open it in your default photo app.
- Use **New post** to publish up to **280 characters** with an optional image. **Share** sends post text through Windows Share and includes the image when it is available.

[QR Attendance]
- **New** Open **Scan presensi** on the Attendance page to scan a class QR code using your camera, choose an image, or enter a supported attendance link.
- Review the result before submitting. Scanning a code alone does not record attendance.

[Schedule Page]
- **New** Choose **Week** from the Schedule **View** menu to see classes grouped into day columns. Each card shows the time, room, class, lecturer, and status where available. The **Table** view is still available.
- Scroll across day columns with the mouse wheel without switching to vertical scrolling.

[Performance]
- **Improvement** Timeline keeps fewer off-screen posts in memory and sizes image previews to fit the feed. In a pre-release comparison on the first live feed page, private memory dropped from **197.6 MiB to 163.1 MiB** (about **17%**).

## What's Fixed?

- **Fixed** Schedule now restores older semesters missing from the iGracias dropdown when the account still has schedule data for those terms.
- **Fixed** The Scan presensi tab indicator, Timeline pager, and active Like buttons now use the app accent instead of the Windows accent. Selected text in Timeline dialogs uses a contrasting shade of the app accent.
- **Fixed** Schedule, Attendance, and Grades could wait on network requests before showing saved data. When Windows reports no internet access, these pages now skip online requests and use previously loaded data where available.
- **Fixed** Schedule and Attendance refreshes now resolve the academic period before fetching data and avoid duplicate requests when the period picker is rebuilt.

## Downloads

- **Windows x64 MSIX:** Recommended for most users. Signed with the existing DeNesis certificate and includes Windows 11 Widgets Board integration.
- **Windows x64 portable ZIP:** Extract the archive and run `MyTelU Launcher.exe` without installing the MSIX. Windows Widgets are not available in portable mode.

> [!NOTE]
> Timeline and attendance submission require an internet connection. Offline academic pages can only show data that was previously loaded.

If MyTel-U makes your day a little easier, give the repository a star. Thanks for using MyTel-U and sharing your feedback!
