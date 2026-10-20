# Contributing to Street Riders

## Reporting Issues

- **Check existing issues** before opening a new one
- Include GTA V version, ScriptHookVDotNet version
- For bugs: steps to reproduce, expected vs actual behavior
- For feature requests: explain the use case

## Pull Requests

1. Fork the repo
2. Create a feature branch (`fix/description` or `feat/description`)
3. Test locally before submitting
4. Keep changes focused and minimal
5. Update README if needed for user-facing changes

## Coding Standards

- Follow the existing code style (C#, GTA scripts)
- Keep backward compatibility with existing INI config
- Test with natural traffic (peds not spawned by the mod)

## Build

Requires the .NET SDK and a GTA V install with ScriptHookV and ScriptHookVDotNet 3.

```bash
dotnet build src/StreetRiders.csproj -c Release
```

The project looks for GTA V in the default Steam folder. If yours is elsewhere, pass its path:

```bash
dotnet build src/StreetRiders.csproj -c Release /p:GTAPath="D:\Games\Grand Theft Auto V"
```

The DLL is written to your GTA V `scripts/` folder and copied to `dist/scripts/`.

## License

By contributing, you agree your work will be licensed under CC BY-NC-SA 4.0.