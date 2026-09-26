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
                if (current.TryGetValue(note.Id, out var revision) && revision == note.Revision) continue;
                writer.UpdateDocument(new Term("id", note.Id.ToString("D")), BuildDocument(note));
                changed++;
            }
            foreach (var id in current.Keys.Where(id => !seen.Contains(id)))
            {
                writer.DeleteDocuments(new Term("id", id.ToString("D")));
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
            writer.UpdateDocument(new Term("id", note.Id.ToString("D")), BuildDocument(note));
            Publish(1);
        }
    }

    public void Delete(Guid id)
    {
        lock (gate)
        {
            writer.DeleteDocuments(new Term("id", id.ToString("D")));
            Publish(1);
        }
    }

    public void MarkFailed() => State = "Failed";

    public int Rebuild(IEnumerable<LibraryNote> notes)
    {
        lock (gate)
        {
            State = "Rebuilding";
            writer.DeleteAll();
            var count = 0;
            foreach (var note in notes) { writer.AddDocument(BuildDocument(note)); count++; }
            writer.Commit();
            readers.MaybeRefreshBlocking();
            Interlocked.Increment(ref searchVersion);
            State = "Ready";
            return count;
        }
    }

    public SearchPage Search(string queryText, int page = 0, int pageSize = 20)
    {
        if (State != "Ready") throw new InvalidOperationException("Search is temporarily unavailable while its derived index is repaired.");
        if (page < 0 || pageSize is < 1 or > 50) throw new SearchQueryException("Search page or page size is invalid.");
        var clauses = Parse(queryText);
        if (clauses.Count == 0) return new(queryText, page, pageSize, 0, [], SearchVersion);
        var query = BuildQuery(clauses);
        var searcher = readers.Acquire();
        try
        {
            var requested = checked((page + 1) * pageSize);
            var hits = searcher.Search(query, requested);
            var results = hits.ScoreDocs.Skip(page * pageSize).Take(pageSize).Select(hit =>
            {
                var document = searcher.Doc(hit.Doc);
                return new SearchHit(
                    Guid.Parse(document.Get("id")),
                    document.Get("title"),
                    document.Get("path_raw"),
                    MakeSnippet(document.Get("plain"), clauses),
                    hit.Score,
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
                result[Guid.Parse(document.Get("id"))] = document.Get("revision");
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
            new StringField("revision", note.Revision, Field.Store.YES),
            new TextField("title", parsed.Title, Field.Store.YES),
            new TextField("path", note.Path.Replace('/', ' '), Field.Store.NO),
            new StringField("path_raw", note.Path, Field.Store.YES),
            new TextField("filename", Path.GetFileNameWithoutExtension(note.Path), Field.Store.NO),
            new TextField("headings", string.Join('\n', parsed.Headings), Field.Store.NO),
            new TextField("aliases", string.Join('\n', parsed.Aliases), Field.Store.NO),
            new TextField("body", parsed.PlainText, Field.Store.NO),
            new StoredField("plain", parsed.PlainText)
        };
        foreach (var tag in parsed.Tags) document.Add(new StringField("tag", tag.ToLowerInvariant(), Field.Store.NO));
        if (parsed.Type is { } type) document.Add(new StringField("type", type.ToLowerInvariant(), Field.Store.NO));
        if (parsed.Status is { } status) document.Add(new StringField("status", status.ToLowerInvariant(), Field.Store.NO));
        return document;
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
                "title" => FieldQuery("title", clause.Value, clause.Phrase, 1f),
                "path" => FieldQuery("path", clause.Value.Replace('/', ' '), clause.Phrase, 1f),
                _ => AnyFieldQuery(clause.Value, clause.Phrase)
            };
            outer.Add(query, Occur.MUST);
        }
        return outer;
    }

    private Query AnyFieldQuery(string value, bool phrase)
    {
        var query = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        foreach (var (field, boost) in new[] { ("title", 6f), ("aliases", 5f), ("headings", 4f), ("filename", 3f), ("path", 2f), ("body", 1f) })
            query.Add(FieldQuery(field, value, phrase, boost), Occur.SHOULD);
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
                if (field is not ("title" or "path" or "tag")) throw new SearchQueryException($"Unknown search filter '{field}'.");
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
