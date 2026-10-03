using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Slate.Lib.Core;
using LuceneDirectory = Lucene.Net.Store.Directory;
using FSDirectory = Lucene.Net.Store.FSDirectory;

namespace Slate.Lib.Api;

public sealed class SearchQueryException(string message) : ArgumentException(message);

public sealed class SearchIndex : IDisposable
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    private readonly Lock gate = new();
    private readonly StandardAnalyzer analyzer = new(Version);
    private readonly string indexPath;
    private LuceneDirectory directory = null!;
    private IndexWriter writer = null!;
    private SearcherManager readers = null!;
    private long searchVersion;
    private readonly Dictionary<Guid, LibraryNote> sources = [];
    private HashSet<Guid> withBacklinks = [];
    public string State { get; private set; } = "Initializing";
    public long SearchVersion => Interlocked.Read(ref searchVersion);
    public string IndexPath => indexPath;

    public SearchIndex(string indexPath)
    {
        this.indexPath = Path.GetFullPath(indexPath);
        OpenRecovering();
    }

    private void OpenRecovering()
    {
        try { Open(); }
        catch (Exception exception) when (exception is CorruptIndexException or IOException or InvalidOperationException)
        {
            try { readers?.Dispose(); writer?.Dispose(); directory?.Dispose(); } catch { }
            if (Directory.Exists(indexPath)) Directory.Delete(indexPath, true);
            State = "Rebuilding";
            Open();
        }
    }

    private void Open()
    {
        Directory.CreateDirectory(indexPath);
        directory = FSDirectory.Open(new DirectoryInfo(indexPath));
        writer = new IndexWriter(directory, new IndexWriterConfig(Version, analyzer) { OpenMode = OpenMode.CREATE_OR_APPEND });
        readers = new SearcherManager(writer, true, null);
        State = "Ready";
    }

    public int Reconcile(IEnumerable<LibraryNote> notes)
    {
        lock (gate)
        {
            var current = ReadIndexedRevisions();
            var seen = new HashSet<Guid>();
            var changed = 0;
            foreach (var note in notes)
            {
                seen.Add(note.Id);
                sources[note.Id] = note;
                if (current.TryGetValue(note.Id, out var revision) && revision == note.Revision) continue;
                writer.UpdateDocument(new Term("id", note.Id.ToString("D")), BuildDocument(note));
                changed++;
            }
            foreach (var id in current.Keys.Where(id => !seen.Contains(id)))
            {
                writer.DeleteDocuments(new Term("id", id.ToString("D")));
                sources.Remove(id);
                changed++;
            }
            Publish(changed);
            return changed;
        }
    }

    public void Upsert(LibraryNote note)
    {
        lock (gate)
        {
            sources[note.Id] = note;
            writer.UpdateDocument(new Term("id", note.Id.ToString("D")), BuildDocument(note));
            Publish(1);
        }
    }

    public void Delete(Guid id)
    {
        lock (gate)
        {
            sources.Remove(id);
            writer.DeleteDocuments(new Term("id", id.ToString("D")));
            Publish(1);
        }
    }

    public void MarkFailed() => State = "Failed";

    public void UpdateBacklinks(IEnumerable<Guid> ids)
    {
        lock (gate)
        {
            var next = ids.ToHashSet(); var changed = withBacklinks.Except(next).Concat(next.Except(withBacklinks)).ToArray();
            withBacklinks = next;
            foreach (var id in changed) if (sources.TryGetValue(id, out var note)) writer.UpdateDocument(new Term("id", id.ToString("D")), BuildDocument(note));
            Publish(changed.Length);
        }
    }

    public int Rebuild(IEnumerable<LibraryNote> notes)
    {
        lock (gate)
        {
            State = "Rebuilding";
            writer.DeleteAll();
            sources.Clear();
            var count = 0;
            foreach (var note in notes) { sources[note.Id] = note; writer.AddDocument(BuildDocument(note)); count++; }
            writer.Commit();
            readers.MaybeRefreshBlocking();
            Interlocked.Increment(ref searchVersion);
            State = "Ready";
            return count;
        }
    }

    public SearchPage Search(string queryText, int page = 0, int pageSize = 20, string? view = null)
    {
        if (State != "Ready") throw new InvalidOperationException("Search is temporarily unavailable while its derived index is repaired.");
        if (page is < 0 or > 1999 || pageSize is < 1 or > 50) throw new SearchQueryException("Search page or page size is invalid.");
        var clauses = Parse(queryText);
        if (clauses.Count == 0 && view is null) return new(queryText, page, pageSize, 0, [], SearchVersion);
        Query query = clauses.Count == 0 ? new MatchAllDocsQuery() : BuildQuery(clauses);
        if (view is not null) query = new BooleanQuery { { query, Occur.MUST }, { ViewQuery(view), Occur.MUST } };
        var searcher = readers.Acquire();
        try
        {
            var requested = checked((page + 1) * pageSize);
            var hits = view == "modified" ? searcher.Search(query, requested, new Sort(new SortField("modified_sort", SortFieldType.INT64, true), new SortField("path_sort", SortFieldType.STRING))) : searcher.Search(query, requested);
            var results = hits.ScoreDocs.Skip(page * pageSize).Take(pageSize).Select(hit =>
            {
                var document = searcher.Doc(hit.Doc);
                return new SearchHit(
                    Guid.Parse(document.Get("id")),
                    document.Get("title"),
                    document.Get("path_raw"),
                    MakeSnippet(document.Get("plain"), clauses),
                    float.IsFinite(hit.Score) ? hit.Score : 0,
                    document.Get("revision"));
            }).ToArray();
            return new(queryText, page, pageSize, hits.TotalHits, results, SearchVersion);
        }
        finally { readers.Release(searcher); }
    }

    private Dictionary<Guid, string> ReadIndexedRevisions()
    {
        var result = new Dictionary<Guid, string>();
        var searcher = readers.Acquire();
        try
        {
            if (searcher.IndexReader.MaxDoc == 0) return result;
            foreach (var hit in searcher.Search(new MatchAllDocsQuery(), searcher.IndexReader.MaxDoc).ScoreDocs)
            {
                var document = searcher.Doc(hit.Doc);
                result[Guid.Parse(document.Get("id"))] = document.Get("schema") == "3" ? document.Get("revision") : "";
            }
            return result;
        }
        finally { readers.Release(searcher); }
    }

    private Document BuildDocument(LibraryNote note)
    {
        var parsed = NoteDocument.Parse(note.Markdown, note.Path);
        var document = new Document
        {
            new StringField("id", note.Id.ToString("D"), Field.Store.YES),
            new StringField("schema", "3", Field.Store.YES),
            new StringField("revision", note.Revision, Field.Store.YES),
            new TextField("title", parsed.Title, Field.Store.YES),
            new StringField("title_exact", parsed.Title.Trim().ToLowerInvariant(), Field.Store.NO),
            new TextField("path", note.Path.Replace('/', ' '), Field.Store.NO),
            new StringField("path_raw", note.Path, Field.Store.YES),
            new TextField("filename", Path.GetFileNameWithoutExtension(note.Path), Field.Store.NO),
            new TextField("headings", string.Join('\n', parsed.Headings), Field.Store.NO),
            new TextField("aliases", string.Join('\n', parsed.Aliases), Field.Store.NO),
            new TextField("body", parsed.PlainText, Field.Store.NO),
            new StoredField("plain", parsed.PlainText)
        };
        document.Add(new Int64Field("created", parsed.Created?.ToUnixTimeSeconds() ?? long.MinValue, Field.Store.NO));
        document.Add(new Int64Field("modified", (parsed.Modified ?? parsed.Created)?.ToUnixTimeSeconds() ?? long.MinValue, Field.Store.NO));
        document.Add(new NumericDocValuesField("modified_sort", (parsed.Modified ?? parsed.Created)?.ToUnixTimeSeconds() ?? long.MinValue));
        document.Add(new SortedDocValuesField("path_sort", new BytesRef(note.Path)));
        foreach (var predicate in NotePredicates.Read(parsed)) document.Add(new StringField("has", predicate, Field.Store.NO));
        if (withBacklinks.Contains(note.Id)) document.Add(new StringField("has", "backlinks", Field.Store.NO));
        document.Add(new StringField("template", note.Path.StartsWith("templates/", StringComparison.OrdinalIgnoreCase) ? "yes" : "no", Field.Store.NO));
        foreach (var tag in parsed.Tags) document.Add(new StringField("tag", tag.ToLowerInvariant(), Field.Store.NO));
        if (parsed.Type is { } type) document.Add(new StringField("type", type.ToLowerInvariant(), Field.Store.NO));
        if (parsed.Status is { } status) document.Add(new StringField("status", status.ToLowerInvariant(), Field.Store.NO));
        return document;
    }

    private static Query ViewQuery(string view)
    {
        SmartViews.Get(view);
        static Query Status(params string[] values) { var any = new BooleanQuery { MinimumNumberShouldMatch = 1 }; foreach (var value in values) any.Add(new TermQuery(new Term("status", value)), Occur.SHOULD); return any; }
        return view switch
        {
            "unanswered" => UnansweredQuery("unanswered"),
            "modified" => new MatchAllDocsQuery(),
            "orphans" => new BooleanQuery { { new TermQuery(new Term("template", "no")), Occur.MUST }, { new TermQuery(new Term("has", "backlinks")), Occur.MUST_NOT } },
            "diagrams" => new TermQuery(new Term("has", "diagram")),
            "code" => new TermQuery(new Term("has", "code")),
            "learning" => Status("learning", "currently-learning", "in-progress"),
            "review" => Status("needs-review", "review"),
            _ => throw new SearchQueryException("Unknown smart view.")
        };
    }

    private void Publish(int changes)
    {
        if (changes == 0) return;
        writer.Commit();
        readers.MaybeRefreshBlocking();
        Interlocked.Increment(ref searchVersion);
        State = "Ready";
    }

    private Query BuildQuery(IReadOnlyList<SearchClause> clauses)
    {
        var outer = new BooleanQuery();
        foreach (var clause in clauses)
        {
            Query query = clause.Field switch
            {
                "tag" => new TermQuery(new Term("tag", clause.Value.ToLowerInvariant())),
                "type" or "status" => new TermQuery(new Term(clause.Field, clause.Value.ToLowerInvariant())),
                "has" => HasQuery(clause.Value),
                "date" or "created" or "modified" => DateQuery(clause.Field == "date" ? "created" : clause.Field, clause.Value),
                "is" => UnansweredQuery(clause.Value),
                "title" => FieldQuery("title", clause.Value, clause.Phrase, 1f),
                "path" => FieldQuery("path", clause.Value.Replace('/', ' '), clause.Phrase, 1f),
                _ => AnyFieldQuery(clause.Value, clause.Phrase)
            };
            outer.Add(query, Occur.MUST);
        }
        var text = string.Join(' ', clauses.Where(c => c.Field is null).Select(c => c.Value)).Trim().ToLowerInvariant();
        if (text.Length > 0)
        {
            outer.Add(new TermQuery(new Term("title_exact", text)) { Boost = 40 }, Occur.SHOULD);
            outer.Add(new PrefixQuery(new Term("title_exact", text)) { Boost = 8 }, Occur.SHOULD);
        }
        return outer;
    }
    private static Query HasQuery(string value)
    {
        var predicate = value.ToLowerInvariant();
        if (predicate is not ("image" or "file" or "code" or "diagram" or "backlinks" or "links" or "question"))
            throw new SearchQueryException("Use has:image, file, code, diagram, backlinks, links, or question.");
        return new TermQuery(new Term("has", predicate));
    }
    private static Query DateQuery(string field, string value)
    {
        static DateTimeOffset Day(string text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var day) ? new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : throw new SearchQueryException("Use YYYY-MM-DD or YYYY-MM-DD..YYYY-MM-DD; * leaves a range end open.");
        var ends = value.Split("..", StringSplitOptions.None);
        if (ends.Length > 2) throw new SearchQueryException("A date range has two ends.");
        var lower = ends[0] == "*" ? DateTimeOffset.MinValue.ToUnixTimeSeconds() : Day(ends[0]).ToUnixTimeSeconds();
        var upperText = ends.Length == 1 ? ends[0] : ends[1];
        var upper = upperText == "*" ? DateTimeOffset.MaxValue.ToUnixTimeSeconds() : Day(upperText).ToUnixTimeSeconds() + 86399;
        if (lower > upper) throw new SearchQueryException("The date range ends before it starts.");
        return NumericRangeQuery.NewInt64Range(field, lower, upper, true, true);
    }
    private static Query UnansweredQuery(string value)
    {
        if (!value.Equals("unanswered", StringComparison.OrdinalIgnoreCase)) throw new SearchQueryException("Use is:unanswered for open questions.");
        var missing = new BooleanQuery { { new MatchAllDocsQuery(), Occur.MUST }, { new WildcardQuery(new Term("status", "*")), Occur.MUST_NOT } };
        var open = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        open.Add(new TermQuery(new Term("status", "open")), Occur.SHOULD); open.Add(missing, Occur.SHOULD);
        return new BooleanQuery { { new TermQuery(new Term("type", "question")), Occur.MUST }, { open, Occur.MUST } };
    }

    private Query AnyFieldQuery(string value, bool phrase)
    {
        var query = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        foreach (var (field, boost) in new[] { ("title", 6f), ("aliases", 5f), ("headings", 4f), ("filename", 3f), ("path", 2f), ("body", 1f) })
            query.Add(FieldQuery(field, value, phrase, boost), Occur.SHOULD);
        if (value.Trim().Length >= 2) query.Add(new PrefixQuery(new Term("title_exact", value.Trim().ToLowerInvariant())) { Boost = 3 }, Occur.SHOULD);
        return query;
    }

    private Query FieldQuery(string field, string value, bool phrase, float boost)
    {
        var terms = Analyze(field, value);
        if (terms.Count == 0) return new BooleanQuery();
        Query query;
        if (phrase && terms.Count > 1)
        {
            var phraseQuery = new PhraseQuery();
            foreach (var term in terms) phraseQuery.Add(new Term(field, term));
            query = phraseQuery;
        }
        else if (terms.Count == 1) query = new TermQuery(new Term(field, terms[0]));
        else
        {
            var all = new BooleanQuery();
            foreach (var term in terms) all.Add(new TermQuery(new Term(field, term)), Occur.MUST);
            query = all;
        }
        query.Boost = boost;
        return query;
    }

    private List<string> Analyze(string field, string value)
    {
        var result = new List<string>();
        using var reader = new StringReader(value);
        using var stream = analyzer.GetTokenStream(field, reader);
        var term = stream.AddAttribute<ICharTermAttribute>();
        stream.Reset();
        while (stream.IncrementToken()) result.Add(term.ToString());
        stream.End();
        return result;
    }

    private static IReadOnlyList<SearchClause> Parse(string text)
    {
        if (text.Length > 512) throw new SearchQueryException("Search queries are limited to 512 characters.");
        var result = new List<SearchClause>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            if (index >= text.Length) break;
            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not ':' and not '"') index++;
            string? field = null;
            if (index < text.Length && text[index] == ':')
            {
                field = text[start..index].ToLowerInvariant(); index++;
                if (field is not ("title" or "path" or "tag" or "is" or "type" or "status" or "date" or "created" or "modified" or "has")) throw new SearchQueryException($"Unknown search filter '{field}'.");
            }
            else index = start;

            var phrase = index < text.Length && text[index] == '"';
            if (phrase)
            {
                index++; start = index;
                while (index < text.Length && text[index] != '"') index++;
                if (index >= text.Length) throw new SearchQueryException("Search phrase has no closing quote.");
                var value = text[start..index++];
                if (value.Length == 0) throw new SearchQueryException("Search terms cannot be empty.");
                result.Add(new(field, value, true));
            }
            else
            {
                start = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
                var value = text[start..index];
                if (value.Contains(':') && field is null) throw new SearchQueryException($"Unknown search filter '{value[..value.IndexOf(':')]}'.");
                if (value.Length == 0) throw new SearchQueryException("Search terms cannot be empty.");
                result.Add(new(field, value, false));
            }
            if (result.Count > 32) throw new SearchQueryException("Search queries are limited to 32 clauses.");
        }
        return result;
    }

    private static string MakeSnippet(string plain, IReadOnlyList<SearchClause> clauses)
    {
        var compact = string.Join(' ', plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (compact.Length <= 180) return compact;
        var terms = clauses.SelectMany(clause => clause.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var match = terms.Select(term => compact.IndexOf(term, StringComparison.OrdinalIgnoreCase)).Where(position => position >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, match - 70);
        if (start > 0) { var space = compact.IndexOf(' ', start); if (space >= 0) start = space + 1; }
        var length = Math.Min(180, compact.Length - start);
        return (start > 0 ? "…" : "") + compact.Substring(start, length) + (start + length < compact.Length ? "…" : "");
    }

    public void Dispose()
    {
        lock (gate) { readers.Dispose(); writer.Dispose(); directory.Dispose(); analyzer.Dispose(); }
    }

    private sealed record SearchClause(string? Field, string Value, bool Phrase);
}
