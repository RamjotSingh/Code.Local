# AGENTS.md

Guidance for AI agents and contributors working in this repository. See
[`DESIGN.md`](DESIGN.md) for architecture and decision records.

## Project

**Code.Local** — a cross-platform .NET CLI that sets up local AI coding models for
GitHub Copilot (model host + provider config + installer generation).

- Namespace: `CodeLocal.*` · Brand/display: `Code.Local` · CLI command/binary: `codelocal`
- Target framework: **.NET 10 (LTS)** — prefer the latest LTS, not STS releases.
- Entry point: `src/Code.Local/Program.cs`. Commands in `Commands/`, host runtimes in
  `Runtimes/` (each runtime's host-specific code — service, installer — under its own
  `Runtimes/<name>/` folder, e.g. `Runtimes/Ollama/`), neutral services in `Services/`.
- **Data types live in a `Models/` folder, placed by ownership not usage.** A type belongs
  to the `Models/` of the subsystem that constructs it / defines its meaning
  (`Services/Models/`, `Runtimes/Models/`, `Runtimes/Ollama/Models/`); a type named in an
  abstraction's contract goes in the abstraction's layer, not the implementation that builds
  it (e.g. `IModelRuntime`'s `InstalledModel`/`GenerationMetrics` live in `Runtimes/Models/`,
  not `Runtimes/Ollama/`). The top-level `Models/` holds only types owned by no single
  subsystem: the persisted `CodeLocalConfig` and the static `ModelCatalog`/`CodingModel`.

## Commands

```bash
# Build (treat warnings as errors before committing)
dotnet build -c Release -warnaserror

# Run
dotnet run --project src/Code.Local -- <command>      # e.g. status | init | copilot | package | version

# Publish self-contained single-file binaries (all RIDs cross-publish from one host)
pwsh scripts/publish.ps1 -Version 0.1.0
```

There is no test project yet. When adding one, wire it up here.

## C# code style (required)

These conventions are enforced for all C# in this repo:

1. **XML doc comments** — put `<summary>` and `</summary>` each on its own line with the
   text in between; never place those tags and text on a single line. Other tags
   (`<param>`, `<returns>`, `<typeparam>`) may keep their open tag, text, and close tag on
   one line.
   ```csharp
   /// <summary>
   /// Resolve a runtime by name. Null/empty selects the default.
   /// </summary>
   /// <param name="name">Runtime key; null or empty selects the default.</param>
   ```

2. **Explicit `using` directives in every file** — `ImplicitUsings` is **disabled** in
   the `.csproj`. Each file declares the `using`s it needs at the top (do not rely on
   global/implicit usings).

3. **Avoid `var`** — use explicit types everywhere, except where `var` is genuinely
   required (e.g. anonymous types). This applies to locals, `foreach` variables,
   `out` declarations, and tuple deconstruction:
   ```csharp
   IModelRuntime runtime = RuntimeFactory.Create(name);
   foreach (CodingModel model in ModelCatalog.All)
   {
       /* ... */
   }
   if (options.TryGetValue("contextTokens", out string? contextValue))
   {
       /* ... */
   }
   (int exitCode, string standardOutput, _) = await ProcessRunner.RunCaptureAsync(/* ... */);
   ```

4. **Full brace blocks** — always use braces with the body on its own line(s) for
   `if`/`else`/`for`/`foreach`/`while`/`using` and similar, even for a single statement.
   No braceless and no single-line bodies.
   ```csharp
   if (condition)
   {
       DoSomething();
   }
   ```

5. **Classes, not records** — use plain classes (POCOs) for data and leaf types; do not
   use `record` / `record struct`. Declare explicit properties (and a constructor where it
   helps). Reference equality is the norm; do not rely on record value-equality.

6. **Full method bodies, not expression-bodied methods** — write methods with a `{ }` block
   and an explicit `return`. The `=>` expression-bodied form is acceptable only for simple
   property/indexer get/set accessors, never for methods.

