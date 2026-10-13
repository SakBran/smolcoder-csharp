using System.Net;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Models;
using Smolcoder.Application.Sessions;
using Smolcoder.Domain.Messages;
using Smolcoder.Infrastructure.Persistence;
using Smolcoder.Infrastructure.Providers.Discovery;

namespace Smolcoder.Tests.Models;

public sealed class ModelSetupTests
{
    [Theory]
    [InlineData("192.168.1.50", "192.168.1.50", 2)]
    [InlineData("gpu-box.local", "gpu-box.local", 2)]
    [InlineData("box:1234", "http://box:1234", 1)]
    [InlineData("https://llm.example.com/", "https://llm.example.com", 1)]
    [InlineData("http://box:80", "http://box:80", 1)]
    [InlineData("fe80::1", "[fe80::1]", 2)]
    public void ParsesTypedAddresses(string input, string address, int urlCount)
    {
        ServerAddress parsed = ServerAddress.Parse(input);
        Assert.Equal(address, parsed.Address);
        Assert.Equal(urlCount, parsed.Urls.Count);
    }

    [Fact]
    public void BareHostTriesBothUsualPorts()
    {
        ServerAddress parsed = ServerAddress.Parse("gpu-box");
        Assert.Equal(new List<string> { "http://gpu-box:11434", "http://gpu-box:1234" }, parsed.Urls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("ftp://box")]
    [InlineData("0.0.0.0")]
    [InlineData("http://user:pw@box")]
    public void RejectsUnusableAddresses(string input)
    {
        Assert.Throws<ArgumentException>(() => ServerAddress.Parse(input));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080/v1")]
    [InlineData("https://llm.example.com/v1", "https://llm.example.com/v1")]
    [InlineData("box:8000", "http://box:8000/v1")]
    public void CustomServersServeUnderV1(string input, string root)
    {
        Assert.Equal(root, ServerAddress.OpenAiRoot(input));
    }

    [Theory]
    [InlineData("192.168.1.5", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("gpu-box", true)]
    [InlineData("box.local", true)]
    [InlineData("llm.example.com", false)]
    public void TellsPrivateHostsApart(string host, bool expected)
    {
        Assert.Equal(expected, ServerAddress.IsPrivateHost(host));
    }

    [Fact]
    public void AutoPickPrefersRememberedThenLocalOllama()
    {
        ModelSelector selector = new ModelSelector(new FakeCatalog());
        ModelDescriptor cloud = Model("deepseek-flash", Backends.DeepSeek, "cloud");
        ModelDescriptor remote = Model("qwen3:8b", Backends.Ollama, "gpu-box");
        ModelDescriptor lmStudio = Model("gemma", Backends.LmStudio, null);
        lmStudio.Loaded = true;
        ModelDescriptor noTools = Model("phi3", Backends.Ollama, null);
        noTools.SupportsTools = false;
        ModelDescriptor local = Model("qwen3:4b", Backends.Ollama, null);
        List<ModelDescriptor> models = new List<ModelDescriptor> { cloud, remote, lmStudio, noTools, local };

        Assert.Same(local, selector.AutoPick(models, null, null, null));
        Assert.Same(remote, selector.AutoPick(models, null, "qwen3:8b", null));
        Assert.Same(cloud, selector.AutoPick(models, "deepseek-v4-flash", null, null));
        Assert.Same(remote, selector.AutoPick(models, "8b", null, null));
        ModelNotFoundException missing = Assert.Throws<ModelNotFoundException>(() => selector.AutoPick(models, "llama", null, null));
        Assert.Equal("llama", missing.Wanted);
    }

    [Fact]
    public void LocalSubnetsSkipVirtualAndClampWideNetworks()
    {
        List<InterfaceAddress> addresses = new List<InterfaceAddress>
        {
            new InterfaceAddress("lo0", IPAddress.Parse("127.0.0.1"), IPAddress.Parse("255.0.0.0"), true),
            new InterfaceAddress("en0", IPAddress.Parse("192.168.1.20"), IPAddress.Parse("255.255.255.0"), false),
            new InterfaceAddress("en1", IPAddress.Parse("10.4.7.9"), IPAddress.Parse("255.255.0.0"), false),
            new InterfaceAddress("bridge100", IPAddress.Parse("192.168.64.1"), IPAddress.Parse("255.255.255.0"), false),
            new InterfaceAddress("utun3", IPAddress.Parse("10.8.0.2"), IPAddress.Parse("255.255.255.0"), false),
            new InterfaceAddress("en5", IPAddress.Parse("8.8.4.4"), IPAddress.Parse("255.255.255.0"), false)
        };
        List<Subnet> subnets = NetworkScanner.LocalSubnets(addresses);
        Assert.Equal(2, subnets.Count);
        Assert.Equal(254u, subnets[0].Last - subnets[0].First + 1);
        Assert.Equal(254u, subnets[1].Last - subnets[1].First + 1);
    }

    [Fact]
    public void RecommendsTheLargestModelThatFits()
    {
        long gigabyte = 1024L * 1024 * 1024;
        Assert.Equal("qwen3:4b", RecommendedModels.BestFor(8 * gigabyte).Name);
        Assert.Equal("qwen3:8b", RecommendedModels.BestFor(16 * gigabyte - 100).Name);
        Assert.Equal("qwen3:14b", RecommendedModels.BestFor(24 * gigabyte).Name);
        Assert.Equal("qwen3.8:27b", RecommendedModels.BestFor(64 * gigabyte).Name);
    }

    [Fact]
    public void SecretsAreSavedReadableByOwnerOnly()
    {
        string home = Path.Combine(Path.GetTempPath(), "smol-secrets-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("SMOLCODER_HOME");
        Environment.SetEnvironmentVariable("SMOLCODER_HOME", home);
        try
        {
            FileSecretStore store = new FileSecretStore();
            store.Set(SecretNames.DeepSeek, " sk-abc ");
            Assert.Equal("sk-abc", store.Get(SecretNames.DeepSeek));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FileSecretStore.SecretsPath));
            }
            store.Set(SecretNames.DeepSeek, null);
            Assert.Null(store.Get(SecretNames.DeepSeek));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMOLCODER_HOME", previous);
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void SecretsFileAcceptsEnvStyleLines()
    {
        System.Text.Json.Nodes.JsonObject secrets = FileSecretStore.FromLines("# mine\nexport DEEPSEEK_API_KEY=\"sk-abc\"\nother=1\n");
        Assert.Equal("sk-abc", secrets[SecretNames.DeepSeek]!.GetValue<string>());
        Assert.Equal("1", secrets["other"]!.GetValue<string>());
    }

    private static ModelDescriptor Model(string id, string backend, string? host)
    {
        ModelDescriptor model = new ModelDescriptor(id, backend, "http://" + (host ?? "localhost"), 8192);
        model.Host = host;
        return model;
    }

    private sealed class FakeCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelDescriptor>> DetectAsync(ModelDetectOptions? options, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public IModelProvider CreateProvider(ModelDescriptor model)
        {
            throw new NotSupportedException();
        }

        public string ResolveAlias(string name)
        {
            return name == "deepseek-v4-flash" ? "deepseek-flash" : name;
        }
    }
}
