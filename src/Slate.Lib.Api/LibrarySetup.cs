using System.Text;
using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public static class LibrarySetup
{
    public static int Initialize(LibraryPaths paths)
    {
        var edits = new List<(string Path, string Source)>();
        var ids = new HashSet<Guid>();
        foreach (var relative in LibraryStore.Discover(paths))
        {
            var file = paths.Resolve(relative);
            if (new FileInfo(file).Length > NoteDocument.MaxBytes) throw new InvalidDataException($"Note too large: {relative}");
            var source = File.ReadAllText(file, new UTF8Encoding(false, true));
            var note = NoteDocument.Parse(source, relative, true);
            var id = note.Id ?? Guid.NewGuid();
            if (!ids.Add(id)) throw new InvalidDataException($"Duplicate ID in {relative}");
            if (note.Id is null) edits.Add((file, NoteDocument.AddId(source, id)));
        }
        var directory = Path.Combine(paths.Root, ".slate");
        LibraryPaths.RejectLink(directory);
        var identity = Path.Combine(directory, "library.json");
        LibraryPaths.RejectLink(identity);
        if (File.Exists(identity))
        {
            var current = JsonSerializer.Deserialize<LibraryIdentity>(File.ReadAllText(identity), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (current is null || current.SchemaVersion != 1 || current.LibraryId == Guid.Empty) throw new InvalidDataException("Invalid library identity.");
        }
        Directory.CreateDirectory(directory);
        foreach (var edit in edits)
        {
            var temp = Path.Combine(Path.GetDirectoryName(edit.Path)!, ".slate-tmp-" + Guid.NewGuid());
            File.WriteAllText(temp, edit.Source, new UTF8Encoding(false));
            File.Move(temp, edit.Path, true);
        }
        if (!File.Exists(identity)) File.WriteAllText(identity,
            JsonSerializer.Serialize(new LibraryIdentity(1, Guid.NewGuid()), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return edits.Count;
    }
}
