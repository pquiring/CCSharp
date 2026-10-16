/*
  CC# --dna : the native / managed partition.

  The unit is the CLASS (a class, struct or interface; an enum or an attribute class is shared by both sides).  Each class
  is one of

      Native    lowered to the Crust C++ subset, then C           (what CC# always did)
      Managed   left as C#, compiled to CIL, run by DotNetAnywhere (DNA)

  The default is "try Native, fall back to Managed": a class that CrustEmitter refuses becomes Managed instead of an error,
  and the reason is recorded.  [Managed] and [Native] (Crust namespace, or a marker class of the program's own) override it:

      [Managed]   never lowered, even if it could be
      [Native]    must be lowered; a refusal is an error, exactly as without --dna

  How the plan is made, in order:
    1. Probe.   A throw-away CrustEmitter emits every class and records what each one is refused for.
    2. Decide.  explicit attribute, else refused => Managed, else Native.  delegates and records are Managed (Crust has neither).
    3. Close.   A class, its base class and its interfaces are one family and must be on one side: a managed class cannot derive
                from a native one (no virtual dispatch across the boundary) nor the reverse.  One managed member makes the family
                managed; a [Native] member of such a family is an error naming the cause.
    4. References.  Which class uses which class on the other side.  This is the surface the bridge has to cover.
*/
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CCSharpCompiler;

enum Part { Auto, Native, Managed }

/** One unit of the partition: a top-level type. */
class ClassPlan
{
  public INamedTypeSymbol Sym;
  public string Name;                                       //C# name, namespace-qualified
  public string Kind;                                       //class struct interface record delegate
  public string File;
  public int Line;
  public Part Explicit = Part.Auto;                         //from [Managed] / [Native]
  public Part Final = Part.Auto;
  public string Reason = "";                                //why it is Managed (empty for Native)
  public List<string> Refusals = new List<string>();        //what the probe was refused for
  public string Demoted;                                    //set by the bridge analysis: it uses a managed class in a way that cannot cross, so it is managed too
  public bool Shared;                                       //enum, attribute class: on both sides
}

/** A use of a class that is on the other side. */
class CrossRef
{
  public ClassPlan From, To;
  public string File;
  public int Line;
  public string What;                                       //the member or type used
  public string Via;                                        //member | type | typed | new : how it was used
  public ISymbol Member;                                    //Via == member: the member
  public bool QualifiesStaticCall;                          //Via == type: `Script` in `Script.Go()` or `Script.count`: a type name qualifying a static member (judged on its own)
}

class PartitionPlan
{
  public Dictionary<INamedTypeSymbol, ClassPlan> Classes = new Dictionary<INamedTypeSymbol, ClassPlan>(SymbolEqualityComparer.Default);
  public List<ClassPlan> Order = new List<ClassPlan>();     //source order
  public List<string> Errors = new List<string>();
  public List<CrossRef> Refs = new List<CrossRef>();
  public List<string> GlobalStatementFiles = new List<string>();   //top-level statements: a managed entry point
  public BridgePlan Bridge;                                 //what crosses between the sides (null until analysed)
  public int DeclareErrorCount;                             //(the errors Declare found stay when the plan is rebuilt)
  public string ManagedEntry;                               //"Class" whose static Main is on the managed side, or null
  public List<string> ManagedEntries = new List<string>();  //every usable managed `static Main`

  /** Plans are per top-level type: a nested type belongs to its outermost one. */
  public static INamedTypeSymbol Outermost(INamedTypeSymbol t)
  {
    t = t.OriginalDefinition;
    while (t.ContainingType != null) t = t.ContainingType.OriginalDefinition;
    return t;
  }

  public ClassPlan Get(ISymbol s)
  {
    var t = s as INamedTypeSymbol;
    if (t == null) return null;
    Classes.TryGetValue(Outermost(t), out var cp);
    return cp;
  }

  public Part SideOf(ISymbol s)
  {
    var cp = Get(s);
    return cp == null ? Part.Auto : cp.Final;
  }

  // ------------------------------------------------------------------ 0: what the program declares

  static string AttrName(AttributeSyntax a)
  {
    string n = a.Name is QualifiedNameSyntax q ? q.Right.ToString() : a.Name.ToString();
    return n.EndsWith("Attribute") ? n.Substring(0, n.Length - "Attribute".Length) : n;
  }

