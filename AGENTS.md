# AGENTS.md

Guidance for coding agents working in the **SSETA React code generator**
repository.

## Project summary

This is a **C# /.NET 8** code generation tool that drives the three portal
frontends in this monorepo:

- `management-portal`
- `partner-portal`
- `learner-portal`

This repo generates both:

- the **current generated frontend layers**: services, types, resource
  contexts, forms, and provider layout wiring
- the **legacy frontend layers**: legacy services, types, field hooks,
  utilities, and legacy pages/components/reports
- shared enums for the component library in
  `component-library/src/types/enums.ts`

If a required frontend change affects generated code, change the **generator,
template, or input in this repo** rather than patching portal output by hand.

## Non-negotiables

- keep code as simple as possible
- always follow existing project standards and local patterns
- prefer consistency with the surrounding generator/template code over generic abstractions
- keep edits minimal and targeted
- determine whether the change affects the **current generator**, the **legacy generator**, or **both** before writing code
- do not hand-edit generated portal output when the correct fix belongs in this repo
- preserve existing generated file names, paths, imports, and broad output structure unless the task clearly requires a coordinated change
- keep files focused on a single responsibility
- if logic is repeated across generators, move it into an appropriate shared utility rather than duplicating it
- be especially careful with `src/utils/Formatters.cs`, `input/codegen.config.json`, template placeholders, and output path conventions
- do not start writing code until you are at least **95% sure** of the correct implementation
- if requirements, behavior, or edge cases are unclear, ask follow-up questions before coding

## Commands

Use **dotnet** in this repo.

```bash
dotnet build
dotnet run
dotnet run legacy
```

- `dotnet run` runs the current generator pipeline
- `dotnet run legacy` runs the legacy generator pipeline
- there is no dedicated test project configured in this repo

Before running generation, make sure:

- SQL Server is reachable using the connection string in
  `input/codegen.config.json`
- the relevant backend APIs are running so Swagger can be fetched
- output paths in `input/codegen.config.json` still point to the correct sibling
  repos

## Architecture

This repo has **two distinct generation modes**.

### Current generator

Primary entry point:

- `src/Program.cs`

Current generation reads config from `input/codegen.config.json`, fetches and
caches Swagger, generates shared enums, then generates portal output for:

- services
- types
- resource contexts
- forms
- provider layout wiring
- field manifests

Key locations:

- `src/generators/`
- `src/templates/`
- `src/utils/`
- `input/form-layout/`
- `input/swagger/`

### Legacy generator

Primary entry point:

- `src/LegacyProgram.cs`

Legacy generation fetches and caches legacy Swagger, generates management-only
report outputs, then generates legacy output across the portals for:

- services
- types
- field hooks
- utilities
- legacy components/pages/reports

Key locations:

- `src/generators/legacy/`
- `src/utils/legacy/`
- `input/legacy/`
- `output/`

Keep the two pipelines conceptually separate. A fix for one mode should not
spill into the other unless the task explicitly requires it.

## Responsibility by layer

- **Program / LegacyProgram**: orchestration, config loading, portal iteration,
  and generation order
- **Generators**: transform Swagger, config, and database metadata into output
  files
- **Templates**: define the stable TS/TSX shell and placeholder insertion
  points
- **Utilities / formatters**: shared naming, type resolution, layout rules,
  exclusions, and helper logic
- **Input files**: source configuration and generation metadata
- **Output files/logs**: generated artifacts and diagnostics, not the source of
  truth

## Config, templates, and outputs

### Main config

Primary source of truth:

- `input/codegen.config.json`

It controls:

- module tokens used for type-name formatting
- SQL connection details for enum generation
- template file paths
- per-portal Swagger URLs and cache paths
- per-portal field layout inputs
- per-portal output directories
- blacklist entries

Important notes:

- output paths are absolute and write directly into sibling repos
- blacklist entries support both `MODULE.Resource` and
  `MODULE.Resource.Operation`

### Templates

Current generation uses templates in `src/templates/`.

When editing templates:

- preserve placeholder markers exactly unless you are intentionally changing the
  generator/template contract on both sides
- keep template shells stable and let generators inject the variable content
- do not bake portal-specific behavior into a shared template unless the config
  or generator explicitly supports it

### Outputs

Current generation writes into sibling portal `src/` folders such as:

- `src/services`
- `src/types`
- `src/contexts`
- `src/forms`
- `src/app`

Legacy generation writes into legacy folders such as:

- `src/services/legacy/**`
- `src/types/legacy/**`
- `src/field-hooks/legacy/**`
- `src/utils/legacy/**`
- legacy components/pages/report outputs where applicable

If portal output needs to change, make the change here first and regenerate.

## Key conventions

### Formatters

A large amount of output consistency depends on:

- `src/utils/Formatters.cs`

This file drives naming, type resolution, field/layout behavior, exclusions, and
other shared generation rules. Small formatter changes can cascade across all
three portals, so be conservative.

### Legacy-specific caution

- legacy mode still generates real code used by the portals
- management has additional report generation responsibilities in legacy mode
- preserve existing legacy output paths and naming unless a coordinated portal
  change is required

## Safe editing guidance

Prefer changing:

- the generator implementation that owns the behavior
- templates when the shell/output structure needs to change
- `Formatters.cs` or shared utilities for truly shared rules
- `input/codegen.config.json` for configuration-driven changes
- form layout JSON files for layout/grouping changes

Be cautious with:

- `src/Program.cs`
- `src/LegacyProgram.cs`
- `src/utils/Formatters.cs`
- output path resolution logic
- provider layout generation
- blacklist behavior
- any change that affects all three portals at once
- changes that touch both current and legacy generation in one pass

## Before making changes

Before writing code:

1. determine whether the work belongs to the current generator, the legacy generator, or both
2. identify the real source of the behavior: generator, template, formatter, or config
3. inspect surrounding files and follow the existing local pattern
4. identify which portals and output directories will be affected
5. if the change touches naming, imports, paths, provider wiring, or blacklist logic, assume the blast radius is cross-portal until proven otherwise
6. if you are not at least 95% sure of the implementation, ask follow-up questions first

## After making changes

At minimum run:

```bash
dotnet build
```

Do not ever run the generators. Leave this to the user.
