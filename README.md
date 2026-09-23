# ClaudeConverter

An Anthropic Messages API compatibility layer, compiled ahead of time to a single native binary.
Point Claude Code at it and the session runs on a different model provider underneath.

NVIDIA NIM is the first provider it speaks, and the transport is abstracted behind an interface so
another one can be added without disturbing the Anthropic-facing side. It is a C# rewrite of the
ideas in [cc-nim](https://github.com/diyism/cc-nim) and
[claude-nim](https://github.com/claude-server/claude-nim), both MIT licensed.

## Scope

NVIDIA NIM only, today. This is not a multi-provider router yet and does not pretend to be one —
the abstraction exists so a second provider is an addition rather than a rewrite.

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
| `Timeouts` | Upstream connect, header-wait, body-read, and stream-idle timeouts |
| `Retries` | Retry attempts and exponential-backoff delay for transient upstream failures |
| `Optimizations` | The local fast paths for Claude Code's housekeeping requests |

### Model discovery

`GET /v1/models` returns the documented shape — `max_input_tokens`, `max_tokens`, `created_at`,
and a nested `capabilities` object covering effort levels, thinking, vision, and structured
outputs — with real pagination (`before_id`, `after_id`, `limit`, and `has_more`/`first_id`/
`last_id` describing the page actually returned). `GET /v1/models/{id}` resolves a single model,
returning `not_found_error` when the credential cannot reach it.

Every chat-capable model the credential can reach is advertised. Reasoning-capable models appear
twice — once normally, once as `(no thinking)` — because a reasoning model with its trace off is a
genuinely different choice for a coding session. The Claude tier names (`claude-opus-5-5`,
`claude-opus-5`, `claude-sonnet-5`, `claude-haiku-4-5`) are also advertised and route according to
`ModelRouting`. A name this proxy does not advertise still routes: anything carrying `opus`,
`fable` or `mythos` takes the Opus tier — those two sit above Opus in Anthropic's line-up and this
proxy has no tier above it, so a client naming one gets the most capable model configured.

A turn NVIDIA's own filters decline is reported the way a Claude 5.5-era client expects one:
`stop_reason: "refusal"` with a `stop_details` object, rather than `end_turn` with nothing in it.
The distinction matters because those clients check the stop reason before reading the content, and
an empty `end_turn` reads as a model with nothing to say — which for a coding session ends the work
rather than retrying it somewhere else.

Capabilities are reported from what this proxy can actually deliver over NIM, not copied from
Anthropic's own listing: `output_config.effort` maps to NIM's top-level `reasoning_effort`, with
Claude's five levels folded onto NIM's three (`xhigh` and `max` both become `high`); an explicit
`thinking.budget_tokens` goes through `nvext.max_thinking_tokens` where a model that lacks it is
rejected up front rather than failing mid-generation; structured outputs are supported through
`response_format` with a JSON schema; images are forwarded to a model the catalogue states has
vision, and replaced with a placeholder for one that does not. Service-level Anthropic features
with no NIM equivalent — batching, citations, code execution, server-side context management, PDF
input — are reported as unsupported.

### Local fast paths

A coding session sends more than the turns you type. It probes the credential, asks for a
conversation title and typeahead suggestions, and asks which prefix a shell command should be
permission-matched on and which files it read. The last two have mechanical answers and are
computed locally; the rest are content-free. The detection rules come from
[cc-nim](https://github.com/diyism/cc-nim), which derived them from the prompts Claude Code
actually sends, and they match on structural markers (`<policy_spec>`, `[SUGGESTION MODE:`,
`<filepaths>`) rather than English prose.

Command-prefix detection feeds a permission decision, so it errs toward asking: a command carrying
a substitution reports `command_injection_detected` rather than a prefix taken from text that is
not what will run. Turn any of these off individually if you would rather the model answered.

### Tool calling

Anthropic's `tool_choice` vocabulary (`auto`/`any`/`none`/named tool) is fully translated onto
NIM's OpenAI-style equivalent, including forced tool use. A tool schema is sanitised of boolean
subschemas NIM's validator rejects, and any parameter name that collides with NIM's own
function-calling wrapper (`type` is the known case) is renamed on the way out and restored on the
way back, invisibly to both ends.

Models that render a tool call as marker tokens inside their answer text instead of filling in the
structured field — several of the Kimi and DeepSeek families — are still supported: the markers are
recovered and turned into ordinary `tool_use` blocks, streaming-safe across chunk boundaries.

### Reasoning

`<think>` tags some model families wrap reasoning in are split out of `content` and re-emitted as
`thinking` blocks rather than reaching the client as literal text. A tag more than 200 characters
into the answer is treated as prose about tags rather than reasoning, so asking a model to explain
how `<think>` works does not eat the rest of its reply.

Reasoning controls are retried away, not just declined: a model that rejects a reasoning field is
retried once without it, and if a model rejects replayed `reasoning_content` on a later turn, that
trace is stripped from history so the conversation keeps going rather than failing on every
subsequent turn.

### Reliability

The pooled HTTP client carries no fixed timeout, because a single bound cannot fit both shapes of
call: a streamed body is bounded by idle time (no bytes for `Timeouts:StreamIdleSeconds`), while
the wait for a streamed response's headers, and a non-streamed body once headers arrive, are each
bounded by `Timeouts:ReadSeconds`. A non-streamed call gets a bound of its own,
`Timeouts:CompletionSeconds`, because NIM withholds even the status line until the whole answer is
generated — so the wait for its headers is the generation, and a reasoning model working through a
long prompt routinely spends longer on it than any header wait should allow.

Transient upstream failures (`429`, `500`, `502`, `503`, `504`) are retried with hand-rolled
exponential backoff and equal jitter — half the window waited, the rest spread randomly — honouring
a `Retry-After` header when the upstream sends one. The budget is five attempts over roughly
fifteen seconds, which is what NVIDIA's `Service temporarily overloaded` actually takes to clear. A
streamed turn that failed before producing anything backs off on the same schedule before it is
asked for again, rather than re-asking in the same instant. A deadline this proxy set itself is the
one failure never retried: the model that could not finish inside it will not finish inside the
next one, and spending the budget twice only moves the failure to a client that has given up
waiting.

A rejected request (`400`/`500`) is instead retried with progressively less of it — reasoning
controls first, then replayed reasoning — since a rejection means the upstream will never accept
that exact body. A failure that arrives inside an already-successful streamed response is surfaced
as an Anthropic `error` event rather than an empty turn.

### Fallback models

NVIDIA's free tier saturates one model at a time, and which one rotates through the day: the tier
that answers every request for an hour is the one returning `429` for the next. Each Claude tier can
name an ordered chain of models to serve its turns when the one it routes to cannot —
`ModelRouting:OpusFallbacks`, `SonnetFallbacks`, `HaikuFallbacks`, `DefaultFallbacks`,
comma-separated, or the flat `OPUS_FALLBACK_MODELS` form.

The chain is walked on one attempt a model, because waiting out a busy model cannot beat asking one
that is not busy — the whole chain costs about as long as a single retry would have. Only when every
model in it is unavailable does the turn go back to the one it routed to and spend the full retry
budget there, which is what a deployment naming no chain keeps doing. A streamed turn that failed
before it produced anything re-issues down the chain too, so a saturated model gets one chance to
start a stream rather than all of them.

Order a chain by how fast its models answer, not by how good they are. A fallback is only worth
having if it answers inside the client's patience, and the difference is not subtle: on a
112,000-character Auto Mode classifier prompt, `nemotron-3-super` and `deepseek-v4.1-flash` both
answer in about 3.5 seconds where `nemotron-3.5-lightning` takes 61 — it is a reasoning model, and
it reasons over the whole prompt before saying anything. A chain that lands on it serves the turn
correctly and still loses, because Claude Code gave up two minutes ago.

What the chain does not cover is a rejected request: a `400` is a property of the body, not of the
model's availability, and the next model would refuse it for the same reason. Nor does a model
named by its gateway identifier get a chain — the client picked that model from the listing this
proxy advertised, and answering as a different one is not a substitution it asked for. Every
substitution is logged at warning naming both models, because the tier the client asked for is now
being served by something else, usually weaker.

### Following a turn through the log

Several turns are served at once and their lines interleave, so every line a turn writes is nested
in a logging scope naming it — `turn msg_a1b2… for claude-sonnet-5` — including the lines written by
the transport and the stream translator, which are never handed the identifier. `IncludeScopes` is
on in the shipped `appsettings.json`; without it the scope is still attached to the structured
properties but the console formatter does not print it.

Every turn ends on a line, which is what makes a stuck one visible: the answer and its status
(1019), a streamed turn's end and duration (1030), the upstream giving up (1015), every model in the
chain being unavailable (1028), or the client walking away before an answer arrived (1029). Each
names the NIM model it happened to, so a model that is quietly failing shows up as a pattern rather
than as noise spread across tiers.

Started by systemd, the proxy writes each entry on one line with the priority prefix the journal
reads, so `journalctl` colours a warning yellow and an error red and `journalctl -p warning` finds
them — without it every line is filed as plain information whatever it says. That is picked by
detecting systemd rather than configured, and a `Logging:Console:FormatterName` in configuration
overrides it either way. One message is written at error: NVIDIA refusing the credential the proxy
itself runs on, which is the only failure here that no turn can recover from and an operator has to
fix.

## Endpoints

| Route | Notes |
|---|---|
| `POST /v1/messages` | Streaming and non-streaming, tools, reasoning, vision |
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
- Retry backoff is hand-rolled rather than taken from a resilience library, so the whole policy is
  one predicate and one delay calculation with no dependency the binary does not need.

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
