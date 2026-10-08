using System.ComponentModel;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using Slate.Lib.Core;
using Slate.Lib.Mcp.Clients;
using Slate.Lib.Mcp.Configuration;
using Slate.Lib.Mcp.Models;
using Slate.Lib.Mcp.Services;

namespace Slate.Lib.Mcp.Tools;

[McpServerToolType]
[Authorize]
public sealed class LearningTools(LearningService learning, CanonicalSlateClient canonical, IntelligenceClient intelligence, ToolExecutor executor, IOptions<SlateMcpOptions> options)
{
    private readonly SlateMcpOptions settings = options.Value;
    [McpServerTool(Name = "analyze_learning_coverage", Title = "Analyze learning coverage", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Summarize which concepts have supporting Slate sources. Counts describe library evidence only and are never mastery percentages.")]
    public Task<McpResult<IReadOnlyList<LearningCoverageItem>>> AnalyzeLearningCoverage(string topic, int sourceLimit = 20, CancellationToken token = default) =>
        executor.Run<IReadOnlyList<LearningCoverageItem>>("analyze_learning_coverage", async libraryId => McpResult<IReadOnlyList<LearningCoverageItem>>.Ok(libraryId, "analyze_learning_coverage", await learning.Coverage(ToolInputs.Text(topic, nameof(topic), 300), ToolInputs.Range(sourceLimit, 1, 30, nameof(sourceLimit)), token)), token);

    [McpServerTool(Name = "recommend_what_to_learn_next", Title = "Recommend what to learn next", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Recommend evidence-backed next study actions from existing Slate sources without inferring personal mastery.")]
    public Task<McpResult<IReadOnlyList<LearningRecommendation>>> RecommendWhatToLearnNext(string topic, CancellationToken token = default) =>
        executor.Run<IReadOnlyList<LearningRecommendation>>("recommend_what_to_learn_next", async libraryId => McpResult<IReadOnlyList<LearningRecommendation>>.Ok(libraryId, "recommend_what_to_learn_next", await learning.Recommend(ToolInputs.Text(topic, nameof(topic), 300), token)), token);

    [McpServerTool(Name = "build_learning_path", Title = "Build learning path", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Order available library evidence into a review path. The sequence is a suggestion grounded in source coverage, not an assessment of the learner.")]
    public Task<McpResult<IReadOnlyList<LearningPathStep>>> BuildLearningPath(string topic, CancellationToken token = default) =>
        executor.Run<IReadOnlyList<LearningPathStep>>("build_learning_path", async libraryId => McpResult<IReadOnlyList<LearningPathStep>>.Ok(libraryId, "build_learning_path", await learning.Path(ToolInputs.Text(topic, nameof(topic), 300), token)), token);

    [McpServerTool(Name = "generate_self_test", Title = "Generate self-test", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Create deterministic study questions tied to Slate source references. No answers are fabricated and no external model is called.")]
    public Task<McpResult<IReadOnlyList<SelfTestQuestion>>> GenerateSelfTest(string topic, int count = 6, CancellationToken token = default) =>
        executor.Run<IReadOnlyList<SelfTestQuestion>>("generate_self_test", async libraryId => McpResult<IReadOnlyList<SelfTestQuestion>>.Ok(libraryId, "generate_self_test", await learning.SelfTest(ToolInputs.Text(topic, nameof(topic), 300), ToolInputs.Range(count, 1, 12, nameof(count)), token)), token);

    [McpServerTool(Name = "create_learning_plan_note", Title = "Create learning plan note", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Create a canonical note containing an evidence-backed learning path and source links. Review build_learning_path first when the destination or content is uncertain.")]
    public Task<McpResult<LibraryNote>> CreateLearningPlanNote(string topic, string folderPath, string name, CancellationToken token = default) =>
        executor.Run<LibraryNote>("create_learning_plan_note", async libraryId =>
        {
            if (!settings.EnableWrites) throw new ArgumentException("Writes are disabled on this MCP server.");
            topic = ToolInputs.Text(topic, nameof(topic), 300);
            var path = await learning.Path(topic, token);
            var markdown = new StringBuilder().AppendLine($"# Learning plan: {topic}").AppendLine().AppendLine("This plan is derived from current Slate library evidence; it does not measure mastery.").AppendLine();
            foreach (var step in path)
            {
                markdown.AppendLine($"## {step.Order}. {step.Topic}").AppendLine().AppendLine(step.Purpose).AppendLine();
                foreach (var source in step.Sources) markdown.AppendLine($"- [[{source.Path}]] — {source.Title}");
                markdown.AppendLine();
            }
            var note = await canonical.Create(new(ToolInputs.Text(folderPath, nameof(folderPath), 1024, true), ToolInputs.Text(name, nameof(name), 200), $"Learning plan: {topic}", InitialMarkdown: markdown.ToString()), token);
            await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token);
            return McpResult<LibraryNote>.Ok(libraryId, "create_learning_plan_note", note, references: [new(note.Id, note.Path, note.Title, note.Revision)]);
        }, token);
}