  static void CollectTypes(SyntaxList<MemberDeclarationSyntax> members, List<MemberDeclarationSyntax> into)
  {
    foreach (var m in members) {
      if (m is NamespaceDeclarationSyntax ns) CollectTypes(ns.Members, into);
      else if (m is FileScopedNamespaceDeclarationSyntax fs) CollectTypes(fs.Members, into);
      else into.Add(m);
    }
  }

  public static bool IsAttributeClass(INamedTypeSymbol s)
  {
    for (var b = s.BaseType; b != null; b = b.BaseType)
      if (b.ToDisplayString() == "System.Attribute") return true;
    return false;
  }

  /** Every top-level type of the program, with its explicit side. */
  public static PartitionPlan Declare(IEnumerable<(SyntaxTree tree, SemanticModel model, string file)> files)
  {
    var plan = new PartitionPlan();
    foreach (var f in files) {
      var decls = new List<MemberDeclarationSyntax>();
      CollectTypes(((CompilationUnitSyntax)f.tree.GetRoot()).Members, decls);
      foreach (var d in decls) {
        if (d is GlobalStatementSyntax) {
          if (!plan.GlobalStatementFiles.Contains(f.file)) plan.GlobalStatementFiles.Add(f.file);
          continue;
        }
        if (!(d is BaseTypeDeclarationSyntax || d is DelegateDeclarationSyntax)) continue;
        var sym = f.model.GetDeclaredSymbol(d) as INamedTypeSymbol;
        if (sym == null) continue;
        var cp = new ClassPlan {
          Sym = sym, File = f.file, Line = d.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
          Name = sym.ToDisplayString(),
        };
        switch (d) {
          case ClassDeclarationSyntax _: cp.Kind = "class"; break;
          case StructDeclarationSyntax _: cp.Kind = "struct"; break;
          case InterfaceDeclarationSyntax _: cp.Kind = "interface"; break;
          case RecordDeclarationSyntax _: cp.Kind = "record"; break;
          case DelegateDeclarationSyntax _: cp.Kind = "delegate"; break;
          case EnumDeclarationSyntax _: cp.Kind = "enum"; break;
        }
        if (cp.Kind == "enum" || (cp.Kind == "class" && IsAttributeClass(sym))) { cp.Shared = true; cp.Final = Part.Native; }
        if (d is TypeDeclarationSyntax td) {
          bool m = false, n = false;
          foreach (var al in td.AttributeLists)
            foreach (var a in al.Attributes) {
              string an = AttrName(a);
              if (an == "Managed") m = true;
              if (an == "Native") n = true;
            }
          if (m && n) plan.Errors.Add(cp.File + ":" + cp.Line + ": `" + cp.Name + "` is marked both [Managed] and [Native].");
          cp.Explicit = m ? Part.Managed : n ? Part.Native : Part.Auto;
        }
        if (plan.Classes.ContainsKey(sym.OriginalDefinition)) continue;     //(partial classes are refused by the emitter)
        plan.Classes[sym.OriginalDefinition] = cp;
        plan.Order.Add(cp);
      }
    }
    plan.DeclareErrorCount = plan.Errors.Count;
    return plan;
  }

  // ------------------------------------------------------------------ 2, 3: decide, then close over families

  static string First(List<string> refusals)
  {
    string r = refusals[0];
    return r.Length > 160 ? r.Substring(0, 157) + "..." : r;
  }

  public void Decide()
  {
    foreach (var cp in Order) {
      if (cp.Shared) continue;
      if (cp.Kind == "delegate") { cp.Final = Part.Managed; cp.Reason = "delegates are not in the Crust C# subset"; continue; }
      if (cp.Kind == "record") { cp.Final = Part.Managed; cp.Reason = "`record` is not in the Crust C# subset"; continue; }
      if (cp.Explicit == Part.Managed) { cp.Final = Part.Managed; cp.Reason = "marked [Managed]"; continue; }
      if (cp.Explicit == Part.Native) { cp.Final = Part.Native; continue; }       //a refusal stays an error
      if (cp.Demoted != null) { cp.Final = Part.Managed; cp.Reason = "follows the managed classes it uses: " + cp.Demoted; continue; }
      if (cp.Refusals.Count > 0) { cp.Final = Part.Managed; cp.Reason = "outside the Crust subset: " + First(cp.Refusals); continue; }
      cp.Final = Part.Native;
    }
  }

