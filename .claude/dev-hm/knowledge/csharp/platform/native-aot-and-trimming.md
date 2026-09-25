# Modern .NET platform patterns — Native AOT and trimming

Section of `knowledge/csharp/platform.md`.


Native AOT is production-viable for ASP.NET Core minimal-API services and console apps on the
current LTS. Design AOT-ready from the start when the target is AOT: prefer source generators over
reflection everywhere (JSON, config, logging, regex, DI), annotate any unavoidable reflection with
`[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`, and enable `<IsAotCompatible>true</IsAotCompatible>`
to surface trim/AOT analyzer warnings in the build. Not every library is AOT-safe — verify
dependencies before committing to it.