7. **Descriptive names; avoid abbreviations** — spell identifiers out (locals, parameters,
   methods, classes). Use a well-known abbreviation only when the full name would be
   genuinely unwieldy; otherwise prefer the full word, e.g. `cancellationToken` (not `ct`),
   `config` (not `cfg`), `response` (not `resp`), `hardware` (not `hw`). Beyond avoiding
   abbreviations, **qualify generic data names by their context** — `configJson` not `json`,
   `optionValue` not `value`, `csvFields` not `parts`, `configPath` not `path`, `fileContent`
   not `content`. Keep names reasonably short, and leave idiomatic single-purpose names that
   aren't ambiguous (e.g. one `StringBuilder builder` per method).

8. **No `yield` / iterator methods** — build and return an explicit collection (e.g.
   `List<T>`); `yield return` reads less clearly and is not used here.

9. **Document every method** — give methods a `<summary>` doc comment at a reasonable level
   (a line or two on intent), including `private`/`internal` ones. Keep it concise, not an
   essay; trivial auto-property accessors don't need one.

10. **One top-level type per file** — exactly one class / interface / enum per `.cs` file,
    named after the type. Do not co-locate multiple types in a single file.

11. **JSON serialization is explicit and off the model** — tag every serializable property
    with `[JsonPropertyName("…")]`. Keep serialization *settings* (indentation, ignore
    conditions) on a `JsonSerializerOptions` owned by the serializing class, never on the
    model type. Decorate any serialized `enum` property with
    `[JsonConverter(typeof(JsonStringEnumConverter))]` so it round-trips as a string.

12. **Comment model/data types** — in any `Models/` folder, briefly comment each field /
    property so its meaning and units are clear.

13. **No code in interfaces** — interfaces are pure contracts: member declarations only,
    never default interface methods. Put shared or default behavior in an `abstract` base
    class that implementors inherit (e.g. `ModelRuntimeBase : IModelRuntime`, with
    `OllamaRuntime : ModelRuntimeBase`).

14. **Fail fast** — validate inputs at the boundary and error immediately with a clear
    message; don't let a bad value flow deep into the call chain before it fails. E.g. an
    invalid numeric option throws at parse time (`--runs must be a positive integer`), and a
    present-but-incomplete saved config throws on load rather than being silently ignored.

15. **Make nullable returns obvious in the name** — a method that can return `null` should
    say so (`LoadOrNull`, `ReadFromEnvironmentOrNull`, `SourceFor`/`…OrNull`). Prefer a
    non-nullable return (or a clear required-value path) where a value is always expected, so
    `?` is reserved for genuinely optional results.

16. **Meaningful blank lines** — separate logical blocks with blank lines (around
    `if`/`else if`/`else` and loops, and before a trailing `return`) to group related lines.
    A variable declaration may sit with the block that consumes it.

17. **Thin models** — types in a `Models/` folder hold data plus trivial derived accessors only (e.g.
    `gb => mb / 1024.0`). Non-trivial logic (selection, parsing, I/O, rendering) lives in a
    service or command, not on the data type.

18. **Single owner (DRY)** — a given piece of logic lives in exactly one place and is reused,
    not copy-pasted. E.g. `ConfigStore` owns config load/save, `HardwareReport` owns the
    hardware table shown by `init` and `status`, and `ProcessRunner.IsWindowsScript` owns the
    `.cmd`/`.bat` check. Don't re-implement shared logic in multiple commands.

19. **No pointless indirection** — extract a helper only when it removes duplication or
    genuinely clarifies intent; avoid one-line wrappers that just add a jump to follow.

### Additional conventions (as used in the codebase)

- `Nullable` is enabled; keep code warning-clean under `-warnaserror`.
- File-scoped namespaces (`namespace CodeLocal.Services;`).
- `sealed` classes for DTOs and leaf types (records are not used — see rule 5).
- JSON uses `System.Text.Json` with reflection-based serialization. Each serializing class
  owns a private `JsonSerializerOptions` for its settings (e.g. `ConfigStore`,
  `OllamaService`); tag every payload property with `[JsonPropertyName]` (see rule 11).
