using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Slate.Lib.Mcp.Prompts;

/// <summary>
/// User-invoked workflows (slash commands in Claude) for the common Slate flows. They only describe the steps; the tools
/// do the work and keep their own scope checks, previews and revision rules.
/// </summary>
[McpServerPromptType]
[Authorize(Policy = "scope:slate.read")]
public sealed class LibraryPrompts
{
    [McpServerPrompt(Name = "capture_and_file", Title = "Capture and file a thought")]
    [Description("Capture a thought to the inbox, then file it where it belongs and link it, or keep it in the inbox.")]
    public static string CaptureAndFile([Description("The thought, quote or snippet to capture.")] string thought) =>
        $"""
        Capture this in Slate and file it:

        {thought}

        1. Call search_notes with a short query for its main subject. If an existing note already covers it, show that note and offer to add
           the thought there (append_to_note with the revision you read) instead of creating a duplicate.
        2. Otherwise call capture_to_inbox with a new captureId (a random GUID) and the text as given.
        3. Suggest where it belongs: the folder of the closest related notes from step 1 (browse_library helps when there is no match).
           Move it with move_library_item only after the owner agrees; leave it in the inbox if they don't answer.
        4. Offer up to three links to related notes (get_related_notes or step 1 results). Add them only with the owner's agreement, using
           preview_note_edit and then apply_note_edit.
        Finish with the note's url.
        """;

    [McpServerPrompt(Name = "process_inbox", Title = "Process the inbox")]
    [Description("Review inbox captures and propose, for each, whether to file, merge, convert to a question, or keep.")]
    public static string ProcessInbox() =>
        """
        Help me process my Slate inbox.
        1. Call triage_inbox. For each capture, give one line: the suggested action (file into a folder, append to an existing note, turn into
           a question, or keep), the reason, and the capture's url.
        2. Apply nothing until I confirm each item, or say "apply all". Then use move_library_item to file, append_to_note (with the revision
           you read) to merge, and preview_note_edit then apply_note_edit for any rewrite.
        3. Never delete a capture. If a capture should go, say so and leave it for me.
        Finish with what changed and what is still in the inbox.
        """;

    [McpServerPrompt(Name = "link_note", Title = "Connect a note")]
    [Description("Find the notes one note should link to, propose the links, and add the accepted ones.")]
    public static string LinkNote([Description("The note: its id, url, title or path.")] string note) =>
        $"""
        Connect the Slate note "{note}" to the rest of the library.
        1. Find it: if you have an id (or a Slate url, whose ?note= value is the id), call read_note; otherwise call search_notes first.
        2. Gather candidates with get_related_notes and find_link_opportunities (scopeKind="note", noteId=<id>). Read get_note_links to skip
           links that already exist.
        3. Propose at most five links, each with a sentence on why, and where in the note it would go.
        4. For the links I accept, build the new Markdown and call preview_note_edit, show me the change, then apply_note_edit.
        """;

    [McpServerPrompt(Name = "write_note", Title = "Write up a topic")]
    [Description("Write or extend a note on a topic, reusing what the library already says.")]
    public static string WriteNote(
        [Description("The topic to write about.")] string topic,
        [Description("Optional folder for a new note, such as Projects/slate.")] string? folder = null) =>
        $"""
        Write up "{topic}" in Slate.
        1. Call search_notes for the topic and read the most relevant notes (read_notes_batch). If one already covers it, extend that note
           (preview_note_edit then apply_note_edit) instead of creating a new one, and say why.
        2. Otherwise create a note with create_note in {(string.IsNullOrWhiteSpace(folder) ? "the folder where related notes live (ask me if unclear)" : $"\"{folder}\"")}.
           Link related notes by title with [[wiki links]], and keep what the owner wrote in their words: quote or link it, don't paraphrase it away.
        3. Mark anything you are unsure of as an open question in the note rather than stating it as fact.
        Finish with the note's url.
        """;
}
