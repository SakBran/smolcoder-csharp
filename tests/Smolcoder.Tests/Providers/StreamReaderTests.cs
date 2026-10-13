using System.Diagnostics;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;
using Smolcoder.Infrastructure.Providers;
using Smolcoder.Infrastructure.Providers.Ollama;
using Smolcoder.Infrastructure.Providers.OpenAi;

namespace Smolcoder.Tests.Providers;

public sealed class StreamReaderTests
{
    [Fact]
    public void OpenAiReaderJoinsContentReasoningAndSplitToolCalls()
    {
        List<string> tokens = new List<string>();
        List<string> thoughts = new List<string>();
        ChatRequestOptions options = new ChatRequestOptions();
        options.OnToken = text => tokens.Add(text);
        options.OnThinking = text => thoughts.Add(text);
        StreamProgress progress = new StreamProgress();
        OpenAiStreamReader reader = new OpenAiStreamReader(options, progress, Stopwatch.StartNew());

        reader.HandleLine("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"think \"}}]}");
        reader.HandleLine("data: {\"choices\":[{\"delta\":{\"reasoning\":\"more\"}}]}");
        reader.HandleLine("data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}");
        reader.HandleLine("data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c1\",\"function\":{\"name\":\"read_\",\"arguments\":\"{\\\"pa\"}}]}}]}");
        reader.HandleLine("data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"file\",\"arguments\":\"th\\\":\\\"a\\\"}\"}}]}}]}");
        reader.HandleLine("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5}}");
        reader.HandleLine("data: [DONE]");

        Assert.True(progress.Streaming);
        Assert.True(reader.Finished);
        ChatResult result = reader.ToResult(false);
        Assert.Equal("Hi", result.Content);
        Assert.Equal("think more", result.Thinking);
        Assert.Single(result.ToolCalls);
        Assert.Equal("c1", result.ToolCalls[0].Id);
        Assert.Equal("read_file", result.ToolCalls[0].Name);
        Assert.Equal("a", result.ToolCalls[0].Arguments["path"]!.GetValue<string>());
        Assert.Equal(10, result.PromptTokens);
        Assert.Equal(5, result.GeneratedTokens);
        Assert.Equal(new List<string> { "Hi" }, tokens);
        Assert.Equal(2, thoughts.Count);
    }

    [Fact]
    public void OpenAiReaderWithoutFinishReasonIsNotFinished()
    {
        OpenAiStreamReader reader = new OpenAiStreamReader(new ChatRequestOptions(), new StreamProgress(), Stopwatch.StartNew());
        reader.HandleLine("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}");
        Assert.False(reader.Finished);
    }

    [Fact]
    public void OpenAiReaderTurnsErrorFramesIntoIOException()
    {
        OpenAiStreamReader reader = new OpenAiStreamReader(new ChatRequestOptions(), new StreamProgress(), Stopwatch.StartNew());
        IOException error = Assert.Throws<IOException>(() => reader.HandleLine("data: {\"error\":{\"message\":\"boom\"}}"));
        Assert.Contains("stream error: boom", error.Message);
        Assert.Throws<IOException>(() => reader.HandleLine("data: {not json"));
    }

    [Fact]
    public void OpenAiReaderReadsAWholeCompletion()
    {
        OpenAiStreamReader reader = new OpenAiStreamReader(new ChatRequestOptions(), new StreamProgress(), Stopwatch.StartNew());
        JsonObject body = (JsonObject)JsonNode.Parse("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"done\",\"tool_calls\":[{\"function\":{\"name\":\"x\",\"arguments\":\"{}\"}}]}}]}")!;
        reader.HandleCompletion(body);
        ChatResult result = reader.ToResult(false);
        Assert.True(reader.Finished);
        Assert.True(result.Truncated);
        Assert.Equal("done", result.Content);
        Assert.Equal("x", result.ToolCalls[0].Name);
        Assert.StartsWith("call_", result.ToolCalls[0].Id);
    }

    [Fact]
    public void OllamaReaderReadsNdjsonWithObjectArguments()
    {
        OllamaStreamReader reader = new OllamaStreamReader(new ChatRequestOptions(), new StreamProgress(), Stopwatch.StartNew());
        reader.HandleLine("{\"message\":{\"thinking\":\"hmm\"},\"done\":false}");
        reader.HandleLine("{\"message\":{\"content\":\"ok\",\"tool_calls\":[{\"function\":{\"name\":\"list_files\",\"arguments\":{\"path\":\".\"}}}]},\"done\":false}");
        reader.HandleLine("");
        reader.HandleLine("{\"message\":{},\"done\":true,\"done_reason\":\"length\",\"prompt_eval_count\":100,\"eval_count\":20,\"eval_duration\":1000000000}");

        Assert.True(reader.Finished);
        ChatResult result = reader.ToResult();
        Assert.Equal("ok", result.Content);
        Assert.Equal("hmm", result.Thinking);
        Assert.Equal(".", result.ToolCalls[0].Arguments["path"]!.GetValue<string>());
        Assert.Equal(100, result.PromptTokens);
        Assert.Equal(20, result.CompletionTokens);
        Assert.Equal(20.0, result.GeneratedTokensPerSecond);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void OllamaWireFormatKeepsOnlyCurrentTurnThinking()
    {
        Message oldAssistant = Message.Assistant("old");
        oldAssistant.Thinking = "old thought";
        oldAssistant.ToolCalls.Add(new ToolCall("a", "list_files", new JsonObject()));
        Message newAssistant = Message.Assistant("new");
        newAssistant.Thinking = "new thought";
        newAssistant.ToolCalls.Add(new ToolCall("b", "read_file", new JsonObject { ["path"] = "x" }));
        List<Message> messages = new List<Message>
        {
            Message.User("first"),
            oldAssistant,
            Message.ToolResult("a", "list_files", "x"),
            Message.User("second"),
            newAssistant,
            Message.ToolResult("b", "read_file", "contents")
        };

        JsonArray wire = OllamaWireFormat.Messages(messages);

        Assert.Null(wire[1]!["thinking"]);
        Assert.Equal("new thought", wire[4]!["thinking"]!.GetValue<string>());
        Assert.Equal("x", wire[4]!["tool_calls"]![0]!["function"]!["arguments"]!["path"]!.GetValue<string>());
        Assert.Equal("read_file", wire[5]!["tool_name"]!.GetValue<string>());
    }

    [Fact]
    public void OllamaThinkValueFollowsEffortAndSupport()
    {
        Assert.Null(OllamaProvider.ThinkValue(null, false, false));
        Assert.False(OllamaProvider.ThinkValue(Domain.Modes.Effort.Off, true, false)!.GetValue<bool>());
        Assert.True(OllamaProvider.ThinkValue(Domain.Modes.Effort.High, false, false)!.GetValue<bool>());
        Assert.Equal("low", OllamaProvider.ThinkValue(Domain.Modes.Effort.Low, true, false)!.GetValue<string>());
        Assert.Null(OllamaProvider.ThinkValue(Domain.Modes.Effort.High, true, true));
    }
}
