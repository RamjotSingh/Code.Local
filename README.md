# Code.Local

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20macOS%20%7C%20Linux-blue)

> Private, local AI models for your coding assistant — set up in one command.

Code.Local is a small cross-platform CLI that wires up the three layers you need to
run **GitHub Copilot against local models** — without hand-editing config files:

1. **Model host** — pulls and tunes a curated coding model via [Ollama](https://ollama.com).
2. **Client config** — points the GitHub Copilot CLI at your local endpoint.
3. **Distribution** — generates a reusable installer so teammates can connect to the
   same gateway in one step.

It is built in .NET 10 and ships as a single binary.

## Contents

- [Status](#status)
- [Prerequisites](#prerequisites)
- [Build](#build)
- [Usage](#usage) — `init` · `copilot` · `status` · `speed` · `package`
- [Bundled models](#bundled-models)
- [How it works](#how-it-works)
- [Roadmap](#roadmap)
- [Design & rationale](#design--rationale)
- [License](#license)

## Status

**v0.1 — solo mode.** Runs Ollama + Copilot directly (no gateway). The LiteLLM
gateway layer (routing, per-user keys, quotas) is planned for v0.2 — see
[Roadmap](#roadmap).

## Prerequisites

- [Ollama](https://ollama.com/download) installed and running.
- [GitHub Copilot CLI](https://github.com/features/copilot/cli) installed.
- An NVIDIA GPU is recommended but not required.

## Build

```bash
dotnet build -c Release
```

The executable is named `codelocal`.

### Release binaries

Self-contained single-file binaries (no .NET runtime required) are produced per RID:

```powershell
pwsh scripts/publish.ps1 -Version 0.1.0
```

CI builds them and attaches them to a GitHub Release when a `v*` tag is pushed
(`.github/workflows/release.yml`). Because we use self-contained single-file (not
Native AOT), all platforms cross-publish from a single machine.

## Usage

### `codelocal init`

Interactive wizard: detects your hardware, recommends a model, pulls and tunes it,
then saves a Code.Local configuration that the launcher uses.

```bash
codelocal init
```

Non-interactive (for scripts / CI):

```bash
codelocal init --non-interactive --model qwen3.5:9b --ctx 16384
```

| Option | Description |
| --- | --- |
| `--non-interactive` | Run without prompts |
| `--model <tag\|id>` | Model to use (e.g. `qwen3.5:9b`) |
| `--ctx <tokens>` | Override context window (default: auto-sized to VRAM; 32K floor, 256K max) |
| `--max-prompt-tokens <n>` | Override the prompt-token limit (default: context minus the output budget) |
| `--max-output-tokens <n>` | Override the output-token limit (default: ¼ of context, capped at 8192) |
| `--vram <GB>` | Override detected VRAM for model recommendation (unified-memory rigs) |
| `--no-optimize` | Skip runtime-specific performance tuning |
| `--auto-install-dependencies` | Install the runtime, the Copilot CLI, and prerequisites if missing (consent for non-interactive runs) |
| `--persist` | Also set user environment variables (always-on, every shell) |
| `--skip-pull` | Configure only; don't download or smoke-test |

If Ollama is missing, interactive `init` offers to install it for you (via `winget`
on Windows, `brew` on macOS, the official `install.sh` on Linux); in non-interactive
mode pass `--auto-install-dependencies` to consent. The same step offers to install the
**GitHub Copilot CLI** (via winget `GitHub.Copilot` on Windows, otherwise
`npm install -g @github/copilot`) when it isn't already present. The exact command is
shown before it runs, and nothing is installed without consent.

By default `init` does **not** mutate your global environment — it writes a config
file and you start Copilot through the launcher. Use `--persist` only if you want
plain `copilot` to use local mode in every shell.

**VRAM-aware context sizing.** Rather than a flat 32K window, `init` estimates the model's
memory — weights + KV cache + runtime overhead — and auto-picks the largest context that
fits your usable VRAM, between a **32K floor** (less is too little for real coding context)
and a **256K ceiling** (the models' limit). The wizard shows a per-context memory/fit
table, then a breakdown for your choice with a clear verdict: fits VRAM, offloads to system
RAM (slower, with the GB that will spill), or won't fit. On a small GPU it stays at the 32K
floor and tells you what offloads; bigger GPUs scale up into the 128K–256K range. Override
with `--ctx <tokens>`.

**Client mode.** Pass `--endpoint <url>` (with `--model`) to skip the local runtime
entirely and point Copilot at an existing OpenAI-compatible endpoint — a LAN Ollama
host, a vLLM server, or a shared gateway. `init` then only installs the Copilot CLI and
writes the config; nothing is pulled or hosted locally:

```bash
codelocal init --endpoint https://gateway.corp/v1 --model qwen3.5-9b --api-key sk-team-key
```

### `codelocal copilot`

Launch the GitHub Copilot CLI in local mode. The saved configuration is injected into
the Copilot child process **only** — nothing global is changed, so a plain `copilot`
in any other shell still runs in normal (cloud) mode.

```bash
codelocal copilot                 # interactive, local mode
codelocal copilot --offline       # also set COPILOT_OFFLINE=true
codelocal copilot -- -p "hello"   # forward args after -- to Copilot
```

If the Copilot CLI isn't installed, the launcher offers to install it (winget on
Windows, otherwise npm) before starting; in a non-interactive shell pass
`--auto-install-dependencies` to consent.

| Option | Description |
| --- | --- |
| `--offline` | Also set `COPILOT_OFFLINE=true` for this launch |
| `--auto-install-dependencies` | Install the Copilot CLI if missing (consent for non-interactive runs) |
| `-- <args>` | Forward the remaining arguments verbatim to Copilot |

Tip: to make `copilot` always mean local mode in your shell, add an alias yourself
(e.g. `alias copilot='codelocal copilot'`) — opt-in, no global mutation.

### `codelocal status`

Show detected hardware, whether Ollama is installed/running, installed models, and
the saved Copilot configuration.

```bash
codelocal status
```

### `codelocal speed`

Benchmark the configured model's generation speed. It **warms the model up first** (so a
cold weight-load doesn't skew the result), then averages a few timed runs and reports
**prefill** and **generation** tokens/sec plus the **GPU/CPU placement** — so you can see
at a glance whether the model is fully on the GPU or partly offloaded (which tanks speed).

```bash
codelocal speed                 # benchmark the configured model
codelocal speed --model qwen3.5-4b --runs 3 --tokens 256
```

```
┌───────────────────┬──────────────────────────────────────┐
│ Speed             │ Value                                │
│ Model             │ qwen3.5-4b                           │
│ Placement         │ 71% GPU / 29% CPU                    │
│ Model load (cold) │ 9.9 s                                │
│ Prefill           │ ~587 tok/s                           │
│ Generation        │ ~34 tok/s  (range 34-35, 64 tok/run) │
└───────────────────┴──────────────────────────────────────┘
```

Speed is measured from Ollama's native decode timing, so it reflects steady-state
generation independent of the one-time load. A partial-CPU placement prints a hint to
resize (smaller model / shorter context / lighter quant) via `codelocal init`.

### `codelocal package`

Generate a **team installer** that points other machines' Copilot at a shared endpoint
(e.g. your v0.2 gateway). It emits per-OS scripts that run `codelocal init` in client
mode — installing the Copilot CLI and writing the config — so a teammate needs no local
model at all.

```bash
codelocal package --endpoint https://gateway.corp/v1 --model qwen3.5-9b --api-key sk-team-key
```

This writes `install-copilot.ps1` / `install-copilot.sh` + `README.md` into
`./codelocal-installer`. Drop the matching `codelocal` binary (from `scripts/publish.ps1`
or a release) next to the script and hand the folder over; the teammate runs the script
for their OS and `copilot` is wired to the endpoint (env persisted).

## Bundled models

Code.Local ships a curated ladder of tool-calling-capable coding models spanning
hardware tiers. `init` recommends the best one that fits your detected VRAM; you can
override with `--model <tag|id>`.

| Model (Ollama tag) | Params | Quant | ~Disk | Min VRAM | License | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `qwen3.5:2b-q4_K_M` | 2B | Q4_K_M | 1.9 GB | ~3 GB | Apache-2.0 | Smallest; ~4 GB GPUs (weakest) |
| `qwen3.5:2b` | 2B | Q8 | 2.7 GB | ~3.5 GB | Apache-2.0 | Qwen3.5 (newer gen) + tools; ultra-light |
| `qwen3.5:4b` | 4B | Q4_K_M | 3.4 GB | ~5 GB | Apache-2.0 | Qwen3.5 + tools; fits ~6 GB GPUs (default) |
| `qwen3.5:4b-q8_0` | 4B | Q8 | 5.3 GB | ~7 GB | Apache-2.0 | Q8 4B: near-FP16 fidelity for tool calling |
| `qwen3.5:9b` | 9B | Q4_K_M | 6.6 GB | ~8 GB | Apache-2.0 | Strongest sub-10B Qwen3.5; tools + reasoning |
| `gemma4:12b-it-qat` | 12B | QAT int4 | 7.2 GB | ~10 GB | Gemma | Gemma 4; Google QAT int4 ≈ bf16 quality at Q4 size; native tools, 256K |
| `qwen3.5:9b-q8_0` | 9B | Q8 | 11 GB | ~13 GB | Apache-2.0 | Q8 9B: highest single-step reliability sub-12B |
| `gpt-oss:20b` | 20B MoE | MXFP4 | 14 GB | ~16 GB | Apache-2.0 | OpenAI open model; reasoning + tools |
| `gemma4:12b-it-q8_0` | 12B | Q8 | 13 GB | ~16 GB | Gemma | Gemma 4 12B max fidelity (QAT is nearly as good, half the size) |
| `devstral:24b` | 24B | Q4_K_M | 14 GB | ~16 GB | Apache-2.0 | Mistral's agentic coding model |
| `qwen3-coder:30b` | 30B MoE | Q4_K_M | 19 GB | ~20 GB | Apache-2.0 | Dedicated agentic coder; very long context |
| `qwen3.5:27b-int4` | 27B | int4 | 16 GB | ~20 GB | Apache-2.0 | Bigger model at Q4 (int4, text-only) |
| `gemma4:26b` | 26B MoE | Q4_K_M | 18 GB | ~22 GB | Gemma | Google Gemma 4 MoE (4B active, fast); native tools (Q8 also exists, 28 GB) |
| `qwen3.5:35b-a3b-int4` | 35B MoE | int4 | 20 GB | ~24 GB | Apache-2.0 | 35B quality, ~3B active (fast) |
| `gpt-oss:120b` | 120B MoE | MXFP4 | 65 GB | ~80 GB | Apache-2.0 | Large open model; big-rig only |
| `qwen3-coder:480b` | 480B MoE | Q4_K_M | 290 GB | ~300 GB | Apache-2.0 | Frontier; 512 GB-class rigs only |

The small/mid tiers use **Qwen3.5** (`2b`/`4b`/`9b`) — a generation newer than
Qwen2.5-Coder, with tool calling. At higher VRAM a *bigger model at Q4* wins, so
`qwen3.5:27b-int4` and the MoE `qwen3.5:35b-a3b-int4` (~3B active, so fast) are included.
**Quantization is a visible choice, not a hidden default:** because Q4's quality gap
widens for agentic (multi-turn, tool-heavy) work while Q8 stays near FP16, the dense small
tiers list both their Q4_K_M default and an explicit **Q8 variant** (`qwen3.5:4b-q8_0`,
`qwen3.5:9b-q8_0`) as separate picks in the one selector. The VRAM-aware recommender
auto-prefers Q8 when it fits (≈8 GB → 4B Q8, ≈13 GB → 9B Q8); 6 GB stays on 4B Q4.
`gpt-oss:20b`/`devstral:24b` fill the 9B→27B gap and `qwen3-coder:30b` stays as the
dedicated coder. **Google Gemma 4** adds non-Qwen options that also do native tool
calling: `gemma4:12b-it-qat` fills the ~10 GB tier (Google's QAT int4 ≈ bf16 quality at Q4
size, with a 13 GB Q8 12B for max fidelity) and the `gemma4:26b` MoE (4B active) is an
efficient mid-tier pick. (Apple Silicon has faster MLX + coder-tuned Qwen3.5 variants — a
planned follow-up.)
The two big-rig entries (`gpt-oss:120b`, `qwen3-coder:480b`) are for workstation/Mac
Studio-class hardware. Frontier models that Ollama only offers as **cloud** tags
(MiniMax M2.1, GLM 4.x, Kimi K2.5) or doesn't carry locally (Devstral 123B) are out of
scope for the local catalog.

## How it works

`codelocal init` saves a configuration file (Windows `%APPDATA%\codelocal\config.json`,
Unix `~/.config/codelocal/config.json`) describing the provider endpoint and model:

```
COPILOT_PROVIDER_BASE_URL = http://localhost:11434/v1
COPILOT_PROVIDER_API_KEY  = ollama
COPILOT_MODEL             = qwen3.5-9b
COPILOT_PROVIDER_WIRE_API = completions
COPILOT_PROVIDER_MAX_PROMPT_TOKENS = 24576
COPILOT_PROVIDER_MAX_OUTPUT_TOKENS = 8192
```

`codelocal copilot` reads that file and injects these variables into the Copilot
child process environment, then hands over the terminal. Because the variables live
only in the spawned process, they survive Copilot's own `/update` and `/restart`
(which inherit the running environment) while never affecting other shells.

With `--persist`, `init` additionally writes the variables to your user environment
(Windows user-scoped variables; macOS/Linux `~/.config/codelocal/copilot.env.sh`
sourced from your shell rc) so plain `copilot` also uses local mode.

## Roadmap

- **v0.2 — gateway mode:** stand up [LiteLLM](https://github.com/BerriAI/litellm) for
  multi-model routing, per-user virtual keys, and token quotas; a local management UI.
- **v0.3 — identity:** SSO (Entra ID / Google Workspace) device-code onboarding so each
  generated installer binds to a real user for quota and audit.
- Tool-agnostic client config (VS Code BYOK, Cursor, Continue, …).

## Design & rationale

The full backend analysis (Ollama vs vLLM vs llama.cpp vs SGLang), the Copilot BYOK
wire-format breakdown, the (corrected) prompt-caching behavior, hardware/model
guidance, and the architecture decision records live in [`DESIGN.md`](DESIGN.md).

Contributor and AI-agent conventions (code style, build commands, architecture rules)
live in [`AGENTS.md`](AGENTS.md).

## License

MIT (see `LICENSE`).
