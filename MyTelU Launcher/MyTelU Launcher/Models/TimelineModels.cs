using System.ComponentModel;

namespace MyTelU_Launcher.Models;

public sealed partial class TimelinePost(int id, string author, string content, string imageUrl, string timestamp,
    int likes, int comments, bool isPinned, bool isLiked = false, bool canComment = true, Uri? photo = null) : INotifyPropertyChanged
{
    public int Id { get; } = id;
    public string Author { get; } = author;
    public Uri? Photo { get; } = photo;
    public string Content { get; } = content;
    public string ImageUrl { get; } = imageUrl;
    public string Timestamp { get; } = timestamp;
    public int Likes { get; private set; } = Math.Max(0, likes);
    public int Comments { get; private set; } = Math.Max(0, comments);
    public bool CanComment { get; } = canComment;
    public bool IsPinned { get; } = isPinned;
    public bool IsLiked { get; private set; } = isLiked;
    public bool HasImage => ImageUrl.Length != 0;
    public bool HasText => !string.IsNullOrWhiteSpace(Content);
    public string LikeLabel => $"{Likes} {(Likes == 1 ? "like" : "likes")}";
    public string LikeAction => IsLiked ? "Unlike post" : "Like post";
    public string CommentLabel => $"{Comments} {(Comments == 1 ? "comment" : "comments")}";
    public string Metadata => string.IsNullOrEmpty(Timestamp) ? Author : $"{Author} \u00b7 {Timestamp}";
    public string Summary => $"{LikeLabel} \u00b7 {CommentLabel}";

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void ApplyLike(bool liked)
    {
        if (liked == IsLiked) return;
        IsLiked = liked;
        Likes = liked ? Likes + 1 : Math.Max(0, Likes - 1);
        foreach (string property in new[] { nameof(IsLiked), nameof(Likes), nameof(LikeLabel), nameof(LikeAction), nameof(Summary) })
            PropertyChanged?.Invoke(this, new(property));
    }

    internal void CommentAdded()
    {
        Comments++;
        foreach (string property in new[] { nameof(Comments), nameof(CommentLabel), nameof(Summary) })
            PropertyChanged?.Invoke(this, new(property));
    }

    public override string ToString() => Author;
}

public sealed record TimelineComment(string Author, string Content, string Timestamp, Uri? Photo = null);

public sealed record TimelinePageResult(IReadOnlyList<TimelinePost> Posts, int CurrentPage, int TotalPages);

internal sealed record TimelinePostImage(string FileName, byte[] Bytes);
