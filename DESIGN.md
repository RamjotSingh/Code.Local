# Code.Local — Design & Decisions

> Living design doc for **Code.Local**, a turnkey CLI that sets up local AI coding
> models for GitHub Copilot. This captures the *why* behind the project: the
> backend landscape, the Copilot BYOK wiring, prompt-caching behavior, hardware
> guidance, and the architecture decision records.

**Status:** v0.1 (solo mode) implemented. v0.2 (gateway mode) planned.

---

## Table of contents

1. [What Code.Local is](#1-what-codelocal-is)
2. [Problem & context](#2-problem--context)
3. [How Copilot connects to custom models (BYOK)](#3-how-copilot-connects-to-custom-models-byok)
4. [Wire-format comparison: Chat Completions vs Responses vs Anthropic](#4-wire-format-comparison)
5. [Backend landscape](#5-backend-landscape)
6. [Prompt / prefix caching](#6-prompt--prefix-caching)
7. [Hardware & model guidance](#7-hardware--model-guidance)
8. [Code.Local architecture](#8-codelocal-architecture)
9. [Decision records (ADRs)](#9-decision-records-adrs)
10. [Roadmap](#10-roadmap)
11. [Open questions](#11-open-questions)

---

## 1. What Code.Local is

A cross-platform .NET 10 CLI that wires up the three layers needed to run **GitHub
Copilot against local models**, without hand-editing config:

1. **Model host** — pulls and tunes a curated coding model (via Ollama today).
2. **Client config** — points the Copilot CLI at the local endpoint.
3. **Distribution** — generates a reusable installer so others can connect to the
   same gateway in one step.

Two intended modes:

- **Solo (v0.1):** Ollama + Copilot directly, no gateway. The common case for an
  individual on one machine.
- **Gateway (v0.2):** a LiteLLM gateway in front for multi-model routing, per-user
  keys, and quotas — the org / power-user case.

---

## 2. Problem & context

- **AI coding costs are rising.** Teams increasingly want local/self-hosted models
  for cost, privacy, offline use, and control.
- **Copilot now supports Bring-Your-Own-Key (BYOK).** As of late 2025 / 2026 this
  exists at the individual and enterprise/org level, so Copilot can be pointed at a
  custom OpenAI-compatible (or Azure/Anthropic) endpoint — including local models.
- **The plumbing is fiddly.** Standing up a model host, writing a gateway config,
  and setting the right client env vars across machines is exactly the friction
  Code.Local removes.

---

## 3. How Copilot connects to custom models (BYOK)

Copilot CLI is configured via environment variables:

| Variable | Required | Notes |
| --- | --- | --- |
| `COPILOT_PROVIDER_BASE_URL` | yes | Provider/gateway base URL |
| `COPILOT_PROVIDER_TYPE` | no | `openai` (default), `azure`, `anthropic` |
| `COPILOT_PROVIDER_API_KEY` | no | Not needed for an unauthenticated local Ollama |
| `COPILOT_MODEL` | yes | Model identifier to request |
| `COPILOT_PROVIDER_WIRE_API` | no | `completions` (default) or `responses` (provider-dependent) |
| `COPILOT_PROVIDER_MAX_PROMPT_TOKENS` | no | Prompt-token budget; `init` derives it from `num_ctx` so Copilot doesn't assume a 128K window |
| `COPILOT_PROVIDER_MAX_OUTPUT_TOKENS` | no | Output-token budget; `init` derives it from `num_ctx` (¼ of context, capped at 8192) |

**Provider types**

| Type | Compatible services |
| --- | --- |
| `openai` | OpenAI, **Ollama**, vLLM, Foundry Local, any OpenAI Chat-Completions-compatible endpoint |
| `azure` | Azure OpenAI Service |
| `anthropic` | Anthropic (Claude) — and Ollama's `/v1/messages` in principle |

**Model requirements:** must support **tool calling** and **streaming**; a context
window ≥128k is recommended (often not achievable on small local GPUs — see §7).

**Important caveat (see §11):** BYOK reliably covers agent/chat/CLI
traffic. Inline (every-keystroke) completion may still default to GitHub-hosted
models on some surfaces. This matters for any future DLP/observability feature and
for "is my code actually going local?" guarantees.

---

## 4. Wire-format comparison

### Chat Completions (`/v1/chat/completions`)
- Ubiquitous, stable, broadest ecosystem and provider support.
- Stateless: client resends history each turn.
- Most local models are fine-tuned on this exact tool-call schema → **highest
  tool-calling reliability** locally.

### Responses (`/v1/responses`)
- Newer OpenAI API; item-based schema, typed streaming events, optional server-side
  state (`previous_response_id`), built-in hosted tools (cloud only).
- **On Ollama:** supported since v0.13.3 but **stateless only** (no
  `previous_response_id` / `conversation`); hosted tools are cloud-only and absent.
- Ollama's own Copilot CLI integration docs recommend `COPILOT_PROVIDER_WIRE_API=responses`.

### Anthropic Messages (`/v1/messages`)
- Native `system` field, `tool_use`/`tool_result` blocks, `thinking` blocks.
- **On Ollama:** supported (good for thinking models), but **no prompt caching
  (`cache_control`), no `tool_choice`, `budget_tokens` not enforced, base64 images
  only**.
- Copilot can target it via `COPILOT_PROVIDER_TYPE=anthropic`.

### What this means for local + Copilot
The hosted-cloud advantages of Responses/Anthropic (server state, hosted tools,
cost-discount caching) **do not materialize against a local backend** — Copilot
resends history anyway. So the wire choice mostly affects **streaming/tool-call
ergonomics**, not capability — and locally, the chat-completions schema is what most
models are fine-tuned on, so it yields the most reliable tool calls.

**Default for Code.Local:** `openai` provider + `completions` (chat-completions) wire. Although
Ollama's Copilot docs mention `responses`, its `/v1/responses` support is newer and
experimental, and small local models were observed emitting malformed tool calls on it
(missing required arguments); chat-completions is the mature, highest-reliability path
locally. `--wire responses` remains available for models/setups that prefer it.

---

## 5. Backend landscape

Local inference backends and where each genuinely wins. The decision is driven by
**six axes**: VRAM tier, concurrency, hardware platform, OS, workload shape, and
operational needs.

| Backend | Sweet spot | Why pick it |
| --- | --- | --- |
| **Ollama** | Single-user, any OS, ≤~12 GB, convenience-first | Model registry, auto chat-template + tool-parser, native installers, the documented Copilot path. **Default for Code.Local.** |
| **llama.cpp** | Single-user, max control, edge/CPU, persistence | The engine under Ollama; adds `--prompt-cache` file (persistence), speculative decoding, advanced samplers — at the cost of owning templates/parsers yourself |
| **vLLM** | Linux, NVIDIA ≥12–16 GB, **multi-user**, production | Block-level prefix caching across many users, `tool_choice`, LoRA, batching, stateful Responses |
| **SGLang** | NVIDIA, agent/reasoning at scale, DeepSeek family | RadixAttention prefix caching; bleeding-edge — but no usable Windows story |
| **LM Studio** | Single-user GUI, Mac/Windows | Easy onboarding, MLX backend on Mac (llama.cpp under the hood) |
| **MLX / mlx-lm** | Apple Silicon | 1.5–2× llama.cpp on M-series; Mac only |
| **Foundry Local** | Windows, no NVIDIA / iGPU / NPU | ONNX-optimized, native Windows; smaller catalog |
| **TGI / TensorRT-LLM / Triton** | HF shops / H100-class / enterprise serving | Production/datacenter tiers |
| **LiteLLM** | **Proxy**, not a backend | Wire translation, routing, fallback, virtual keys, budgets, observability — sits *in front* of any backend (Code.Local v0.2) |

### When does vLLM beat Ollama? (any one trigger)
- **≥~12 GB VRAM**, or
- **2+ concurrent users**, or
- You need APC at scale / `tool_choice` / LoRA serving / production observability.

Below that — single user, modest VRAM, no special needs — **Ollama wins** on
ergonomics for ~equivalent single-stream performance.

### Performance: single-stream vs throughput (the key distinction)

vLLM is a **throughput** engine, not a **single-stream latency** engine. For one user
(one request at a time — a solo Copilot session) vLLM does **not** outpace Ollama;
it's roughly equal or marginally slower (Python + scheduler overhead). Its large wins
are **aggregate throughput under concurrency**.

Approximate pattern from 2026 benchmarks (Llama-3 class, consumer NVIDIA; absolute
numbers vary, the pattern is robust):

| Scenario | llama.cpp | Ollama | vLLM |
| --- | --- | --- | --- |
| 1 user (single-stream tok/s) | ~36 | ~34 | ~32 |
| 10 concurrent users (aggregate tok/s) | ~38 | ~36 | ~250–300 |
| 50 users, 8B (aggregate) | stalls | stalls | ~900+ |

**On better GPUs:** more VRAM / bandwidth / multiple GPUs speeds up *both* engines
similarly for single-stream — the solo gap stays small. A bigger GPU unlocks vLLM's
**multi-user** dimension (more concurrent sequences for batching, APC across users,
tensor parallelism for big models); it amplifies the throughput lead, not single-user
speed. The only single-stream places vLLM can edge ahead: TTFT on very long prompts
(chunked prefill) and speculative decoding (EAGLE) — both advanced, and llama.cpp has
the latter too.

### Platform quick-routing
- Apple Silicon → MLX / LM Studio (vLLM doesn't run on macOS).
- Windows, no NVIDIA → Foundry Local.
- Windows + NVIDIA + want vLLM → WSL2, or **native via
  [SystemPanic/vllm-windows](https://github.com/SystemPanic/vllm-windows)** (prebuilt
  wheels, CUDA 13 + Blackwell, even multi-GPU; setup heavier than Ollama).
  SGLang on Windows is not viable.
- Linux + NVIDIA ≥16 GB → vLLM / SGLang.
- Multi-backend routing / cloud fallback → LiteLLM in front.

---

## 6. Prompt / prefix caching

> **Note:** Ollama has prefix caching **enabled by default** (Ollama issue
> [#1573 "Enable prompt cache"](https://github.com/ollama/ollama/issues/1573),
> completed) — a point that is often misunderstood. The single-user benefit is automatic.

**What Ollama actually does:** keeps the model loaded (`keep_alive`) and reuses the
**longest matching prompt prefix** already in the in-memory KV cache, prefilling only
the new tokens. For a single user with a stable system prompt + growing history —
i.e. Copilot's agent loop — this delivers the main caching benefit automatically.

| Capability | Ollama | vLLM (APC) | llama.cpp |
| --- | --- | --- | --- |
| Prefix reuse across requests | ✅ default | ✅ default | ✅ default |
| Many prefixes / concurrent users (block-level) | ⚠️ slot-limited (`OLLAMA_NUM_PARALLEL`) | ✅✅ radix/block sharing | ⚠️ limited |
| Persists across restart | ❌ in-memory | ❌ in-memory | ✅ `--prompt-cache <file>` |
| Degrades on context-shift past `num_ctx` | yes | yes | yes |

**Implications for Code.Local:**
- **Solo tier:** Ollama's default prefix caching already covers it. This
  *strengthens* "Ollama as default" and *removes* prompt caching as a reason to
  reach for vLLM/llama.cpp on a single machine.
- **vLLM's real edge** is **multi-user / concurrent** block-level prefix sharing —
  i.e. the gateway tier, not "Ollama can't cache."
- **llama.cpp's unique add** narrows to **persistence across restarts**
  (`--prompt-cache` file). Niche → lower priority as an extra backend.

---

## 7. Hardware & model guidance

### Reference machine
NVIDIA RTX 4050 Laptop GPU, **6 GB VRAM**, 32 GB RAM, Windows. Ada Lovelace
(Tensor Cores, FlashAttention 2) but the **6 GB VRAM is the binding constraint**.

### Bundled models
A curated ladder of tool-calling-capable instruct models (tool calling is required by
Copilot's agent loop). `init` recommends the highest-quality entry that fits detected
VRAM; `--model` overrides.

| Model | Params | Quant | ~Disk | Min VRAM | Default ctx | License | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `qwen3.5:2b-q4_K_M` | 2B | Q4_K_M | 1.9 GB | ~3 GB | 32k | Apache-2.0 | Smallest; ~4 GB GPUs (weakest) |
| `qwen3.5:2b` | 2B | Q8 | 2.7 GB | ~3.5 GB | 32k | Apache-2.0 | Qwen3.5 (newer gen) + tools |
| `qwen3.5:4b` | 4B | Q4_K_M | 3.4 GB | ~5 GB | 32k | Apache-2.0 | Fits ~6 GB GPUs (default) |
| `qwen3.5:4b-q8_0` | 4B | Q8 | 5.3 GB | ~7 GB | 32k | Apache-2.0 | Q8 4B: better agentic tool-calling |
| `qwen3.5:9b` | 9B | Q4_K_M | 6.6 GB | ~8 GB | 32k | Apache-2.0 | Strongest sub-10B Qwen3.5 |
| `gemma4:12b-it-qat` | 12B | QAT int4 | 7.2 GB | ~10 GB | 32k | Gemma | Google Gemma 4; QAT int4 ≈ bf16 quality at Q4 size |
| `qwen3.5:9b-q8_0` | 9B | Q8 | 11 GB | ~13 GB | 32k | Apache-2.0 | Q8 9B: highest sub-12B reliability |
| `gpt-oss:20b` | 20B MoE | MXFP4 | 14 GB | ~16 GB | 32k | Apache-2.0 | Reasoning + tools |
| `gemma4:12b-it-q8_0` | 12B | Q8 | 13 GB | ~16 GB | 32k | Gemma | Gemma 4 12B max fidelity (QAT is nearly as good, smaller) |
| `devstral:24b` | 24B | Q4_K_M | 14 GB | ~16 GB | 32k | Apache-2.0 | Mistral agentic coder |
| `qwen3-coder:30b` | 30B MoE | Q4_K_M | 19 GB | ~20 GB | 32k | Apache-2.0 | Dedicated agentic coder |
| `qwen3.5:27b-int4` | 27B | int4 | 16 GB | ~20 GB | 32k | Apache-2.0 | Bigger model at Q4 (text-only) |
| `gemma4:26b` | 26B MoE | Q4_K_M | 18 GB | ~22 GB | 32k | Gemma | Google Gemma 4 MoE (4B active); also has a 28 GB Q8 |
| `qwen3.5:35b-a3b-int4` | 35B MoE | int4 | 20 GB | ~24 GB | 32k | Apache-2.0 | MoE ~3B active (fast) |
| `gpt-oss:120b` | 120B MoE | MXFP4 | 65 GB | ~80 GB | 32k | Apache-2.0 | Large open; big-rig only |
| `qwen3-coder:480b` | 480B MoE | Q4_K_M | 290 GB | ~300 GB | 32k | Apache-2.0 | Frontier; 512 GB-class rigs |

Selection rationale: the small/mid tiers use **Qwen3.5** (`2b`/`4b`/`9b`) — a generation
newer than Qwen2.5-Coder and tool-calling-capable, so it wins at equal size despite not
being "Coder"-tuned. Quantization strategy: a larger model at ~Q4 beats a smaller model
at Q8 down to the Q4 floor (below that, coding degrades), so the high tiers use
`qwen3.5:27b-int4` and the MoE `qwen3.5:35b-a3b-int4` (~3B active → fast) — text-only
int4 to drop the unused vision weights. But Q4's quality gap *widens for agentic
workloads* (multi-turn tool-calling and long context accumulate quantization error, where
Q8 stays ~99% of FP16), so quantization is a **first-class catalog axis**, not a hidden
default: the dense small tiers ship both their default Q4_K_M and an explicit **Q8
variant** (`qwen3.5:4b-q8_0` ~7 GB, `qwen3.5:9b-q8_0` ~13 GB) plus a `2b` Q4 floor option,
each a separate entry in the single model selector. The VRAM-aware recommender naturally
prefers the Q8 build when it fits the GPU budget (e.g. 8 GB → 4B Q8, 13 GB → 9B Q8), while
6 GB stays on 4B Q4. `gpt-oss:20b`/`devstral:24b` fill the 9B→27B gap
in Qwen3.5's dense line; `qwen3-coder:30b` stays as the dedicated coder. **Google Gemma 4**
(native tool-calling) adds non-Qwen diversity: `gemma4:12b-it-qat` (7.2 GB) fills the
previously-empty ~10 GB tier — Google's **QAT** (quantization-aware-trained) int4 delivers
~bf16 quality at Q4 size, so it's strictly better than the plain `gemma4:12b` Q4_K_M
(7.6 GB) — plus a 13 GB Q8 12B for max fidelity, and the `gemma4:26b` MoE (4B active) is an
efficient ~22 GB mid pick. Gemma 4's **Edge** models (`e2b`/`e4b`) are excluded: "Effective 2B/4B"
is *compute*, not memory — on Ollama they're 7.2/9.6 GB, so `gemma4:12b` (QAT, 7.2 GB) is
smaller-or-equal yet more capable, while genuinely-tiny duty is better served by
`qwen3.5:2b`/`4b` (2.7/3.4 GB, stronger small coders). The Edge models also target
on-device multimodal assistants, not coding (`e4b`: LiveCodeBench 52%, Tau2 42%).
Hardware note:
NVIDIA/AMD run CUDA/ROCm-accelerated GGUF automatically; Apple Silicon has faster MLX
(and coder-tuned) Qwen3.5 variants — a planned platform-aware follow-up. `gpt-oss:120b` and `qwen3-coder:480b` serve workstation/Mac-Studio-class
rigs. Models **without native Ollama tool-calling are hard-excluded** because Copilot's
agent loop is built on tool calls: e.g. **Gemma 3 / CodeGemma** (Ollama returns `does not
support tools`) and deepseek-coder-v2. (**Gemma 4**, by contrast, added native
function-calling + strong agentic/coding benchmarks and *is included* — `gemma4:12b`
and `gemma4:26b`.)
Community-recommended frontier models were
checked against the Ollama library and excluded when not cleanly pullable: MiniMax
M2.1 is **cloud-only** on Ollama (`minimax-m2.1:cloud`, no local weights tag), GLM-4.x
/ Kimi K2.5 are cloud tags, and Devstral 123B is **not in the Ollama library** (only
the 24B). A cloud-model tier (`:cloud` tags, needs sign-in, not offline) is a possible
future addition for those.

### KV-cache math (why 6 GB is the limit, not 32 GB RAM)
- Capacity isn't the issue — 32 GB RAM can *hold* a 128k KV cache.
- **Bandwidth is.** Every generated token re-reads the whole KV cache. In VRAM
  (~192 GB/s) that's fast; spilled to system RAM over PCIe (~10–20 GB/s real) a
  128k cache drops generation to ~1.5–3 tok/s.
- Practical move on 6 GB: **single-user `OLLAMA_NUM_PARALLEL=1` + KV-cache quantization
  (`q8_0`) + FlashAttention 2** to keep a usable context (~32k for 7B) on-GPU and avoid
  layers spilling to CPU. For full 128k on-GPU, use the 3B.

### Recommended Ollama settings (applied by `init` / on server start)
```
OLLAMA_NUM_PARALLEL=1     # single-user: don't reserve KV cache for 4 slots (the #1 cause of CPU offload)
OLLAMA_KEEP_ALIVE=-1      # keep the model resident instead of unloading after ~5 min idle
OLLAMA_FLASH_ATTENTION=1
OLLAMA_KV_CACHE_TYPE=q8_0
```
plus a tuned Modelfile (`FROM <tag>`, `PARAMETER num_ctx …`). Code.Local **injects these
into the `ollama serve` process it starts** (so they apply with no manual restart) and,
under `--optimize`, also persists them as user env vars for servers it didn't start.

### VRAM-aware context sizing (what `init` computes)
A flat 32K context silently offloads to CPU on small GPUs (the KV cache for 32K on a 4B
model is ~2.3 GB at q8 — on a 6 GB card that pushes weights + cache past usable VRAM).
So `init` **sizes the context to the GPU** instead of using a flat default:

- It estimates resident memory = **weights** (≈ GGUF size) + **KV cache**
  (`KvBytesPerTokenFp16 × num_ctx × ½` at q8) + **runtime overhead** (CUDA context +
  compute buffers), per `MemoryEstimator`.
- It picks the largest context on a ladder (32K…256K) that fits **usable VRAM**
  (≈ 90 % of total, leaving headroom for the driver/desktop). It never drops below the
  **32K floor** (less is too little for real coding context — it accepts some KV-cache
  offload to RAM instead) and never exceeds the **256K ceiling** (the catalog models'
  limit). On a 6 GB card a 4B model stays at the 32K floor (with ~1 GB offloaded, flagged);
  on a 24 GB card the small models scale up into the 128K–256K range.
- The interactive wizard shows the per-context memory/fit table and lets you pick any
  value; `--ctx <n>` overrides entirely.
- It then prints a weights / KV / overhead breakdown and a **fit verdict**: fits VRAM
  (green), offloads to system RAM (yellow, with the GB that will spill), or won't fit
  VRAM + RAM (red). The derived `num_ctx` also drives the
  `COPILOT_PROVIDER_MAX_PROMPT_TOKENS` / `MAX_OUTPUT_TOKENS` limits.

`KvBytesPerTokenFp16` per model is an estimate from a representative architecture (layers ×
KV-dim), good enough for capacity planning and warnings, not an exact per-build reservation.

### vLLM vs Ollama on this class of hardware
On a 6 GB Windows laptop, vLLM is the **wrong** default: ~1.5 GB idle overhead
(25% of VRAM), can't fit 7B comfortably, needs WSL2, and single-user negates its
batching wins. Ollama is the higher-ergonomics-per-token choice here.

### Unified-memory machines (RTX Spark, Strix Halo, Mac)
A growing class of machines has a large **unified memory** pool shared by CPU and GPU,
which can hold big models locally:

| Machine | Unified mem | Bandwidth | `gpt-oss:120b` (65 GB) | `qwen3-coder:480b` (290 GB) |
| --- | --- | --- | --- | --- |
| **NVIDIA RTX Spark** (GB10; incl. Surface) | up to 128 GB | ~273–300 GB/s | ✅ (headline use case) | ❌ |
| **AMD Strix Halo** (Ryzen AI Max+ 395) | up to 128 GB | ~256 GB/s | ✅ | ❌ |
| **Apple Mac Studio** (M-Ultra) | up to 512 GB | ~800 GB/s | ✅ | ✅ |

Both big models are MoE (gpt-oss-120b ≈ 5B active, qwen3-coder-480b ≈ 35B active), so
they run far better than their total size implies despite modest bandwidth.

**Detection:** `HardwareDetector` resolves usable VRAM in priority order:
1. **`nvidia-smi`** — discrete RTX *and* unified GB10 parts (RTX/DGX Spark report their
   pool here), so NVIDIA unified machines are auto-detected and recommend correctly.
2. **Apple Silicon** — `sysctl hw.memsize` (via P/Invoke), reported at a conservative
   75% of unified RAM as GPU-usable.
3. Otherwise VRAM is unknown → safe 7B default.

AMD Strix Halo (no `nvidia-smi`, no portable APU-VRAM signal) and any other unidentified
unified rig are handled by the **`--vram <GB>` override**, which feeds `Recommend()`
and the picker directly. `SystemMemory` reports total physical RAM cross-platform
(Windows `GlobalMemoryStatusEx`, Linux `/proc/meminfo`, macOS `sysctl`) for display.

### GPU drivers & readiness
Ollama **bundles its own CUDA runtime**, so on NVIDIA the only prerequisite is a recent
**driver (531+, or 570+ for compute capability 5.0–6.2)** — no CUDA Toolkit. AMD needs a
**ROCm v7 / HIP-capable driver stack** (Strix Halo `gfx1151` is supported); unsupported
AMD cards fall back to Vulkan or CPU. `init` (post-detection) and `status` show a
readiness line via `GpuReadinessAssessor`: it reads the NVIDIA driver version from
`nvidia-smi` and warns if it's below 531, confirms Apple Silicon (Metal, no driver), or
points AMD/unknown users at the ROCm driver. Code.Local never installs GPU drivers
(too heavy/risky to automate) — it only installs Ollama and tells you if the driver is
the missing piece.

---

## 8. Code.Local architecture

### Stack
- **.NET 10 (LTS)**, single console project, **Spectre.Console** for wizard UX.
- Distribution: **self-contained single-file** binaries (no runtime needed),
  cross-published for all RIDs from one machine — **not** Native AOT (see ADR-7).
- Naming: namespace `CodeLocal.*`, brand **Code.Local**, CLI command `codelocal`.

### Two modes
```
SOLO (default)              GATEWAY (v0.2)
  Ollama → Copilot            Ollama ─┐
  (no gateway)                vLLM   ─┤→ LiteLLM → Copilot (many users)
                              cloud  ─┘   (routing, keys, quota)
```

### Runtime abstraction
`IModelRuntime` decouples the wizard from the host. `OllamaRuntime` is the first
implementation; `RuntimeFactory` is the single registration point. Shared runtime
behavior lives in `ModelRuntimeBase`, and `RemoteRuntime` is a null-object runtime for
external (unmanaged) endpoints. Each runtime keeps its host-specific code (runtime,
service, installer) in its own `Runtimes/<name>/` folder, with its JSON DTOs under
`Runtimes/<name>/Models/`, so vLLM / llama.cpp slot in without touching the commands.
Models stay runtime-agnostic via `CodingModel.Sources` (a runtime-key → model-reference
map); `IModelRuntime.Supports` / `ModelRef` resolve a model for a given runtime.

```
Runtimes/
  IModelRuntime.cs      # Key, IsInstalled, Supports/ModelRef, Download, Prepare, SmokeTest, ListModels, Installer
  IRuntimeInstaller.cs  # install abstraction (ManualInstallHint, InstallAsync)
  ModelRuntimeBase.cs   # shared runtime behavior; concrete runtimes inherit it
  RemoteRuntime.cs      # null-object runtime for an external (unmanaged) endpoint
  RuntimeFactory.cs     # key -> runtime (the seam where vLLM/llama.cpp register)
  Models/               # runtime-agnostic contract types (InstalledModel, GenerationMetrics)
  Ollama/               # everything Ollama-specific lives here
    OllamaRuntime.cs    # implements IModelRuntime over OllamaService
    OllamaService.cs    # CLI + HTTP calls to the local Ollama daemon
    OllamaInstaller.cs  # IRuntimeInstaller (winget / brew / install.sh)
    Models/             # Ollama wire DTOs (reflection-based System.Text.Json)
```

### Commands (v0.1)
- `init` — **local mode** (default): detect HW → (consent-gated) install Ollama if
  missing → ensure the Copilot CLI is installed → recommend model → pull → tune
  (Modelfile, ctx, KV) → save config file → smoke test. With `--persist`, also writes
  user env vars. **Client mode** (`--endpoint <url>` + `--model`): skip the runtime
  entirely — just ensure the Copilot CLI and write a config pointing at an existing
  OpenAI-compatible endpoint. This is the payload the team installer runs.
- `copilot` — launcher: read the saved config, (consent-gated) install the Copilot CLI
  if missing, inject the provider env vars into the Copilot **child process only**,
  inherit the terminal, return Copilot's exit code.
- `status` — HW + runtime + installed models + saved Copilot config (config file,
  falling back to environment).
- `speed` — benchmark the configured model: ensure the server is up → **warm the model
  up** (so a cold weight-load doesn't skew results) → average N timed runs → report
  prefill/decode tok/s plus GPU/CPU placement (from `/api/ps`). Decode speed comes from
  Ollama's native `eval_count`/`eval_duration`, so it's load-independent; the placement
  readout makes VRAM offload visible and ties back to the model/quant/context choices.
- `package` — generate a **team installer**: per-OS scripts (`install-copilot.ps1` /
  `.sh`) + README that run `codelocal init` in client mode against a shared `--endpoint`,
  so it reuses `init` rather than duplicating config logic. The published `codelocal`
  binary is dropped alongside the script to form the bundle.

### Copilot config saved by `init`
Written to `%APPDATA%\codelocal\config.json` (Windows) / `~/.config/codelocal/config.json`
(Unix); injected by `codelocal copilot` into the Copilot child environment:
```
COPILOT_PROVIDER_BASE_URL = http://localhost:11434/v1
COPILOT_PROVIDER_API_KEY  = ollama
COPILOT_MODEL             = qwen3.5-9b
COPILOT_PROVIDER_WIRE_API = completions
COPILOT_PROVIDER_MAX_PROMPT_TOKENS = 24576
COPILOT_PROVIDER_MAX_OUTPUT_TOKENS = 8192
```
`--persist` additionally sets user-scoped env vars (Windows) or
`~/.config/codelocal/copilot.env.sh` sourced from the shell rc (macOS/Linux).

---

## 9. Decision records (ADRs)

**ADR-1 — Implementation stack: .NET 10 (LTS).**
.NET 10 is the current LTS (we deliberately avoid the .NET 9 STS). Spectre.Console
gives the wizard UX; System.Text.Json handles config/wire serialization (see ADR-11);
Blazor is a clean path to a future web UI. Chosen for maintainer productivity and a clean
single-binary distribution story (see ADR-7).

**ADR-2 — Default runtime: Ollama.**
Its management layer (registry, auto chat-template + tool-parser, native installers)
removes the per-model config burden that would otherwise be our top source of
"Copilot tool calls don't work" bugs. Default prefix caching (see §6) covers the
single-user performance case.

**ADR-3 — Runtime abstraction now, not later.**
Introduced `IModelRuntime` while the codebase is small to hedge the
Ollama/vLLM/llama.cpp uncertainty. Runtimes map to tiers; we never choose globally.

**ADR-4 — Wire API default: `openai` + `completions` (chat-completions).**
Chat-completions is the schema most local models are fine-tuned on and Ollama's most
mature endpoint, giving the highest tool-calling reliability. We initially defaulted to
`responses` to match Ollama's Copilot docs, but its `/v1/responses` support is newer/
experimental and small models emitted malformed tool calls on it (missing required
args), so the default flipped to `completions`. `--wire responses` remains opt-in;
Anthropic wire only if specifically wanting `thinking` rendering.

**ADR-5 — vLLM is the gateway/multi-user tier, not the default.**
Benchmarks confirm vLLM gives a *single* user no meaningful speedup over Ollama
(single-stream ~equal, sometimes Ollama faster). Its edge is **throughput under
concurrency** (≈5–15× aggregate at 10–50 users), block-level prefix caching across
users, `tool_choice`, LoRA, and multi-GPU — needing ≥12–16 GB and/or multiple users.
Native Windows vLLM is now possible via
[SystemPanic/vllm-windows](https://github.com/SystemPanic/vllm-windows) (no WSL2),
which helps a Windows gateway host but doesn't change the solo default. Wrong fit for
a 6 GB single-user laptop.

**ADR-6 — Not a commercial product (for now).**
Built as an open-source tool for individuals and orgs. Earlier enterprise framing
(gateway + identity quota + code DLP) is informative but out of scope for v0.1–v0.2;
the gateway/identity pieces are the natural growth path, not the starting point.

**ADR-7 — Distribution: self-contained single-file, not Native AOT.**
The tool is setup/config, not perf-sensitive, so AOT's wins (fast startup, small
size) don't matter, while its costs do (per-OS build matrix, native toolchains,
trim-safety). Self-contained single-file gives the same "no runtime needed" outcome
and **cross-publishes all RIDs from one host**. Trimming (smaller size) is a future
lever if size ever matters.

**ADR-8 — Launcher over persisted environment variables.**
`init` writes a config file and Copilot is started via `codelocal copilot`, which
injects the provider env vars into the **child process only**. This avoids mutating
the global/user environment (which would make every shell's `copilot` local and be
awkward to undo), lets local and cloud modes coexist in the same shell, and still
survives Copilot's `/update` and `/restart` (they inherit the running process's
environment). `--persist` remains as an opt-in for users who want always-on local
mode in every shell. Verified: injecting `COPILOT_MODEL` reaches the child (Copilot
reported the custom model), and interactive TUI passthrough works via inherited
stdio. The Windows `.cmd`/`.bat` npm shim is run through `cmd /c` since it can't be
exec'd directly with `UseShellExecute=false`.

**ADR-9 — Consent-gated runtime install, on the runtime.**
`init` can install a missing runtime (and prerequisites it can manage), but never
silently: interactive runs prompt for consent; non-interactive runs require
`--auto-install-dependencies`. Installs prefer the platform package manager (winget /
brew / official `install.sh`) and print the exact command first. The install capability
is a separate `IRuntimeInstaller` exposed via `IModelRuntime.Installer` (null when a
runtime has no auto-installer) so each runtime owns its own mechanism and commands stay
thin. The **Copilot CLI** itself is treated as another installable dependency through the
same `IRuntimeInstaller` abstraction (`Copilot/CopilotCliInstaller` — winget
`GitHub.Copilot` on Windows, otherwise `npm install -g @github/copilot`): `init` ensures
it up front and the `copilot` launcher offers to install it on demand. A shared
`DependencyInstallationManager` helper centralises the consent → install flow for both runtimes and
the CLI. Because a fresh install leaves the current process `PATH` stale, detection also
checks known install locations, and `init` briefly polls for the server before continuing
(otherwise it tells the user to reopen the terminal).

**ADR-10 — `package` reuses `init` (client mode), not a parallel config path.**
The team installer used to emit shell scripts that set the provider env vars directly —
a second, drifting implementation of "configure Copilot". Instead, `init` gained a
**client mode** (`--endpoint` + `--model`) that skips the local runtime and only installs
the Copilot CLI + writes the config. `package` now emits thin per-OS scripts that invoke
`codelocal init --endpoint … --api-key … --model … --persist --auto-install-dependencies`
against a bundled `codelocal` binary. One code path configures Copilot (local or remote),
the secret/endpoint are baked into the script so teammates don't paste keys, and the
installer benefits from everything `init` already does (CLI install, consistent config).
This is the seed of the v0.2 gateway onboarding flow.

**ADR-11 — JSON via reflection, not source generation.**
Config and Ollama wire DTOs use plain reflection-based `System.Text.Json`, not the
source-generated contexts the project started with. The CLI isn't perf- or
startup-sensitive and isn't trimmed/AOT'd (ADR-7), so source-gen's wins don't apply
while its costs do (a `JsonSerializerContext` per payload set, extra indirection). Each
serializing class owns a small `JsonSerializerOptions`, and every DTO keeps an explicit
`[JsonPropertyName]` so the on-disk / wire shape is independent of the serializer.

**ADR-12 — Tool-agnostic core config; assistant integrations isolated.**
The saved configuration is `CodeLocalConfig` — a tool-agnostic record of the endpoint,
model, token limits, and backing runtime. Everything that knows the *Copilot* CLI
(locating/installing it, projecting the config into `COPILOT_*` env vars via
`CopilotEnvironment`) lives under `Copilot/`. A second assistant (e.g. Claude Code) would
be a sibling integration consuming the same config, not a change to it — the seed of the
roadmap's "tool-agnostic clients" item. Data types follow the same ownership rule: each
subsystem keeps its types in its own `Models/` folder, and the top-level `Models/` holds
only types owned by no single subsystem (`CodeLocalConfig`, the `ModelCatalog`).

---

## 10. Roadmap

- **v0.1 — solo mode** ✅ `init` / `status` / `speed` / `package`, Ollama runtime, runtime
  abstraction.
- **v0.2 — gateway mode:** stand up LiteLLM (routing, virtual keys, budgets), a
  local management UI, per-user installer generation.
- **v0.3 — identity:** SSO (Entra ID / Google Workspace) device-code onboarding so
  each installer binds to a real user for quota/audit.
- **Runtimes:** add vLLM (gateway/multi-user tier); optionally llama.cpp (persistent
  `--prompt-cache`).
- **Clients:** tool-agnostic config beyond Copilot CLI (VS Code BYOK, etc.).
- **Distribution:** self-contained single-file binaries, cross-published per RID.

---

## 11. Open questions

Things still being validated, listed so the assumptions behind the design stay explicit.

1. **Inline completion routing.** Which Copilot surfaces honor the org/BYOK endpoint
   (chat, agent, CLI, *inline*)? If inline bypasses BYOK, a "code stays local" / DLP
   guarantee has a hole — best confirmed with a logging proxy.
2. **Org BYOK identity.** Configured org-wide, does Copilot pass per-user identity
   (needed for per-user quota) or a single shared key? This decides whether the v0.2
   gateway needs its own auth step.
3. **`OLLAMA_NUM_PARALLEL` vs prefix-cache reuse** for multi-stream use.
4. **Copilot `/update` & `/restart` environment inheritance.** By design a self-restart
   inherits the running process environment, so launcher-injected vars persist into local
   mode; a periodic empirical check guards against an update path resetting it.
