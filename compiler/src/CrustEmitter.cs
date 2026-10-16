/**

  CC# --crust back end

  Emits the C++ subset accepted by Crust's `cpprust` (https://github.com/brentharts/crust)
  instead of the garbage-collected C++ that the classic CC# generator produces.

  Conventions follow Crust's own C# lowering (tools/cs2cpp.py, CSRUST.md) so that
  CC# and `csrust` agree on what a C# program means:

    * `class` is single ownership: a lowered object is a value, destroyed at scope exit.
    * class / array / List / Dictionary parameters are borrowed (C++ references).
    * enum Kind : byte { A }       -> enum Kind_values { Kind_A }; typedef unsigned char Kind;
    * properties                   -> field + get_P() / set_P(v)
    * T[] / List<T>                -> std::vector<T>
    * Console.Write/WriteLine      -> printf
    * line numbers are preserved, so a diagnostic in the generated C++ points at the .cs line.

  Anything outside the subset is REFUSED, at the C# line, with the reason and the
  replacement -- never approximated.  (Crust: "a refusal is the deliverable".)

*/

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CCSharpCompiler;

/** A construct that is outside the Crust C# subset. */
class CrustRefusal : Exception
{
  public SyntaxNode Node;
  public CrustRefusal(SyntaxNode node, string msg) : base(msg) { Node = node; }
}

/** Output buffer that tracks its own line number so source lines can be preserved. */
class CrustOut
{
  StringBuilder sb = new StringBuilder();
  public int Line = 1;
  public override string ToString() { return sb.ToString(); }
  public bool AtLineStart { get { return sb.Length == 0 || sb[sb.Length - 1] == '\n'; } }

  public void Raw(string s)
  {
    sb.Append(s);
    foreach (char c in s) if (c == '\n') Line++;
  }

  /** Advance (never rewind) to the given source line. */
  public void PadTo(int srcLine)
  {
    while (Line < srcLine) { sb.Append('\n'); Line++; }
  }

  /** Write text, separated from what precedes it on the same line. */
  public void Text(string s)
  {
    if (s.Length == 0) return;
    if (!AtLineStart && sb[sb.Length - 1] != ' ') sb.Append(' ');
    Raw(s);
  }

  public void At(int srcLine, string s) { PadTo(srcLine); Text(s); }
}

class CrustEmitter
{
  public const string WRAPV_PRAGMA = "_Pragma(\"GCC optimize(\\\"wrapv\\\")\") ";

  public List<string> errors = new List<string>();

  public PartitionPlan plan;                     //--dna: which classes are native and which managed (null: every class is native, or refused)
  public bool probing;                           //--dna, first pass: emit to find what each class is refused for; the output is discarded

  Source src;
  IMethodSymbol currentMethod;                   //the method whose body is being emitted
  INamedTypeSymbol currentType;                  //the class whose members are being emitted
  SemanticModel model;
  CrustOut o;

  string mainClass;                              //--main=Class (flat name) or null
  public static string entryCall;                //"Cls::Main" once found
  public static bool entryReturnsInt;
  public static bool entryTakesArgs;
  bool preambleDone;
  HashSet<string> arrayHelpers = new HashSet<string>();
  StringBuilder preamble = new StringBuilder();
  int tmp;
  List<string> hoisted = new List<string>();     //statements emitted just before the current statement, in order
  bool canHoist;
  ExpressionSyntax hoistRoot;                    //a composite whose parts are hoisted strictly in order
  public bool UsesString;                        //a string value is built somewhere (the aggregate file needs fastring)
  public bool UsesStrcmp;                        //... and <string.h>
  public SortedSet<string> Includes = new SortedSet<string>(StringComparer.Ordinal);   //headers the corelib members used need
  IMethodSymbol entryMethod;                     //Main, whose string[] args are `const char *` elements

  static readonly HashSet<string> reserved = new HashSet<string> {
    "auto","register","template","typename","namespace","operator","new","delete","friend","virtual",
    "inline","static_cast","dynamic_cast","reinterpret_cast","const_cast","typeid","typedef","union",
    "signed","unsigned","long","short","extern","export","explicit","mutable","private","protected",
    "public","throw","try","catch","using","this","main","and","or","not","xor","asm","bool","char",
    "double","float","int","void","wchar_t","goto","default","enum","struct","class","volatile",
    "restrict","class_","NULL","errno","stdin","stdout","stderr","printf","abort","size_t"
  };

  public CrustEmitter(string mainClass) { this.mainClass = mainClass; }

  /** The semantic model that can answer questions about `node`. `model` only knows the file being emitted: a class declared in
      another file has its base list in another syntax tree, and asking the wrong model throws "Syntax node is not within syntax tree". */
  SemanticModel ModelOf(SyntaxNode node)
  {
    if (node.SyntaxTree == model.SyntaxTree) return model;
    return Program.compiler.GetSemanticModel(node.SyntaxTree);
  }

  // ------------------------------------------------------------------ entry points

  /** One top-level type's C++, with what the aggregate needs to order it. */
  public class CppPiece
  {
    public string Name;                          //a file name for it (without .cpp)
    public string Text;
    public INamedTypeSymbol Sym;
    public MemberDeclarationSyntax Decl;
    public SemanticModel Model;
    public int FirstLine;                        //the source line it starts on
    public bool IsHelper;                        //a helper function a [Cpp(Helper=..)] member needs (no source of its own)
    public List<ITypeSymbol> NeedTypes = new List<ITypeSymbol>();     //(a helper) the types it is written for: it comes after them
    public List<CppPiece> Helpers = new List<CppPiece>();             //the helpers this piece calls: they come before it
  }

  Dictionary<string, CppPiece> helperPieces = new Dictionary<string, CppPiece>();     //one for each distinct helper text in the program
  List<CppPiece> newHelperPieces = new List<CppPiece>();                              //made while emitting the file in hand
  HashSet<CppPiece> currentHelpers = new HashSet<CppPiece>();                         //called by the piece in hand

  /** Emit one source file, a piece for each type in it (in the order they should come, within the file).  null when the file was refused.
      The types are separate pieces so that the aggregate can order them across files: see Program.OrderPieces. */
  public List<CppPiece> EmitFile(Source file)
  {
    src = file;
    model = file.model;
    preambleDone = false;
    int before = errors.Count;
    var root = (CompilationUnitSyntax)file.tree.GetRoot();
    var decls = new List<MemberDeclarationSyntax>();
    CollectTypes(root.Members, decls);
    var pieces = new List<CppPiece>();
    string stem = Path.GetFileNameWithoutExtension(file.cppFile);
    newHelperPieces.Clear();
    foreach (var d in DependencyOrder(decls)) {
      o = new CrustOut();                                   //(its own line count: a piece starts at its own source line)
      currentHelpers = new HashSet<CppPiece>();
      TopLevel(d);
      string text = o.ToString();
      if (text.Trim().Length == 0) continue;                //(an attribute class is dropped, a foreign type is not emitted)
      var sym = model.GetDeclaredSymbol(d) as INamedTypeSymbol;
      pieces.Add(new CppPiece { Name = stem + "." + (sym != null ? sym.Name : pieces.Count.ToString()), Text = text, Sym = sym, Decl = d, Model = model, FirstLine = LineOf(d), Helpers = currentHelpers.ToList() });
    }
    if (errors.Count > before) return null;
    pieces.AddRange(newHelperPieces);
    return pieces;
  }

  /** What every file needs at the top of the program: the wrapv pragma, and the helpers the code used. */
  public string Preamble { get { return WRAPV_PRAGMA + preamble.ToString(); } }

  public string EmitMain()
  {
    if (entryCall == null) return "";
    var sb = new StringBuilder();
    sb.Append("int main(int argc, char **argv) {\n");
    if (entryTakesArgs) {
      sb.Append("  std::vector<const char *> args;\n");
      sb.Append("  for (int i = 1; i < argc; i++) { args.push_back(argv[i]); }\n");
    }
    string call = entryCall + "(" + (entryTakesArgs ? "args" : "") + ")";
    if (entryReturnsInt) sb.Append("  return " + call + ";\n");
    else sb.Append("  " + call + ";\n  return 0;\n");
    sb.Append("}\n");
    return sb.ToString();
  }

  void Refuse(SyntaxNode node, string msg) { throw new CrustRefusal(node, msg); }

  int LineOf(SyntaxNode n) { return n.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }
  int LineOf(SyntaxToken t) { return t.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }

  void Report(CrustRefusal r)
  {
    int line = r.Node == null ? 0 : LineOf(r.Node);
    errors.Add(src.csFile + ":" + line + ": " + r.Message);
  }

  /** Called before the first token of real output: include + helpers on the way in. */
  void Begin(int srcLine)
  {
    preambleDone = true;
    Resync(srcLine);          //no include here: it lives in the aggregate file, so line 1 stays line 1
  }

  /** Put the next output line on source line `srcLine`: pad forward, or `#line` back when a type was moved. */
  void Resync(int srcLine)
  {
    if (o.Line <= srcLine) { o.PadTo(srcLine); return; }
    if (!o.AtLineStart) o.Raw("\n");
    o.Raw("#line " + srcLine + "\n");
    o.Line = srcLine;
  }

  // ------------------------------------------------------------------ declarations

  /** Namespaces are flattened; what is left is the list of declarations in source order. */
  void CollectTypes(SyntaxList<MemberDeclarationSyntax> members, List<MemberDeclarationSyntax> into)
  {
    foreach (var m in members) {
      if (m is NamespaceDeclarationSyntax ns) CollectTypes(ns.Members, into);
      else if (m is FileScopedNamespaceDeclarationSyntax fs) CollectTypes(fs.Members, into);
      else into.Add(m);
    }
  }

  /** C# lets a derived type come before its base; C++ does not.  Bases and interfaces first, otherwise source order. */
  List<MemberDeclarationSyntax> DependencyOrder(List<MemberDeclarationSyntax> decls)
  {
    var bySym = new Dictionary<ISymbol, MemberDeclarationSyntax>(SymbolEqualityComparer.Default);
    foreach (var d in decls)
      if (d is BaseTypeDeclarationSyntax) { var s = model.GetDeclaredSymbol(d); if (s != null) bySym[s.OriginalDefinition] = d; }
    var result = new List<MemberDeclarationSyntax>();
    var seen = new HashSet<MemberDeclarationSyntax>();
    void Visit(MemberDeclarationSyntax d)
    {
      if (!seen.Add(d)) return;
      if (d is TypeDeclarationSyntax td && td.BaseList != null) {
        foreach (var b in td.BaseList.Types) {
          var bs = model.GetSymbolInfo(b.Type).Symbol as INamedTypeSymbol;
          if (bs != null && bySym.TryGetValue(bs.OriginalDefinition, out var bd)) Visit(bd);
        }
      }
      result.Add(d);
    }
    foreach (var d in decls) Visit(d);
    return result;
  }

  void TopLevel(MemberDeclarationSyntax d)
  {
    try {
      switch (d) {
        case NamespaceDeclarationSyntax ns:
          foreach (var m in ns.Members) TopLevel(m);
          break;
        case FileScopedNamespaceDeclarationSyntax fs:
          foreach (var m in fs.Members) TopLevel(m);
          break;
        case EnumDeclarationSyntax e: Enum(e); break;
        case ClassDeclarationSyntax c: ClassPart(c); break;
        case StructDeclarationSyntax s: ClassPart(s); break;
        case InterfaceDeclarationSyntax i: ClassPart(i); break;
        case DelegateDeclarationSyntax _ when plan != null: break;       //--dna: managed only, see PartitionPlan.Decide
        case GlobalStatementSyntax _ when plan != null: break;           //--dna: the managed side's entry point
        case RecordDeclarationSyntax _ when plan != null: break;         //--dna: managed only
        case DelegateDeclarationSyntax dg:
          Refuse(dg, "`delegate` is not in the Crust C# subset: delegates need a captured-state representation. Use a static method, or an interface with one method.");
          break;
        case GlobalStatementSyntax gs:
          Refuse(gs, "top-level statements are not in the Crust C# subset: C# requires them before every type declaration and C needs them after. Put the code in a class with a `static Main`.");
          break;
        case RecordDeclarationSyntax rd:
          Refuse(rd, "`record` is not in the Crust C# subset. Write a class with fields.");
          break;
        default:
          Refuse(d, "`" + d.Kind() + "` is not in the Crust C# subset.");
          break;
      }
    } catch (CrustRefusal r) { Report(r); }
  }

  string FlatName(INamedTypeSymbol t)
  {
    string n = Ident(t.Name);
    if (t.ContainingType != null) n = FlatName(t.ContainingType) + "_" + n;
    else if (t.ContainingNamespace != null && !t.ContainingNamespace.IsGlobalNamespace)
      n = t.ContainingNamespace.ToDisplayString().Replace(".", "_") + "_" + n;
    return n;
  }

  string Ident(string name)
  {
    if (reserved.Contains(name)) return name + "_";
    return name;
  }

  /** `Ident`, for the bridge (Bridge.cs), which spells names as Crust does without an emitter at hand. */
  internal static string IdentOf(string name) { return reserved.Contains(name) ? name + "_" : name; }

  string Underlying(INamedTypeSymbol e)
  {
    switch (e.EnumUnderlyingType.SpecialType) {
      case SpecialType.System_Byte: return "unsigned char";
      case SpecialType.System_SByte: return "signed char";
      case SpecialType.System_Int16: return "short";
      case SpecialType.System_UInt16: return "unsigned short";
      case SpecialType.System_UInt32: return "unsigned";
      case SpecialType.System_Int64: return "long long";
      case SpecialType.System_UInt64: return "unsigned long long";
      default: return "int";
    }
  }

  void Enum(EnumDeclarationSyntax e)
  {
    var sym = (INamedTypeSymbol)model.GetDeclaredSymbol(e);
    string name = FlatName(sym);
    Begin(LineOf(e));
    var sb = new StringBuilder();
    sb.Append("enum " + name + "_values { ");
    bool first = true;
    foreach (var m in e.Members) {
      var ms = (IFieldSymbol)model.GetDeclaredSymbol(m);
      if (!first) sb.Append(", ");
      first = false;
      sb.Append(name + "_" + Ident(m.Identifier.Text));
      if (ms.HasConstantValue) sb.Append(" = " + Convert.ToInt64(ms.ConstantValue));
    }
    sb.Append(" }; typedef " + Underlying(sym) + " " + name + ";");
    FirstLinePrefix(sb);
    o.Text(sb.ToString());
    o.PadTo(LineOf(e.CloseBraceToken));
  }

  /** helpers + wrapv pragma ride on the first line of output. */
  void FirstLinePrefix(StringBuilder sb) { }       //(the wrapv pragma and the helpers are at the top of the program: Preamble)

  bool IsOwnedTypeDecl(TypeDeclarationSyntax t) { return true; }

  /** --dna: a class goes native or managed as the plan says.  The probe pass emits everything, and records for each class what
      it was refused for (it never reports: a refusal is how a class becomes managed).  The real pass skips the managed classes. */
  void ClassPart(TypeDeclarationSyntax c)
  {
    if (plan == null) { Class(c, "class"); return; }
    var cp = plan.Get(model.GetDeclaredSymbol(c));
    if (probing) {
      int before = errors.Count;
      try { Class(c, "class"); } catch (CrustRefusal r) { Report(r); }
      if (cp != null) for (int i = before; i < errors.Count; i++) cp.Refusals.Add(errors[i]);
      return;
    }
    if (cp != null && cp.Final == Part.Managed) return;
    Class(c, "class");
  }

