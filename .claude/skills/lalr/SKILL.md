---
name: lalr
description: Bootstrap context for the LALR.CC repo. Run at the start of a session to recover the canonical state — phase status, where the YAML/schema/generator/visitor pipeline lives, the verification commands that must pass before committing, and the ranked list of outstanding work. Invoke when you need "what is this repo and what should I do next?".
---

# LALR repo skill

LALR(1) parser-table generator + runtime in C# (.NET 10 / AOT). Modernised in 2026 from Phillip Voyle's CodeProject article. Architecture and conventions live in `CLAUDE.md` at the repo root — read it first if you haven't. This skill is the *what's done / what's next / how to verify* layer on top.

## Read these first, in order

1. `CLAUDE.md` — project architecture, conventions, AOT/test gotchas. Authoritative.
2. `handoff.md` — last-session notes, untracked. May be stale; treat as a hint, not gospel.
3. `git log --oneline -10` — recent commits. The last few are large milestones (`Phase 2`, `Phase 3`, `Phase 4`).

## Phase status (as of last commit)

| Phase | Status | What landed |
|---|---|---|
| Modernisation | ✅ | .NET 10 / C# 14 / AOT, byte-DFA lexer pipeline (`PipeBytesLexer`), xUnit v3 on MTP, fail-fast diagnostics across all layers (`GrammarConflictException`, `LexerException`, `ParseErrorException`), `Item.SourcePosition`. |
| Schema layer (1) | ✅ | `Schema/GrammarSchema.cs` POCOs, `Schema/IRxParser.cs` regex dialect, `Schema/SchemaCompiler.cs`. Pure C# data → `(Grammar, LexRule[])`. |
| Source generator (2) | ✅ | `LALR.CC.SourceGenerators` — Roslyn `IIncrementalGenerator` consumes `*.lalr.yaml` AdditionalFiles, emits `<ClassName>.g.cs` with `GrammarSchema Schema { get; }`. YamlDotNet build-time only. |
| Typed AST (3a) | ✅ | `AstEmitter` writes `<ClassName>.Ast.g.cs` — `public sealed record <Action>(Item Arg0, …)` per distinct action. Arity conflicts surface as `LALR0002`. |
| Visitor + wiring (3b) | ✅ | `VisitorEmitter` writes `<ClassName>.Visitor.g.cs` — nested `IVisitor` with one `Visit(<Record>)` overload per action. `BuildActions(IVisitor)` constructs the record from the parser's reduction frame and dispatches by C# overload resolution. |
| Self-host (4) | ✅ | `Bootstrap.Stage1/` consumes `bnf.lalr.yaml` end-to-end. `Bootstrap/` (stage0) keeps the inline grammar as the no-generator-no-YAML reference; both produce byte-identical Accept output. |
| JSON example | ✅ | `examples/Json/` parses real JSON with a 50-line visitor that builds `Dictionary<string,object>` / `List<object>` / primitives. Demonstrates the pipeline on a grammar nobody designed for this parser. |
| NuGet packaging + CI | ✅ | Runtime project packs `LALR.CC.{nupkg,snupkg}` with the source generator + YamlDotNet bundled in `analyzers/dotnet/cs/` (so PackageReference consumers don't need the analyzer-DLL workaround). Deterministic + SourceLink + symbols. `.github/workflows/dotnet.yml` builds + tests + stage-parity-checks on push, packs + uploads artifact on master, publishes to NuGet on `v*` tag using `NUGET_API_KEY` (SharpAstro org-level secret). |
| 2.1.0 features | ✅ | (a) `SourcePosition.GetCodepointColumn(ReadOnlySpan<byte> source)` for diagnostics-quality non-ASCII columns. (b) Generic `IVisitor<out T>` + `BuildActions<T>` so evaluators can return typed values directly (`IVisitor<int>` etc.) instead of always boxing to `object`. (c) `LALR0003` Roslyn diagnostics from a generator-side `SchemaValidator` — unknown symbol refs, missing root state, duplicate symbols, mutually-exclusive lexer instructions surface at build time instead of runtime. |
| LaTeX rich-render example | ✅ | `examples/Latex.Grammar/` (shared `Latex` partial class — generator runs once) feeds two consumers: `examples/Latex/` (Unicode-string visitor) and `examples/LatexConsole/` (Box-layout visitor → sixel/sextant/half-block via `Console.Lib.BoxRenderer` + `DIR.Lib.MathLayout`). Both ship `PublishAot=true`. |
| `lalr-tui` debugger | ✅ | `Tui/LALR.CC.Tui.csproj` (alias `lalr-tui`). Loads `*.lalr.yaml` live, runs `SchemaCompiler` + builds the `Parser`, displays grammar / lexer rules / token stream / parse-table cells in a `Console.Lib` dock layout. AOT-off because YamlDotNet runtime deserializer needs reflection — Phase 5 doesn't unblock this (Tui edits arbitrary YAML at runtime, so YamlDotNet stays). |
| Phase 5 / slice 1 (compiler-compiler) | ✅ | `Parser.cs` table-construction extracted into `ParserTableBuilder.cs` — pure C#, no `LexicalGrammar` dependency, identical public API on `Parser` (delegates via passthrough). Sets up the netstandard2.0 link path for slice 2. Also tightened all introspection-property types from `IList<>` / `ICollection<>` / `ISet<>` / `HashSet<int>[]` to `IReadOnly*` (or concrete `HashSet<int>` for the IReadOnlySet-needing ones, since the latter is .NET 5+ and the builder will be linked into netstandard2.0). |
| Phase 5 / slice 2 (compiler-compiler) | ✅ | `ParserTableBuilder` + dependencies (`Grammar`, `ParseTable`, `LRItems`, `GrammarConflict`, `SymbolName`) now `<Compile Link>`-shared into the netstandard2.0 source generator. Required: extracting `Reduction` to its own file, splitting `Production` into a `partial readonly struct` (linkable partial owns `Left`/`Right`/`HasRewriter` + `Delegate?` storage; runtime partial in `Production.Rewriter.cs` adds the strongly-typed `Func<int, Item[], object>` ctor + `Rewrite()`), and replacing `HashCode.Combine` (netstandard 2.1+) with a manual fold. Generator can now run table-build at compile time. |
| Phase 5 / slice 3 (compiler-compiler) | ✅ | New `TablesEmitter` runs `ParserTableBuilder` at build time and emits `<Name>.Tables.g.cs` with `Definition` (Grammar literal), `ParseTable` (Action[,] literal), and `BuildParser()` factory. New `Parser(Grammar, ParseTable)` ctor skips table-build (introspection getters throw `NotSupportedException` on this path; `Productions`/`NonTerminals` derived directly from `Grammar`). New `LALR0004` Roslyn diagnostic surfaces unresolved S/R + R/R conflicts at build time with YAML locator (replaces `GrammarConflictException` for pre-baked consumers). 306/306 tests, stage parity holds. Action-free `BuildParser()` only — visitor-aware overload is slice 4. |

## Canonical re-verification (run before any commit)

```bash
dotnet build LALR.CC.sln -c Debug --nologo                                # 0 warnings
dotnet test  LALR.CC.Tests/LALR.CC.Tests.csproj -c Debug  # 304+ pass
dotnet run   --project Bootstrap/Bootstrap.csproj                  -c Release --no-build  # ends "Accept (…)"
dotnet run   --project Bootstrap.Stage1/Bootstrap.Stage1.csproj    -c Release --no-build  # ends "Accept (…)"
dotnet run   --project TestProject/TestProject.csproj              -c Release --no-build  # ends "Accept (…): S' -> [0]"
dotnet run   --project examples/Json/Examples.Json.csproj          -c Release --no-build  # prints parsed JSON tree
dotnet publish Bootstrap/Bootstrap.csproj                           -c Release            # AOT clean
dotnet publish Bootstrap.Stage1/Bootstrap.Stage1.csproj             -c Release            # AOT clean
dotnet publish TestProject/TestProject.csproj                       -c Release            # AOT clean
dotnet publish examples/LatexConsole/Examples.LatexConsole.csproj   -c Release            # AOT clean (Latex.Grammar consumer)
dotnet pack LALR.CC/LALR.CC.csproj -c Release -o packages # nupkg + snupkg
```

To cut a release: bump `<Version>` in `LALR.CC.csproj`, commit, tag `vX.Y.Z` matching the version, push the tag. The CI workflow's publish job verifies tag-matches-csproj before pushing to NuGet.

**Stage parity check** — when changes touch the YAML/schema/generator/visitor stack, diff stage0 vs stage1 Accept output (timing-stripped):

```bash
diff <(dotnet run --project Bootstrap/Bootstrap.csproj --no-build -c Release \
        | grep -oE '^Accept.*' | sed 's/^Accept ([^)]*): /Accept: /') \
     <(dotnet run --project Bootstrap.Stage1/Bootstrap.Stage1.csproj --no-build -c Release \
        | grep -oE '^Accept.*' | sed 's/^Accept ([^)]*): /Accept: /')
```

No output ⇒ they match.

## Outstanding work (ranked by value)

Pick from here when the user asks "what's next?":

1. **Phase 5 (compiler-compiler) — done.** All six slices shipped. The generator runs `ParserTableBuilder` *and* the regex parser at build time and emits `<Name>.Tables.g.cs` (`Definition` + `ParseTable` + `BuildParser()`) plus `<Name>.Lexer.g.cs` (`LexRule[]` arrays + `BuildLexer()`) per YAML grammar; `VisitorEmitter` adds the visitor-aware `BuildParser<T>(IVisitor<T>)`. All in-tree consumers (`Bootstrap.Stage1`, `Examples.Calculator` / `.Json` / `.Latex` / `.LatexConsole`) route through `BuildParser(visitor)` + `BuildLexer()` so `ParserTableBuilder`, `IRxParser`, and the runtime DFA-builder are all unreachable in their AOT trim graphs. Build-time diagnostics: `LALR0004` (parse-table conflicts) and `LALR0005` (bad regex syntax). `lalr-tui` stays runtime-build — editing arbitrary YAML live is its whole point. The runtime regex parser stays as the canonical implementation; the generator has its own `RegexEmitter` that mirrors its semantics and emits constructor expressions for the runtime IRx public API (no source linking, no netstandard2.0 polyfill drama).
2. **Sync parser/lexer surface — done.** Pair of `ISyncIterator<T>` / `ISyncLAIterator<T>` interfaces alongside the existing async ones; sync `BytesLexer` walks `ReadOnlyMemory<byte>` directly (no `PipeReader`, no `Task` machinery, otherwise the same DFA per state); `Parser.ParseInput` mirrors the async parse loop with no awaits; `SyncLATokenIterator` + `SyncEnumerableWrapper` round out the iterator wrappers. All in-memory consumers (`Bootstrap.Stage1`, `TestProject`, `Examples.Calculator` / `.Json` / `.Latex` / `.LatexConsole`) route through the sync path — no per-token `Task<T>` allocations or async state-machine restores. `Bootstrap` (stage 0) deliberately stays on the async path so the stage-parity diff is now a sync ↔ async equivalence test as well. The async path stays for streaming consumers (stdin / network / on-disk) which still need `await PipeReader.ReadAsync`. Code-duplicates the parse loop on purpose — extracting a shared body would force `ValueTask` boxing or generic gymnastics that erodes the savings; `SyncAsyncParityTests` keeps the two implementations in lockstep.
3. **More example grammars.** TOML config, C declaration syntax (the famous "Lexer Hack"), an arithmetic-with-unary-ops upgrade for `TestProject`. Each ships under `examples/`.

## Importers — explicitly *not* on the roadmap

EBNF, YACC/Bison, ANTLR4, GOLD all have format-specific edge cases (EBNF's `{}`/`[]` desugaring, YACC's C action bodies, ANTLR's lexer modes + semantic predicates). Each is a multi-week project worth doing only when a real user is blocked. Until then, prefer manual ports of well-known grammars to YAML — that gives example-doc value without the importer maintenance burden. Revisit if/when this changes.

## When asked to commit

- Don't include `handoff.md` (untracked by intent).
- Co-author trailer is `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`.
- Use HEREDOC for the commit message body (formatting).
- Don't push without explicit user request — the branch typically runs ahead of `origin/master`.

## When stuck

- `git log --oneline -20` — what landed recently
- `Bootstrap/Program.cs` — the always-buildable reference grammar (no generator, no YAML)
- `Bootstrap.Stage1/Program.cs` — the same grammar, pipeline-driven
- `bnf.lalr.yaml` (Stage1) — what a real grammar definition looks like
- `LALR.CC.Tests/SourceGenerators/` — driving the generator under test

When something breaks in the pipeline, the diagnosis order is: (1) does Bootstrap (stage0) still parse? if yes, the runtime is fine — move on. (2) does `dotnet build` of `Bootstrap.Stage1` succeed? if yes, the generator + YAML loader work — move to runtime. (3) does the test suite pass? (4) only then dive into the specific symptom.
