# Modern .NET platform patterns — Structured logging

Section of `knowledge/csharp/platform.md`.


Use `ILogger<T>` with message templates and named placeholders, or `LoggerMessage`-generated
methods for hot paths (see `knowledge/csharp/source-generators.md#logging`). Do not build log text
with string interpolation — it defeats structured logging and allocates even when the level is
disabled.
