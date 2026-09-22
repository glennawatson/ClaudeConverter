// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeNim.Aot.Anthropic;
using ClaudeNim.Aot.Anthropic.Streaming;
using ClaudeNim.Aot.Endpoints;
using ClaudeNim.Aot.Nvidia;

namespace ClaudeNim.Aot.Serialization;

/// <summary>The compile-time JSON contracts for every type crossing the proxy's wire.</summary>
/// <remarks>
/// <para>
/// Native AOT has no reflection to fall back on, so every payload must be declared here.
/// <see cref="JsonSourceGenerationMode.Default"/> emits both the metadata contract used for
/// reading and the serialization fast path used for writing, which streams a payload straight
/// to the output buffer instead of walking property metadata for each event. On a streamed turn
/// that path runs once per delta, so it is the one that matters.
/// </para>
/// <para>
/// The project also sets <c>JsonSerializerIsReflectionEnabledByDefault=false</c>, which turns a
/// contract missing from this list into an exception during development rather than a trimming
/// warning at publish time.
/// </para>
/// <para>
/// One type here carries a custom converter: <see cref="Anthropic.MessageContent"/>, whose wire
/// form is either a string or an array and cannot be expressed without one. A converter forfeits
/// the fast path for the type that declares it, but not for the types containing it, and this one
/// is only ever read — so every payload the proxy writes still takes the generated path.
/// </para>
/// </remarks>
[System.Diagnostics.DebuggerDisplay("ProxyJsonContext: {ToString(),nq}")]
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(MessagesRequest))]
[JsonSerializable(typeof(MessagesResponse))]
[JsonSerializable(typeof(TokenCountRequest))]
[JsonSerializable(typeof(TokenCountResponse))]
[JsonSerializable(typeof(AnthropicMessage))]
[JsonSerializable(typeof(MessageContent))]
[JsonSerializable(typeof(ContentBlock))]
[JsonSerializable(typeof(List<ContentBlock>))]
[JsonSerializable(typeof(ToolDefinition))]
[JsonSerializable(typeof(ThinkingConfig))]
[JsonSerializable(typeof(OutputConfig))]
[JsonSerializable(typeof(ImageSource))]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(ModelDescriptor))]
[JsonSerializable(typeof(ModelListResponse))]
[JsonSerializable(typeof(ModelCapabilities))]
[JsonSerializable(typeof(CapabilitySupport))]
[JsonSerializable(typeof(ContextManagementCapability))]
[JsonSerializable(typeof(EffortCapability))]
[JsonSerializable(typeof(ThinkingCapability))]
[JsonSerializable(typeof(ThinkingTypes))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ErrorDetail))]
[JsonSerializable(typeof(StreamMessage))]
[JsonSerializable(typeof(StreamMessageStart))]
[JsonSerializable(typeof(StreamContentBlockStart))]
[JsonSerializable(typeof(StreamContentBlockDelta))]
[JsonSerializable(typeof(StreamContentBlockStop))]
[JsonSerializable(typeof(StreamDelta))]
[JsonSerializable(typeof(StreamStopDetail))]
[JsonSerializable(typeof(StreamMessageDelta))]
[JsonSerializable(typeof(StreamMessageStop))]
[JsonSerializable(typeof(NimChatRequest))]
[JsonSerializable(typeof(NimExtensions))]
[JsonSerializable(typeof(NimChatCompletion))]
[JsonSerializable(typeof(NimChatCompletionChunk))]
[JsonSerializable(typeof(NimStreamError))]
[JsonSerializable(typeof(NimChatMessage))]
[JsonSerializable(typeof(NimContent))]
[JsonSerializable(typeof(NimContentPart))]
[JsonSerializable(typeof(NimImageUrl))]
[JsonSerializable(typeof(NimTool))]
[JsonSerializable(typeof(NimToolCall))]
[JsonSerializable(typeof(NimUsage))]
[JsonSerializable(typeof(NimModel))]
[JsonSerializable(typeof(NimModelList))]
[JsonSerializable(typeof(HealthReport))]
public sealed partial class ProxyJsonContext : JsonSerializerContext;