  /** Direct bases and interfaces of `cp` that the program declares. */
  IEnumerable<ClassPlan> Bases(ClassPlan cp)
  {
    var list = new List<INamedTypeSymbol>();
    if (cp.Sym.BaseType != null) list.Add(cp.Sym.BaseType);
    list.AddRange(cp.Sym.Interfaces);
    foreach (var b in list) {
      var bp = Get(b);
      if (bp != null && bp != cp && !bp.Shared) yield return bp;
    }
  }

  public void CloseFamilies()
  {
    //union-find over "derives from / implements"
    var parent = new Dictionary<ClassPlan, ClassPlan>();
    foreach (var cp in Order) parent[cp] = cp;
    ClassPlan Find(ClassPlan x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
    foreach (var cp in Order) foreach (var b in Bases(cp)) parent[Find(cp)] = Find(b);

    foreach (var g in Order.Where(c => !c.Shared).GroupBy(Find)) {
      var members = g.ToList();
      if (members.Count < 2) continue;
      var cause = members.FirstOrDefault(m => m.Final == Part.Managed);
      if (cause == null) continue;
      foreach (var m in members) {
        if (m.Final == Part.Managed) continue;
        if (m.Explicit == Part.Native) {
          Errors.Add(m.File + ":" + m.Line + ": `" + m.Name + "` is marked [Native] but `" + cause.Name + "` is managed (" + cause.Reason
            + ") and the two are in one inheritance family. A class and its base class and interfaces must be on the same side of the native/managed boundary.");
          continue;
        }
        m.Final = Part.Managed;
        m.Reason = "in the inheritance family of managed `" + cause.Name + "`";
      }
    }
  }

  // ------------------------------------------------------------------ 4: which class uses which one across the boundary

  static ITypeSymbol TypeOfSymbol(ISymbol s)
  {
    switch (s) {
      case ILocalSymbol l: return l.Type;
      case IParameterSymbol p: return p.Type;
      case IFieldSymbol f: return f.Type;
      case IPropertySymbol p: return p.Type;
      default: return null;
    }
  }

  static INamedTypeSymbol Named(ITypeSymbol t)
  {
    while (t is IArrayTypeSymbol a) t = a.ElementType;
    return t as INamedTypeSymbol;
  }

  public void FindReferences(IEnumerable<(SyntaxTree tree, SemanticModel model, string file)> files)
  {
    foreach (var f in files) {
      var decls = new List<MemberDeclarationSyntax>();
      CollectTypes(((CompilationUnitSyntax)f.tree.GetRoot()).Members, decls);
      foreach (var d in decls) {
        if (!(d is BaseTypeDeclarationSyntax)) continue;
        var from = Get(f.model.GetDeclaredSymbol(d));
        if (from == null || from.Shared) continue;
        var seen = new HashSet<(ClassPlan, string, int)>();
        void Note(ClassPlan to, SyntaxNode at, string what, string via, ISymbol member = null)
        {
          if (to == null || to == from || to.Shared || to.Final == from.Final) return;
          if (!seen.Add((to, what, at.SpanStart))) return;
          bool qualifies = false;
          if (via == "type" && at.Parent is MemberAccessExpressionSyntax ma && ma.Expression == at)
            qualifies = f.model.GetSymbolInfo(ma).Symbol is { IsStatic: true } and (IMethodSymbol or IFieldSymbol or IPropertySymbol);
          Refs.Add(new CrossRef { From = from, To = to, File = f.file, What = what, Via = via, Member = member, QualifiesStaticCall = qualifies,
            Line = at.GetLocation().GetLineSpan().StartLinePosition.Line + 1 });
        }
        foreach (var n in d.DescendantNodes()) {
          if (!(n is SimpleNameSyntax)) continue;
          var sym = f.model.GetSymbolInfo(n).Symbol;
          if (sym == null) continue;
          if (sym is INamedTypeSymbol nt) { Note(Get(nt), n, nt.Name, "type"); continue; }
          var ct = sym.ContainingType;
          if (ct != null && Get(ct) != null && !(sym is IMethodSymbol { MethodKind: MethodKind.Constructor }))
            Note(Get(ct), n, ct.Name + "." + sym.Name, "member", sym);
          var ty = TypeOfSymbol(sym);
          if (ty != null) { var nn = Named(ty); if (nn != null) Note(Get(nn), n, nn.Name, "typed"); }
        }
        //members used without being named: `t[1]` (an indexer), `a + b` and `(int)x` (a user-defined operator or conversion)
        foreach (var n in d.DescendantNodes()) {
          if (!(n is ElementAccessExpressionSyntax || n is BinaryExpressionSyntax || n is PrefixUnaryExpressionSyntax || n is PostfixUnaryExpressionSyntax
                || n is CastExpressionSyntax || n is AssignmentExpressionSyntax)) continue;
          var sym = f.model.GetSymbolInfo(n).Symbol;
          var ct = sym?.ContainingType;
          if (ct == null || Get(ct) == null) continue;
          bool indexer = sym is IPropertySymbol { IsIndexer: true };
          if (indexer || sym is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion })
            Note(Get(ct), n, ct.Name + (indexer ? " indexer" : "." + sym.Name), "member", sym);
        }
        foreach (var n in d.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
          var nn = f.model.GetTypeInfo(n).Type as INamedTypeSymbol;
          if (nn != null) Note(Get(nn), n, "new " + nn.Name, "new", f.model.GetSymbolInfo(n).Symbol);
        }
      }
    }
  }

