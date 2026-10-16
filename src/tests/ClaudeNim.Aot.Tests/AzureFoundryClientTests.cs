// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Endpoints.Anthropic;
using ClaudeNim.Aot.Endpoints.Codex;
using ClaudeNim.Aot.Hosting;
using ClaudeNim.Aot.Nvidia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace ClaudeNim.Aot.Tests;

/// <summary>Verifies Azure deployment request controls and independent credentials.</summary>
public sealed class AzureFoundryClientTests
{
    /// <summary>The non-default sampling setting used to verify omission.</summary>
    private const double SamplingTemperature = 0.5;

    /// <summary>A completion ceiling used by the test conversation.</summary>
    private const int CompletionLimit = 128;

    /// <summary>A GPT deployment shared by the request and hosting tests.</summary>
    private const string GptDeployment = "gpt-6-astra";

    /// <summary>A custom deployment name used to verify model mapping.</summary>
    private const string CustomDeployment = "coding";

    /// <summary>Disabled Azure clients return unavailable without sending a request.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task DisabledProviderDoesNotSend()
    {
        var api = new CapturingAzureApi();
        var client = new AzureFoundryClient(api, new AzureFoundryOptions());

        using var response = await client.SendChatAsync(Request(GptDeployment), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(api.Requests.Count).IsEqualTo(0);
    }

    /// <summary>Custom deployment names use the underlying GPT model's controls.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CustomDeploymentUsesUnderlyingModelControls()
    {
        var api = new CapturingAzureApi();
        var options = new AzureFoundryOptions(Enabled: true, ModelNames: new() { [CustomDeployment] = "gpt-6.1-sol" });
        var client = new AzureFoundryClient(api, options);
        var request = Request(CustomDeployment) with { Temperature = SamplingTemperature, ReasoningEffort = "high" };

        using var response = await client.SendChatAsync(request, CancellationToken.None);
        var sent = api.Requests[0];

        await Assert.That(sent.Model).IsEqualTo(CustomDeployment);
        await Assert.That(sent.MaxTokens).IsEqualTo(0);
        await Assert.That(sent.MaxCompletionTokens).IsEqualTo(CompletionLimit);
        await Assert.That(sent.Temperature).IsNull();
        await Assert.That(sent.ReasoningEffort).IsEqualTo("high");
    }

    /// <summary>Azure GPT-6 tools use Responses so reasoning remains enabled.</summary>
    /// <param name="stream">Whether the caller requests streaming.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GptToolsAndReasoningUseResponses(bool stream)
    {
        var api = new CapturingAzureApi();
        var client = new AzureFoundryClient(api, new AzureFoundryOptions(Enabled: true));
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        using var choice = JsonDocument.Parse("""{"type":"function","function":{"name":"ping"}}""");
        var request = Request(GptDeployment) with
        {
            Stream = stream,
            ReasoningEffort = "high",
            Tools = [new(new("ping", "Respond to a ping.", schema.RootElement.Clone()))],
            ToolChoice = choice.RootElement.Clone(),
        };

        using var response = await client.SendChatAsync(request, CancellationToken.None);

        await Assert.That(api.ResponseRequests.Count).IsEqualTo(1);
        await Assert.That(api.Requests.Count).IsEqualTo(0);
        var sent = api.ResponseRequests[0];
        await Assert.That(sent.GetProperty("max_output_tokens").GetInt32()).IsEqualTo(CompletionLimit);
        await Assert.That(sent.GetProperty("reasoning").GetProperty("effort").GetString()).IsEqualTo("high");
        await Assert.That(sent.GetProperty("tools")[0].GetProperty("type").GetString()).IsEqualTo("function");
        await Assert.That(sent.GetProperty("tool_choice").TryGetProperty("namespace", out _)).IsFalse();
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("ping");
        await Assert.That(body).Contains(stream ? "[DONE]" : "tool_calls");
    }

    /// <summary>Other Azure publishers retain their chat-completions token limit and reasoning history.</summary>
    /// <param name="model">The Azure chat deployment.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("grok-4.6")]
    [Arguments("DeepSeek-V4-Pro")]
    [Arguments("DeepSeek-V4.1-Flash")]
    [Arguments("Kimi-K2.7-Code")]
    public async Task NonOpenAiModelsKeepCompatibleControls(string model)
    {
        var api = new CapturingAzureApi();
        var client = new AzureFoundryClient(api, new AzureFoundryOptions(Enabled: true));
        var request = Request(model) with
        {
            ChatTemplateKwargs = new(EnableThinking: true),
            Extensions = new(CompletionLimit),
            TopK = 1,
            MinTokens = 1,
            ReasoningEffort = "high",
            Messages = [new("assistant", NimContent.FromText("answer"), ReasoningContent: "trace")],
        };

        using var response = await client.SendChatAsync(request, CancellationToken.None);
        var sent = api.Requests[0];

        await Assert.That(sent.MaxTokens).IsEqualTo(CompletionLimit);
        await Assert.That(sent.MaxCompletionTokens).IsNull();
        await Assert.That(sent.ChatTemplateKwargs).IsNull();
        await Assert.That(sent.Extensions).IsNull();
        await Assert.That(sent.TopK).IsNull();
        await Assert.That(sent.MinTokens).IsNull();
        await Assert.That(sent.ReasoningEffort).IsEqualTo("high");
        await Assert.That(sent.Messages[0].ReasoningContent).IsEqualTo("trace");
    }

    /// <summary>Non-reasoning GPT models keep sampling controls but omit reasoning effort.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task NonReasoningGptKeepsSamplingControls()
    {
        var api = new CapturingAzureApi();
        var client = new AzureFoundryClient(api, new AzureFoundryOptions(Enabled: true));
        var request = Request("gpt-4o") with { Temperature = SamplingTemperature, ReasoningEffort = "high" };

        using var response = await client.SendChatAsync(request, CancellationToken.None);

        await Assert.That(api.Requests[0].MaxCompletionTokens).IsEqualTo(CompletionLimit);
        await Assert.That(api.Requests[0].Temperature).IsEqualTo(SamplingTemperature);
        await Assert.That(api.Requests[0].ReasoningEffort).IsNull();
    }

    /// <summary>Provider timeout cancellation remains distinct from caller cancellation.</summary>
    /// <param name="stream">Whether the header or completion timeout applies.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProviderTimeoutCancelsTransport(bool stream)
    {
        var api = new CapturingAzureApi { WaitForCancellation = true };
        var client = new AzureFoundryClient(api, new AzureFoundryOptions(Enabled: true, ReadSeconds: 0, CompletionSeconds: 0));
        var request = Request("gpt-6-luna") with { Stream = stream };

        await Assert.That(async () => await client.SendChatAsync(request, CancellationToken.None)).Throws<OperationCanceledException>();
    }

    /// <summary>Caller cancellation reaches the upstream transport.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CallerCancellationReachesTransport()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var api = new CapturingAzureApi { WaitForCancellation = true };
        var client = new AzureFoundryClient(api, new AzureFoundryOptions(Enabled: true));

        await Assert.That(async () => await client.SendChatAsync(Request("gpt-6-luna"), cancellation.Token)).Throws<OperationCanceledException>();
    }

