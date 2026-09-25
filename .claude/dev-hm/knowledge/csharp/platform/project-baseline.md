# Modern .NET platform patterns — Project baseline

Section of `knowledge/csharp/platform.md`.


Set once in `Directory.Build.props` so every project inherits it:

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

Central Package Management (`Directory.Packages.props` with `ManagePackageVersionsCentrally`) keeps
one version per package across a solution. Pin the SDK in `global.json` for reproducible builds.
