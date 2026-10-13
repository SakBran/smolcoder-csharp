using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Modes;
using Smolcoder.Infrastructure.Providers.Discovery;
using Smolcoder.Infrastructure.Providers.LmStudio;
using Smolcoder.Infrastructure.Providers.Ollama;

namespace Smolcoder.Tests.Providers;

public sealed class DiscoveryTests
{
    [Theory]
    [InlineData(null, "http://127.0.0.1:11434")]
    [InlineData("", "http://127.0.0.1:11434")]
    [InlineData("0.0.0.0", "http://127.0.0.1:11434")]
    [InlineData("0.0.0.0:11434", "http://127.0.0.1:11434")]
    [InlineData("localhost", "http://localhost:11434")]
    [InlineData("http://gpu-box:11434/", "http://gpu-box:11434")]
    [InlineData("https://ollama.example.com", "https://ollama.example.com")]
    public void NormalizesOllamaHost(string? value, string expected)
    {
        Assert.Equal(expected, LocalEndpoints.NormalizeOllamaBase(value));
    }

    [Fact]
    public void StaleOllamaHostStillTriesLoopback()
    {
        List<string> urls = LocalEndpoints.OllamaBaseUrls("gpu-box:11434");
        Assert.Equal("http://gpu-box:11434", urls[0]);
        Assert.Contains("http://127.0.0.1:11434", urls);
        Assert.Contains("http://[::1]:11434", urls);
    }

    [Fact]
    public void GroupsLoopbackSpellingsOfOnePort()
    {
        List<string> urls = new List<string> { "http://127.0.0.1:11434", "http://localhost:11434", "http://127.0.0.1:1234", "http://box:1234" };
        List<ServerGroup> groups = LocalEndpoints.GroupServers(urls, TimeSpan.FromSeconds(1), null);
        Assert.Equal(3, groups.Count);
        Assert.Equal(2, groups[0].Urls.Count);
    }

    [Fact]
    public void ReadsLmStudioPortAndDockerMappings()
    {
        Assert.Equal(4321, LocalEndpoints.ParseLmStudioServerPort("{\"port\":4321}"));
        Assert.Null(LocalEndpoints.ParseLmStudioServerPort("{\"port\":0}"));
        Assert.Null(LocalEndpoints.ParseLmStudioServerPort("nope"));
        List<string> urls = DockerPorts.ParseBaseUrls("0.0.0.0:11435->11434/tcp, [::]:11435->11434/tcp\n127.0.0.1:5432->5432/tcp", new int[] { 11434, 1234 });
        Assert.Equal(new List<string> { "http://127.0.0.1:11435", "http://[::1]:11435" }, urls);
    }

    [Fact]
    public void ParsesOllamaTagsWithoutEmbeddingModels()
    {
        JsonNode data = JsonNode.Parse("{\"models\":[{\"name\":\"qwen3:8b\",\"size\":5200000000,\"details\":{\"parameter_size\":\"8.2B\",\"quantization_level\":\"Q4_K_M\",\"family\":\"qwen3\"}},{\"name\":\"nomic-embed-text\",\"details\":{\"family\":\"nomic-bert\"}}]}")!;
        List<ModelDescriptor> models = ServerIdentifier.ParseOllamaTags(data, "http://h:11434")!;
        Assert.Single(models);
        Assert.Equal("qwen3:8b", models[0].Id);
        Assert.Equal(Backends.Ollama, models[0].Backend);
        Assert.Equal("8.2B", models[0].ParameterSize);
        Assert.Equal(5200000000L, models[0].SizeBytes);
        Assert.Null(ServerIdentifier.ParseOllamaTags(JsonNode.Parse("{\"data\":[]}"), "x"));
    }

    [Fact]
    public void ParsesLmStudioV1LoadedAndNotLoaded()
    {
        JsonNode data = JsonNode.Parse(@"{""models"":[
            {""key"":""qwen3-8b"",""type"":""llm"",""max_context_length"":40960,
             ""loaded_instances"":[{""id"":""qwen3-8b"",""config"":{""context_length"":16384}}],
             ""capabilities"":{""reasoning"":{""allowed_options"":[""off"",""low"",""high""],""default"":""high""},""trained_for_tool_use"":true}},
            {""key"":""gemma-3"",""type"":""vlm"",""max_context_length"":131072,""loaded_instances"":[]},
            {""key"":""text-embedding"",""type"":""embedding""}]}")!;
        Assert.True(ServerIdentifier.IsKeyedListing(data));
        List<ModelDescriptor> models = ServerIdentifier.ParseLmStudioV1(data, "http://127.0.0.1:1234")!;
        Assert.Equal(2, models.Count);
        Assert.Equal(16384, models[0].ContextWindow);
        Assert.True(models[0].Loaded);
        Assert.Equal("high", models[0].ReasoningDefault);
        Assert.True(models[0].SupportsTools);
        Assert.Null(models[0].Note);
        Assert.Equal(4096, models[1].ContextWindow);
        Assert.False(models[1].Loaded);
        Assert.True(models[1].SupportsVision);
        Assert.NotNull(models[1].Note);
    }

    [Fact]
    public void ParsesOpenAiModelsWithVllmContext()
    {
        JsonNode data = JsonNode.Parse("{\"data\":[{\"id\":\"Qwen/Qwen3-8B\",\"max_model_len\":32768},{\"id\":\"llama\"},{\"id\":\"bge-embed\"}]}")!;
        List<ModelDescriptor> models = ServerIdentifier.ParseOpenAiModels(data, "http://h:8000/v1", Backends.OpenAi)!;
        Assert.Equal(2, models.Count);
        Assert.Equal(32768, models[0].ContextWindow);
        Assert.Null(models[0].Note);
        Assert.Equal(4096, models[1].ContextWindow);
        Assert.NotNull(models[1].Note);
    }

    [Fact]
    public void OllamaShowFillsCapabilitiesAndMaximum()
    {
        ModelDescriptor model = new ModelDescriptor("m", Backends.Ollama, "u", 0);
        OllamaBackend.ApplyShow(model, JsonNode.Parse("{\"capabilities\":[\"completion\",\"tools\"],\"model_info\":{\"general.architecture\":\"qwen3\",\"qwen3.context_length\":40960}}"));
        Assert.True(model.SupportsTools);
        Assert.False(model.SupportsVision);
        Assert.Equal(40960, model.MaxContext);
        Assert.Equal(8192, OllamaBackend.RunningContextLength(JsonNode.Parse("{\"models\":[{\"name\":\"other\",\"context_length\":1},{\"model\":\"m\",\"context_length\":8192}]}"), "m"));
        Assert.Null(OllamaBackend.RunningContextLength(JsonNode.Parse("{\"models\":[{\"name\":\"other\",\"context_length\":1}]}"), "m"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(Effort.Off, "none")]
    [InlineData(Effort.High, "high")]
    [InlineData(Effort.Medium, "low")]
    [InlineData(Effort.Low, "low")]
    public void MapsLmStudioEffortToAllowedLevels(Effort? effort, string? expected)
    {
        List<string> allowed = new List<string> { "off", "low", "high", "xhigh" };
        Assert.Equal(expected, LmStudioReasoning.MapEffort(effort, allowed));
    }

    [Fact]
    public void LmStudioEffortWithoutListIsSentAsIs()
    {
        Assert.Equal("medium", LmStudioReasoning.MapEffort(Effort.Medium, null));
        Assert.Equal("xhigh", LmStudioReasoning.MapEffort(Effort.High, new List<string> { "low", "xhigh" }));
    }
}
