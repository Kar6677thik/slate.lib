using System.Security.Cryptography;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LinkIndex
{
    private long summaryVersion = -1;
    private KnowledgeSummary[] summaries = [];
    private KnowledgeSummary[] Summaries()
    {
        if (summaryVersion == Version) return summaries;
        summaries = notes.Values.Select(x => new KnowledgeSummary(x.Note.Id, x.Note.Path, x.Note.Title, x.Document.Tags.ToArray(), x.Document.Headings.ToArray(), x.Document.Type, x.Document.Status, x.Document.Created, x.Document.Modified)).ToArray();
        summaryVersion = Version; return summaries;
    }
    public RediscoveryPage Rediscover(string view, DateOnly today, int page)
    {
        if (page is < 0 or > 1999) throw new ArgumentException("Invalid rediscovery page.");
        lock (gate)
        {
            var cutoff = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-30);
            var eligible = Summaries().Where(x => !x.Path.StartsWith("templates/", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (view == "random")
            {
                var chosen = eligible.Length == 0 ? null : eligible[RandomNumberGenerator.GetInt32(eligible.Length)];
                return new(view, 0, eligible.Length, chosen is null ? [] : [new(chosen.Id, chosen.Path, chosen.Title, "Random note from this library; templates excluded.", chosen.Created)]);
            }
            IEnumerable<KnowledgeSummary> selected = view switch
            {
                "forgotten" => eligible.Where(x => (x.Created ?? x.Modified) < cutoff),
                "old-idea" => eligible.Where(x => x.Type?.Equals("idea", StringComparison.OrdinalIgnoreCase) == true && (x.Created ?? x.Modified) < cutoff),
                "old-question" => eligible.Where(x => x.Type?.Equals("question", StringComparison.OrdinalIgnoreCase) == true && (x.Status is null || x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)) && (x.Created ?? x.Modified) < cutoff),
                "on-this-day" => eligible.Where(x => x.Created is { } created && created.Year < today.Year && created.Month == today.Month && created.Day == today.Day),
                "recently-learned" => eligible.Where(x => x.Status?.ToLowerInvariant() is "learned" or "completed" && (x.Modified ?? x.Created) >= cutoff && (x.Modified ?? x.Created) < cutoff.AddDays(31)),
                "continue-learning" => eligible.Where(x => x.Status?.ToLowerInvariant() is "learning" or "currently-learning" or "in-progress"),
                "timeline" => eligible.Where(x => x.Created is not null || x.Modified is not null),
                _ => throw new ArgumentException("Unknown rediscovery view.")
            };
            selected = view is "timeline" or "recently-learned" ? selected.OrderByDescending(x => x.Modified ?? x.Created).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                : selected.OrderBy(x => x.Created ?? x.Modified).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase);
            var results = selected.ToArray();
            return new(view, page, results.Length, results.Skip(page * 20).Take(20).Select(x =>
            {
                var date = view is "timeline" or "recently-learned" ? x.Modified ?? x.Created : x.Created ?? x.Modified;
                var reason = view switch
                {
                    "old-question" => "Open question older than 30 days",
                    "old-idea" => "Marked idea, older than 30 days",
                    "on-this-day" => $"Created on this date in {x.Created!.Value.Year}",
                    "continue-learning" => $"Marked {x.Status}",
                    "recently-learned" => $"Marked {x.Status}; metadata date in the last 30 days",
                    "forgotten" => "Older than 30 days according to explicit date metadata",
                    _ => x.Modified is null ? "Known creation date" : "Known updated/modified date"
                };
                if (date is { } known && view != "on-this-day") reason += $" · {known:yyyy-MM-dd}";
                return new RediscoveryHit(x.Id, x.Path, x.Title, reason, date);
            }).ToArray());
        }
    }
}
