# smolcoder (C#)

A C# (.NET 10) rewrite of smolcoder, built with clean architecture. It runs on local models (Ollama, LM Studio, or any OpenAI-compatible server) or DeepSeek's cloud API, and has a browser UI that shows Myanmar and all other scripts correctly.

## Run

```bash
dotnet run --project src/Smolcoder.Cli -- --web            # browser UI for the current folder
dotnet run --project src/Smolcoder.Cli -- -p "fix the failing test" --effort off   # one headless prompt
```

### Install as a command

```bash
./install.sh          # publishes to ~/.smolcoder/app and adds /usr/local/bin/smallcode
smallcode --web       # same as the dotnet run line above; run it from any project folder
```

Run `./install.sh` again after changing the code.

`smallcode --web` prints a private link (127.0.0.1 only, with a random key). Open it in a browser.

## Models

No setup is needed for a model on this computer. At startup smolcoder looks for:

- **Ollama** at `OLLAMA_HOST` (default `127.0.0.1:11434`), including ports published by Docker or Podman containers.
- **LM Studio** on the port its settings name (default 1234). Turn on its server under Developer → Local Server.
- **Servers you added** in the web UI: another machine running Ollama or LM Studio, or any OpenAI-compatible server such as llama.cpp, vLLM or Jan (with an optional API key).
- **DeepSeek cloud** when `DEEPSEEK_API_KEY` is set or a key was pasted in the web UI.

The answer from each address decides what it is, not the port. Without `--model`, the last model used is picked again; otherwise a model on this computer, Ollama first.

In the web UI, click the model name in the status bar to switch models, download Ollama models (with a progress bar and a size suggestion for your memory), add or remove servers, search the local network for Ollama and LM Studio, or save a DeepSeek key. When no model is found, the session shows these steps instead of an error.

| Setting | Meaning |
|---|---|
| `OLLAMA_HOST` | Where Ollama listens. `0.0.0.0` is read as this computer |
| `SMOLCODER_KEEP_ALIVE` | How long Ollama keeps a model in memory between requests (default `30m`) |
| `DEEPSEEK_API_KEY` / `DEEPSEEK_BASE_URL` | DeepSeek key and endpoint. The environment wins over a pasted key |

A pasted DeepSeek key, and API keys of added servers, are stored in `~/.smolcoder/secrets.json`, readable by you only (mode 0600), never in the session database. Saved servers are kept in the database.

Context windows: Ollama keeps the context set in its app (Settings → Context length); `--ctx` sets one per request instead. LM Studio uses the context the model was loaded with; `--ctx` loads a model that is not loaded yet at that size. Custom servers report their size when they can (vLLM, llama.cpp); otherwise 4096 is assumed until you pass `--ctx`.

Searching the network sends a quick connection attempt to every address of your local subnets on ports 11434 and 1234. On macOS the terminal app needs the Local Network permission (System Settings → Privacy & Security → Local Network) for this and for servers on other machines.

| Option | Meaning |
|---|---|
| `[workspace]` | Project folder (default: the current folder) |
| `-m, --mode ro\|edit\|bypass` | Permission mode |
| `--model qwen3` | Model by (partial) name, for example `qwen3:8b` or `deepseek-flash` |
| `--effort off\|low\|medium\|high\|default` | Reasoning effort. `off` is cheapest for long tool loops |
| `--ctx <tokens>` | Context budget (see Models; DeepSeek default 128k, up to 1M) |
| `-p "<prompt>"` | Headless: run one prompt, print a `[stats]` line, then exit |
| `--verify "<cmd>"` | Headless acceptance command, repaired until it passes |
| `--web [port]` | Browser UI (default port 7433) |

Data lives in `~/.smolcoder/smolcoder.db` (SQLite). Set `SMOLCODER_HOME` to use another folder.

## Layout

```
src/
  Smolcoder.Domain          entities and pure rules (messages, plan, modes)
  Smolcoder.Application     agent loop, context manager, tools registry, sessions; ports (interfaces)
  Smolcoder.Infrastructure  model providers and discovery, file tools, sandbox, shell, SQLite (EF Core)
  Smolcoder.Web             Blazor Server UI, hub, API endpoints
  Smolcoder.Cli             the "smol" executable (System.CommandLine)

tests/
  Smolcoder.Tests           xUnit tests (dotnet test)
```

Dependencies point inward: Cli/Web → Infrastructure → Application → Domain.

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Smolcoder.Infrastructure --startup-project src/Smolcoder.Infrastructure --output-dir Persistence/Migrations
```

Migrations are applied automatically at startup.
