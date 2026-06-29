# /project-stats — verification run (2026-06-29)

Host: Windows + Git Bash. Engine: bash + gawk over `git ls-files`.

## Test harness (classification fixtures)

```
ok   src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs -> C#/Production/Business
ok   src/Modules/Catalog/Kartova.Catalog.Application/AssignApplicationTeamCommand.cs -> C#/Production/Business
ok   src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs -> C#/Production/Infrastructure
ok   src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationCountByTeamReader.cs -> C#/Production/Infrastructure
ok   src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs -> C#/Test/-
ok   web/src/features/auth/api/session.ts -> TypeScript/Production/Business
ok   web/src/features/auth/api/__tests__/session.test.tsx -> TSX/Test/-
ok   src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/20260421192803_InitialCatalog.cs -> C#/Production/Boilerplate
ALL PASS
harness exit=0
```

## Full report

```
Headline:  1344 files · 164937 LOC · 28% business code (of production)

Language          Files       LOC
----------------------------------
Markdown            287     73278
C#                  555     42807
TSX                 249     24014
JSON                 27     11343
TypeScript           80      4191
HTML                 12      3815
Shell                 6      1309
MSBuild/XML          39      1080
YAML/Helm            13       843
CSS                   2       765
Config/Other         11       455
PowerShell            1       386
JavaScript            4       285
Python                1       175
Dockerfile            5       121
SQL                   1        36
Makefile              1        34
Asset                50         0
TOTAL              1344    164937

Role              Files       LOC
----------------------------------
Production          600     35797
Test                292     36314
Non-code            452     92826

Domain (production)  Files       LOC   % prod LOC
------------------------------------------------
Business              166     10060       28%
Infrastructure        371     20290       57%
Boilerplate            53      4846       14%
Other                  10       601        2%
  non-business        434     25737       72%

Notes: LOC = non-blank physical lines (comments included). The generated web
API client is gitignored and absent from this count. Classification is file-level.
engine exit=0
```

## Notes

- shellcheck not installed on host → advisory step skipped (non-blocking per plan).
- Reconciliation: Language TOTAL files == sum of language rows; Production+Test+Non-code == total; Business+Infrastructure+Boilerplate+Other == Production. All hold (engine exit 0).