- Avoid narrating obvious code, but do add `<summary>` docs on methods (rule 9) and
  member comments on model/data types (rule 12).
- Async methods use `.ConfigureAwait(false)` in library/service code.

## Architecture notes

- **Runtimes are abstracted** behind `IModelRuntime`. To add a runtime (e.g. vLLM,
  llama.cpp), create a `Runtimes/<name>/` folder, add a runtime class there that inherits
  `ModelRuntimeBase` (which supplies the shared defaults; `IModelRuntime` is a pure
  contract), and register it in `RuntimeFactory.Create` — do not special-case runtimes inside commands.
  Models are runtime-agnostic: `CodingModel.Sources` maps a runtime key (e.g. `ollama`)
  to that runtime's model reference (an Ollama tag, a HF repo, …); `IModelRuntime.Supports`
  / `ModelRef` resolve it. Runtime-specific install logic is a separate
  `IRuntimeInstaller` implementation exposed via `IModelRuntime.Installer` (null when
  auto-install isn't supported), living alongside the runtime — never in the commands.
  The user-facing selector is `--runtime`.
- **Remote endpoints are a runtime too.** A config that points Copilot at an existing
  OpenAI-compatible endpoint (`init --endpoint`, or a team installer) saves
  `RuntimeKey = "remote"`, which `RuntimeFactory.Create` resolves to `RemoteRuntime` — a
  null-object runtime that installs nothing and whose server management is a no-op (model
  provisioning/benchmarking throw `NotSupportedException`). Commands therefore never special-case
  "is this remote?"; they create the runtime and call it. Don't reintroduce a `RuntimeKey == null`
  check — null just falls back to the default local runtime.
- **Runtime installs** are abstracted behind `IRuntimeInstaller` (`ManualInstallHint`,
  `InstallAsync`). Runtimes expose one via `IModelRuntime.Installer`; the GitHub Copilot
  CLI is treated as another installable dependency (`Copilot/CopilotCli` +
  `Copilot/CopilotCliInstaller`, winget/npm-based). The shared `DependencyInstallationManager`
  helper centralises the consent → install flow — reuse it instead of hand-rolling
  install prompts in a command.
- Commands are thin and talk to `IModelRuntime` / services, never directly to a host.
- **Each command lives in its own folder** under `Commands/<Command>/` (e.g. `Commands/Init/`),
  holding the command and its strongly-typed options POCO together (`InitCommand` +
  `InitOptions`, namespace `CodeLocal.Commands.Init`). `Program` tokenises the raw
  `--key value` pairs into a dictionary; each POCO's `Parse` binds typed fields through the
  shared `OptionParsingExtensions` (at `Commands/`, namespace `CodeLocal.Commands`); commands
  receive the typed options and never touch the dictionary. Add a new command as a new
  `Commands/<Name>/` folder containing its command + options POCO.
- Keep the solo (no-gateway) path working; the LiteLLM gateway is a future tier.
- Prefer the launcher model: `init` saves a config file (`ConfigStore`) and
  `codelocal copilot` injects env vars into the Copilot child process only. Do not
  reintroduce global env mutation as the default (`--persist` is the opt-in).
- **Copilot specifics are isolated under `Copilot/`.** The core `CodeLocalConfig` is
  tool-agnostic (endpoint + model + limits + runtime); the Copilot integration — locating/
  installing the `copilot` CLI and projecting the config into `COPILOT_*` env vars — lives
  in `Copilot/` (`CopilotCli`, `CopilotCliInstaller`, `CopilotEnvironment`). A second
  assistant CLI would be a sibling integration consuming the same config, not a change to it.
- `init` has two modes sharing one config path: **local** (provision a runtime) and
  **client** (`--endpoint` + `--model`, skip the runtime, just point Copilot at it).
  `package` generates a team installer that *runs `init` in client mode* — don't add a
  separate "write the config" implementation to `package`/`InstallerGenerator`.
