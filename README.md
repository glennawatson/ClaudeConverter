# Claude Nim AOT

An Anthropic Messages API in front of NVIDIA NIM, compiled ahead of time to a single native
binary. Point Claude Code at it and the session runs on NVIDIA's free models.

It is a C# rewrite of the ideas in [cc-nim](https://github.com/diyism/cc-nim) and
[claude-nim](https://github.com/claude-server/claude-nim), both MIT licensed — written to fix the
things those proxies get wrong rather than to reproduce them.

## What it fixes

The headline problem with the existing proxies is that **model discovery does not work**, so a
client's model picker shows nothing usable. Several separate defects add up to that, and each is
fixed here:

| Defect | What it causes | Fixed by |
|---|---|---|
| `/v1/models` entries in an invented shape | Clients cannot size a request, or ignore the entry | The documented shape: `max_input_tokens`, `max_tokens`, `created_at`, and the real nested `capabilities` object |
| `has_more` hardcoded to `false` | Every model past the first page is stranded | A real cursor — `before_id`, `after_id`, `limit`, and `has_more`/`first_id`/`last_id` that describe the page actually returned |
| No `GET /v1/models/{id}` | A client resuming with a stored model id cannot confirm it | Implemented, with a proper `not_found_error` |
| `tool_choice` pinned to `"auto"` | Forced tool use is silently dropped | Full translation of `auto`/`any`/`none`/`tool` |
| `<think>` reasoning discarded | GLM and DeepSeek reasoning is thrown away | Split out of `content` and re-emitted as `thinking` blocks |
| `chat_template_kwargs` nested under `extra_body` | Reasoning control is silently ignored | Sent as the top-level member NIM actually reads |
| No `stream_options.include_usage` | Token counts on streamed turns are guesswork | Requested up front, so real counts are reported |
| Mid-stream failures swallowed | The client sees an empty turn, not an error | An `error` event carrying the upstream's own reason |

Two of those were found by running this proxy against the live API, not by reading code:

- NVIDIA's `reasoning_budget` chat-template argument is **rejected by Nemotron 3 Ultra's runner**.
  Deriving it from `max_tokens` — which looks harmless — made every reasoning turn come back empty,
  and on a streamed turn it failed *inside* a 200 response where no status-code retry could catch
  it. It is now only forwarded when the caller explicitly asks for one.
- A leading `/` on a Refit route is an absolute path under RFC 3986, which discards the `/v1` the
  base address is rooted at. The routes are relative for that reason.

## Scope

NVIDIA NIM only. The transport is abstracted behind an interface, but this is not a multi-provider
router and does not pretend to be one.

## Requirements

- .NET 10 or 11 SDK
- An NVIDIA API key (the free tier is enough)

## Quick start

```bash
dotnet publish src/ClaudeNim.Aot/ClaudeNim.Aot.csproj -c Release -r linux-x64

export NVIDIA_API_KEY="nvapi-..."
./src/ClaudeNim.Aot/bin/Release/net10.0/linux-x64/publish/ClaudeNim.Aot
```

Then point Claude Code at it:

```bash
export ANTHROPIC_BASE_URL="http://localhost:5000"
export ANTHROPIC_AUTH_TOKEN="whatever-you-set-in-Authentication:AuthToken"
claude
```

## Configuration

Everything can be set three ways, in increasing precedence: `appsettings.json`, a flat environment
variable, or the .NET-style path form. Only the API key is required.

```bash
# Flat names, matching what the Node and Python proxies use
export NVIDIA_API_KEY="nvapi-..."
export BIG_MODEL="nvidia/nemotron-3-ultra-550b-a55b"
export SMALL_MODEL="z-ai/glm-5.3-flash"

# Or the .NET path form, which reaches every setting without being enumerated
export NvidiaNim__MaxTokens=8192
export CLAUDENIM_ModelRouting__EnableThinking=false
```

| Section | Purpose |
|---|---|
| `NvidiaNim` | Credential, base address, and sampling defaults |
| `Authentication` | The shared secret clients must present; empty disables the check |
| `ModelRouting` | Which NIM model serves each Claude tier, and whether it reasons |
| `ModelCatalog` | Listing cache lifetime and the sizes reported for unknown models |
| `RateLimits` | Concurrency and sliding-window limits applied before the upstream |
| `Timeouts` | Upstream connect and read timeouts |
| `Optimizations` | The local fast paths for Claude Code's housekeeping requests |

### Choosing a model

Every chat-capable model the credential can reach is advertised. Reasoning-capable models appear
twice — once normally, once as `(no thinking)` — because a reasoning model with its trace off is a
genuinely different choice for a coding session.

The Claude tier names (`claude-opus-5`, `claude-sonnet-5`, `claude-haiku-4-5`) are also advertised
and route according to `ModelRouting`.

## Endpoints

| Route | Notes |
|---|---|
| `POST /v1/messages` | Streaming and non-streaming, tools, reasoning |
| `POST /v1/messages/count_tokens` | Answered locally; NIM exposes no tokenizer |
| `GET /v1/models` | Paginated with `before_id`, `after_id`, `limit` |
| `GET /v1/models/{id}` | Single model, including its capabilities |
| `GET /health` | Unauthenticated; reports how many models are reachable |
| `GET /health/live` | Liveness only |

## Native AOT

The whole thing publishes with zero trim or AOT warnings to an ~18 MB self-contained binary.
That is not free — it shapes the design:

- Every payload is declared in a single `JsonSerializerContext`, and
  `JsonSerializerIsReflectionEnabledByDefault=false` turns a missing contract into an error during
  development rather than a surprise at publish time.
- Content blocks are one flat record with optional members rather than a type hierarchy, because
  the polymorphic resolver cannot be generated ahead of time.
- The upstream transport is a Refit interface registered with `AddRefitGeneratedClient`, so the
  request-building code is generated at compile time and the JSON goes through the same context.
- Logging goes through source-generated `LoggerMessage` delegates.

## Development

```bash
dotnet build src/ClaudeNim.Aot/ClaudeNim.Aot.csproj
dotnet test src/tests/ClaudeNim.Aot.Tests/ClaudeNim.Aot.Tests.csproj
```

Analyzers run as errors — StyleSharp, PerformanceSharp, SecuritySharp and PublicApiSharp. The
house rules that shape the code: records over classes, record structs where they fit, no base or
abstract classes anywhere, concrete `List<T>` rather than collection interfaces, and XML
documentation on every member including private ones.

## License

MIT — see [LICENSE](LICENSE). Based on [cc-nim](https://github.com/diyism/cc-nim) (MIT).
