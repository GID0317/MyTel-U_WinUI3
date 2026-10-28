using System.Net.Http;
using HtmlAgilityPack;
using MyTelU_Launcher.Models;
using Json = MyTelU_Launcher.Core.Helpers.Json;

namespace MyTelU_Launcher.Services;

public sealed class StudyPlanService
{
    private readonly object _cacheGate = new();
    private int _generation;
    private static string CachePath => AppDataStore.GetFilePath("study_plan_cache.json");

    public async Task<StudyPlan> LoadAsync(CancellationToken ct = default)
    {
        int generation = Volatile.Read(ref _generation);
        try
        {
            using var client = ScheduleService.BuildPageHttpClient();
            using var response = await client.GetAsync("https://igracias.telkomuniversity.ac.id/registration/index.php?pageid=705", ct);
            response.EnsureSuccessStatusCode();
            var plan = Parse(await response.Content.ReadAsStringAsync(ct));
            ct.ThrowIfCancellationRequested();
            lock (_cacheGate)
            {
                if (generation != _generation)
                    throw new InvalidOperationException("The account session has changed.");
                SecureFileStore.Save(CachePath, Json.Serialize(plan));
            }
            return plan;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            var cached = SecureFileStore.Load(CachePath);
            if (cached == null) throw;
            var plan = Json.Deserialize<StudyPlan>(cached);
            if (plan == null) throw;
            plan.IsCached = true;
            return plan;
        }
    }

    internal static StudyPlan Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var table = doc.DocumentNode.SelectSingleNode("//table[@id='table']");
        if (table == null || doc.DocumentNode.SelectSingleNode("//input[@name='textPassword']") != null)
            throw new InvalidDataException("Sign in to iGracias again to load your study plan.");

        var plan = new StudyPlan
        {
            FullName = Metadata(doc, "Nama"), StudentId = Metadata(doc, "NIM"),
            StudyProgram = Metadata(doc, "Program Studi"), Period = Metadata(doc, "Semester"),
            FetchTime = DateTimeOffset.Now.ToString("O")
        };
        foreach (var row in table.SelectNodes(".//tr") ?? new HtmlNodeCollection(table))
        {
            var cells = row.SelectNodes("./td");
            if (cells?.Count != 5 || !int.TryParse(Text(cells[0]).TrimEnd('.'), out _)
                || !int.TryParse(Text(cells[2]), out int credits))
                continue;
            plan.Courses.Add(new StudyPlanCourse
            {
                Course = Text(cells[1]), Credits = credits,
                Class = Text(cells[3]), Schedule = Text(cells[4])
            });
        }
        return plan;
    }

    private static string Metadata(HtmlDocument doc, string label)
    {
        foreach (var row in doc.DocumentNode.SelectNodes("//tr") ?? new HtmlNodeCollection(doc.DocumentNode))
        {
            var cells = row.SelectNodes("./td");
            if (cells?.Count >= 2 && Text(cells[0]).TrimEnd(':').Trim().Equals(label, StringComparison.OrdinalIgnoreCase))
                return Text(cells[cells.Count - 1]).TrimStart(':').Trim();
        }
        return string.Empty;
    }

    private static string Text(HtmlNode node)
    {
        var copy = node.CloneNode(true);
        foreach (var lineBreak in copy.Descendants("br").ToList())
            lineBreak.ParentNode.ReplaceChild(node.OwnerDocument.CreateTextNode(" "), lineBreak);
        return string.Join(" ", HtmlEntity.DeEntitize(copy.InnerText).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public void ResetSession()
    {
        lock (_cacheGate) _generation++;
    }
}
