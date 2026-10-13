# smolcoder (C#)

A C# (.NET 10) rewrite of smolcoder, built with clean architecture. It uses DeepSeek's cloud API and has a browser UI that shows Myanmar and all other scripts correctly.

## Run

```bash
export DEEPSEEK_API_KEY=sk-...
dotnet run --project src/Smolcoder.Cli -- --web            # browser UI for the current folder
dotnet run --project src/Smolcoder.Cli -- -p "fix the failing test" --effort off   # one headless prompt
```

### Install as a command

```bash
./install.sh          # publishes to ~/.smolcoder/app and adds /usr/local/bin/smallcode
smallcode --web       # same as the dotnet run line above; run it from any project folder
```

Run `./install.sh` again after changing the code.

`smol --web` prints a private link (127.0.0.1 only, with a random key). Open it in a browser.

| Option | Meaning |
|---|---|
| `[workspace]` | Project folder (default: the current folder) |
| `-m, --mode ro\|edit\|bypass` | Permission mode |
| `--model deepseek-flash` | Model (the old name `deepseek-v4-flash` also works) |
| `--effort off\|low\|medium\|high\|default` | Reasoning effort. `off` is cheapest for long tool loops |
| `--ctx <tokens>` | Context budget (default 128k, up to 1M) |
| `-p "<prompt>"` | Headless: run one prompt, print a `[stats]` line, then exit |
| `--verify "<cmd>"` | Headless acceptance command, repaired until it passes |
| `--web [port]` | Browser UI (default port 7433) |

Data lives in `~/.smolcoder/smolcoder.db` (SQLite). Set `SMOLCODER_HOME` to use another folder.

## Layout

```
src/
  Smolcoder.Domain          entities and pure rules (messages, plan, modes)
  Smolcoder.Application     agent loop, context manager, tools registry, sessions; ports (interfaces)
  Smolcoder.Infrastructure  DeepSeek provider, file tools, sandbox, shell, SQLite (EF Core)
  Smolcoder.Web             Blazor Server UI, hub, API endpoints
  Smolcoder.Cli             the "smol" executable (System.CommandLine)
```

Dependencies point inward: Cli/Web → Infrastructure → Application → Domain.

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Smolcoder.Infrastructure --startup-project src/Smolcoder.Infrastructure --output-dir Persistence/Migrations
```

Migrations are applied automatically at startup.
