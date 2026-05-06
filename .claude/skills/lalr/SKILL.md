---
name: lalr
description: Bootstrap context for the CodeProject.Syntax.LALR repo. Run at the start of a session to recover the canonical state — phase status, where the YAML/schema/generator/visitor pipeline lives, the verification commands that must pass before committing, and the ranked list of outstanding work. Invoke when you need "what is this repo and what should I do next?".
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
| Source generator (2) | ✅ | `CodeProject.Syntax.LALR.SourceGenerators` — Roslyn `IIncrementalGenerator` consumes `*.lalr.yaml` AdditionalFiles, emits `<ClassName>.g.cs` with `GrammarSchema Schema { get; }`. YamlDotNet build-time only. |
| Typed AST (3a) | ✅ | `AstEmitter` writes `<ClassName>.Ast.g.cs` — `public sealed record <Action>(Item Arg0, …)` per distinct action. Arity conflicts surface as `LALR0002`. |
| Visitor + wiring (3b) | ✅ | `VisitorEmitter` writes `<ClassName>.Visitor.g.cs` — nested `IVisitor` with one `Visit(<Record>)` overload per action. `BuildActions(IVisitor)` constructs the record from the parser's reduction frame and dispatches by C# overload resolution. |
| Self-host (4) | ✅ | `Bootstrap.Stage1/` consumes `bnf.lalr.yaml` end-to-end. `Bootstrap/` (stage0) keeps the inline grammar as the no-generator-no-YAML reference; both produce byte-identical Accept output. |
| JSON example | ✅ | `examples/Json/` parses real JSON with a 50-line visitor that builds `Dictionary<string,object>` / `List<object>` / primitives. Demonstrates the pipeline on a grammar nobody designed for this parser. |
| NuGet packaging + CI | ✅ | Runtime project packs `CodeProject.Syntax.LALR.{nupkg,snupkg}` with the source generator + YamlDotNet bundled in `analyzers/dotnet/cs/` (so PackageReference consumers don't need the analyzer-DLL workaround). Deterministic + SourceLink + symbols. `.github/workflows/dotnet.yml` builds + tests + stage-parity-checks on push, packs + uploads artifact on master, publishes to NuGet on `v*` tag using `NUGET_SECRET`. |
| 2.1.0 features | ✅ | (a) `SourcePosition.GetCodepointColumn(ReadOnlySpan<byte> source)` for diagnostics-quality non-ASCII columns. (b) Generic `IVisitor<out T>` + `BuildActions<T>` so evaluators can return typed values directly (`IVisitor<int>` etc.) instead of always boxing to `object`. (c) `LALR0003` Roslyn diagnostics from a generator-side `SchemaValidator` — unknown symbol refs, missing root state, duplicate symbols, mutually-exclusive lexer instructions surface at build time instead of runtime. |
| LaTeX rich-render example | ✅ | `examples/Latex.Grammar/` (shared `Latex` partial class — generator runs once) feeds two consumers: `examples/Latex/` (Unicode-string visitor) and `examples/LatexConsole/` (Box-layout visitor → sixel/sextant/half-block via `Console.Lib.BoxRenderer` + `DIR.Lib.MathLayout`). Both ship `PublishAot=true`. |
| `lalr-tui` debugger | ✅ | `Tui/CodeProject.Syntax.LALR.Tui.csproj` (alias `lalr-tui`). Loads `*.lalr.yaml` live, runs `SchemaCompiler` + builds the `Parser`, displays grammar / lexer rules / token stream / parse-table cells in a `Console.Lib` dock layout. AOT-off because YamlDotNet runtime deserializer needs reflection — Phase 5 doesn't unblock this (Tui edits arbitrary YAML at runtime, so YamlDotNet stays). |
| Phase 5 / slice 1 (compiler-compiler) | ✅ | `Parser.cs` table-construction extracted into `ParserTableBuilder.cs` — pure C#, no `LexicalGrammar` dependency, identical public API on `Parser` (delegates via passthrough). Sets up the netstandard2.0 link path for slice 2. Also tightened all introspection-property types from `IList<>` / `ICollection<>` / `ISet<>` / `HashSet<int>[]` to `IReadOnly*` (or concrete `HashSet<int>` for the IReadOnlySet-needing ones, since the latter is .NET 5+ and the builder will be linked into netstandard2.0). |

## Canonical re-verification (run before any commit)

```bash
dotnet build CodeProject.Syntax.LALR.sln -c Debug --nologo                                # 0 warnings
dotnet test  CodeProject.Syntax.LALR.Tests/CodeProject.Syntax.LALR.Tests.csproj -c Debug  # 304+ pass
dotnet run   --project Bootstrap/Bootstrap.csproj                  -c Release --no-build  # ends "Accept (…)"
dotnet run   --project Bootstrap.Stage1/Bootstrap.Stage1.csproj    -c Release --no-build  # ends "Accept (…)"
dotnet run   --project TestProject/TestProject.csproj              -c Release --no-build  # ends "Accept (…): S' -> [0]"
dotnet run   --project examples/Json/Examples.Json.csproj          -c Release --no-build  # prints parsed JSON tree
dotnet publish Bootstrap/Bootstrap.csproj                           -c Release            # AOT clean
dotnet publish Bootstrap.Stage1/Bootstrap.Stage1.csproj             -c Release            # AOT clean
dotnet publish TestProject/TestProject.csproj                       -c Release            # AOT clean
dotnet publish examples/LatexConsole/Examples.LatexConsole.csproj   -c Release            # AOT clean (Latex.Grammar consumer)
dotnet pack CodeProject.Syntax.LALR/CodeProject.Syntax.LALR.csproj -c Release -o packages # nupkg + snupkg
```

To cut a release: bump `<Version>` in `CodeProject.Syntax.LALR.csproj`, commit, tag `vX.Y.Z` matching the version, push the tag. The CI workflow's publish job verifies tag-matches-csproj before pushing to NuGet.

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

1. **Phase 5 (compiler-compiler) — slice 2+.** Slice 1 landed (`ParserTableBuilder` extracted, pure C#, no `LexicalGrammar` dependency). Remaining slices:
   - **Slice 2 — link the builder into the generator.** Add `<Compile Link>` for `ParserTableBuilder.cs` + dependencies (`Grammar.cs`, `ParseTable.cs`, `LRItems.cs`, `GrammarConflict.cs`, `SymbolName.cs`) into `CodeProject.Syntax.LALR.SourceGenerators.csproj` (netstandard2.0). Anything in those files that references `LexicalGrammar.Item` (`Production.Rewrite`, `Reduction`) blocks netstandard2.0 — split via partial classes or move types out.
   - **Slice 3 — emit pre-baked tables.** Generator runs `SchemaCompiler.Compile` + `new ParserTableBuilder(grammar)` at build time; emits a populated `Action[,]` literal alongside the schema. Add `Parser(Grammar, ParseTable)` ctor that skips the build. Existing `new Parser(grammar)` keeps working unchanged (additive).
   - **Slice 4 — `LALR0004`.** Surface S/R + R/R conflicts as Roslyn diagnostics with YAML locators instead of runtime `GrammarConflictException`.
   - **Slice 5 — migrate `Bootstrap.Stage1` / `Examples.Json` / `Examples.Latex` / `Examples.LatexConsole` to the pre-baked path** (drop their runtime `SchemaCompiler.Compile` calls in favour of the generated `BuildParser()`). `lalr-tui` stays runtime-build because its purpose is editing arbitrary YAML live.
2. **Deeper generator-time validation: regex patterns.** Slice 1 (LALR0003) covers structural errors. Linking `IRxParser` source into the generator would let bad `match:` regexes surface at build time too. Smaller scope than Phase 5 — `IRxParser` only needs the regex AST nodes (`CharRx`, `GroupRx`, etc.) which live in `LexicalGrammar/`. Naturally pairs with slice 2 (same linking work).
3. **More example grammars.** TOML config, C declaration syntax (the famous "Lexer Hack"), an arithmetic-with-unary-ops upgrade for `TestProject`. Each ships under `examples/`.
4. **Async-per-token at the parser/iterator boundary.** The lexer's inner loop is sync and the only `await` per byte-buffer is `PipeReader.ReadAsync`. The remaining async-per-token cost lives in `IAsyncIterator<Item>` callers and the parser loop. A pure-sync, `ref struct`-style parser is possible; not a priority.

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
- `CodeProject.Syntax.LALR.Tests/SourceGenerators/` — driving the generator under test

When something breaks in the pipeline, the diagnosis order is: (1) does Bootstrap (stage0) still parse? if yes, the runtime is fine — move on. (2) does `dotnet build` of `Bootstrap.Stage1` succeed? if yes, the generator + YAML loader work — move to runtime. (3) does the test suite pass? (4) only then dive into the specific symptom.
