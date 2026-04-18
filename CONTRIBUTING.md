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

```bash
# Requires .NET SDK and reference to GTA.Native.dll, GTA.UI.dll
dotnet build
```

Output: `bin/Debug/StreetRiders.dll` → copy to `GTA V/scripts/`

## License

By contributing, you agree your work will be licensed under CC BY-NC-SA 4.0.