  // ------------------------------------------------------------------ entry points on the managed side

  public void FindManagedEntries(string mainClass)
  {
    foreach (var cp in Order) {
      if (cp.Final != Part.Managed || cp.Kind == "delegate") continue;
      if (mainClass != null && cp.Sym.Name != mainClass && cp.Name != mainClass) continue;
      foreach (var m in cp.Sym.GetMembers("Main").OfType<IMethodSymbol>()) {
        if (!m.IsStatic) continue;
        if (m.ReturnType.SpecialType != SpecialType.System_Int32 && !m.ReturnsVoid) continue;
        if (m.Parameters.Length > 1) continue;
        if (m.Parameters.Length == 1 && !(m.Parameters[0].Type is IArrayTypeSymbol)) continue;
        ManagedEntries.Add(cp.Name);
        break;
      }
    }
    if (ManagedEntries.Count > 0) ManagedEntry = ManagedEntries[0];
  }

  // ------------------------------------------------------------------ result

  public void Build(IEnumerable<(SyntaxTree tree, SemanticModel model, string file)> files, string mainClass)
  {
    Decide();
    CloseFamilies();
    FindReferences(files);
    FindManagedEntries(mainClass);
  }

  /** Decide again after the bridge analysis demoted classes: their sides changed, so what crosses between the sides did too. */
  public void Rebuild(IEnumerable<(SyntaxTree tree, SemanticModel model, string file)> files, string mainClass)
  {
    Errors.RemoveRange(DeclareErrorCount, Errors.Count - DeclareErrorCount);
    Refs.Clear();
    ManagedEntries.Clear();
    ManagedEntry = null;
    Build(files, mainClass);
  }

  public IEnumerable<ClassPlan> ManagedClasses { get { return Order.Where(c => c.Final == Part.Managed); } }
  public IEnumerable<ClassPlan> NativeClasses { get { return Order.Where(c => c.Final == Part.Native && !c.Shared); } }

  public string ToJson()
  {
    var o = new {
      classes = Order.Select(c => new {
        name = c.Name, kind = c.Kind, file = Path.GetFileName(c.File), line = c.Line,
        partition = c.Shared ? "shared" : c.Final == Part.Managed ? "managed" : "native",
        @explicit = c.Explicit == Part.Auto ? "" : c.Explicit.ToString().ToLowerInvariant(),
        reason = c.Reason,
      }).ToList(),
      refs = Refs.Select(r => new {
        from = r.From.Name, fromSide = r.From.Final.ToString().ToLowerInvariant(),
        to = r.To.Name, toSide = r.To.Final.ToString().ToLowerInvariant(),
        file = Path.GetFileName(r.File), line = r.Line, what = r.What,
      }).ToList(),
      bridge = (Bridge == null ? new List<BridgeMethod>() : Bridge.Methods).Select(m => new {
        method = (m.Ns == "" ? "" : m.Ns + ".") + m.Cls + "." + m.Name, kind = m.Kind.ToString().ToLowerInvariant(),
        direction = m.Dir == Dir.NativeToManaged ? "native->managed" : "managed->native",
        signature = m.Sig, symbol = m.CName,
      }).ToList(),
      entry = new {
        side = ManagedEntry != null || GlobalStatementFiles.Count > 0 ? "managed" : "native",
        @class = ManagedEntry ?? "",
      },
    };
    return JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
  }