    /// <summary>A null conversation is rejected before sending.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task NullRequestThrows()
    {
        var client = new AzureFoundryClient(new CapturingAzureApi(), new AzureFoundryOptions());

        await Assert.That(async () => await client.SendChatAsync(null!, CancellationToken.None)).Throws<ArgumentNullException>();
    }

    /// <summary>Resource-root and v1 URLs resolve to the same transport path.</summary>
    /// <param name="baseUrl">The configured endpoint.</param>
    /// <param name="expected">The normalized endpoint.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("", "http://localhost/openai/v1/")]
    [Arguments("https://example.cognitiveservices.azure.com/", "https://example.cognitiveservices.azure.com/openai/v1/")]
    [Arguments("https://example.openai.azure.com/openai/v1", "https://example.openai.azure.com/openai/v1/")]
    [Arguments("https://example.services.ai.azure.com/openai/v1/", "https://example.services.ai.azure.com/openai/v1/")]
    public async Task EndpointAppendsV1OnlyOnce(string baseUrl, string expected) =>
        await Assert.That(new AzureFoundryOptions(BaseUrl: baseUrl).Endpoint()).IsEqualTo(expected);

    /// <summary>Azure and OpenAI services retain separate endpoint settings and credentials.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task HostingKeepsProvidersIndependent()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureFoundry:Enabled"] = "true",
            ["AzureFoundry:BaseUrl"] = "https://example.cognitiveservices.azure.com",
            ["AzureFoundry:ApiKey"] = "azure-key",
            [$"AzureFoundry:ModelNames:{CustomDeployment}"] = GptDeployment,
            ["OpenAi:Enabled"] = "true",
            ["OpenAi:ApiKey"] = "openai-key",
        }).Build();
        var services = new ServiceCollection();
        _ = services.AddClaudeNimProxy(configuration);
        await using var provider = services.BuildServiceProvider();
        var azure = provider.GetRequiredService<AzureFoundryClient>();
        var openAi = provider.GetRequiredService<OpenAiClient>();

        await Assert.That(azure.Options.ApiKey).IsEqualTo("azure-key");
        await Assert.That(openAi.Options.ApiKey).IsEqualTo("openai-key");
        await Assert.That(azure.Options.ModelNames?[CustomDeployment]).IsEqualTo(GptDeployment);
        await Assert.That(provider.GetRequiredService<MessageServices>().AzureClient).IsEqualTo(azure);
        await Assert.That(provider.GetRequiredService<CodexServices>().AzureClient).IsEqualTo(azure);
    }

    /// <summary>The generated Azure transport sends its resource key and preserves the v1 path.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task TransportUsesAzureKeyAndV1Path()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureFoundry:Enabled"] = "true",
            ["AzureFoundry:BaseUrl"] = "https://resource.example",
            ["AzureFoundry:ApiKey"] = "resource-key",
            ["OpenAi:ApiKey"] = "unrelated-key",
        }).Build();
        var services = new ServiceCollection();
        _ = services.AddClaudeNimProxy(configuration);
        using var handler = new CapturingAzureHandler();
        _ = services.AddHttpClient(UniqueName.ForType<IAzureFoundryApi>()).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        using var response = await provider.GetRequiredService<AzureFoundryClient>().SendChatAsync(Request(GptDeployment), CancellationToken.None);

        await Assert.That(handler.Address?.AbsoluteUri).IsEqualTo("https://resource.example/openai/v1/chat/completions");
        await Assert.That(handler.ApiKey).IsEqualTo("resource-key");
        await Assert.That(handler.Authorization).IsNull();
    }

    /// <summary>Legacy Azure aliases retain OpenAi configuration when the independent provider is disabled.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task DisabledAzureKeepsLegacyHostedConfiguration()
    {
        var services = new ServiceCollection();
        _ = services.AddClaudeNimProxy(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetRequiredService<MessageServices>().AzureClient).IsNull();
        await Assert.That(provider.GetRequiredService<CodexServices>().AzureClient).IsNull();
    }

    /// <summary>Builds a minimal Azure conversation.</summary>
    /// <param name="model">The deployment name.</param>
    /// <returns>The request sent by the test.</returns>
    private static NimChatRequest Request(string model) =>
        new(model, [new("user", NimContent.FromText("hello"))], CompletionLimit, false);

    /// <summary>Captures headers from the generated HTTP transport.</summary>
    private sealed class CapturingAzureHandler : HttpMessageHandler
    {
        /// <summary>Gets the request destination.</summary>
        internal Uri? Address { get; private set; }

        /// <summary>Gets the resource key sent in the request.</summary>
        internal string? ApiKey { get; private set; }

        /// <summary>Gets the bearer authorization header, if supplied.</summary>
        internal string? Authorization { get; private set; }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Address = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("api-key", out var values) ? string.Join(",", values) : null;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>Captures Azure requests without contacting a cloud endpoint.</summary>
    private sealed class CapturingAzureApi : IAzureFoundryApi
    {
        /// <summary>The tool-call event stream returned by the Azure Responses test transport.</summary>
        private const string ToolEvents =
            "data: {\"type\":\"response.output_item.added\",\"output_index\":0,\"item\":{\"type\":\"function_call\",\"call_id\":\"call-1\",\"name\":\"ping\",\"arguments\":\"\"}}\n\n"
            + "data: {\"type\":\"response.function_call_arguments.delta\",\"output_index\":0,\"delta\":\"{}\"}\n\n"
            + "data: {\"type\":\"response.completed\",\"response\":{\"id\":\"response-1\",\"model\":\"gpt-6-astra\",\"status\":\"completed\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"call-1\",\"name\":\"ping\",\"arguments\":\"{}\"}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1,\"total_tokens\":2}}}\n\n";

        /// <summary>Gets the requests sent by the client.</summary>
        internal List<NimChatRequest> Requests { get; } = [];

        /// <summary>Gets the Responses API requests sent by the client.</summary>
        internal List<JsonElement> ResponseRequests { get; } = [];

        /// <summary>Gets a value indicating whether the transport waits for cancellation.</summary>
        internal bool WaitForCancellation { get; init; }

        /// <inheritdoc/>
        public async Task<HttpResponseMessage> SendChatAsync(NimChatRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new(HttpStatusCode.OK);
        }

        /// <inheritdoc/>
        public Task<HttpResponseMessage> SendResponsesAsync(JsonElement request, CancellationToken cancellationToken)
        {
            ResponseRequests.Add(request.Clone());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ToolEvents, Encoding.UTF8, "text/event-stream") });
        }
    }
}
