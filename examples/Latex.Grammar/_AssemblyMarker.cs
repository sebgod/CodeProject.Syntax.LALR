// Stub source file — the C# compiler skips CoreCompile entirely if the
// project has zero .cs inputs, which means the source generator (which only
// runs as part of CSC) never fires. A single empty namespace declaration is
// enough to force CSC to run, after which the generator picks up the YAML
// from AdditionalFiles and emits Latex.g.cs / Latex.Ast.g.cs / Latex.Visitor.g.cs.
namespace LatexGrammar;