  // ------------------------------------------------------------------ the managed side's C#

  /** The managed classes of one file, as C# for DNA: the native classes are blanked out (their newlines kept, so every line stays on its
      own number and a diagnostic names the line of the original file) and Crust's own attributes are removed. */
  string SliceText(SyntaxTree tree, SemanticModel model)
  {
    string text = tree.GetText().ToString();
    var root = tree.GetRoot();
    var edits = new List<(int start, int len, string repl)>();
    var removed = new List<Microsoft.CodeAnalysis.Text.TextSpan>();
    string Blank(Microsoft.CodeAnalysis.Text.TextSpan sp) { return new string('\n', text.Substring(sp.Start, sp.Length).Count(c => c == '\n')); }

    foreach (var n in root.DescendantNodes().OfType<TypeDeclarationSyntax>()) {
      if (n.Parent is TypeDeclarationSyntax) continue;                          //(a nested type goes with its class)
      var cp = Get(model.GetDeclaredSymbol(n));
      if (cp == null || cp.Shared || cp.Final != Part.Native) continue;
      removed.Add(n.Span);
      edits.Add((n.Span.Start, n.Span.Length, Blank(n.Span)));
    }
    foreach (var al in root.DescendantNodes().OfType<AttributeListSyntax>()) {
      if (removed.Any(r => r.Contains(al.Span))) continue;
      var keep = al.Attributes.Where(a => { var an = AttrName(a); return an != "Managed" && an != "Native"; }).ToList();
      if (keep.Count == al.Attributes.Count) continue;
      string repl = keep.Count == 0 ? "" : "[" + string.Join(", ", keep.Select(a => a.ToString())) + "]";
      string old = text.Substring(al.Span.Start, al.Span.Length);
      repl += new string('\n', old.Count(c => c == '\n') - repl.Count(c => c == '\n'));
      edits.Add((al.Span.Start, al.Span.Length, repl));
    }
    foreach (var e in edits.OrderByDescending(e => e.start))
      text = text.Substring(0, e.start) + e.repl + text.Substring(e.start + e.len);
    return text;
  }

  /** Does the slice declare anything?  (a file of native classes only has nothing for DNA) */
  static bool HasMembers(SyntaxNode root)
  {
    return root.DescendantNodes().Any(n => n is BaseTypeDeclarationSyntax || n is DelegateDeclarationSyntax || n is GlobalStatementSyntax);
  }

  /** Write the managed C# of every file under `dir`, plus the `Crust` attribute declarations they may still name.
      Returns the files written. */
  public List<string> WriteManagedSources(IEnumerable<(SyntaxTree tree, SemanticModel model, string file)> files, string dir)
  {
    var written = new List<string>();
    if (!ManagedClasses.Any() && GlobalStatementFiles.Count == 0) return written;
    Directory.CreateDirectory(dir);
    var used = new HashSet<string>();
    foreach (var f in files) {
      string sliced = SliceText(f.tree, f.model);
      if (!HasMembers(CSharpSyntaxTree.ParseText(sliced).GetRoot())) continue;
      string name = Path.GetFileNameWithoutExtension(f.file);
      string unique = name; int k = 2;
      while (!used.Add(unique)) unique = name + (k++);
      string path = dir + "/" + unique + ".cs";
      File.WriteAllText(path, sliced);   //(line for line the original file, so a diagnostic names its line)
      written.Add(path);
    }
    //the attributes, for `using Crust;` and for code that names them: DNA's corlib does not have the CC# corelib's Crust namespace
    string crust = dir + "/_Crust.cs";
    File.WriteAllText(crust,
      "namespace Crust {\n"
      + "  [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)] public sealed class ManagedAttribute : System.Attribute { }\n"
      + "  [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)] public sealed class NativeAttribute : System.Attribute { }\n"
      + "}\n");
    written.Add(crust);
    return written;
  }
}
