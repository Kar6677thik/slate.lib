using System.Globalization;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LibraryStore
{
    public LibraryNote Daily(DailyNoteRequest request)
    {
        lock (gate)
        {
            if (request.Date.Year < 1900) throw new ArgumentException("Choose a supported local date.");
            // Preserve legacy uppercase Daily directories without an unreviewed content move.
            var folder = Directory.EnumerateDirectories(paths.Root).Select(Path.GetFileName).FirstOrDefault(x => x!.Equals("daily", StringComparison.OrdinalIgnoreCase)) ?? "daily";
            if (!Directory.Exists(paths.Resolve(folder!, true))) CreateFolder(new("", folder!));
            var name = request.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); var relative = folder + "/" + name + ".md";
            if (File.Exists(paths.Resolve(relative))) return ReadPath(relative);
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow.ToString("O");
            var markdown = FrontMatter.SetScalars(NoteDocument.AddId("# " + name + "\n\n", id), new Dictionary<string, string> { ["created"] = now, ["updated"] = now });
            return CreateNote(new(folder!, name, name, id, markdown));
        }
    }
    public LibraryNote AnswerQuestion(Guid id, AnswerQuestionRequest request)
    {
        lock (gate)
        {
            var note = Read(id); if (note.Revision != request.Revision) throw new LibraryPreconditionException("The question changed. Review the latest version before answering.");
            return Update(id, new(KnowledgeWorkflows.Answer(note.Markdown, request.Answer, request.AnsweredAt), request.Revision));
        }
    }
    public CaptureAppendPreview PreviewCaptureAppend(Guid destinationId, Guid captureId)
    {
        lock (gate)
        {
            if (destinationId == captureId) throw new ArgumentException("Choose another note as destination.");
            var destination = Read(destinationId); var capture = Read(captureId);
            var next = KnowledgeWorkflows.AppendCapture(destination.Markdown, destination.Path, capture.Markdown, capture.Path);
            if (Utf8.GetByteCount(next) > NoteDocument.MaxBytes) throw new InvalidDataException("The combined note exceeds the size limit.");
            return new(captureId, destinationId, destination.Path, destination.Revision, capture.Revision, next);
        }
    }
    public LibraryNote AppendCapture(Guid destinationId, AppendCaptureRequest request)
    {
        lock (gate)
        {
            var preview = PreviewCaptureAppend(destinationId, request.CaptureId);
            if (preview.DestinationRevision != request.DestinationRevision || preview.CaptureRevision != request.CaptureRevision) throw new LibraryPreconditionException("A note changed after the append preview.");
            return Update(destinationId, new(preview.ProposedMarkdown, preview.DestinationRevision));
        }
    }
}
