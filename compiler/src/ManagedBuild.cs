/*
  CC# --dna : the managed side to CIL.

  The classes that stay C# (and the C# proxies of the native classes they call) are compiled by Roslyn, here, in the compiler, against
  DotNetAnywhere's own class library: `--dna-corlib=PATH` is its corlib.dll, and nothing else is referenced, which is what
  `csc -nostdlib -r:corlib.dll` does.  The result runs on DNA (native/src/Host.h); corlib.dll must sit beside it.
*/
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CCSharpCompiler;

static class ManagedBuild
{
  /** Compile `files` to `outPath`.  Returns the diagnostics that are errors (empty on success). */
  public static List<string> Compile(IEnumerable<string> files, string corlibPath, string outPath, bool exe, string mainType)
  {
    var errors = new List<string>();
    var texts = files.Select(f => (path: f, text: File.ReadAllText(f))).ToList();
    var options = new CSharpCompilationOptions(exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
      mainTypeName: exe ? mainType : null, allowUnsafe: true, optimizationLevel: OptimizationLevel.Release);
    for (int attempt = 0; attempt < 2; attempt++) {
      errors.Clear();
      var trees = texts.Select(t => CSharpSyntaxTree.ParseText(t.text, path: t.path)).ToList();
      var comp = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(outPath), trees, new[] { MetadataReference.CreateFromFile(corlibPath) }, options);
      using (var ms = new MemoryStream()) {
        var result = comp.Emit(ms);
        foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) errors.Add("managed: " + d);
        if (result.Success) { File.WriteAllBytes(outPath, ms.ToArray()); return errors; }
        // A `using` of a namespace that has no managed side (the bindings of a native library, say) in a file that kept some managed classes: the
        // directive cannot be satisfied, and nothing managed uses it. Blank those directives out (line for line) and compile once more.
        var bad = new Dictionary<SyntaxTree, List<Microsoft.CodeAnalysis.Text.TextSpan>>();
        foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && (d.Id == "CS0234" || d.Id == "CS0246") && d.Location.SourceTree != null)) {
          var u = d.Location.SourceTree.GetRoot().FindToken(d.Location.SourceSpan.Start).Parent?.AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>().FirstOrDefault();
          if (u == null) continue;
          if (!bad.TryGetValue(d.Location.SourceTree, out var l)) bad[d.Location.SourceTree] = l = new List<Microsoft.CodeAnalysis.Text.TextSpan>();
          if (!l.Contains(u.Span)) l.Add(u.Span);
        }
        if (bad.Count == 0 || attempt == 1) { if (errors.Count == 0) errors.Add("managed: the managed assembly did not compile"); break; }
        for (int i = 0; i < texts.Count; i++) {
          var tree = trees[i];
          if (!bad.TryGetValue(tree, out var spans)) continue;
          var chars = texts[i].text.ToCharArray();
          foreach (var sp in spans) for (int k = sp.Start; k < sp.End; k++) if (chars[k] != '\n' && chars[k] != '\r') chars[k] = ' ';
          texts[i] = (texts[i].path, new string(chars));
        }
      }
    }
    return errors;
  }
}
