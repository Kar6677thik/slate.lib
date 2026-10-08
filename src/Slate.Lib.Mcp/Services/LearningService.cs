using System.Text.RegularExpressions;
using Slate.Lib.Core;
using Slate.Lib.Mcp.Clients;
using Slate.Lib.Mcp.Models;

namespace Slate.Lib.Mcp.Services;

public sealed partial class LearningService(CanonicalSlateClient canonical)
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "again", "also", "because", "before", "being", "between", "could", "from", "have", "into", "more", "most", "only", "other", "should", "some", "such", "than", "that", "their", "there", "these", "they", "this", "through", "under", "using", "very", "what", "when", "where", "which", "while", "with", "would", "your"
    };

    public async Task<IReadOnlyList<LearningCoverageItem>> Coverage(string topic, int limit, CancellationToken token)
    {
        var search = await canonical.Search(topic, 0, Math.Clamp(limit, 1, 30), token);
        var groups = search.Results
            .SelectMany(hit => Terms(hit.Title + " " + hit.Path).Select(term => (term, hit)))
            .GroupBy(item => item.term, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Take(12);
        return groups.Select(group => new LearningCoverageItem(group.Key, group.Select(item => item.hit.Id).Distinct().Count(),
            group.Select(item => Ref(item.hit)).DistinctBy(reference => reference.NoteId).Take(8).ToArray(),
            ["Topic appears in matching note titles or paths", "Coverage is evidence count, not a mastery score"])).ToArray();
    }

    public async Task<IReadOnlyList<LearningRecommendation>> Recommend(string topic, CancellationToken token)
    {
        var coverage = await Coverage(topic, 20, token);
        return coverage.OrderBy(item => item.SourceCount).Take(6).Select(item => new LearningRecommendation(item.Topic,
            item.SourceCount == 1 ? "Only one matching source was found; another explanation or worked example may improve coverage." : "This is a recurring concept with source material available for review.",
            item.Sources,
            item.SourceCount == 1 ? ["Read the source", "Write a worked example", "Link it to a prerequisite"] : ["Compare the sources", "Summarize agreements and differences", "Create a self-test"])).ToArray();
    }

    public async Task<IReadOnlyList<LearningPathStep>> Path(string topic, CancellationToken token)
    {
        var recommendations = await Recommend(topic, token);
        return recommendations.Select((item, index) => new LearningPathStep(index + 1, item.Topic,
            index == 0 ? "Establish the vocabulary and prerequisites." : index == recommendations.Count - 1 ? "Synthesize and test transfer to a new example." : "Connect this concept to the preceding material.", item.Sources)).ToArray();
    }

    public async Task<IReadOnlyList<SelfTestQuestion>> SelfTest(string topic, int count, CancellationToken token)
    {
        var coverage = await Coverage(topic, 20, token);
        return coverage.Take(Math.Clamp(count, 1, 12)).Select((item, index) => new SelfTestQuestion(
            (index % 3) switch
            {
                0 => $"Explain {item.Topic} in your own words and give one example.",
                1 => $"How does {item.Topic} connect to another concept in these notes?",
                _ => $"What assumption or limitation should you check when applying {item.Topic}?"
            }, "Answer from the cited Slate sources, then compare your explanation with their evidence.", item.Sources)).ToArray();
    }

    private static McpReference Ref(SearchHit hit) => new(hit.Id, hit.Path, hit.Title, hit.Revision);
    private static IEnumerable<string> Terms(string text) => Word().Matches(text).Select(match => match.Value.ToLowerInvariant()).Where(word => word.Length >= 4 && !StopWords.Contains(word));
    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}+#.-]*", RegexOptions.CultureInvariant)] private static partial Regex Word();
}