  void Class(TypeDeclarationSyntax c, string kw)
  {
    var sym = (INamedTypeSymbol)model.GetDeclaredSymbol(c);
    string name = FlatName(sym);
    CheckClassAttributes(c, sym);
    if (sym.ContainingType != null)
      Refuse(c, "nested type `" + sym.Name + "` is not in the Crust C# subset yet. Move it to the top level of the file.");
    if (c.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
      Refuse(c, "`partial` is not in the Crust C# subset. Put the whole class in one declaration.");
    if (c.Modifiers.Any(m => m.IsKind(SyntaxKind.UnsafeKeyword)))
      Refuse(c, "`unsafe` is not in the Crust C# subset.");
    //attribute classes are markers the C# compiler needs; Crust drops them
    if (sym.BaseType != null && sym.BaseType.ToDisplayString() == "System.Attribute") return;
    //a type that names a C type (`[Crust.Cpp("PB2Body")] struct PB2Body { .. }`) is that type, declared by the header it includes: nothing to emit
    if (CppTemplate(sym) != null) return;

    Begin(LineOf(c));
    var head = new StringBuilder();
    if (c.TypeParameterList != null) {
      head.Append("template<");
      head.Append(string.Join(", ", c.TypeParameterList.Parameters.Select(p => "typename " + Ident(p.Identifier.Text))));
      head.Append("> ");
    }
    head.Append(kw + " " + name);
    var bases = new List<string>();
    if (c.BaseList != null) {
      foreach (var b in c.BaseList.Types) {
        var bs = model.GetSymbolInfo(b.Type).Symbol as INamedTypeSymbol;
        if (bs == null) continue;
        if (bs.SpecialType == SpecialType.System_Object || bs.SpecialType == SpecialType.System_ValueType) continue;
        if (bs.Name == "ICloneable" || bs.Name == "IEquatable" || bs.Name == "IComparable")
          Refuse(b, "base type `" + bs.Name + "` is not in the Crust C# subset.");
        bases.Add("public " + (IsArena(bs) ? FlatName(bs) : TypeName(bs)));      //an arena base is named, not its pointer type
      }
    }
    if (bases.Count > 0) head.Append(" : " + string.Join(", ", bases));
    head.Append(" {");
    var sbh = new StringBuilder(head.ToString());
    int arena = ArenaSize(sym);
    if (arena > 0) {
      //capacity for cpprust, and `new T[n]`: n null references (a helper here rather than at the top of the file, where T is not yet a complete type)
      string flat = FlatName(sym);
      ArenaClasses.Add(flat);
      sbh.Append(" static const int __max_instances = " + arena + ";"
        + " static std::vector<" + flat + " *> __new_array(int n) { std::vector<" + flat + " *> v; int i = 0; if (n < 0) { abort(); }"
        + " while (i < n) { v.push_back(0); i = i + 1; } return v; }");
    }
    FirstLinePrefix(sbh);
    o.Text(sbh.ToString());

    bool isInterface = c is InterfaceDeclarationSyntax;
    currentType = sym;
    foreach (var m in c.Members) {
      try { Member(m, sym, isInterface); }
      catch (CrustRefusal r) { Report(r); }
    }
    //A C# struct always has a parameterless constructor (`default(V)`, `new V()`, an array of V): when it declares constructors of its own, C++ would have none.
    //Its fields start at zero (`int A = 0;`), which is what default(V) is.
    if (c is StructDeclarationSyntax) {
      var ctors = c.Members.OfType<ConstructorDeclarationSyntax>().Where(k => !Has(k.Modifiers, SyntaxKind.StaticKeyword)).ToList();
      if (ctors.Count > 0 && !ctors.Any(k => k.ParameterList.Parameters.Count == 0)) {
        o.PadTo(LineOf(c.CloseBraceToken));
        o.Text(FlatName(sym) + "() {}");
      }
    }
    o.PadTo(LineOf(c.CloseBraceToken));
    o.Text("};");
    currentType = null;
  }

  bool HasMaxInstancesAttr(TypeDeclarationSyntax c)
  {
    foreach (var al in c.AttributeLists)
      foreach (var a in al.Attributes) {
        string n = a.Name.ToString();
        if (n == "MaxInstances" || n == "MaxInstancesAttribute") return true;
      }
    return false;
  }

  void CheckClassAttributes(TypeDeclarationSyntax c, INamedTypeSymbol sym)
  {
    foreach (var al in c.AttributeLists)
      foreach (var a in al.Attributes) {
        string n = a.Name.ToString();
        if (n == "Shared" || n == "SharedAttribute")
          Refuse(a, "`[Shared]` (shared_ptr classes) is not supported by CC# --crust yet. Default single-owner classes are.");
        if (n == "MaxInstances" || n == "MaxInstancesAttribute") {
          if (!(c is ClassDeclarationSyntax))
            Refuse(a, "`[MaxInstances(N)]` is for a class: a struct is a value and an interface has no instances.");
          if (ArenaSize(sym) <= 0)
            Refuse(a, "`[MaxInstances(N)]` needs a constant N greater than 0.");
          if (c.TypeParameterList != null)
            Refuse(a, "an arena class cannot be generic: each instantiation would need an arena of its own. Make the arena class concrete.");
          if (c.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            Refuse(a, "an arena class cannot be static: it has instances.");
        }
      }
    //An arena class (one with [MaxInstances(N)], or one that derives from one) may derive from one other arena class, so that a reference to it is also a
    //reference to its base: the object begins with its base, and a pointer to it is the address of both.  A base that is not an arena class would
    //be a value embedded in the object, and an interface is a second base with a pointer of its own.
    if (IsArena(sym) && c.TypeParameterList != null && !HasMaxInstancesAttr(c))      //(one that says [MaxInstances] was refused above)
      Refuse(c, "an arena class cannot be generic: each instantiation would need an arena of its own. `" + sym.Name + "` is one because it derives from the arena class `" + sym.BaseType.Name + "`.");
    if (IsArena(sym) && c.BaseList != null && c.BaseList.Types.Count > 0) {
      bool ok = sym.BaseType != null && IsArena(sym.BaseType) && sym.Interfaces.Length == 0;
      if (!ok)
        Refuse(c.BaseList, "an arena class may derive from one other arena class and nothing else: a base class that is not an arena class, or an interface, is not supported by CC# --crust yet.");
    }
  }

  void Member(MemberDeclarationSyntax m, INamedTypeSymbol owner, bool isInterface)
  {
    switch (m) {
      case FieldDeclarationSyntax f: Field(f); break;
      case PropertyDeclarationSyntax p: Property(p, owner, isInterface); break;
      case MethodDeclarationSyntax md: Method(md, owner, isInterface); break;
      case ConstructorDeclarationSyntax cd: Ctor(cd, owner); break;
      case DestructorDeclarationSyntax dd: Dtor(dd, owner); break;
      case OperatorDeclarationSyntax od: OperatorDecl(od, owner, od.ReturnType); break;
      case ConversionOperatorDeclarationSyntax cod: OperatorDecl(cod, owner, cod.Type); break;
      case IndexerDeclarationSyntax ix:
        Refuse(ix, "indexers are not in the Crust C# subset. Write `Get(i)` / `Set(i, v)` methods.");
        break;
      case EventFieldDeclarationSyntax ev:
        Refuse(ev, "`event` is not in the Crust C# subset. Call the handler through an interface.");
        break;
      case BaseTypeDeclarationSyntax _:
      case DelegateDeclarationSyntax _:
        Refuse(m, "nested types are not in the Crust C# subset yet. Move it to the top level of the file.");
        break;
      default:
        Refuse(m, "`" + m.Kind() + "` is not in the Crust C# subset.");
        break;
    }
  }

  bool Has(SyntaxTokenList mods, SyntaxKind k) { return mods.Any(x => x.IsKind(k)); }

  void RefuseExtern(MemberDeclarationSyntax m, SyntaxTokenList mods)
  {
    if (Has(mods, SyntaxKind.ExternKeyword))
      Refuse(m, "`extern` methods are not in the Crust C# subset: the CC# corelib is not available under --crust. Declare the function in C and call it from a .c file.");
    if (Has(mods, SyntaxKind.UnsafeKeyword))
      Refuse(m, "`unsafe` is not in the Crust C# subset.");
  }

  string DefaultInit(ITypeSymbol t)
  {
    switch (t.SpecialType) {
      case SpecialType.System_Boolean: return "false";
      case SpecialType.System_Single: return "0.0f";
      case SpecialType.System_Double: return "0.0";
      case SpecialType.System_Byte: case SpecialType.System_SByte:
      case SpecialType.System_Int16: case SpecialType.System_UInt16:
      case SpecialType.System_Int32: case SpecialType.System_UInt32:
      case SpecialType.System_Int64: case SpecialType.System_UInt64:
        return "0";
    }
    if (t.TypeKind == TypeKind.Enum) return "0";
    if (IsArena(t)) return "NULL";                       //a reference that refers to nothing
    return null;   //aggregates: default-constructed (C# would have null for a class: see Refuse in TypeName)
  }

  void Field(FieldDeclarationSyntax f)
  {
    RefuseExtern(f, f.Modifiers);
    string prefix = "";
    bool isConst = Has(f.Modifiers, SyntaxKind.ConstKeyword);
    bool isStatic = Has(f.Modifiers, SyntaxKind.StaticKeyword) || isConst;
    if (isStatic) prefix = "static ";
    if (isConst) prefix += "const ";
    foreach (var v in f.Declaration.Variables) {
      var fs = (IFieldSymbol)model.GetDeclaredSymbol(v);
      string t = TypeName(fs.Type, v);
      var sb = new StringBuilder();
      sb.Append(prefix + t + " " + Ident(v.Identifier.Text));
      if (v.Initializer != null) {
        var init = v.Initializer.Value;
        if (fs.Type.SpecialType == SpecialType.System_String)
          Refuse(init, "a string field initialiser is not in the Crust C# subset yet. Assign it in the constructor; until then the field is the empty string.");
        if (init is ObjectCreationExpressionSyntax oc && (IsOwnedClass(fs.Type) || CppTemplate(fs.Type) != null)) {
          if (oc.ArgumentList != null && oc.ArgumentList.Arguments.Count > 0)
            Refuse(init, "a class-typed field initialised with constructor arguments is not supported by CC# --crust yet. Assign it in the constructor.");
          //default constructor runs when the owner is built
        } else if (init is ArrayCreationExpressionSyntax || init is ImplicitArrayCreationExpressionSyntax
                   || init is InitializerExpressionSyntax) {
          Refuse(init, "a field initialised with an array is not supported by CC# --crust yet. Assign it in the constructor.");
        } else {
          sb.Append(" = " + Expr(init));
        }
      } else {
        string d = DefaultInit(fs.Type);
        if (d != null) sb.Append(" = " + d);
      }
      sb.Append(";");
      o.At(LineOf(v), sb.ToString());
    }
  }

  bool IsOwnedClass(ITypeSymbol t)
  {
    return t.TypeKind == TypeKind.Class && t.SpecialType == SpecialType.None
           && !(t is IArrayTypeSymbol) && !IsList(t) && !IsDictionary(t) && !IsArena(t);
  }

  // ---- arena classes: `[MaxInstances(N)] class T`
  //
  // C#'s reference semantics without a GC: the class has N statically allocated slots, and a reference to it is a plain `T *`.  Assignment copies
  // the pointer, `null` is 0, `==` compares pointers, a reference may be stored in a field, a List<T> or a T[] and passed and returned freely.
  // `new T(..)` takes the next slot, zeroed, and runs the constructor (cpprust's `T__alloc`); nothing is freed one at a time, and an (N+1)th live
  // object aborts.  Crust's side of this is `static const int __max_instances = N;` in the class (CPPRUST.md, "Arena classes").
  // An arena class has no type parameter and no interface, and at most one base class, which is itself an arena class: the object begins with its base
  // (cpprust lays a derived struct out as `{ Base _base; ... }`), so a pointer to it is the address of the object, whatever it is cast to.  Every class of
  // the family has an arena of its own: `new Derived()` takes a slot of Derived's, and a `Base *` that refers to it is that slot's address.  A class that
  // derives from an arena class is one itself, with the base's N unless it declares its own (an abstract base's N is only that default).  C# converts
  // `Derived` to `Base` implicitly; here that is a cast (Expr), because C types the two pointers apart.

  Dictionary<INamedTypeSymbol, int> arenaSizes = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
  public SortedSet<string> ArenaClasses = new SortedSet<string>(StringComparer.Ordinal);   //the arena classes named so far: the aggregate declares them first

  /** N of `[MaxInstances(N)]`, else 0.  The attribute is found by name, as `[Shared]` is: a program may declare its own marker class. */
  int ArenaSize(ITypeSymbol t)
  {
    if (t is ITypeParameterSymbol tparam) return tparam.ConstraintTypes.Select(ArenaSize).FirstOrDefault(z => z > 0);     //`T : Component` is a reference too
    var n = t as INamedTypeSymbol;
    if (n == null || n.TypeKind != TypeKind.Class) return 0;
    n = n.OriginalDefinition;
    if (arenaSizes.TryGetValue(n, out int size)) return size;
    //A class that derives from an arena class is an arena class: its objects are as much references as its base's, and a reference to one is also a
    //reference to the base.  It has slots of its own (the arenas are per class); without an N of its own it takes its base's (ArenaClass.Size).
    size = ArenaClass.Size(n);
    arenaSizes[n] = size;
    return size;
  }
  bool IsArena(ITypeSymbol t) { return t != null && ArenaSize(t) > 0; }
  bool ArenaConstrained(ITypeParameterSymbol tp) { return tp.ConstraintTypes.Any(c => ArenaSize(c) > 0); }

  /** A type argument as the C++ template argument: an arena class by its flat name (the class, not the pointer to it). */
  string TemplateArg(ITypeSymbol a, SyntaxNode at)
  {
    if (a is ITypeParameterSymbol tp) return Ident(tp.Name);
    if (a is INamedTypeSymbol n && IsArena(n)) return FlatName(n);
    return TypeName(a, at);
  }
  string TypeArgs(IMethodSymbol m, SyntaxNode at)
  {
    return m.IsGenericMethod ? "<" + string.Join(", ", m.TypeArguments.Select(a => TemplateArg(a, at))) + ">" : "";
  }

  /** Is this `null` a reference to an arena object?  Where it is assigned, passed or returned, C# converts it to that type; in `x == null` it is
      converted to `object` (reference equality), so what matters is the type of the other operand. */
  bool NullIsArena(LiteralExpressionSyntax l)
  {
    if (IsArena(model.GetTypeInfo(l).ConvertedType)) return true;
    SyntaxNode at = l;
    while (at.Parent is ParenthesizedExpressionSyntax) at = at.Parent;
    if (at.Parent is BinaryExpressionSyntax be && (be.IsKind(SyntaxKind.EqualsExpression) || be.IsKind(SyntaxKind.NotEqualsExpression))) {
      var other = be.Left == at ? be.Right : be.Left;
      return IsArena(model.GetTypeInfo(other).Type);
    }
    return false;
  }

  /** A method returning its own class that does `return this;` is a fluent API.  C# returns the same
      object; a by-value return would silently copy it, so it returns `T *` and the chain uses `->`. */
  Dictionary<IMethodSymbol, bool> fluentCache = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
  bool IsFluent(IMethodSymbol ms)
  {
    if (ms == null || !IsOwnedClass(ms.ReturnType)) return false;
    ms = ms.OriginalDefinition;
    if (fluentCache.TryGetValue(ms, out bool v)) return v;
    bool r = false;
    foreach (var sr in ms.DeclaringSyntaxReferences) {
      if (sr.GetSyntax() is MethodDeclarationSyntax md && md.Body != null)
        r = ReturnsOf(md).Any(x => x.Expression is ThisExpressionSyntax);
    }
    fluentCache[ms] = r;
    return r;
  }
  IEnumerable<ReturnStatementSyntax> ReturnsOf(MethodDeclarationSyntax md)
  {
    return md.Body.DescendantNodes(n => !(n is LambdaExpressionSyntax || n is LocalFunctionStatementSyntax)).OfType<ReturnStatementSyntax>();
  }
  bool IsFluentCall(ExpressionSyntax e)
  {
    return e is InvocationExpressionSyntax inv && IsFluent(model.GetSymbolInfo(inv).Symbol as IMethodSymbol);
  }

  /** an rvalue object: a construction or a call result -- a new value, so no alias is created. */
  bool IsFresh(ExpressionSyntax e)
  {
    if (e is ObjectCreationExpressionSyntax || e is ImplicitObjectCreationExpressionSyntax || e is ArrayCreationExpressionSyntax) return true;
    if (e is InvocationExpressionSyntax inv && !IsFluentCall(inv)) return true;
    if (e is ParenthesizedExpressionSyntax p) return IsFresh(p.Expression);
    return false;
  }

  bool IsList(ITypeSymbol t)
  {
    return t is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>";
  }
  bool IsDictionary(ITypeSymbol t)
  {
    return t is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>";
  }

  void Property(PropertyDeclarationSyntax p, INamedTypeSymbol owner, bool isInterface)
  {
    RefuseExtern(p, p.Modifiers);
    var ps = (IPropertySymbol)model.GetDeclaredSymbol(p);
    string t = TypeName(ps.Type, p);
    string name = Ident(p.Identifier.Text);
    bool isStatic = Has(p.Modifiers, SyntaxKind.StaticKeyword);
    string st = isStatic ? "static " : "";
    string virt = "";
    if (!isStatic && !isInterface) {
      if (Has(p.Modifiers, SyntaxKind.VirtualKeyword) || Has(p.Modifiers, SyntaxKind.AbstractKeyword)) virt = "virtual ";
    }
    bool abs = Has(p.Modifiers, SyntaxKind.AbstractKeyword);
    int line = LineOf(p);

    if (isInterface || abs) {
      var sb0 = new StringBuilder();
      foreach (var a in p.AccessorList.Accessors) {
        if (a.IsKind(SyntaxKind.GetAccessorDeclaration)) sb0.Append("virtual " + t + " get_" + name + "() = 0; ");
        else sb0.Append("virtual void set_" + name + "(" + t + " value) = 0; ");
      }
      o.At(line, sb0.ToString().TrimEnd());
      return;
    }

    if (p.ExpressionBody != null) {
      BeginHoist();
      var gm = ps.GetMethod;
      currentMethod = gm;
      string gb;
      try { gb = Return(new ReturnStatementData(p.ExpressionBody.Expression)); } finally { currentMethod = null; }
      string gpre = string.Join(" ", hoisted);
      canHoist = false; hoisted.Clear();
      o.At(line, st + virt + t + " get_" + name + "() { " + (gpre.Length > 0 ? gpre + " " : "") + gb + " }");
      return;
    }
    var accs = p.AccessorList.Accessors;
    bool auto = accs.All(a => a.Body == null && a.ExpressionBody == null);
    if (auto) {
      var sb = new StringBuilder();
      string d = DefaultInit(ps.Type);
      string init = p.Initializer != null ? " = " + Expr(p.Initializer.Value) : (d != null ? " = " + d : "");
      sb.Append(st + t + " _" + name + init + ";");
      bool strProp = ps.Type.SpecialType == SpecialType.System_String;
      if (strProp && p.Initializer != null)
        Refuse(p.Initializer, "a string property initialiser is not in the Crust C# subset yet. Assign it in the constructor.");
      foreach (var a in accs) {
        if (a.IsKind(SyntaxKind.GetAccessorDeclaration) && strProp)
          sb.Append(" " + st + virt + t + " get_" + name + "() { fastring _r(" + (isStatic ? "" : "this->") + "_" + name + "); return _r; }");
        else if (a.IsKind(SyntaxKind.GetAccessorDeclaration))
          sb.Append(" " + st + virt + t + " get_" + name + "() { return " + (isStatic ? "" : "this->") + "_" + name + "; }");
        else if (a.IsKind(SyntaxKind.SetAccessorDeclaration))
          sb.Append(" " + st + virt + "void set_" + name + "(" + t + " v) { " + (isStatic ? "" : "this->") + "_" + name + " = v; }");
        else Refuse(a, "`init` accessors are not in the Crust C# subset. Use `set`.");
      }
      o.At(line, sb.ToString());
      return;
    }
    foreach (var a in accs) {
      if (a.IsKind(SyntaxKind.InitAccessorDeclaration))
        Refuse(a, "`init` accessors are not in the Crust C# subset. Use `set`.");
      bool get = a.IsKind(SyntaxKind.GetAccessorDeclaration);
      string head = st + virt + (get ? t + " get_" + name + "()" : "void set_" + name + "(" + t + " value)");
      if (a.ExpressionBody != null) {
        string e = Expr(a.ExpressionBody.Expression);
        o.At(LineOf(a), head + (get ? " { return " + e + "; }" : " { " + e + "; }"));
      } else if (a.Body != null) {
        o.At(LineOf(a), head);
        Block(a.Body);
      } else {
        Refuse(a, "an accessor mixing auto and bodied forms is not in the Crust C# subset.");
      }
    }
  }

  string ParamList(ParameterListSyntax pl, bool isMain = false)
  {
    var parts = new List<string>();
    foreach (var p in pl.Parameters) {
      var ps = (IParameterSymbol)model.GetDeclaredSymbol(p);
      if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.ParamsKeyword)))
        Refuse(p, "`params` is not in the Crust C# subset. Pass an array.");
      if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.InKeyword)))
        Refuse(p, "`in` parameters are not in the Crust C# subset.");
      string t;
      if (isMain && ps.Type is IArrayTypeSymbol at && at.ElementType.SpecialType == SpecialType.System_String)
        t = "std::vector<const char *> &";
      else {
        t = TypeName(ps.Type, p);
        bool byRef = ps.RefKind == RefKind.Ref || ps.RefKind == RefKind.Out;
        if (byRef || Borrowed(ps.Type)) t += " &";
      }
      parts.Add(t + " " + Ident(p.Identifier.Text));
    }
    return string.Join(", ", parts);
  }

  /** class / array / List / Dictionary / interface: C# passes a reference; here it is borrowed. */
  bool Borrowed(ITypeSymbol t)
  {
    if (t is IArrayTypeSymbol || IsList(t) || IsDictionary(t)) return true;
    if (t.TypeKind == TypeKind.Interface) return true;
    return IsOwnedClass(t);
  }

  void CheckTypeParams(MethodDeclarationSyntax md)
  {
    if (md.TypeParameterList == null) return;
    var ms = (IMethodSymbol)model.GetDeclaredSymbol(md);
    if (!ms.TypeParameters.All(ArenaConstrained) || Has(md.Modifiers, SyntaxKind.VirtualKeyword) || Has(md.Modifiers, SyntaxKind.AbstractKeyword)
        || Has(md.Modifiers, SyntaxKind.OverrideKeyword))
      Refuse(md, "generic methods are not in the Crust C# subset, except a non-virtual one whose every type parameter is constrained to an arena class (`T GetComponent<T>() where T : Component`). Make the enclosing class generic, or write one method per type.");
  }

  void Method(MethodDeclarationSyntax md, INamedTypeSymbol owner, bool isInterface)
  {
    //a foreign C function (`[Cpp("pb2_step({0})")] public static extern int Step(..);`) is declared by the header its type includes; there is
    //nothing to emit, and its calls spell the template
    if (Has(md.Modifiers, SyntaxKind.ExternKeyword) && CppTemplate(model.GetDeclaredSymbol(md)) != null) return;
    RefuseExtern(md, md.Modifiers);
    CheckTypeParams(md);
    if (md.ExplicitInterfaceSpecifier != null)
      Refuse(md, "explicit interface implementations are not in the Crust C# subset. Name the method as the interface does.");
    var ms = (IMethodSymbol)model.GetDeclaredSymbol(md);
    bool isStatic = Has(md.Modifiers, SyntaxKind.StaticKeyword);
    bool isAbstract = Has(md.Modifiers, SyntaxKind.AbstractKeyword) || (isInterface && md.Body == null && md.ExpressionBody == null);
    bool isVirtual = Has(md.Modifiers, SyntaxKind.VirtualKeyword) || Has(md.Modifiers, SyntaxKind.AbstractKeyword) || (isInterface && !isStatic);
    bool isMain = isStatic && md.Identifier.Text == "Main" && IsEntryCandidate(owner, md);

    string ret = ms.ReturnsVoid ? "void" : TypeName(ms.ReturnType, md.ReturnType);
    if (IsFluent(ms)) {
      if (ReturnsOf(md).Any(x => !(x.Expression is ThisExpressionSyntax)))
        Refuse(md, "a method that returns `this` and something else is not in the Crust C# subset: `this` is returned as a pointer, a new object as a value. Split it into two methods.");
      if (isVirtual)
        Refuse(md, "a virtual method returning `this` is not in the Crust C# subset: Crust cannot chain through a virtual call. Return void.");
      ret += " *";
    }
    string pl = ParamList(md.ParameterList, isMain);
    string name = Ident(md.Identifier.Text);
    var head = (isStatic ? "static " : "") + (isVirtual ? "virtual " : "") + ret + " " + name + "(" + pl + ")";
    if (md.TypeParameterList != null)       //an arena-constrained generic method is a member template (cpprust instantiates it per type argument)
      head = "template <" + string.Join(", ", md.TypeParameterList.Parameters.Select(p => "class " + Ident(p.Identifier.Text))) + "> " + head;

    if (isMain) { RegisterEntry(owner, ms); entryMethod = ms; }

    if (isAbstract) { o.At(LineOf(md), head + " = 0;"); return; }
    if (md.ExpressionBody != null) {
      BeginHoist();
      currentMethod = ms;
      string body;
      try {
        if (ms.ReturnsVoid) body = Expr(md.ExpressionBody.Expression, statement: true) + ";";
        else body = Return(new ReturnStatementData(md.ExpressionBody.Expression));
      } finally { currentMethod = null; }
      string pre = string.Join(" ", hoisted);
      canHoist = false; hoisted.Clear();
      o.At(LineOf(md), head + " { " + (pre.Length > 0 ? pre + " " : "") + body + " }");
      return;
    }
    o.At(LineOf(md), head);
    currentMethod = ms;
    try { Block(md.Body); } finally { currentMethod = null; }
  }

  bool IsEntryCandidate(INamedTypeSymbol owner, MethodDeclarationSyntax md)
  {
    if (mainClass != null) return FlatName(owner) == mainClass.Replace("::", "_").Replace(".", "_");
    return true;
  }

  public static List<string> entryCandidates = new List<string>();   //every usable `static Main`, to report an ambiguity

  void RegisterEntry(INamedTypeSymbol owner, IMethodSymbol ms)
  {
    if (ms.ReturnType.SpecialType != SpecialType.System_Int32 && !ms.ReturnsVoid) return;
    if (ms.Parameters.Length > 1) return;
    if (ms.Parameters.Length == 1 && !(ms.Parameters[0].Type is IArrayTypeSymbol)) return;
    string call = FlatName(owner) + "::Main";
    if (!entryCandidates.Contains(call)) entryCandidates.Add(call);
    if (entryCall != null) return;                      //the first wins; Program reports it when there are several
    entryCall = FlatName(owner) + "::Main";
    entryReturnsInt = !ms.ReturnsVoid;
    entryTakesArgs = ms.Parameters.Length == 1;
  }

  /** `public static V operator +(V a, V b)` is a static method called `op_Addition` (Roslyn's name for it); a use of the operator is a call of it (UserOperator).
      Only for a struct: for a class `a + b` would have to copy or alias, which is exactly what the subset refuses. */
  void OperatorDecl(BaseMethodDeclarationSyntax od, INamedTypeSymbol owner, TypeSyntax returnType)
  {
    if (owner.TypeKind != TypeKind.Struct)
      Refuse(od, "operator overloading is for a struct in the Crust C# subset: on a class `a + b` would have to copy or alias an object, and a class is single-owner here. Make the type a struct, or write a named method.");
    var ms = (IMethodSymbol)model.GetDeclaredSymbol(od);
    //(a conversion operator, `implicit operator Vector3(Vector2 v)`, is the static method `op_Implicit`; a use of the conversion is a call of it: Expr, Cast)
    string head = "static " + TypeName(ms.ReturnType, returnType) + " " + OpName(ms) + "(" + ParamList(od.ParameterList) + ")";
    if (od.ExpressionBody != null) {
      BeginHoist();
      currentMethod = ms;
      string body;
      try { body = Return(new ReturnStatementData(od.ExpressionBody.Expression)); } finally { currentMethod = null; }
      string pre = string.Join(" ", hoisted);
      canHoist = false; hoisted.Clear();
      o.At(LineOf(od), head + " { " + (pre.Length > 0 ? pre + " " : "") + body + " }");
      return;
    }
    o.At(LineOf(od), head);
    currentMethod = ms;
    try { Block(od.Body); } finally { currentMethod = null; }
  }

  /** `a + b` where + is a struct's own operator */
  bool IsUserOperatorUse(ExpressionSyntax e)
  {
    while (e is ParenthesizedExpressionSyntax pp) e = pp.Expression;
    if (!(e is BinaryExpressionSyntax || e is PrefixUnaryExpressionSyntax)) return false;
    return model.GetSymbolInfo(e).Symbol is IMethodSymbol m && m.MethodKind == MethodKind.UserDefinedOperator && !IsLib(m);
  }

  /** The C name of an operator: Roslyn's (`op_Addition`), and when the type has several of that name (`Vec * int`, `int * Vec`, `Vec * Vec`) the parameter types
      too, because cpprust tells overloads apart by their argument count only. */
  string OpName(IMethodSymbol ms)
  {
    var same = ms.ContainingType.GetMembers(ms.Name).OfType<IMethodSymbol>().Count();
    if (same <= 1) return Ident(ms.Name);
    return Ident(ms.Name) + "_" + string.Join("_", ms.Parameters.Select(p => Mangle(TypeName(p.Type, null))));
  }

  /** a use of a user-defined operator: a call of its static method.  The operands of a call are not evaluated in order in C, so two that have effects are refused. */
  string UserOperator(ExpressionSyntax whole, IMethodSymbol sym, params ExpressionSyntax[] operands)
  {
    if (sym.ContainingType.TypeKind != TypeKind.Struct)
      Refuse(whole, "the operator `" + sym.Name + "` of `" + sym.ContainingType.Name + "` is not in the Crust C# subset: operators are for structs.");
    if (sym.Name == "op_Increment" || sym.Name == "op_Decrement")
      Refuse(whole, "`++` / `--` on a struct with an operator is not in the Crust C# subset. Write `v = v + one`.");
    if (operands.Length == 2 && Conflicts(operands[0], operands[1]))
      Refuse(whole, "both operands of `" + sym.Name.Replace("op_", "") + "` have effects, and a call does not evaluate its arguments in order in C. Assign one to a local first.");
    //cpprust does not lower `v[i]` inside the arguments of a static call: an element is read into a plain local first
    var texts = new List<string>();
    foreach (var x in operands) {
      string t = Expr(x);
      var xe = x;
      while (xe is ParenthesizedExpressionSyntax pp) xe = pp.Expression;
      var oty = model.GetTypeInfo(x).Type;
      if (xe is BaseObjectCreationExpressionSyntax && oty != null && IsStructValue(oty)) {
        //a constructor call is not an expression in C: the value is built in a local
        RequireHoist(x, "a `new " + oty.Name + "(..)` used as an operand of `" + sym.Name.Replace("op_", "") + "`");
        var oal = (xe as ObjectCreationExpressionSyntax)?.ArgumentList ?? (xe as ImplicitObjectCreationExpressionSyntax)?.ArgumentList;
        var oargs = oal == null ? new List<string>() : ArgTexts(oal.Arguments, xe).ToList();
        oargs = WithDefaults(oargs.ToArray(), model.GetSymbolInfo(xe).Symbol as IMethodSymbol, xe);
        string tmpn = NewTemp("_n");
        hoisted.Add(TypeName(oty, x) + " " + tmpn + (oargs.Count > 0 ? "(" + string.Join(", ", oargs) + ")" : "") + ";");
        t = tmpn;
      } else if (xe is ElementAccessExpressionSyntax) {
        var xty = model.GetTypeInfo(x).Type;
        RequireHoist(x, "an element used as an operand of `" + sym.Name.Replace("op_", "") + "`");
        string tmp = NewTemp("_e");
        hoisted.Add(TypeName(xty, x) + " " + tmp + " = " + t + ";");
        t = tmp;
      }
      texts.Add(t);
    }
    return FlatName(sym.ContainingType) + "::" + OpName(sym) + "(" + string.Join(", ", texts) + ")";
  }

  void Ctor(ConstructorDeclarationSyntax cd, INamedTypeSymbol owner)
  {
    RefuseExtern(cd, cd.Modifiers);
    string name = FlatName(owner);
    string head = name + "(" + ParamList(cd.ParameterList) + ")";
    if (Has(cd.Modifiers, SyntaxKind.StaticKeyword))
      Refuse(cd, "static constructors are not supported by CC# --crust yet. Initialise the static field where it is declared.");
    if (cd.Initializer != null) {
      var args = cd.Initializer.ArgumentList.Arguments.Select(a => Arg(a)).ToList();
      if (cd.Initializer.IsKind(SyntaxKind.ThisConstructorInitializer))
        Refuse(cd.Initializer, "`: this(..)` constructor chaining is not in the Crust C# subset. Call a shared method from each constructor.");
      var bt = owner.BaseType;
      head += " : " + (IsArena(bt) ? FlatName(bt) : TypeName(bt)) + "(" + string.Join(", ", args) + ")";   //(an arena base is named, not its pointer type)
    }
    o.At(LineOf(cd), head);
    Block(cd.Body);
  }

  void Dtor(DestructorDeclarationSyntax dd, INamedTypeSymbol owner)
  {
    o.At(LineOf(dd), "~" + FlatName(owner) + "()");
    Block(dd.Body);
  }

  // ------------------------------------------------------------------ the corelib mapping ([Cpp])

  /** the C++ template a corelib declaration carries, or null. */
  string CppTemplate(ISymbol s)
  {
    if (s == null) return null;
    foreach (var a in s.OriginalDefinition.GetAttributes())
      if (a.AttributeClass != null && a.AttributeClass.Name == "CppAttribute" && a.ConstructorArguments.Length == 1)
        return a.ConstructorArguments[0].Value as string;
    return null;
  }

  bool HasCppAttr(ISymbol s, string name)
  {
    return s != null && s.OriginalDefinition.GetAttributes().Any(a => a.AttributeClass != null && a.AttributeClass.Name == name);
  }

  /** headers named by [CppInclude] on the member and on the types around it. */
  void AddIncludes(ISymbol s)
  {
    for (var x = s == null ? null : s.OriginalDefinition; x != null; x = x.ContainingType)
      foreach (var a in x.GetAttributes())
        if (a.AttributeClass != null && a.AttributeClass.Name == "CppIncludeAttribute" && a.ConstructorArguments.Length == 1)
          Includes.Add((string)a.ConstructorArguments[0].Value);
  }

  /** declared in the corelib sources (as opposed to the user's program). */
  bool IsLib(ISymbol s)
  {
    return s != null && s.OriginalDefinition.Locations.Any(l => l.SourceTree != null && Program.libTrees.Contains(l.SourceTree));
  }

  void RefuseUnimplemented(SyntaxNode at, ISymbol s)
  {
    string what = s is INamedTypeSymbol nt ? nt.ToDisplayString() : (s.ContainingType != null ? s.ContainingType.ToDisplayString() + "." : "") + s.Name;
    Refuse(at, "`" + what + "` is declared in the CC# corelib (the .NET surface) but has no Crust implementation yet. See corelib/src, and the [Cpp] attributes there for what exists.");
  }

  ExpressionSyntax Unparen(ExpressionSyntax e)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    return e;
  }

  /** a corelib method that returns its receiver: `sb.Append(a).Append(b)` is one statement per call. */
  bool IsCppFluentCall(ExpressionSyntax e)
  {
    return Unparen(e) is InvocationExpressionSyntax inv
      && HasCppAttr(model.GetSymbolInfo(inv).Symbol, "CppFluentAttribute");
  }

  /** the receiver of a corelib call as C++ text.  A fluent chain is flattened: every earlier call in it is
      emitted as its own statement, in order, and the receiver is the variable at the start of the chain. */
  string RecvText(ExpressionSyntax recv)
  {
    var u = Unparen(recv);
    if (IsCppFluentCall(u)) {
      var inner = (InvocationExpressionSyntax)u;
      var ima = inner.Expression as MemberAccessExpressionSyntax;
      if (ima == null) Refuse(inner, "a chained call needs a receiver.");
      var baseExpr = ima.Expression;
      var b = Unparen(baseExpr);
      if (!IsCppFluentCall(b) && !(b is IdentifierNameSyntax || b is MemberAccessExpressionSyntax || b is ElementAccessExpressionSyntax))
        Refuse(baseExpr, "a chain of calls must start from a variable, not from `" + b.Kind() + "`. Put it in a local first.");
      string stmt = Invocation(inner);                       //emits any earlier calls of the chain first
      RequireHoist(inner, "a chained call");
      hoisted.Add(stmt + ";");
      while (IsCppFluentCall(b)) {                           //walk down to the variable the chain started from
        var bi = (InvocationExpressionSyntax)b;
        b = Unparen(((MemberAccessExpressionSyntax)bi.Expression).Expression);
      }
      return Expr(b);                                        //the same variable, named again
    }
    return Expr(u);
  }

  /** Expand a [Cpp] template.  {this} receiver, {0}.. arguments, {0:c} argument as `const char *`, {T0}.. type arguments. */
  string Expand(string tpl, ISymbol sym, string recvText, IReadOnlyList<ArgumentSyntax> args, string[] argTexts, ITypeSymbol[] typeArgs, SyntaxNode at)
  {
    var sb = new StringBuilder();
    for (int i = 0; i < tpl.Length; i++) {
      char c = tpl[i];
      if (c != '{') { sb.Append(c); continue; }
      int j = tpl.IndexOf('}', i);
      if (j < 0) { sb.Append(c); continue; }
      string tok = tpl.Substring(i + 1, j - i - 1);
      i = j;
      if (tok == "this") {
        if (recvText == null) Refuse(at, "`" + sym.Name + "` needs a receiver.");
        sb.Append(recvText);
      } else if (tok.Length > 0 && tok[0] == 'T' && int.TryParse(tok.Substring(1), out int ti)) {
        sb.Append(TypeName(typeArgs[ti], at));
      } else if (tok.Length > 0 && tok[0] == 'M' && int.TryParse(tok.Substring(1), out int mi)) {
        sb.Append(Mangle(TypeName(typeArgs[mi], at)));
      } else {
        bool cstr = tok.EndsWith(":c");
        string num = cstr ? tok.Substring(0, tok.Length - 2) : tok;
        if (!int.TryParse(num, out int ai) || ai >= args.Count) { Refuse(at, "internal: bad placeholder {" + tok + "} in the template for `" + sym.Name + "`."); }
        if (!cstr) sb.Append(argTexts[ai]);
        else if (argTexts[ai] != null) sb.Append(argTexts[ai] + ".c_str()");      //already a named string
        else sb.Append(StrCText(args[ai].Expression));                            //a constant: the literal itself
      }
    }
    return sb.ToString();
  }

  // ------------------------------------------------------------------ types

  string TypeName(ITypeSymbol t, SyntaxNode at = null)
  {
    switch (t.SpecialType) {
      case SpecialType.System_Boolean: return "bool";
      case SpecialType.System_Byte: return "unsigned char";
      case SpecialType.System_SByte: return "signed char";
      case SpecialType.System_Int16: return "short";
      case SpecialType.System_UInt16: return "unsigned short";
      case SpecialType.System_Int32: return "int";
      case SpecialType.System_UInt32: return "unsigned";
      case SpecialType.System_Int64: return "long long";
      case SpecialType.System_UInt64: return "unsigned long long";
      case SpecialType.System_Single: return "float";
      case SpecialType.System_Double: return "double";
      case SpecialType.System_Void: return "void";
      case SpecialType.System_Char:
        Refuse(at, "`char` is not in the Crust C# subset. Use `byte` (or `int` for a code point).");
        break;
      case SpecialType.System_String:
        UsesString = true;
        AddIncludes(t);
        return "fastring";         //coost's string; immutable in C#, so a by-value copy is indistinguishable from sharing
      case SpecialType.System_Object:
        Refuse(at, "`object` is not in the Crust C# subset: there is no common base class and no boxing. Use an interface or a generic class.");
        break;
      case SpecialType.System_Decimal:
        Refuse(at, "`decimal` is not in the Crust C# subset.");
        break;
      case SpecialType.System_IntPtr: case SpecialType.System_UIntPtr:
        Refuse(at, "`IntPtr` is not in the Crust C# subset.");
        break;
    }
    if (t is IArrayTypeSymbol arr) {
      if (arr.Rank > 1) Refuse(at, "multidimensional arrays are not in the Crust C# subset. Use a jagged array `T[][]`.");
      return "std::vector<" + TypeName(arr.ElementType, at) + ">";
    }
    if (t is IPointerTypeSymbol) Refuse(at, "pointers are not in the Crust C# subset.");
    if (t is ITypeParameterSymbol tp) return Ident(tp.Name) + (ArenaSize(tp) > 0 ? " *" : "");
    if (t is INamedTypeSymbol n) {
      if (n.TypeKind == TypeKind.Delegate)
        Refuse(at, "delegates are not in the Crust C# subset. Use an interface with one method.");
      if (n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T || n.Name == "Nullable")
        Refuse(at, "`T?` (nullable value types) is not in the Crust C# subset. Use a bool flag beside the value.");
      if (n.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>") {
        var kt = n.TypeArguments[0];
        bool okKey = kt.TypeKind == TypeKind.Enum || (kt.SpecialType >= SpecialType.System_Boolean && kt.SpecialType <= SpecialType.System_UInt64 && kt.SpecialType != SpecialType.System_Char);
        if (!okKey)
          Refuse(at, "a Dictionary key of type `" + kt.ToDisplayString() + "` is not supported yet: Crust orders map keys with a `compare` method, and coost's fastring has a different one. Use an int / enum key, or a List and a loop.");
      }
      string tpl = CppTemplate(n);
      if (tpl != null) {                                  //a corelib type: how it is spelled in C++
        AddIncludes(n);
        var targs = n.TypeArguments.ToArray();
        return Expand(tpl, n, null, new List<ArgumentSyntax>(), new string[0], targs, at);
      }
      if (IsLib(n)) RefuseUnimplemented(at, n);
      string name = FlatName(n);
      if (n.TypeArguments.Length > 0)
        name += "<" + string.Join(", ", n.TypeArguments.Select(a => TypeName(a, at))) + ">";
      if (IsArena(n)) { ArenaClasses.Add(name); return name + " *"; }       //a reference to an arena object
      return name;
    }
    Refuse(at, "type `" + t.ToDisplayString() + "` is not in the Crust C# subset.");
    return "";
  }

  // ------------------------------------------------------------------ statements

  void Block(BlockSyntax b)
  {
    o.Text("{");
    foreach (var s in b.Statements) Stmt(s);
    o.At(LineOf(b.CloseBraceToken), "}");
  }

  /** a statement used as a loop / if body: always braced. */
  void Body(StatementSyntax s)
  {
    if (s is BlockSyntax b) { Block(b); return; }
    o.Text("{");
    Stmt(s);
    o.At(LineOf(s.GetLastToken()), "}");
  }

  void Stmt(StatementSyntax s)
  {
    try { StmtInner(s); }
    catch (CrustRefusal r) { Report(r); }
  }

  void StmtInner(StatementSyntax s)
  {
    canHoist = false;
    int line = LineOf(s);
    switch (s) {
      case BlockSyntax b:
        o.At(line, "");
        Block(b);
        return;
      case LocalDeclarationStatementSyntax ld:
        if (ld.UsingKeyword.RawKind != 0) Refuse(ld, "`using` declarations are not in the Crust C# subset. Use a class with a destructor and scope exit.");
        BeginHoist();
        var decls = LocalDecl(ld.Declaration, ld.IsConst);
        o.PadTo(line);
        FlushHoisted();
        foreach (var d in decls) o.Text(d);
        return;
      case ExpressionStatementSyntax es:
        BeginHoist();
        string est = ExprStmt(es.Expression);
        est = est.Length == 0 ? "" : est + ";";
        o.PadTo(line);
        FlushHoisted();
        o.Text(est);
        return;
      case ReturnStatementSyntax rs:
        BeginHoist();
        string rst = Return(rs);
        o.PadTo(line);
        FlushHoisted();
        o.Text(rst);
        return;
      case IfStatementSyntax ifs:
        IfChain(ifs, line, false);
        return;
      case WhileStatementSyntax ws:
        o.At(line, "while (" + Expr(ws.Condition) + ")");
        Body(ws.Statement);
        return;
      case DoStatementSyntax dos:
        o.At(line, "do");
        Body(dos.Statement);
        o.Text("while (" + Expr(dos.Condition) + ");");
        return;
      case ForStatementSyntax fs:
        For(fs, line);
        return;
      case ForEachStatementSyntax fe:
        ForEach(fe, line);
        return;
      case BreakStatementSyntax _: o.At(line, "break;"); return;
      case ContinueStatementSyntax _: o.At(line, "continue;"); return;
      case EmptyStatementSyntax _: return;
      case SwitchStatementSyntax sw: Switch(sw, line); return;
      case ThrowStatementSyntax ts:
        Refuse(ts, "`throw` is not supported by CC# --crust yet (Crust lowers it to the checked `raise` model). Return an error code.");
        return;
      case TryStatementSyntax tr:
        Refuse(tr, "`try` / `catch` / `finally` is not supported by CC# --crust yet (Crust has the checked `except` model, not an unwinder). Return an error code.");
        return;
      case LockStatementSyntax lk:
        Refuse(lk, "`lock` is not in the Crust C# subset. Threads are declared to the compiler, not locked.");
        return;
      case UsingStatementSyntax us:
        Refuse(us, "`using (..)` is not in the Crust C# subset. Use a class with a destructor and scope exit.");
        return;
      case GotoStatementSyntax gt:
        Refuse(gt, "`goto` is not in the Crust C# subset.");
        return;
      case YieldStatementSyntax ys:
        Refuse(ys, "`yield` (iterators) is not in the Crust C# subset.");
        return;
      case CheckedStatementSyntax cs:
        Refuse(cs, "`checked` is not in the Crust C# subset: wrapping is the arithmetic here.");
        return;
      case LocalFunctionStatementSyntax lf:
        Refuse(lf, "local functions are not in the Crust C# subset. Make it a static method.");
        return;
      case UnsafeStatementSyntax us2:
        Refuse(us2, "`unsafe` is not in the Crust C# subset.");
        return;
      case FixedStatementSyntax fx:
        Refuse(fx, "`fixed` is not in the Crust C# subset.");
        return;
      default:
        Refuse(s, "statement `" + s.Kind() + "` is not in the Crust C# subset.");
        return;
    }
  }

  void BeginHoist() { hoisted.Clear(); canHoist = true; }
  void FlushHoisted()
  {
    canHoist = false;
    foreach (var h in hoisted) o.Text(h);
    hoisted.Clear();
  }

  class ReturnStatementData { public ExpressionSyntax Expression; public ReturnStatementData(ExpressionSyntax e) { Expression = e; } }
  string Return(ReturnStatementData rd) { return ReturnCore(rd.Expression); }
  string Return(ReturnStatementSyntax rs) { return ReturnCore(rs.Expression); }

  string ReturnCore(ExpressionSyntax rexpr)
  {
    if (rexpr == null) return "return;";
    var m = currentMethod;
    if (m != null && m.ReturnType.SpecialType == SpecialType.System_String) return ReturnString(rexpr);
    if (m != null && IsFluent(m)) return "return this;";
    if (m != null && Borrowed(m.ReturnType) && m.ReturnType.TypeKind != TypeKind.Interface && !IsFreshOrLocal(rexpr))
      Refuse(rexpr, "returning an existing object would copy it where C# returns the same one: a class is single-owner here. Return a new object, or give the caller a method that does the work.");
    return "return " + Expr(rexpr) + ";";
  }

  /** `return <string>;` -- cpprust moves a bare local out and refuses an expression, so go through a local. */
  string ReturnString(ExpressionSyntax e)
  {
    UsesString = true;
    var inner = e;
    while (inner is ParenthesizedExpressionSyntax pp) inner = pp.Expression;
    string n = Expr(inner);          //a named fastring: a local, a hoisted temporary, or a field / parameter
    if (inner is IdentifierNameSyntax id && model.GetSymbolInfo(id).Symbol is ILocalSymbol) return "return " + n + ";";
    if (n.StartsWith("_s") && n.All(ch => char.IsLetterOrDigit(ch) || ch == '_')) return "return " + n + ";";
    if (!canHoist) Refuse(e, "returning this string needs a temporary, which is not supported here.");
    string r = NewTemp("_r");
    hoisted.Add("fastring " + r + "(" + n + ");");
    return "return " + r + ";";
  }

  /** a fresh value, or a local variable (moved out); not a field, parameter or element. */
  bool IsFreshOrLocal(ExpressionSyntax e)
  {
    if (IsFresh(e)) return true;
    if (e is ParenthesizedExpressionSyntax p) return IsFreshOrLocal(p.Expression);
    if (e is IdentifierNameSyntax id) return model.GetSymbolInfo(id).Symbol is ILocalSymbol;
    return false;
  }

  void IfChain(IfStatementSyntax ifs, int line, bool isElseIf)
  {
    //the first condition is evaluated once, first: a string it needs can be built just before the `if`.
    //An `else if` condition runs only when the earlier ones failed, so it cannot be hoisted.
    if (!isElseIf) BeginHoist(); else canHoist = false;
    string cond = Expr(ifs.Condition);
    if (!isElseIf) { o.PadTo(line); FlushHoisted(); }
    canHoist = false;
    if (isElseIf) o.Text("if (" + cond + ")");
    else o.At(line, "if (" + cond + ")");
    Body(ifs.Statement);
    if (ifs.Else != null) {
      o.At(LineOf(ifs.Else.ElseKeyword), "else");
      if (ifs.Else.Statement is IfStatementSyntax inner) IfChain(inner, LineOf(inner), true);
      else Body(ifs.Else.Statement);
    }
  }

  void For(ForStatementSyntax fs, int line)
  {
    string init = "";
    if (fs.Declaration != null) {
      var parts = LocalDecl(fs.Declaration, false, forInit: true);
      init = parts[0];
    } else {
      init = string.Join(", ", fs.Initializers.Select(e => ExprStmt(e)));
    }
    string cond = fs.Condition == null ? "" : Expr(fs.Condition);
    string inc = string.Join(", ", fs.Incrementors.Select(e => ExprStmt(e)));
    o.At(line, "for (" + init + "; " + cond + "; " + inc + ")");
    Body(fs.Statement);
  }

  void ForEach(ForEachStatementSyntax fe, int line)
  {
    var coll = model.GetTypeInfo(fe.Expression).Type;
    if (!(coll is IArrayTypeSymbol) && !IsList(coll))
      Refuse(fe.Expression, "`foreach` over `" + coll.ToDisplayString() + "` is not in the Crust C# subset. Iterate an array or a List<T>.");
    var info = model.GetDeclaredSymbol(fe) as ILocalSymbol;
    string t = TypeName(info.Type, fe.Type);
    string e = Expr(fe.Expression);
    // a class element is borrowed: iterate by reference so nothing is copied
    string amp = IsOwnedClass(info.Type) ? "& " : " ";
    if (IsArena(info.Type)) {
      //a reference to an arena object is a pointer, and cpprust's range-for does not take one reliably (a spelled-out pointer type is not
      //lowered; with `auto` a static container's bound comes out wrong).  An indexed loop is exact -- and the collection is read for each
      //element, so it must not be a call or have a side effect.
      if (!IsPure(fe.Expression))
        Refuse(fe.Expression, "`foreach` over arena references needs a variable, field or property to iterate, not a call: assign the collection to a local first.");
      string ix = NewTemp("_fe");
      o.At(line, "for (int " + ix + " = 0; " + ix + " < " + e + ".size(); " + ix + " = " + ix + " + 1)");
      o.Text("{ " + t + " " + Ident(fe.Identifier.Text) + " = " + e + "[" + ix + "];");
      if (fe.Statement is BlockSyntax fb) {
        foreach (var st in fb.Statements) Stmt(st);
        o.At(LineOf(fb.CloseBraceToken), "}");
      } else {
        Stmt(fe.Statement);
        o.At(LineOf(fe.Statement.GetLastToken()), "}");
      }
      return;
    }
    o.At(line, "for (" + t + amp + Ident(fe.Identifier.Text) + " : " + e + ")");
    Body(fe.Statement);
  }

  void Switch(SwitchStatementSyntax sw, int line)
  {
    var tt = model.GetTypeInfo(sw.Expression).Type;
    if (tt.SpecialType == SpecialType.System_String)
      Refuse(sw.Expression, "`switch` on a string is not in the Crust C# subset (no string). Switch on an int or an enum.");
    BeginHoist();
    string subject = Expr(sw.Expression);
    o.PadTo(line); FlushHoisted();
    o.At(line, "switch (" + subject + ") {");
    foreach (var sec in sw.Sections) {
      foreach (var lab in sec.Labels) {
        if (lab is CaseSwitchLabelSyntax cl) o.At(LineOf(lab), "case " + Expr(cl.Value) + ":");
        else if (lab is DefaultSwitchLabelSyntax) o.At(LineOf(lab), "default:");
        else Refuse(lab, "pattern `case` labels are not in the Crust C# subset. Use constant labels.");
      }
      o.Text("{");
      foreach (var st in sec.Statements) Stmt(st);
      o.At(LineOf(sec.GetLastToken()), "}");
    }
    o.At(LineOf(sw.CloseBraceToken), "}");
  }

  List<string> LocalDecl(VariableDeclarationSyntax decl, bool isConst, bool forInit = false)
  {
    var outp = new List<string>();
    var first = true;
    var common = new StringBuilder();
    foreach (var v in decl.Variables) {
      var ls = (ILocalSymbol)model.GetDeclaredSymbol(v);
      //`object o = new object();` -- an opaque value with identity and nothing else
      if (ls.Type.SpecialType == SpecialType.System_Object && v.Initializer != null
          && v.Initializer.Value is ObjectCreationExpressionSyntax oo && (oo.ArgumentList == null || oo.ArgumentList.Arguments.Count == 0)
          && oo.Initializer == null) {
        if (arrayHelpers.Add("_cs_object")) preamble.Append("class _cs_object { public: char _unused; }; ");
        string piece0 = "_cs_object " + Ident(v.Identifier.Text);
        if (forInit) { outp.Add(piece0); first = false; common.Append(piece0); continue; }
        outp.Add(piece0 + ";");
        continue;
      }
      if (v.Initializer != null && v.Initializer.Value is QueryExpressionSyntax)
        Refuse(v.Initializer.Value, "LINQ is not in the Crust C# subset. Write a loop.");
      string t = TypeName(ls.Type, decl.Type);
      string name = Ident(v.Identifier.Text);
      string piece;
      if (v.Initializer == null) {
        piece = t + " " + name;
      } else {
        var init = v.Initializer.Value;
        if (ls.Type.SpecialType == SpecialType.System_String) {
          if (forInit) Refuse(init, "a string declared in a `for` header is not in the Crust C# subset. Declare it before the loop.");
          UsesString = true;
          var icv = model.GetConstantValue(init);
          if (init is LiteralExpressionSyntax nlit && nlit.IsKind(SyntaxKind.NullLiteralExpression)) {
            Refuse(init, "`null` is not in the Crust C# subset: a string is a value here, empty at worst. Use \"\".");
            piece = "";
          } else if (icv.HasValue && icv.Value is string cs0) {
            piece = (isConst ? "const " : "") + "fastring " + name + " = fastring::from_cstr(\"" + CLit(cs0) + "\")";
          } else if (IsStringLvalue(init) || IsStringCall(init)) {
            piece = "fastring " + name + " = " + CallOrName(init);
          } else {
            //a concatenation / interpolation: build straight into the variable
            RequireHoist(init, "building this string");
            hoisted.Add("fastring " + name + ";");
            var sp = new List<object>();
            StringParts(init, sp);
            var saveRoot2 = hoistRoot; hoistRoot = init;
            try { foreach (var part in sp) AppendPart(name, part); } finally { hoistRoot = saveRoot2; }
            piece = "";
          }
        } else if (init is ObjectCreationExpressionSyntax || init is ImplicitObjectCreationExpressionSyntax) {
          piece = NewLocal(ls.Type, init, t, name);
        } else if (init is ArrayCreationExpressionSyntax ac) {
          piece = t + " " + name + " = " + NewArray(ac);
        } else if (init is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression)) {
          if (!IsArena(ls.Type))
            Refuse(init, "`null` is not in the Crust C# subset for owned classes: a class is a value, not a reference. Construct it, or use an arena class.");
          piece = t + " " + name + " = NULL";
        } else if ((IsOwnedClass(ls.Type) || IsList(ls.Type) || ls.Type is IArrayTypeSymbol) && IsFresh(init)) {
          piece = t + " " + name + " = " + Expr(init);
        } else if (IsFluentCall(init)) {
          Refuse(init, "the result of a method that returns `this` can only start a chain or be dropped: storing it would alias the object, and a class is single-owner here. Call the methods on the original variable.");
          piece = "";
        } else if (IsOwnedClass(ls.Type) || IsList(ls.Type) || ls.Type is IArrayTypeSymbol) {
          Refuse(init, "initialising `" + v.Identifier.Text + "` from another object would alias it: a class is single-owner here, so there is one owner and no copy. Construct a new one with `new`, or pass the original by reference.");
          piece = "";
        } else {
          piece = (isConst ? "const " : "") + t + " " + name + " = " + Expr(init);
        }
      }
      if (forInit) {
        //`int i = 0, j = 0` shares one type; only valid when the types agree
        if (first) { common.Append(piece); first = false; }
        else {
          int sp = piece.IndexOf(' ');
          common.Append(", " + piece.Substring(t.Length + 1));
        }
      } else {
        if (piece.Length > 0) outp.Add(piece + ";");
      }
    }
    if (forInit) { outp.Add(common.ToString()); }
    return outp;
  }

  string NewLocal(ITypeSymbol type, ExpressionSyntax init, string t, string name)
  {
    ArgumentListSyntax al = null;
    if (init is ObjectCreationExpressionSyntax oc) {
      al = oc.ArgumentList;
      if (oc.Initializer != null)
        Refuse(oc.Initializer, "object / collection initializers are not in the Crust C# subset. Assign the fields on the next lines.");
    } else if (init is ImplicitObjectCreationExpressionSyntax ioc) {
      al = ioc.ArgumentList;
    }
    var args = al == null ? new List<string>() : ArgTexts(al.Arguments, init).ToList();
    if (CppTemplate(type) == null) args = WithDefaults(args.ToArray(), model.GetSymbolInfo(init).Symbol as IMethodSymbol, init);
    if (IsArena(type)) {
      //the object made is the type of the `new`, which may derive from the type of the variable (`Component c = new Mover(..)`)
      var made = model.GetTypeInfo(init).Type;
      return t + " " + name + " = new " + TemplateArg(IsArena(made) ? made : type, init) + "(" + string.Join(", ", args) + ")";
    }
    if (CppTemplate(type) != null) return t + " " + name + CollectionCtor(type, al, init, args);
    if (args.Count == 0) return t + " " + name;
    return t + " " + name + "(" + string.Join(", ", args) + ")";
  }

  // ------------------------------------------------------------------ expressions

  string Arg(ArgumentSyntax a)
  {
    if (a.NameColon != null) Refuse(a, "named arguments are not in the Crust C# subset. Pass them in order.");
    if (a.RefKindKeyword.RawKind != 0) {
      if (a.Expression is DeclarationExpressionSyntax)
        Refuse(a, "`out var x` is not in the Crust C# subset. Declare the variable first, then pass `out x`.");
      return Expr(a.Expression);                              //a C++ reference: the variable itself
    }
    var at = model.GetTypeInfo(a.Expression).Type;
    string text;
    if (at != null && (IsOwnedClass(at) || IsList(at) || at is IArrayTypeSymbol)
        && IsFresh(a.Expression) && !(a.Expression is ArrayCreationExpressionSyntax))
      text = Hoist(a, at);
    else if (at != null && IsStructValue(at) && Unparen(a.Expression) is BaseObjectCreationExpressionSyntax)
      text = Hoist(a, at);                      //`f(new Handle2D(i, g))`: a constructor call is not an expression in C, so the value is built in a local first
    else
      text = Expr(a.Expression);
    var pt = ParamTypeOf(a);
    if (pt != null && at != null && NeedsUpcast(at, pt)) return Upcast(a, text, at, pt);
    return text;
  }

  /** a struct of the program's own (not a number, not an enum, not a foreign C type): built by a constructor */
  bool IsStructValue(ITypeSymbol t)
  {
    return t.TypeKind == TypeKind.Struct && t.SpecialType == SpecialType.None && CppTemplate(t) == null && !IsLib(t);
  }

  /** the declared type of the parameter this argument binds to. */
  ITypeSymbol ParamTypeOf(ArgumentSyntax a)
  {
    var list = a.Parent as ArgumentListSyntax;
    if (list == null) return null;
    var ms = model.GetSymbolInfo(list.Parent).Symbol as IMethodSymbol;
    if (ms == null || ms.Parameters.Length == 0) return null;
    int i = list.Arguments.IndexOf(a);
    return ms.Parameters[Math.Min(i, ms.Parameters.Length - 1)].Type;
  }

  /** a class (or interface) value bound to a parameter of one of its base types */
  bool NeedsUpcast(ITypeSymbol from, ITypeSymbol to)
  {
    if (!(IsOwnedClass(from) || from.IsValueType == false && from.TypeKind == TypeKind.Class)) return false;
    if (!(IsOwnedClass(to) || to.TypeKind == TypeKind.Interface)) return false;
    if (SymbolEqualityComparer.Default.Equals(from.OriginalDefinition, to.OriginalDefinition)) return false;
    for (var b = from.BaseType; b != null; b = b.BaseType)
      if (SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, to.OriginalDefinition)) return true;
    return from.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, to.OriginalDefinition));
  }

  /** Is `to` reachable from `from` along primary bases only?  Crust lays out one class base first and
      then interface bases, so only the primary chain is a pure cast (address unchanged). */
  bool IsPrimaryBase(ITypeSymbol from, ITypeSymbol to)
  {
    for (var t = from as INamedTypeSymbol; t != null; t = t.BaseType) {
      if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, to.OriginalDefinition)) return true;
      //the first base listed in the declaration is the layout base, whether class or interface
      var decl = t.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().FirstOrDefault();
      if (decl == null || decl.BaseList == null || decl.BaseList.Types.Count == 0) return false;
      var first = ModelOf(decl).GetSymbolInfo(decl.BaseList.Types[0].Type).Symbol as INamedTypeSymbol;
      if (first == null) return false;
      if (first.TypeKind == TypeKind.Interface) return SymbolEqualityComparer.Default.Equals(first.OriginalDefinition, to.OriginalDefinition);
      t = first;
      if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, to.OriginalDefinition)) return true;
      //continue from `first`, whose own BaseType is the next link
      var next = first;
      while (true) {
        var d2 = next.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (d2 == null || d2.BaseList == null || d2.BaseList.Types.Count == 0) return false;
        var f2 = ModelOf(d2).GetSymbolInfo(d2.BaseList.Types[0].Type).Symbol as INamedTypeSymbol;
        if (f2 == null) return false;
        if (SymbolEqualityComparer.Default.Equals(f2.OriginalDefinition, to.OriginalDefinition)) return true;
        if (f2.TypeKind == TypeKind.Interface) return false;
        next = f2;
      }
    }
    return false;
  }

  /** `Call(derived)` where Call takes `Base &`: cpprust lowers it to `Call(&derived)`, a B* -> A* conversion
      that shivyc rejects.  A base reference bound through an explicit pointer cast lowers to
      `A *r = &(*(A *)&derived)`, which it accepts and which dispatches virtually as before. */
  string Upcast(ArgumentSyntax a, string text, ITypeSymbol from, ITypeSymbol to)
  {
    if (!IsPrimaryBase(from, to))
      Refuse(a, "`" + to.Name + "` is a secondary base of `" + from.Name + "`: Crust lays out one class base first and interface bases after it, so converting needs an address adjustment that is not supported yet. Make `" + to.Name + "` the first base, or pass the object through a method of its own class.");
    var e = a.Expression;
    while (e is ParenthesizedExpressionSyntax pp) e = pp.Expression;
    bool lvalue = e is IdentifierNameSyntax || e is MemberAccessExpressionSyntax || e is ElementAccessExpressionSyntax
                  || text.StartsWith("_t");
    if (!lvalue || (model.GetSymbolInfo(e).Symbol is IPropertySymbol ps && !ps.IsIndexer))
      Refuse(a, "passing a `" + from.Name + "` where a `" + to.Name + "` is expected needs a named variable, field or element. Assign the object to a local first.");
    if (!canHoist)
      Refuse(a, "passing a `" + from.Name + "` where a `" + to.Name + "` is expected is only supported in a plain statement, not inside a condition or initialiser. Do the call on its own line and keep the result in a local.");
    var list = (ArgumentListSyntax)a.Parent;
    var parts = new List<ExpressionSyntax>();
    foreach (var sib in list.Arguments) parts.Add(sib.Expression);
    if (list.Parent is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax rma) parts.Add(rma.Expression);
    if (!parts.All(x => x == a.Expression || IsPure(x) || text.StartsWith("_t")))
      Refuse(a, "the upcast of `" + from.Name + "` to `" + to.Name + "` would be evaluated before the other arguments, which changes what C# does. Assign it to a local first.");
    string name = "_u" + (++tmp);
    hoisted.Add(TypeName(to, a) + " &" + name + " = *(" + TypeName(to, a) + " *)&" + text + ";");
    return name;
  }

  /** `f(new T(x))`: Crust passes a reference as a pointer, so the argument needs an address.
      Declare a named local first -- only when that cannot change what C# evaluates, and in what order. */
  string Hoist(ArgumentSyntax a, ITypeSymbol at)
  {
    var call = a.Parent.Parent;
    string why = "a temporary object passed by reference has no address in Crust. Assign it to a local first and pass that.";
    if (!canHoist)
      Refuse(a, why);
    var parts = new List<ExpressionSyntax>();
    ArgumentListSyntax list = (ArgumentListSyntax)a.Parent;
    foreach (var sib in list.Arguments) if (sib != a) parts.Add(sib.Expression);
    if (call is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax rma) parts.Add(rma.Expression);
    var inner = a.Expression;
    while (inner is ParenthesizedExpressionSyntax pp) inner = pp.Expression;
    ArgumentListSyntax innerArgs = (inner as ObjectCreationExpressionSyntax)?.ArgumentList
                                   ?? (inner as ImplicitObjectCreationExpressionSyntax)?.ArgumentList
                                   ?? (inner as InvocationExpressionSyntax)?.ArgumentList;
    if (innerArgs != null) foreach (var ia in innerArgs.Arguments) parts.Add(ia.Expression);
    if (inner is InvocationExpressionSyntax) Refuse(a, "the result of a call passed by reference is not supported by CC# --crust yet. Assign it to a local first and pass that.");
    if (!parts.All(IsPure))
      Refuse(a, "a temporary object with side effects in this call would be built before the other arguments are evaluated, which changes what C# does. Assign it to a local first and pass that.");
    string name = "_t" + (++tmp);
    string t = TypeName(at, a);
    var args = innerArgs == null ? new List<string>() : innerArgs.Arguments.Select(x => Arg(x)).ToList();
    hoisted.Add(args.Count == 0 ? t + " " + name + ";" : t + " " + name + "(" + string.Join(", ", args) + ");");
    return name;
  }

  /** no calls, assignments, ++/--, or constructions: evaluating it earlier or later gives the same value. */
  bool IsPure(ExpressionSyntax e)
  {
    return !e.DescendantNodesAndSelf().Any(n =>
      n is InvocationExpressionSyntax || n is AssignmentExpressionSyntax || n is ObjectCreationExpressionSyntax
      || n is ImplicitObjectCreationExpressionSyntax || n is AwaitExpressionSyntax
      || (n is PostfixUnaryExpressionSyntax pu && (pu.IsKind(SyntaxKind.PostIncrementExpression) || pu.IsKind(SyntaxKind.PostDecrementExpression)))
      || (n is PrefixUnaryExpressionSyntax pr && (pr.IsKind(SyntaxKind.PreIncrementExpression) || pr.IsKind(SyntaxKind.PreDecrementExpression))));
  }

  /** an expression used as a statement: property ++/--/op= become a setter call. */
  string ExprStmt(ExpressionSyntax e)
  {
    return Expr(e, statement: true);
  }

  /** An expression's C++, with the cast C needs where C# converts an arena reference to the arena class it derives from (`Component c = mover;`, `Add(mover)`,
      `return mover;` from a method of `Component`): the two pointers are one address, but C types them apart, and newer C compilers refuse the implicit conversion. */
  HashSet<SyntaxNode> inConversion = new HashSet<SyntaxNode>();

  string Expr(ExpressionSyntax e, bool statement = false, bool recv = false)
  {
    //an implicit conversion the program (or the Unity types) declares, `Vector3 p = new Vector2(1f, 2f)`: a call of the operator's static method
    if (!statement && !recv && !(e is LiteralExpressionSyntax) && !inConversion.Contains(e)) {
      var cv = model.GetConversion(e);
      if (cv.IsUserDefined && cv.IsImplicit && cv.MethodSymbol != null && !IsLib(cv.MethodSymbol)) {
        inConversion.Add(e);                                  //(its operand is e itself: not converted again)
        try { return UserOperator(e, cv.MethodSymbol, e); } finally { inConversion.Remove(e); }
      }
    }
    string s = ExprRaw(e, statement, recv);
    if (statement || recv || e is LiteralExpressionSyntax) return s;
    var ti = model.GetTypeInfo(e);
    if (ti.Type is INamedTypeSymbol from && ti.ConvertedType is INamedTypeSymbol to && IsArena(from) && IsArena(to)
        && !SymbolEqualityComparer.Default.Equals(from.OriginalDefinition, to.OriginalDefinition) && DerivesFrom(from, to))
      return "((" + TypeName(to) + ")(" + s + "))";
    return s;
  }

  bool DerivesFrom(INamedTypeSymbol t, INamedTypeSymbol baseType) { return ArenaClass.DerivesFrom(t, baseType); }

  string ExprRaw(ExpressionSyntax e, bool statement = false, bool recv = false)
  {
    if (!statement && !recv && IsFluentCall(e))
      Refuse(e, "the result of a method that returns `this` can only start a chain or be dropped: storing it would alias the object, and a class is single-owner here. Call the methods on the original variable.");
    //string-typed expressions: a named fastring (building it first when it is a composite)
    if (!(e is LiteralExpressionSyntax nl && nl.IsKind(SyntaxKind.NullLiteralExpression)) && !(e is AssignmentExpressionSyntax)) {
      var sti = model.GetTypeInfo(e).Type;
      if (sti != null && sti.SpecialType == SpecialType.System_String && !IsStringLvalue(e))
        return StrNamed(e);
    }
    //constants fold exactly as C# folds them
    var cv = model.GetConstantValue(e);
    if (cv.HasValue && !(e is LiteralExpressionSyntax le0 && le0.IsKind(SyntaxKind.NullLiteralExpression)))
      return Constant(e, cv.Value);

    switch (e) {
      case ParenthesizedExpressionSyntax p: return "(" + Expr(p.Expression) + ")";
      case LiteralExpressionSyntax l:
        if (l.IsKind(SyntaxKind.NullLiteralExpression)) {
          if (NullIsArena(l)) return "NULL";
          Refuse(l, "`null` is not in the Crust C# subset for owned classes: a class is a value, not a reference. Use an arena class `[MaxInstances(N)]` for references.");
        }
        return l.Token.Text;
      case IdentifierNameSyntax id: return IdentifierExpr(id);
      case GenericNameSyntax gn: return IdentifierExpr(gn);
      case ThisExpressionSyntax _: return IsArena(currentType) ? "this" : "(*this)";
      case BaseExpressionSyntax b:
        Refuse(b, "`base.M()` is not in the Crust C# subset. Call a shared protected method, or restructure.");
        return "";
      case MemberAccessExpressionSyntax ma: return MemberAccess(ma);
      case InvocationExpressionSyntax inv: return Invocation(inv);
      case ObjectCreationExpressionSyntax oc: return NewExpr(oc);
      case ImplicitObjectCreationExpressionSyntax ioc: return NewExpr(ioc);
      case ArrayCreationExpressionSyntax ac: return NewArray(ac);
      case ElementAccessExpressionSyntax ea:
        if (ea.ArgumentList.Arguments.Count != 1) Refuse(ea, "multi-index element access is not in the Crust C# subset.");
        if (model.GetTypeInfo(ea.Expression).Type.SpecialType == SpecialType.System_String)
          Refuse(ea, "`s[i]` is a `char`, which is not in the Crust C# subset (a C# char is UTF-16). Compare substrings: `s.Substring(i, 1) == \"x\"`.");
        return Expr(ea.Expression) + "[" + Expr(ea.ArgumentList.Arguments[0].Expression) + "]";
      case BinaryExpressionSyntax b: return Binary(b);
      case PrefixUnaryExpressionSyntax pu: return Prefix(pu, statement);
      case PostfixUnaryExpressionSyntax po: return Postfix(po, statement);
      case AssignmentExpressionSyntax asg: return Assign(asg, statement);
      case ConditionalExpressionSyntax c:
        return "(" + Expr(c.Condition) + " ? " + Expr(c.WhenTrue) + " : " + Expr(c.WhenFalse) + ")";
      case CastExpressionSyntax ce: return Cast(ce);
      case DefaultExpressionSyntax de: return DefaultOf(model.GetTypeInfo(de.Type).Type, de);
      case InterpolatedStringExpressionSyntax isx:
        Refuse(isx, "`$\"..\"` is only supported as the argument of Console.Write/WriteLine: Crust has no managed string.");
        break;
      case IsPatternExpressionSyntax ip:
        Refuse(ip, "`is` patterns are not in the Crust C# subset. Compare values with `==`.");
        break;
      case TypeOfExpressionSyntax to:
        Refuse(to, "`typeof` is not in the Crust C# subset: there is no reflection.");
        break;
      case AwaitExpressionSyntax aw:
        Refuse(aw, "`async` / `await` is not in the Crust C# subset.");
        break;
      case SimpleLambdaExpressionSyntax sl:
        Refuse(sl, "lambdas are not supported by CC# --crust yet.");
        break;
      case ParenthesizedLambdaExpressionSyntax pl:
        Refuse(pl, "lambdas are not supported by CC# --crust yet.");
        break;
      case QueryExpressionSyntax q:
        Refuse(q, "LINQ is not in the Crust C# subset. Write a loop.");
        break;
      case ThrowExpressionSyntax te:
        Refuse(te, "`throw` is not supported by CC# --crust yet.");
        break;
      case SwitchExpressionSyntax sx:
        Refuse(sx, "`switch` expressions are not in the Crust C# subset. Use a `switch` statement.");
        break;
      case StackAllocArrayCreationExpressionSyntax sa:
        Refuse(sa, "`stackalloc` is not in the Crust C# subset.");
        break;
      case ImplicitArrayCreationExpressionSyntax ia:
        Refuse(ia, "array initializers are not in the Crust C# subset yet. Allocate with `new T[n]` and assign the elements.");
        break;
      case InitializerExpressionSyntax ie:
        Refuse(ie, "array / collection initializers are not in the Crust C# subset yet. Allocate with `new T[n]` and assign the elements.");
        break;
    }
    Refuse(e, "expression `" + e.Kind() + "` is not in the Crust C# subset.");
    return "";
  }

  // ------------------------------------------------------------------ strings

  /** a string already in a variable: reading it needs no temporary. */
  bool IsStringLvalue(ExpressionSyntax e)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    var cv = model.GetConstantValue(e);
    if (cv.HasValue) return false;                                   //literals and const strings
    var sym = model.GetSymbolInfo(e).Symbol;
    if (e is IdentifierNameSyntax || e is MemberAccessExpressionSyntax) {
      if (sym is ILocalSymbol) return true;
      if (sym is IParameterSymbol) return true;
      if (sym is IFieldSymbol fs) return fs.ContainingType.SpecialType != SpecialType.System_String;   //string.Empty is not
      return false;                                                   //a property is a call
    }
    if (e is ElementAccessExpressionSyntax ea) {
      var rt = model.GetTypeInfo(ea.Expression).Type;
      if (IsMainArgs(ea.Expression)) return false;                    //`const char *` element: copied into a fastring
      return rt is IArrayTypeSymbol || IsList(rt);
    }
    return false;
  }

  bool IsMainArgs(ExpressionSyntax e)
  {
    var ps = model.GetSymbolInfo(e).Symbol as IParameterSymbol;
    return ps != null && entryMethod != null && SymbolEqualityComparer.Default.Equals(ps.ContainingSymbol, entryMethod)
           && ps.Type is IArrayTypeSymbol;
  }

  /** a call / property read returning a string, which cpprust accepts directly as an initialiser. */
  bool IsStringCall(ExpressionSyntax e)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    if (model.GetConstantValue(e).HasValue) return false;
    if (e is InvocationExpressionSyntax) return true;
    if (e is MemberAccessExpressionSyntax ma && model.GetSymbolInfo(ma).Symbol is IPropertySymbol) return true;
    return false;
  }
  string CallOrName(ExpressionSyntax e)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    if (e is InvocationExpressionSyntax inv) return Invocation(inv);
    if (e is MemberAccessExpressionSyntax ma) return MemberAccess(ma);
    return Expr(e);
  }

  string NewTemp(string prefix) { return prefix + (++tmp); }

  /** Hoisting a statement ahead of the current one is only sound when nothing else in the statement is
      evaluated before it and could be affected: every impure node must enclose it or be inside it. */
  /** A temporary that cannot change what the statement does wherever it is built (a literal, a zero): it only needs a statement to be put before. */
  void RequireHoistPlace(ExpressionSyntax e, string what)
  {
    if (!canHoist)
      Refuse(e, what + " needs a temporary, which is only supported in a plain statement, not inside a condition or loop header. Build it on its own line first.");
  }

  void RequireHoist(ExpressionSyntax e, string what)
  {
    if (!canHoist)
      Refuse(e, what + " needs a temporary, which is only supported in a plain statement, not inside a condition or loop header. Build it on its own line first.");
    SyntaxNode scope = e.FirstAncestorOrSelf<StatementSyntax>();
    if (scope == null) return;
    //an `if` condition or a `switch` subject is evaluated first and once; its bodies are not part of it
    if (scope is IfStatementSyntax ifs && ifs.Condition.Span.Contains(e.Span)) scope = ifs.Condition;
    else if (scope is SwitchStatementSyntax sw && sw.Expression.Span.Contains(e.Span)) scope = sw.Expression;
    //an impure part that is evaluated only conditionally (the right of &&, ||, ??, or an arm of ?:) must not be
    //hoisted: it would run unconditionally
    if (IsImpure(e)) {
      SyntaxNode child = e;
      for (var par = e.Parent; par != null && par != scope.Parent; child = par, par = par.Parent) {
        if ((par is BinaryExpressionSyntax pb && (pb.IsKind(SyntaxKind.LogicalAndExpression) || pb.IsKind(SyntaxKind.LogicalOrExpression) || pb.IsKind(SyntaxKind.CoalesceExpression)) && pb.Right == child)
            || (par is ConditionalExpressionSyntax pc && (pc.WhenTrue == child || pc.WhenFalse == child)))
          Refuse(e, what + " has side effects and is only conditionally evaluated here (right of `&&` / `||`, or an arm of `?:`): hoisting it would run it unconditionally. Evaluate it into a local first.");
        if (par == scope) break;
      }
    }
    foreach (var n in scope.DescendantNodesAndSelf()) {
      if (!IsImpureNode(n)) continue;
      if (e.Span.Contains(n.Span) || n.Span.Contains(e.Span)) continue;
      if (hoistRoot != null && hoistRoot.Span.Contains(n.Span)) continue;
      Refuse(e, what + " would be built before the rest of this statement is evaluated, which changes what C# does (" + n.ToString().Split('\n')[0].Trim() + " has a side effect). Build it on its own line first.");
    }
  }

  // ------------------------------------------------------------------ evaluation order
  //
  // C# evaluates call arguments and binary operands strictly left to right.  C leaves the order unspecified, so
  // wherever one part could observe or change what another does, the parts are evaluated into named temporaries,
  // in order, and C is handed only names.  (That also keeps cpprust away from calls nested in the arguments of a
  // free-function call, which it does not lower.)  Where that cannot be done -- a loop condition -- it is refused.

  bool IsImpure(SyntaxNode n) { return n.DescendantNodesAndSelf().Any(IsImpureNode); }

  static bool IsLocalLike(ISymbol sym)
  {
    return sym is ILocalSymbol || (sym is IParameterSymbol p && p.RefKind == RefKind.None);
  }

  /** does evaluating `n` read something other than locals and constants (a field, an element, a ref parameter, a property)? */
  bool ReadsHeap(SyntaxNode n)
  {
    foreach (var x in n.DescendantNodesAndSelf()) {
      if (x is ElementAccessExpressionSyntax) return true;
      if (x is IdentifierNameSyntax id) {
        var sym = model.GetSymbolInfo(id).Symbol;
        if (sym is IFieldSymbol f && !f.IsConst && f.ContainingType.TypeKind != TypeKind.Enum) return true;
        if (sym is IPropertySymbol) return true;
        if (sym is IParameterSymbol p && p.RefKind != RefKind.None) return true;
      }
    }
    return false;
  }

  HashSet<ISymbol> LocalReads(SyntaxNode n)
  {
    var set = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
    foreach (var x in n.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
      var sym = model.GetSymbolInfo(x).Symbol;
      if (IsLocalLike(sym)) set.Add(sym);
    }
    return set;
  }

  /** the locals `n` assigns, increments or passes by ref/out; and whether it can change anything else. */
  void Writes(SyntaxNode n, HashSet<ISymbol> locals, out bool heap)
  {
    heap = false;
    foreach (var x in n.DescendantNodesAndSelf()) {
      ExpressionSyntax target = null;
      if (x is AssignmentExpressionSyntax a) target = a.Left;
      else if (x is PrefixUnaryExpressionSyntax pr && (pr.IsKind(SyntaxKind.PreIncrementExpression) || pr.IsKind(SyntaxKind.PreDecrementExpression))) target = pr.Operand;
      else if (x is PostfixUnaryExpressionSyntax po && (po.IsKind(SyntaxKind.PostIncrementExpression) || po.IsKind(SyntaxKind.PostDecrementExpression))) target = po.Operand;
      else if (x is ArgumentSyntax ag && ag.RefKindKeyword.RawKind != 0) target = ag.Expression;
      else if (x is InvocationExpressionSyntax || x is ObjectCreationExpressionSyntax || x is ImplicitObjectCreationExpressionSyntax) heap = true;
      if (target == null) continue;
      var t = Unparen(target);
      var sym = t is IdentifierNameSyntax ? model.GetSymbolInfo(t).Symbol : null;
      if (IsLocalLike(sym)) locals.Add(sym); else heap = true;
    }
  }

  /** could evaluating `later` change what `earlier` sees (or do two side effects need their order)? */
  bool Conflicts(ExpressionSyntax earlier, ExpressionSyntax later)
  {
    if (!IsImpure(later)) return false;                  //a pure later part cannot change anything
    if (model.GetConstantValue(earlier).HasValue) return false;
    if (IsImpure(earlier)) return true;                  //two side effects: their order is observable
    var writes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
    Writes(later, writes, out bool heapWrite);
    if (heapWrite && ReadsHeap(earlier)) return true;
    return LocalReads(earlier).Overlaps(writes);
  }

  bool ArgsNeedOrder(IReadOnlyList<ArgumentSyntax> args)
  {
    for (int i = 0; i < args.Count; i++)
      for (int j = i + 1; j < args.Count; j++)
        if (Conflicts(args[i].Expression, args[j].Expression)) return true;
    return false;
  }

  /** Argument texts in C# order.  When the arguments conflict they are evaluated into temporaries, left to right.
      `skip(i)` leaves an argument alone (a literal a template only uses as a `const char *`). */
  string[] ArgTexts(IReadOnlyList<ArgumentSyntax> args, ExpressionSyntax call, Func<int, bool> skip = null)
  {
    var texts = new string[args.Count];
    bool order = ArgsNeedOrder(args);
    var saveRoot = hoistRoot;
    if (order) {
      RequireHoist(call, "arguments with side effects");
      if (hoistRoot == null || !hoistRoot.Span.Contains(call.Span)) hoistRoot = call;   //never narrow an enclosing root
    }
    try {
      for (int i = 0; i < args.Count; i++) {
        if (skip != null && skip(i)) continue;
        string t = Arg(args[i]);
        if (order) {
          var ty = model.GetTypeInfo(args[i].Expression).Type;
          bool byRef = args[i].RefKindKeyword.RawKind != 0;
          if (!byRef && ty != null && !Borrowed(ty) && ty.TypeKind != TypeKind.Interface && !model.GetConstantValue(args[i].Expression).HasValue) {
            string tn = NewTemp("_a");
            hoisted.Add(TypeName(ty, args[i]) + " " + tn + " = " + t + ";");
            t = tn;
          }
        }
        texts[i] = t;
      }
    } finally { hoistRoot = saveRoot; }
    return texts;
  }

  bool IsImpureNode(SyntaxNode n)
  {
    return n is InvocationExpressionSyntax || n is AssignmentExpressionSyntax
      || n is ObjectCreationExpressionSyntax || n is ImplicitObjectCreationExpressionSyntax
      || (n is PostfixUnaryExpressionSyntax pu && (pu.IsKind(SyntaxKind.PostIncrementExpression) || pu.IsKind(SyntaxKind.PostDecrementExpression)))
      || (n is PrefixUnaryExpressionSyntax pr && (pr.IsKind(SyntaxKind.PreIncrementExpression) || pr.IsKind(SyntaxKind.PreDecrementExpression)));
  }

  static string CLit(string s) { return CEscape(s).Replace("%%", "%"); }

  /** flatten `a + b + (c + d)` and `$"..{x}.."` into its operands, in evaluation order. */
  void StringParts(ExpressionSyntax e, List<object> parts)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    var cv = model.GetConstantValue(e);
    if (cv.HasValue && cv.Value is string cs) { parts.Add(cs); return; }
    if (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression)
        && model.GetTypeInfo(b).Type.SpecialType == SpecialType.System_String) {
      StringParts(b.Left, parts); StringParts(b.Right, parts); return;
    }
    if (e is InterpolatedStringExpressionSyntax isx) {
      foreach (var c in isx.Contents) {
        if (c is InterpolatedStringTextSyntax tx) parts.Add(tx.TextToken.ValueText);
        else if (c is InterpolationSyntax ip) {
          if (ip.AlignmentClause != null || ip.FormatClause != null)
            Refuse(ip, "alignment / format specifiers in `$\"..\"` are not in the Crust C# subset.");
          StringParts(ip.Expression, parts);
        }
      }
      return;
    }
    parts.Add(e);
  }

  /** Append one operand of a concatenation to the fastring `t`. */
  void AppendPart(string t, object part)
  {
    if (part is string lit) { if (lit.Length > 0) hoisted.Add(t + ".append_cstr(\"" + CLit(lit) + "\");"); return; }
    var e = (ExpressionSyntax)part;
    var ty = model.GetTypeInfo(e).Type;
    if (ty != null && ty.TypeKind == TypeKind.Enum)
      Refuse(e, "a string made from an enum is not in the Crust C# subset (no `ToString`). Use `(int)value`.");
    switch (ty == null ? SpecialType.None : ty.SpecialType) {
      case SpecialType.System_String:
        hoisted.Add(t + ".append_str(" + Expr(e) + ");");   //Expr yields a named fastring
        return;
      case SpecialType.System_Boolean:
        hoisted.Add(t + ".append_cstr((" + Expr(e) + ") ? \"True\" : \"False\");"); return;
      case SpecialType.System_Byte: case SpecialType.System_SByte: case SpecialType.System_Int16:
      case SpecialType.System_UInt16: case SpecialType.System_Int32: case SpecialType.System_Int64:
        hoisted.Add(t + ".append_int((long long)(" + Expr(e) + "));"); return;
      case SpecialType.System_UInt32: case SpecialType.System_UInt64:
        hoisted.Add(t + ".append_uint((unsigned long long)(" + Expr(e) + "));"); return;
      case SpecialType.System_Single: case SpecialType.System_Double:
        Refuse(e, "a string made from a float / double is not exact in the Crust C# subset: C# prints the shortest round-trip form. Use scaled integers.");
        return;
      case SpecialType.System_Char:
        Refuse(e, "`char` is not in the Crust C# subset.");
        return;
    }
    Refuse(e, "a string made from `" + (ty == null ? "?" : ty.ToDisplayString()) + "` is not in the Crust C# subset. Use ints, bools and strings.");
  }

  /** The value of a string expression as a *named* fastring, built into a temporary when it is a composite. */
  string StrNamed(ExpressionSyntax e)
  {
    UsesString = true;
    var inner = e;
    while (inner is ParenthesizedExpressionSyntax pp) inner = pp.Expression;
    //Main's string[] args hold `const char *`
    if (inner is ElementAccessExpressionSyntax ea && IsMainArgs(ea.Expression)) {
      RequireHoist(e, "an element of `args`");
      string a = NewTemp("_s");
      //cpprust does not lower `v[i]` inside the arguments of a static call: read the element into a plain name first
      string cp = NewTemp("_p");
      hoisted.Add("const char *" + cp + " = " + Expr(ea.Expression) + "[" + Expr(ea.ArgumentList.Arguments[0].Expression) + "];");
      hoisted.Add("fastring " + a + " = fastring::from_cstr(" + cp + ");");
      return a;
    }
    var parts = new List<object>();
    StringParts(inner, parts);
    bool concat = inner is InterpolatedStringExpressionSyntax || (inner is BinaryExpressionSyntax)
                  || (model.GetConstantValue(inner).HasValue);
    RequireHoist(e, "building this string");
    string t = NewTemp("_s");
    if (concat) {
      hoisted.Add("fastring " + t + ";");
      var saveRoot = hoistRoot; hoistRoot = inner;
      try { foreach (var part in parts) AppendPart(t, part); } finally { hoistRoot = saveRoot; }
      return t;
    }
    //a call, a property read, string.Empty ...
    if (inner is MemberAccessExpressionSyntax ma0 && model.GetSymbolInfo(ma0).Symbol is IFieldSymbol f0 && f0.ContainingType.SpecialType == SpecialType.System_String) {
      hoisted.Add("fastring " + t + ";");           //string.Empty
      return t;
    }
    string call;
    switch (inner) {
      case InvocationExpressionSyntax inv: call = Invocation(inv); break;
      case MemberAccessExpressionSyntax ma: call = MemberAccess(ma); break;
      case IdentifierNameSyntax id: call = IdentifierExpr(id); break;
      case ConditionalExpressionSyntax _:
        Refuse(e, "`?:` producing a string is not in the Crust C# subset. Use an `if` that assigns the string.");
        return "";
      default:
        Refuse(e, "this string expression (`" + inner.Kind() + "`) is not in the Crust C# subset.");
        return "";
    }
    hoisted.Add("fastring " + t + " = " + call + ";");
    return t;
  }

  /** a `const char *` for printf / strcmp: a literal as written, a variable's c_str(), else a built temporary. */
  string StrCText(ExpressionSyntax e)
  {
    var inner = e;
    while (inner is ParenthesizedExpressionSyntax pp) inner = pp.Expression;
    var cv = model.GetConstantValue(inner);
    if (cv.HasValue && cv.Value is string s) return "\"" + CLit(s) + "\"";
    UsesString = true;
    return (IsStringLvalue(inner) ? Expr(inner) : StrNamed(inner)) + ".c_str()";
  }

  string StrCompare(BinaryExpressionSyntax b)
  {
    UsesStrcmp = true;
    string l = StrCText(b.Left);
    string r = StrCText(b.Right);
    return "(strcmp(" + l + ", " + r + ") " + (b.OperatorToken.Text == "==" ? "==" : "!=") + " 0)";
  }

  string Constant(ExpressionSyntax e, object v)
  {
    var ti = model.GetTypeInfo(e);
    var t = ti.ConvertedType ?? ti.Type;
    //an enum member keeps its name
    var sym = model.GetSymbolInfo(e).Symbol;
    if (sym is IFieldSymbol fs && fs.ContainingType != null && fs.ContainingType.TypeKind == TypeKind.Enum)
      return FlatName(fs.ContainingType) + "_" + Ident(fs.Name);
    if (t != null && t.TypeKind == TypeKind.Enum)
      return "(" + FlatName((INamedTypeSymbol)t) + ")" + Convert.ToInt64(v);
    if (v is bool b) return b ? "true" : "false";
    if (v is string) Refuse(e, "internal: a string constant must go through StrNamed.");
    if (v is char) Refuse(e, "`char` is not in the Crust C# subset. Use `byte` (or `int` for a code point).");
    if (v is int i) return i == int.MinValue ? "(-2147483647 - 1)" : i.ToString();
    if (v is uint u) return u + "U";
    if (v is long l) return l == long.MinValue ? "(-9223372036854775807LL - 1)" : l + "LL";
    if (v is ulong ul) return ul + "ULL";
    if (v is short sh) return sh.ToString();
    if (v is ushort us) return us.ToString();
    if (v is byte by) return by.ToString();
    if (v is sbyte sb) return sb.ToString();
    if (v is double d) return Dbl(d);
    if (v is float f) return Flt(f);
    Refuse(e, "constant of type `" + v.GetType().Name + "` is not in the Crust C# subset.");
    return "";
  }

  static string Dbl(double d)
  {
    if (double.IsNaN(d)) return "(0.0 / 0.0)";
    if (double.IsPositiveInfinity(d)) return "(1.0 / 0.0)";
    if (double.IsNegativeInfinity(d)) return "(-1.0 / 0.0)";
    string s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
    return s;
  }
  static string Flt(float f)
  {
    if (float.IsNaN(f)) return "(0.0f / 0.0f)";
    if (float.IsPositiveInfinity(f)) return "(1.0f / 0.0f)";
    if (float.IsNegativeInfinity(f)) return "(-1.0f / 0.0f)";
    string s = f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
    return s + "f";
  }

  string DefaultOf(ITypeSymbol t, SyntaxNode at)
  {
    string d = DefaultInit(t);
    if (d != null) return d;
    if (t.IsValueType) return TypeName(t, at) + "()";
    Refuse(at, "`default(" + t.Name + ")` is `null` for a class, which is not in the Crust C# subset.");
    return "";
  }

  string IdentifierExpr(SimpleNameSyntax id)
  {
    var sym = model.GetSymbolInfo(id).Symbol;
    if (sym is IPropertySymbol ps) {
      string recv = ps.IsStatic ? FlatName(ps.ContainingType) + "::" : "";
      return recv + "get_" + Ident(ps.Name) + "()";
    }
    if (sym is IFieldSymbol fs && fs.IsStatic && fs.ContainingType.TypeKind != TypeKind.Enum)
      return StaticField(fs);
    if (sym is IMethodSymbol ms && ms.IsStatic)
      return FlatName(ms.ContainingType) + "::" + Ident(ms.Name) + TypeArgs(ms, id);
    if (sym is INamedTypeSymbol nt) return FlatName(nt);
    if (sym is IMethodSymbol gm && gm.IsGenericMethod) return Ident(id.Identifier.Text) + TypeArgs(gm, id);
    return Ident(id.Identifier.Text);
  }

  string MemberAccess(MemberAccessExpressionSyntax ma)
  {
    var sym = model.GetSymbolInfo(ma).Symbol;
    var recvType = model.GetTypeInfo(ma.Expression).Type;

    //arrays are std::vector: their one property is built in
    if (recvType is IArrayTypeSymbol && ma.Name.Identifier.Text == "Length")
      return Expr(ma.Expression) + ".size()";

    if (sym is IPropertySymbol lps && IsLib(lps)) return CorelibProperty(ma, lps);
    if (sym is IFieldSymbol lfs && IsLib(lfs) && lfs.ContainingType.TypeKind != TypeKind.Enum) RefuseUnimplemented(ma, lfs);

    if (sym is IPropertySymbol ps) {
      if (ps.IsStatic) return FlatName(ps.ContainingType) + "::get_" + Ident(ps.Name) + "()";
      return Receiver(ma.Expression) + "get_" + Ident(ps.Name) + "()";
    }
    if (sym is IFieldSymbol fs) {
      if (fs.ContainingType.TypeKind == TypeKind.Enum)
        return FlatName(fs.ContainingType) + "_" + Ident(fs.Name);
      if (fs.IsStatic) return StaticField(fs);
      return Receiver(ma.Expression) + Ident(fs.Name);
    }
    if (sym is IMethodSymbol ms) {
      if (ms.IsStatic) return FlatName(ms.ContainingType) + "::" + Ident(ms.Name) + TypeArgs(ms, ma);
      return Receiver(ma.Expression) + Ident(ms.Name) + TypeArgs(ms, ma);
    }
    if (sym is INamedTypeSymbol nt) return FlatName(nt);
    if (sym is INamespaceSymbol) return "";
    Refuse(ma, "member access `" + ma + "` is not in the Crust C# subset.");
    return "";
  }

  /** Inside its own class a static field is a bare name; elsewhere it is `Cls::Field`. */
  string StaticField(IFieldSymbol fs)
  {
    if (currentType != null && SymbolEqualityComparer.Default.Equals(currentType, fs.ContainingType))
      return Ident(fs.Name);
    return FlatName(fs.ContainingType) + "::" + Ident(fs.Name);
  }

  string Receiver(ExpressionSyntax e)
  {
    if (e is ThisExpressionSyntax) return "this->";
    if (IsFluentCall(e)) return Expr(e, recv: true) + "->";
    if (IsArena(model.GetTypeInfo(e).Type)) return Expr(e) + "->";       //a reference to an arena object is a pointer
    return Expr(e) + ".";
  }

  string Invocation(InvocationExpressionSyntax inv)
  {
    var sym = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
    if (sym == null) Refuse(inv, "call `" + inv + "` could not be resolved.");

    // Console
    if (sym.ContainingType != null && sym.ContainingType.ToDisplayString() == "System.Console")
      return ConsoleCall(inv, sym);

    // the corelib: a [Cpp] template says how the call is spelled; a declaration without one is not implemented
    string tpl = CppTemplate(sym);
    if (tpl != null) return CorelibCall(inv, sym, tpl);
    if (IsLib(sym)) RefuseUnimplemented(inv, sym);
    //`Arena.Free(obj)`: an arena object given back, its slot the next `new` takes.  The class is found by name (as MaxInstances is), so a program or the
    //engine declares its own: `static class Arena { public static void Free<T>(T o) where T : Base { } }`.  Its body is for .NET, where nothing is freed.
    if (sym.Name == "Free" && sym.IsGenericMethod && sym.IsStatic && sym.ContainingType != null && sym.ContainingType.Name == "Arena"
        && sym.Parameters.Length == 1 && inv.ArgumentList.Arguments.Count == 1) {
      var freed = model.GetTypeInfo(inv.ArgumentList.Arguments[0].Expression).Type as INamedTypeSymbol;
      if (freed == null || !IsArena(freed)) Refuse(inv, "Arena.Free needs an arena object.");
      return FlatName(freed) + "__free(" + Expr(inv.ArgumentList.Arguments[0].Expression) + ")";
    }
    if (sym.IsGenericMethod && !sym.TypeParameters.All(ArenaConstrained))
      Refuse(inv, "generic methods are not in the Crust C# subset (except one whose type parameters are constrained to an arena class).");

    string callee = Expr(inv.Expression);
    var args = WithDefaults(ArgTexts(inv.ArgumentList.Arguments, inv), sym, inv);
    return callee + "(" + string.Join(", ", args) + ")";
  }

  /** `f(1)` for `void f(int a, int b = 5)`: the arguments the call leaves out are the declared defaults, which Roslyn knows, so the C declaration does
      not repeat them and they are written at each call. */
  List<string> WithDefaults(string[] given, IMethodSymbol sym, ExpressionSyntax call)
  {
    var all = given.ToList();
    if (sym == null || IsLib(sym)) return all;
    for (int i = all.Count; i < sym.Parameters.Length; i++) all.Add(DefaultArg(sym.Parameters[i], call));
    return all;
  }

  /** the text of a parameter's default value */
  string DefaultArg(IParameterSymbol p, ExpressionSyntax call)
  {
    if (!p.HasExplicitDefaultValue) { Refuse(call, "the call leaves out `" + p.Name + "`, which has no default."); return ""; }
    var t = p.Type;
    object v = p.ExplicitDefaultValue;
    if (t.TypeKind == TypeKind.Enum) {
      long n = Convert.ToInt64(v);
      foreach (var m in t.GetMembers().OfType<IFieldSymbol>())
        if (m.HasConstantValue && Convert.ToInt64(m.ConstantValue) == n) return FlatName((INamedTypeSymbol)t) + "_" + Ident(m.Name);      //(an enum member keeps its name)
      return "(" + FlatName((INamedTypeSymbol)t) + ")" + n;
    }
    if (t.SpecialType == SpecialType.System_String) {
      if (v == null) { Refuse(call, "the default of `" + p.Name + "` is `null`: a string is never null in Crust. Use \"\" as the default."); return ""; }
      RequireHoistPlace(call, "the default string of `" + p.Name + "`");
      UsesString = true;
      string tmp = NewTemp("_s");
      hoisted.Add("fastring " + tmp + ";");
      AppendPart(tmp, (string)v);
      return tmp;
    }
    if (v == null) {
      if (IsArena(t)) return "NULL";                                    //a reference that refers to nothing
      if (t.IsValueType) {                                              //default(T): `T()` is not an expression in C, so it is a named local
        string zero = DefaultInit(t);
        if (zero != null) return zero;
        RequireHoistPlace(call, "the default value of `" + p.Name + "`");
        string tmp = NewTemp("_d");
        hoisted.Add(TypeName(t, call) + " " + tmp + ";");
        return tmp;
      }
      Refuse(call, "the default of `" + p.Name + "` is `null` for `" + t.Name + "`, a class that is a value in Crust: there is no null for it. Use an arena class, or write an overload.");
      return "";
    }
    if (v is bool b) return b ? "true" : "false";
    if (v is char) { Refuse(call, "the default of `" + p.Name + "` is a `char`, which is not in the Crust C# subset. Use `byte`."); return ""; }
    if (v is int i) return i == int.MinValue ? "(-2147483647 - 1)" : i.ToString();
    if (v is uint u) return u + "U";
    if (v is long l) return l == long.MinValue ? "(-9223372036854775807LL - 1)" : l + "LL";
    if (v is ulong ul) return ul + "ULL";
    if (v is short sh) return sh.ToString();
    if (v is ushort us) return us.ToString();
    if (v is byte by) return by.ToString();
    if (v is sbyte sb) return sb.ToString();
    if (v is double d) return Dbl(d);
    if (v is float f) return Flt(f);
    Refuse(call, "the default of `" + p.Name + "` (" + v.GetType().Name + ") is not in the Crust C# subset.");
    return "";
  }

  static int CountOf(string text, string what)
  {
    int n = 0, i = 0;
    while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
    return n;
  }

  /** A [Cpp(Helper = "..")] member needs a C++ function: written once for each type it is used with ({T0} is the type, {M0} the type as part of a name), as a
      piece of its own.  It is not in the preamble, which comes before any type of the program, because a helper that copies an element needs the element
      complete: the ordering puts it after the types it is written for, and before what calls it. */
  void UseHelper(string text, ITypeSymbol[] typeArgs, SyntaxNode at)
  {
    for (int i = 0; i < typeArgs.Length; i++) {
      string tn = TypeName(typeArgs[i], at);
      text = text.Replace("{T" + i + "}", tn).Replace("{M" + i + "}", Mangle(tn));
    }
    if (!helperPieces.TryGetValue(text, out var piece)) {
      piece = new CppPiece { Name = "helper." + helperPieces.Count + "_" + Mangle(text).Substring(0, Math.Min(24, Mangle(text).Length)), Text = text + "\n", IsHelper = true, Model = model };
      piece.NeedTypes.AddRange(typeArgs);
      helperPieces[text] = piece;
      newHelperPieces.Add(piece);
    }
    currentHelpers.Add(piece);
  }

  /** a call to a corelib member that has a [Cpp] template. */
  string CorelibCall(InvocationExpressionSyntax inv, IMethodSymbol sym, string tpl)
  {
    AddIncludes(sym);
    foreach (var a in sym.OriginalDefinition.GetAttributes())
      if (a.AttributeClass != null && a.AttributeClass.Name == "CppAttribute")
        foreach (var na in a.NamedArguments)
          if (na.Key == "Helper" && na.Value.Value is string helperText) UseHelper(helperText, sym.ContainingType.TypeArguments.ToArray(), inv);
    if (sym.IsGenericMethod) Refuse(inv, "generic methods are not in the Crust C# subset.");
    var saveRoot = hoistRoot;
    if (HasCppAttr(sym, "CppFluentAttribute") && hoistRoot == null) {
      //the calls of a chain are emitted strictly in order, so what is inside the chain is exempt from the reorder check
      ExpressionSyntax top = inv;
      while (top.Parent is MemberAccessExpressionSyntax pm && pm.Expression == top && pm.Parent is InvocationExpressionSyntax pi) top = pi;
      hoistRoot = top;
    }
    try {
      var ma = inv.Expression as MemberAccessExpressionSyntax;
      string recv = null;
      if (!sym.IsStatic) {
        if (ma == null) Refuse(inv, "`" + sym.Name + "` needs a receiver.");
        recv = RecvText(ma.Expression);
      }
      var args = inv.ArgumentList.Arguments;
      //a template that spells the receiver or an argument twice (`{this}.erase({this}.begin() + {0})`) evaluates it twice: only sound if that is invisible
      if (recv != null && CountOf(tpl, "{this}") > 1 && inv.Expression is MemberAccessExpressionSyntax rma && !IsPure(rma.Expression))
        Refuse(inv, "`" + sym.Name + "` uses its receiver twice in C, and `" + rma.Expression + "` has a side effect that would happen twice. Assign it to a local first.");
      for (int ai = 0; ai < args.Count; ai++)
        if (CountOf(tpl, "{" + ai + "}") + CountOf(tpl, "{" + ai + ":c}") > 1 && !IsPure(args[ai].Expression))
          Refuse(args[ai], "`" + sym.Name + "` uses argument " + ai + " twice in C, and `" + args[ai].Expression + "` has a side effect that would happen twice. Assign it to a local first.");
      //only the arguments the template spells as {N} are materialised: `{0:c}` of a literal needs no temporary
      var plain = new HashSet<int>();
      foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(tpl, @"\{(\d+)\}"))
        plain.Add(int.Parse(m.Groups[1].Value));
      var texts = ArgTexts(args, inv, i => !plain.Contains(i) && model.GetConstantValue(args[i].Expression).HasValue);
      for (int ai = 0; ai < args.Count; ai++) {
        var aty = model.GetTypeInfo(args[ai].Expression).Type;
        if (texts[ai] != null && aty != null && IsStructValue(aty) && IsUserOperatorUse(args[ai].Expression)) {
          RequireHoist(args[ai].Expression, "an operator result passed to `" + sym.Name + "`");
          string ot = NewTemp("_o");
          hoisted.Add(TypeName(aty, args[ai]) + " " + ot + " = " + texts[ai] + ";");
          texts[ai] = ot;
        }
      }
      return Expand(tpl, sym, recv, args, texts, sym.ContainingType.TypeArguments.ToArray(), inv);
    } finally { hoistRoot = saveRoot; }
  }

  /** a read of a corelib property that has a [Cpp] template. */
  string CorelibProperty(MemberAccessExpressionSyntax ma, IPropertySymbol ps)
  {
    string tpl = CppTemplate(ps);
    if (tpl == null) RefuseUnimplemented(ma, ps);
    AddIncludes(ps);
    string recv = ps.IsStatic ? null : RecvText(ma.Expression);
    return Expand(tpl, ps, recv, new List<ArgumentSyntax>(), new string[0], ps.ContainingType.TypeArguments.ToArray(), ma);
  }

  // ---- Console.Write / WriteLine -> printf

  string ConsoleCall(InvocationExpressionSyntax inv, IMethodSymbol sym)
  {
    string n = sym.Name;
    if (n != "Write" && n != "WriteLine")
      Refuse(inv, "`Console." + n + "` is not in the Crust C# subset. Crust provides Console.Write and Console.WriteLine.");
    string nl = n == "WriteLine" ? "\\n" : "";
    var args = inv.ArgumentList.Arguments;
    if (args.Count == 0) return "printf(\"" + nl + "\")";
    if (args.Count > 1)
      Refuse(inv, "`Console." + n + "` with a format string is not in the Crust C# subset. Use `$\"..{x}..\"` or several Write calls.");
    var fmt = new StringBuilder();
    var vals = new List<PVal>();
    PrintfPart(args[0].Expression, fmt, vals);
    fmt.Append(nl);
    //C leaves the order in which printf's arguments are evaluated unspecified, C# does not: when any value
    //has a side effect, snapshot every value into a temporary, left to right.  Strings that need building
    //are built at their own position in that order.
    bool ordered = vals.Count >= 2 && vals.Any(v => v.Impure);
    if (ordered || vals.Any(v => v.Deferred != null)) {
      var saveRoot = hoistRoot; hoistRoot = inv;
      try {
        foreach (var v in vals) {
          if (v.Deferred != null) { v.Text = StrNamed(v.Deferred) + ".c_str()"; v.Deferred = null; }
          if (ordered) {
            string tn = NewTemp("_v");
            hoisted.Add(v.CType + " " + tn + " = " + v.Text + ";");
            v.Text = tn;
          }
        }
      } finally { hoistRoot = saveRoot; }
    }
    string call = "printf(\"" + fmt + "\"";
    foreach (var v in vals) call += ", " + v.Text;
    return call + ")";
  }

  class PVal { public string Text, CType; public bool Impure; public ExpressionSyntax Deferred; }

  PVal Val(ExpressionSyntax node, string text, string ctype)
  {
    return new PVal { Text = text, CType = ctype, Impure = node.DescendantNodesAndSelf().Any(IsImpureNode) };
  }

  static string CEscape(string s)
  {
    var sb = new StringBuilder();
    foreach (char c in s) {
      switch (c) {
        case '%': sb.Append("%%"); break;
        case '\\': sb.Append("\\\\"); break;
        case '"': sb.Append("\\\""); break;
        case '\n': sb.Append("\\n"); break;
        case '\r': sb.Append("\\r"); break;
        case '\t': sb.Append("\\t"); break;
        case '\0': sb.Append("\\0"); break;
        default:
          if (c < 32 || c > 126) {
            foreach (byte b in Encoding.UTF8.GetBytes(c.ToString())) sb.Append("\\" + Convert.ToString(b, 8).PadLeft(3, '0'));
          } else sb.Append(c);
          break;
      }
    }
    return sb.ToString();
  }

  void PrintfPart(ExpressionSyntax e, StringBuilder fmt, List<PVal> vals)
  {
    while (e is ParenthesizedExpressionSyntax pe) e = pe.Expression;
    if (e is InterpolatedStringExpressionSyntax isx) {
      foreach (var part in isx.Contents) {
        if (part is InterpolatedStringTextSyntax tx) fmt.Append(CEscape(tx.TextToken.ValueText));
        else if (part is InterpolationSyntax ip) {
          if (ip.AlignmentClause != null || ip.FormatClause != null)
            Refuse(ip, "alignment / format specifiers in `$\"..\"` are not in the Crust C# subset.");
          PrintfPart(ip.Expression, fmt, vals);
        }
      }
      return;
    }
    var cv = model.GetConstantValue(e);
    if (cv.HasValue && cv.Value is string s) { fmt.Append(CEscape(s)); return; }
    var t = model.GetTypeInfo(e).Type;
    if (t != null && t.SpecialType == SpecialType.System_String) {
      if (e is BinaryExpressionSyntax sb && sb.IsKind(SyntaxKind.AddExpression)) {      //"a" + x: print the pieces in order
        PrintfPart(sb.Left, fmt, vals); PrintfPart(sb.Right, fmt, vals); return;
      }
      fmt.Append("%s");
      if (IsStringLvalue(e)) { UsesString = true; vals.Add(Val(e, Expr(e) + ".c_str()", "const char *")); }
      else vals.Add(new PVal { CType = "const char *", Impure = e.DescendantNodesAndSelf().Any(IsImpureNode), Deferred = e });
      return;
    }
    if (t != null && t.TypeKind == TypeKind.Enum)
      Refuse(e, "printing an enum is not in the Crust C# subset (no `ToString`). Print `(int)value`.");
    switch (t == null ? SpecialType.None : t.SpecialType) {
      case SpecialType.System_Boolean:
        fmt.Append("%s"); vals.Add(Val(e, "(" + Expr(e) + ") ? \"True\" : \"False\"", "const char *")); return;
      case SpecialType.System_Byte: case SpecialType.System_SByte:
      case SpecialType.System_Int16: case SpecialType.System_UInt16:
      case SpecialType.System_Int32:
        fmt.Append("%d"); vals.Add(Val(e, Expr(e), "int")); return;
      case SpecialType.System_UInt32: fmt.Append("%u"); vals.Add(Val(e, Expr(e), "unsigned")); return;
      case SpecialType.System_Int64: fmt.Append("%lld"); vals.Add(Val(e, Expr(e), "long long")); return;
      case SpecialType.System_UInt64: fmt.Append("%llu"); vals.Add(Val(e, Expr(e), "unsigned long long")); return;
      case SpecialType.System_Single: case SpecialType.System_Double:
        Refuse(e, "printing a float / double is not exact in the Crust C# subset: C# prints the shortest round-trip form, printf cannot. Print scaled integers.");
        return;
      case SpecialType.System_Char:
        Refuse(e, "`char` is not in the Crust C# subset.");
        return;
    }
    Refuse(e, "Console output of `" + (t == null ? "?" : t.ToDisplayString()) + "` is not in the Crust C# subset. Print ints, bools and string literals.");
  }

  // ---- object / array creation

  string NewExpr(ExpressionSyntax e)
  {
    var type = model.GetTypeInfo(e).Type;
    ArgumentListSyntax al = null;
    if (e is ObjectCreationExpressionSyntax oc) {
      al = oc.ArgumentList;
      if (oc.Initializer != null)
        Refuse(oc.Initializer, "object / collection initializers are not in the Crust C# subset. Assign the fields on the next lines.");
    } else if (e is ImplicitObjectCreationExpressionSyntax ioc) al = ioc.ArgumentList;
    var args = al == null ? new List<string>() : ArgTexts(al.Arguments, e).ToList();
    if (CppTemplate(type) == null) args = WithDefaults(args.ToArray(), model.GetSymbolInfo(e).Symbol as IMethodSymbol, e);
    if (IsArena(type)) return "new " + TemplateArg(type, e) + "(" + string.Join(", ", args) + ")";     //cpprust: T__alloc(..)
    if (CppTemplate(type) != null) return TypeName(type, e) + (CollectionCtor(type, al, e, args) is string c && c.Length > 0 ? c : "()");
    return TypeName(type, e) + "(" + string.Join(", ", args) + ")";
  }

  /** What follows the name in `new List<T>(..)`: nothing for an empty list, `(other)` for a copy.  A C++ `vector<T> v(n)` is n elements and a C# `new List<T>(n)`
      is an empty list with room for n, so the arguments are not passed on as they are:
        * an int is a capacity, a hint that changes nothing a program can see: the list is built empty (the int must have no side effect, which would be lost);
        * a List<T> is a copy of the elements, which is what C# does too when they are values (numbers, structs, enums, strings) or arena references; for an
          owned class C# would share the objects and a copy would not, so it is refused.
      Anything else is refused. */
  string CollectionCtor(ITypeSymbol type, ArgumentListSyntax al, SyntaxNode at, List<string> args)
  {
    if (args.Count == 0) return "";
    string def = type.OriginalDefinition.ToDisplayString();
    if (def != "System.Collections.Generic.List<T>" || args.Count != 1)
      Refuse(at, "`new " + type.Name + "(..)` with these arguments is not in the Crust C# subset. Construct it empty and fill it.");
    var arg = al.Arguments[0].Expression;
    var at2 = model.GetTypeInfo(arg).Type;
    if (at2 != null && at2.SpecialType == SpecialType.System_Int32) {
      if (!IsPure(arg)) Refuse(arg, "the capacity of a list is only a hint and is not used here, but `" + arg + "` has a side effect that would be lost. Assign it to a local first.");
      return "";
    }
    if (at2 != null && at2.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>") {
      var elem = ((INamedTypeSymbol)type).TypeArguments[0];
      if (IsOwnedClass(elem))
        Refuse(arg, "`new List<" + elem.Name + ">(list)` copies the elements here, and C# would share the objects: " + elem.Name + " is a class owned by its list in Crust. Add them in a loop, or make it an arena class.");
      return "(" + args[0] + ")";
    }
    Refuse(arg, "`new " + type.Name + "(..)` takes a capacity (an int) or another List<T> here. Construct it empty and fill it.");
    return "";
  }

  string NewArray(ArrayCreationExpressionSyntax ac)
  {
    if (ac.Initializer != null)
      Refuse(ac, "array initializers are not in the Crust C# subset yet. Allocate with `new T[n]` and assign the elements.");
    var at = (IArrayTypeSymbol)model.GetTypeInfo(ac).Type;
    if (at.Rank > 1) Refuse(ac, "multidimensional arrays are not in the Crust C# subset. Use a jagged array `T[][]`.");
    var elem = at.ElementType;
    if (IsArena(elem)) {
      TypeName(elem, ac);                                  //(names the class, for its forward declaration)
      return FlatName((INamedTypeSymbol)elem) + "::__new_array(" + Expr(ac.Type.RankSpecifiers[0].Sizes[0]) + ")";
    }
    string d = DefaultInit(elem);
    if (d == null)
      Refuse(ac, "`new T[n]` of a class or struct is not in the Crust C# subset: the elements would be null. Use a List<T> and Add, or an arena class.");
    string et = TypeName(elem, ac);
    string helper = "_cs_new_array_" + Mangle(et);
    if (arrayHelpers.Add(helper)) {
      string def = "static std::vector<" + et + "> " + helper + "(int n) { std::vector<" + et + "> v; int i = 0; if (n < 0) { abort(); } while (i < n) { v.push_back(" + d + "); i = i + 1; } return v; } ";
      preamble.Append(def);
    }
    return helper + "(" + Expr(ac.Type.RankSpecifiers[0].Sizes[0]) + ")";
  }

  static string Mangle(string s)
  {
    var sb = new StringBuilder();
    foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
    return sb.ToString();
  }

  // ---- operators

  string Binary(BinaryExpressionSyntax b)
  {
    string op = b.OperatorToken.Text;
    switch (b.Kind()) {
      case SyntaxKind.AsExpression:
      case SyntaxKind.IsExpression: {
        //between arena classes the object knows its type (cpprust --rtti: a descriptor in each vtable); `x is T` / `x as T` ask it
        var target = model.GetTypeInfo(b.Right).Type;
        var subject = model.GetTypeInfo(b.Left).Type;
        if (!IsArena(target) || !IsArena(subject))
          Refuse(b, "`" + (b.IsKind(SyntaxKind.AsExpression) ? "as" : "is") + "` is not in the Crust C# subset, except between arena classes. Use a virtual method.");
        string dc = "dynamic_cast<" + TemplateArg(target, b) + " *>(" + Expr(b.Left) + ")";
        return b.IsKind(SyntaxKind.AsExpression) ? dc : "(" + dc + " != 0)";
      }
      case SyntaxKind.CoalesceExpression:
        Refuse(b, "`??` is not in the Crust C# subset: there is no null for a value. ");
        break;
    }
    if (model.GetSymbolInfo(b).Symbol is IMethodSymbol userOp && userOp.MethodKind == MethodKind.UserDefinedOperator && !IsLib(userOp))
      return UserOperator(b, userOp, b.Left, b.Right);
    var lt = model.GetTypeInfo(b.Left).Type;
    var rt = model.GetTypeInfo(b.Right).Type;
    if ((lt != null && lt.SpecialType == SpecialType.System_String) || (rt != null && rt.SpecialType == SpecialType.System_String)) {
      if (op == "==" || op == "!=") return StrCompare(b);
      Refuse(b, "the string operator `" + op + "` is not in the Crust C# subset. Compare with `==` / `!=`, build with `+`.");
    }
    if ((op == "==" || op == "!=") && lt != null && (IsOwnedClass(lt) || lt.TypeKind == TypeKind.Interface))
      Refuse(b, "`==` on class references is not in the Crust C# subset: a class is a value, there is no identity. Compare a field.");
    if (op == ">>>")
      Refuse(b, "`>>>` is not in the Crust C# subset.");
    //&& and || sequence their operands in C as in C#; everything else does not
    string lhs = null;
    var saveRoot = hoistRoot;
    try {
      if (op != "&&" && op != "||" && lt != null && !Borrowed(lt) && lt.TypeKind != TypeKind.Interface && Conflicts(b.Left, b.Right)) {
        if (!canHoist)
          Refuse(b, "the operands of `" + op + "` conflict (the right one has a side effect the left one could observe), and C does not say which is evaluated first. Evaluate the left operand into a local on its own line first.");
        RequireHoist(b, "the operands of `" + op + "`");
        if (hoistRoot == null || !hoistRoot.Span.Contains(b.Span)) hoistRoot = b;   //what is inside is generated strictly in order
        lhs = NewTemp("_a");
        hoisted.Add(TypeName(lt, b) + " " + lhs + " = " + Expr(b.Left) + ";");
      }
      string left = lhs ?? Expr(b.Left);
      if (op == "<<" || op == ">>") {
        int bits = BitWidth(model.GetTypeInfo(b).Type);
        return "(" + left + " " + op + " (" + Expr(b.Right) + " & " + (bits - 1) + "))";
      }
      if (op == "%" && IsFloating(model.GetTypeInfo(b).Type)) {
        //C and C++ have no % on floating point; C#'s is the truncated remainder, which is fmod
        Includes.Add("\"cs/math.h\"");
        return (model.GetTypeInfo(b).Type.SpecialType == SpecialType.System_Single ? "cs_fmodf(" : "cs_fmod(") + left + ", " + Expr(b.Right) + ")";
      }
      //two references of different arena classes of one family (`base == derived`): C# compares the objects, here the addresses, which are equal when it does
      if ((op == "==" || op == "!=") && lt is INamedTypeSymbol lna && rt is INamedTypeSymbol rna && IsArena(lna) && IsArena(rna)
          && !SymbolEqualityComparer.Default.Equals(lna.OriginalDefinition, rna.OriginalDefinition))
        return "((void *)" + left + ") " + op + " ((void *)" + Expr(b.Right) + ")";
      return left + " " + op + " " + Expr(b.Right);
    } finally { hoistRoot = saveRoot; }
  }

  static bool IsFloating(ITypeSymbol t)
  {
    return t != null && (t.SpecialType == SpecialType.System_Single || t.SpecialType == SpecialType.System_Double);
  }

  int BitWidth(ITypeSymbol t)
  {
    if (t == null) return 32;
    switch (t.SpecialType) {
      case SpecialType.System_Int64: case SpecialType.System_UInt64: return 64;
      default: return 32;
    }
  }

  string Prefix(PrefixUnaryExpressionSyntax pu, bool statement)
  {
    string op = pu.OperatorToken.Text;
    if (model.GetSymbolInfo(pu).Symbol is IMethodSymbol userOp && userOp.MethodKind == MethodKind.UserDefinedOperator && !IsLib(userOp))
      return UserOperator(pu, userOp, pu.Operand);
    if (op == "++" || op == "--") {
      var prop = PropertyTarget(pu.Operand);
      if (prop != null) {
        if (!statement) Refuse(pu, "`" + op + "` on a property inside a larger expression is not in the Crust C# subset. Make it a statement.");
        return PropSet(pu.Operand, prop, PropGet(pu.Operand, prop) + " " + op[0] + " 1");
      }
    }
    if (op == "^") op = "~";
    return op + Expr(pu.Operand);
  }

  string Postfix(PostfixUnaryExpressionSyntax po, bool statement)
  {
    string op = po.OperatorToken.Text;
    var prop = PropertyTarget(po.Operand);
    if (prop != null) {
      if (!statement) Refuse(po, "`" + op + "` on a property inside a larger expression is not in the Crust C# subset. Make it a statement.");
      return PropSet(po.Operand, prop, PropGet(po.Operand, prop) + " " + op[0] + " 1");
    }
    if (op == "!") Refuse(po, "the null-forgiving operator `!` is not in the Crust C# subset.");
    return Expr(po.Operand) + op;
  }

  IPropertySymbol PropertyTarget(ExpressionSyntax e)
  {
    var ps = model.GetSymbolInfo(e).Symbol as IPropertySymbol;
    return ps != null && !ps.IsIndexer ? ps : null;   //xs[i] on a List<T> is an indexer, not a get_/set_ pair
  }

  /** `list.Capacity = n`: a corelib property is assigned through its [CppSet] template, or not at all. */
  string CorelibPropertySet(AssignmentExpressionSyntax a, IPropertySymbol ps, string op)
  {
    string tpl = null;
    foreach (var at in ps.OriginalDefinition.GetAttributes())
      if (at.AttributeClass != null && at.AttributeClass.Name == "CppSetAttribute" && at.ConstructorArguments.Length == 1) tpl = at.ConstructorArguments[0].Value as string;
    if (tpl == null) RefuseUnimplemented(a.Left, ps);
    if (op != "=") Refuse(a, "`" + op + "` on `" + ps.Name + "` is not in the Crust C# subset. Assign it with `=`.");
    AddIncludes(ps);
    string recv = null;
    if (!ps.IsStatic) {
      if (!(a.Left is MemberAccessExpressionSyntax ma)) { Refuse(a, "`" + ps.Name + "` needs a receiver."); return ""; }
      recv = RecvText(ma.Expression);
    }
    var args = new List<ArgumentSyntax> { SyntaxFactory.Argument(a.Right) };
    return Expand(tpl, ps, recv, args, new[] { Expr(a.Right) }, ps.ContainingType.TypeArguments.ToArray(), a);
  }

  string PropRecv(ExpressionSyntax e, IPropertySymbol ps)
  {
    if (ps.IsStatic) return FlatName(ps.ContainingType) + "::";
    if (e is MemberAccessExpressionSyntax ma) return Receiver(ma.Expression);
    return "";
  }
  string PropGet(ExpressionSyntax e, IPropertySymbol ps) { return PropRecv(e, ps) + "get_" + Ident(ps.Name) + "()"; }
  /** `p.position = new Vector3(..)`: a constructor call is not an expression in C, so a struct built for the setter is made in a local first (as for
      an argument, Arg).  Only where that cannot change what C# evaluates: the constructor's arguments are pure. */
  string SetterValue(ExpressionSyntax rhs)
  {
    var r = Unparen(rhs);
    var ty = model.GetTypeInfo(r).Type;
    var conv = model.GetTypeInfo(r).ConvertedType;
    if (ty != null && IsStructValue(ty) && r is BaseObjectCreationExpressionSyntax && canHoist
        && conv != null && SymbolEqualityComparer.Default.Equals(conv, ty)) {          //(a `new Vector2` stored in a Vector3 property is converted: Expr does that)
      var al = (r as ObjectCreationExpressionSyntax)?.ArgumentList ?? (r as ImplicitObjectCreationExpressionSyntax)?.ArgumentList;
      if (al == null || al.Arguments.All(x => IsPure(x.Expression))) {
        string name = "_t" + (++tmp);
        var args = al == null ? new List<string>() : al.Arguments.Select(x => Arg(x)).ToList();
        hoisted.Add(args.Count == 0 ? TypeName(ty, rhs) + " " + name + ";" : TypeName(ty, rhs) + " " + name + "(" + string.Join(", ", args) + ");");
        return name;
      }
    }
    return Expr(rhs);
  }

  string PropSet(ExpressionSyntax e, IPropertySymbol ps, string value)
  {
    return PropRecv(e, ps) + "set_" + Ident(ps.Name) + "(" + value + ")";
  }

  string Assign(AssignmentExpressionSyntax a, bool statement)
  {
    string op = a.OperatorToken.Text;
    if (a.IsKind(SyntaxKind.CoalesceAssignmentExpression))
      Refuse(a, "`??=` is not in the Crust C# subset.");
    var prop = PropertyTarget(a.Left);
    if (prop != null) {
      if (!statement) Refuse(a, "assigning a property inside a larger expression is not in the Crust C# subset. Make it a statement.");
      if (IsLib(prop)) return CorelibPropertySet(a, prop, op);
      if (op == "=") return PropSet(a.Left, prop, SetterValue(a.Right));
      string bop = op.Substring(0, op.Length - 1);
      return PropSet(a.Left, prop, PropGet(a.Left, prop) + " " + bop + " " + Expr(a.Right));
    }
    if (op != "=" && model.GetSymbolInfo(a).Symbol is IMethodSymbol cop && cop.MethodKind == MethodKind.UserDefinedOperator && !IsLib(cop)) {
      if (!statement) Refuse(a, "`" + op + "` on a struct with an operator inside a larger expression is not in the Crust C# subset. Make it a statement.");
      if (!IsPure(a.Left)) Refuse(a, "`" + a.Left + "` has a side effect and `" + op + "` would evaluate it twice in C. Assign it to a local first.");
      return Expr(a.Left) + " = " + UserOperator(a, cop, a.Left, a.Right);
    }
    var lt = model.GetTypeInfo(a.Left).Type;
    if (lt != null && lt.SpecialType == SpecialType.System_String) {
      if (!statement) Refuse(a, "assigning a string inside a larger expression is not in the Crust C# subset. Make it a statement.");
      if (op == "+=") {
        var cvr = model.GetConstantValue(a.Right);
        if (cvr.HasValue && cvr.Value is string lit) return Expr(a.Left) + ".append_cstr(\"" + CLit(lit) + "\")";
        var rty = model.GetTypeInfo(a.Right).Type;
        if (rty != null && rty.SpecialType == SpecialType.System_String) return Expr(a.Left) + ".append_str(" + Expr(a.Right) + ")";
        //s += 5  ->  append the digits
        RequireHoist(a.Right, "appending a number to a string");
        string lhs = Expr(a.Left);
        AppendPart(lhs, a.Right);
        return "";
      }
      if (op != "=") Refuse(a, "the string operator `" + op + "` is not in the Crust C# subset.");
      return Expr(a.Left) + " = " + Expr(a.Right);
    }
    if (op == "=" && lt != null && (IsOwnedClass(lt) || IsList(lt)) && !IsFresh(a.Right))
      Refuse(a, "assigning one object to another would alias it: a class is single-owner here. Construct with `new`, or copy the fields.");
    if (op != "=" && IsImpure(a.Right) && !(Unparen(a.Left) is IdentifierNameSyntax lid && IsLocalLike(model.GetSymbolInfo(lid).Symbol))) {
      var wl = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
      Writes(a.Right, wl, out bool wh);
      if (wh)
        Refuse(a, "`" + op + "` with a right side that has a side effect: C# reads the left side first, C does not say. Evaluate the right side into a local first.");
    }
    if (op == "<<=" || op == ">>=") {
      int bits = BitWidth(lt);
      return Expr(a.Left) + " " + op + " (" + Expr(a.Right) + " & " + (bits - 1) + ")";
    }
    if (op == ">>>=") Refuse(a, "`>>>=` is not in the Crust C# subset.");
    if (op == "%=" && IsFloating(lt)) {
      //x %= y  ->  x = fmod(x, y): the left side is written twice, so it must not have a side effect
      if (IsImpure(a.Left))
        Refuse(a, "`%=` on a floating-point value whose left side has a side effect: C has no float %, so the left side would be evaluated twice. Use a local.");
      Includes.Add("\"cs/math.h\"");
      string fn = lt.SpecialType == SpecialType.System_Single ? "cs_fmodf" : "cs_fmod";
      return Expr(a.Left) + " = " + fn + "(" + Expr(a.Left) + ", " + Expr(a.Right) + ")";
    }
    return Expr(a.Left) + " " + op + " " + Expr(a.Right);
  }

  string Cast(CastExpressionSyntax ce)
  {
    var from = model.GetTypeInfo(ce.Expression).Type;
    var to = model.GetTypeInfo(ce).Type;
    if (to != null && to.SpecialType == SpecialType.System_Char) Refuse(ce, "`char` is not in the Crust C# subset.");
    if (to != null && IsArena(to) && IsArena(from))       //a downcast: null where C# throws
      return "dynamic_cast<" + TemplateArg(to, ce) + " *>(" + Expr(ce.Expression) + ")";
    if (to != null && !to.IsValueType) Refuse(ce, "reference casts are not in the Crust C# subset, except between arena classes: there is no runtime type information.");
    var cv = from != null && to != null ? model.ClassifyConversion(ce.Expression, to) : default(Conversion);
    if (cv.Exists && cv.IsUserDefined && cv.MethodSymbol != null && !IsLib(cv.MethodSymbol))      //`(Vector3)v2`, with an explicit (or implicit) operator of the program's
      return UserOperator(ce, cv.MethodSymbol, ce.Expression);
    return "((" + TypeName(to, ce) + ")" + Expr(ce.Expression) + ")";
  }

  // ------------------------------------------------------------------ finish

}
