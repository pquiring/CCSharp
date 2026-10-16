/**

  CC# direct C back end, step 1: the C++ subset -> C, in C#, without Crust's cpprust.

  `ccs --lower-cpp P.main.cpp` reads the aggregate C++ that the --crust back end writes (its `#include "Type.cpp"` pieces are
  expanded in place) and prints the C for the PROGRAM part: its forward declarations, its prototypes, and its definitions
  (types, then `main`).  What goes around that -- the standard-library instances and the coost sources the program's headers
  reach -- is still cpprust's, spliced in by crust/test_direct_c.py today (see its direct_c()); lowering coost is a later step.

  The output is meant to be THE SAME C as cpprust's (crust/test_direct_c.py compares them after normalize_c), so this follows
  cpprust's rules, including its naming:   class Cls { int M(int a) }  ->  static int Cls_M(Cls *this, int a)
                                           Counter c(5);              ->  Counter c; Counter_new(&c, 5);
                                           a ref parameter            ->  a pointer, and `&arg` at the call.

  Anything not handled yet raises LowerUnsupported (exit status 3, the reason on stderr): never a guess.

*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CCSharpCompiler;

/** C++ the direct lowerer does not handle yet.  The caller falls back to cpprust. */
class LowerUnsupported : Exception
{
  public LowerUnsupported(string msg) : base(msg) { }
}

class CppLowerer
{
  public class Sections
  {
    public string Skeleton = "";     //the aggregate with the program's types replaced by one empty slot class: lowered by cpprust, it is the program's surroundings
    public string Fwd = "";          //struct X; typedef struct X X;  for each type
    public string Proto = "";        //prototypes of the types' functions
    public string Defs = "";         //the struct definitions and function bodies, then main
  }

  public const string Slot = "CcsSlot";

  /** `ccs --lower-cpp FILE.cpp`: the sections, each after a line `@@name`.  Exit status 3: not handled yet (the reason on stderr). */
  public static int Run(string path, string foreignPath)
  {
    try {
      var s = new CppLowerer().Lower(path, foreignPath);
      if (foreignPath == null) Console.Out.Write("@@skeleton\n" + s.Skeleton + "\n");          // phase 1: only the surroundings' source
      else Console.Out.Write("@@skeleton\n" + s.Skeleton + "\n@@fwd\n" + s.Fwd + "@@proto\n" + s.Proto + "@@defs\n" + s.Defs);
      return 0;
    } catch (LowerUnsupported e) {
      Console.Error.WriteLine("not lowered directly yet: " + e.Message);
      return 3;
    }
  }

  // ------------------------------------------------------------------ tokens

  enum K { Ws, Ident, Num, Str, Chr, Punct, Pre, Comment }

  class Tok
  {
    public K Kind;
    public string S;
    public override string ToString() { return S; }
  }

  static readonly string[] Puncts3 = { "<<=", ">>=", "...", "->*" };
  static readonly string[] Puncts2 = { "::", "->", "++", "--", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=" };

  static bool IsIdStart(char c) { return char.IsLetter(c) || c == '_'; }
  static bool IsIdChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }
  static bool IsSpace(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }

  static List<Tok> Lex(string s)
  {
    var r = new List<Tok>();
    int i = 0, n = s.Length;
    bool lineStart = true;
    while (i < n) {
      char c = s[i];
      if (IsSpace(c)) {
        int j = i;
        while (j < n && IsSpace(s[j])) j++;
        string ws = s.Substring(i, j - i);
        r.Add(new Tok { Kind = K.Ws, S = ws });
        if (ws.Contains('\n')) lineStart = true;
        i = j;
        continue;
      }
      if (c == '#' && lineStart) {
        int j = i;
        while (j < n && s[j] != '\n') j++;
        r.Add(new Tok { Kind = K.Pre, S = s.Substring(i, j - i).TrimEnd('\r') });
        i = j;
        continue;
      }
      lineStart = false;
      if (c == '/' && i + 1 < n && s[i + 1] == '/') {
        int j = i;
        while (j < n && s[j] != '\n') j++;
        r.Add(new Tok { Kind = K.Comment, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      if (c == '/' && i + 1 < n && s[i + 1] == '*') {
        int j = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
        j = j < 0 ? n : j + 2;
        r.Add(new Tok { Kind = K.Comment, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      if (IsIdStart(c)) {
        int j = i;
        while (j < n && IsIdChar(s[j])) j++;
        r.Add(new Tok { Kind = K.Ident, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1]))) {
        int j = i;
        bool hex = c == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X');
        while (j < n) {
          char d = s[j];
          if (IsIdChar(d) || d == '.') { j++; continue; }
          if ((d == '+' || d == '-') && !hex && j > i && (s[j - 1] == 'e' || s[j - 1] == 'E')) { j++; continue; }
          break;
        }
        r.Add(new Tok { Kind = K.Num, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      if (c == '"' || c == '\'') {
        int j = i + 1;
        while (j < n && s[j] != c) { if (s[j] == '\\') j++; j++; }
        j = Math.Min(n, j + 1);
        r.Add(new Tok { Kind = c == '"' ? K.Str : K.Chr, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      string p = null;
      foreach (var q in Puncts3) if (string.CompareOrdinal(s, i, q, 0, 3) == 0) { p = q; break; }
      if (p == null) foreach (var q in Puncts2) if (string.CompareOrdinal(s, i, q, 0, 2) == 0) { p = q; break; }
      if (p == null) p = c.ToString();
      r.Add(new Tok { Kind = K.Punct, S = p });
      i += p.Length;
    }
    return r;
  }

  static bool IsSig(Tok t) { return t.Kind != K.Ws && t.Kind != K.Comment; }
  static bool Is(Tok t, string s) { return (t.Kind == K.Punct || t.Kind == K.Ident) && t.S == s; }

  static int NextSig(List<Tok> t, int p)
  {
    while (p < t.Count && !IsSig(t[p])) p++;
    return p < t.Count ? p : -1;
  }

  static string Join(List<Tok> t, int a, int b)
  {
    var sb = new StringBuilder();
    for (int i = a; i < b; i++) sb.Append(t[i].S);
    return sb.ToString();
  }

  static int MatchParen(List<Tok> t, int open)
  {
    int d = 0;
    for (int i = open; i < t.Count; i++) {
      if (Is(t[i], "(")) d++;
      else if (Is(t[i], ")")) { d--; if (d == 0) return i; }
    }
    throw new LowerUnsupported("unbalanced parenthesis");
  }

  static int MatchBrace(List<Tok> t, int open)
  {
    int d = 0;
    for (int i = open; i < t.Count; i++) {
      if (Is(t[i], "{")) d++;
      else if (Is(t[i], "}")) { d--; if (d == 0) return i; }
    }
    throw new LowerUnsupported("unbalanced brace");
  }

  /** The `;` that ends the statement starting at t[from], skipping anything nested. */
  static int StmtEnd(List<Tok> t, int from)
  {
    int depth = 0;
    for (int i = from; i < t.Count; i++) {
      if (Is(t[i], "(") || Is(t[i], "{") || Is(t[i], "[")) depth++;
      else if (Is(t[i], ")") || Is(t[i], "}") || Is(t[i], "]")) depth--;
      else if (depth == 0 && Is(t[i], ";")) return i;
    }
    throw new LowerUnsupported("a statement without its `;`");
  }

  // ------------------------------------------------------------------ the program's declarations

  class Param
  {
    public string Type;        //without a trailing `&`
    public string Name;
    public bool IsRef;
  }

  class Member
  {
    public bool IsMethod;
    public bool IsCtor;
    public bool Synthesized;           //a constructor that only runs the field initialisers: the class wrote none
    public bool Static;
    public string Name;
    public string Ret;                 //methods
    public string Type;                //fields
    public string Init;                //fields: the initialiser text, or null
    public List<Param> Params = new List<Param>();
    public List<Tok> Body;             //the `{ ... }` of a function
  }

  class ClassInfo
  {
    public string Name;
    public List<Member> Members = new List<Member>();
    public IEnumerable<Member> Fields { get { return Members.Where(m => !m.IsMethod && !m.Static); } }
    public IEnumerable<Member> Statics { get { return Members.Where(m => !m.IsMethod && m.Static); } }
    public IEnumerable<Member> Methods { get { return Members.Where(m => m.IsMethod && !m.IsCtor); } }
    public IEnumerable<Member> Ctors { get { return Members.Where(m => m.IsCtor); } }
  }

  class Local
  {
    public string Type;                //the lowered type
    public string Class;               //the class it is a value of, or null
    public string Vector;              //the lowered name of a std::vector<T> instance, or null
    public string Foreign;             //a library type (coost's fastring ...) it is a value of, or null
    public bool PtrRef;                //a reference to such a pointer (C: a pointer to the pointer)
    public bool Pointer;               //a pointer to a program class (an arena reference), not a by-reference parameter
    public string Elem;                //a vector's element class, or null
    public string Alias;               //a range-for variable that is a reference: its uses are the element expression itself
    public bool IsPtr;                 //a reference parameter: a pointer in C
    public bool Droppable;             //something to destroy at every exit from its scope
  }

  class Proto
  {
    public string Ret, Name;
    public List<string> Params = new List<string>();
  }

  /** What cpprust knows about the library types a program uses, read from the prototypes in ITS C for the program's surroundings (see
      direct_c in crust/test_direct_c.py): which names are types, which functions are their methods (with cpprust's overload numbers),
      which parameters are references (a pointer to a type), and which types have a destructor.  Step 2 reads the coost headers itself. */
  class ForeignInfo
  {
    public HashSet<string> Types = new HashSet<string>();
    public Dictionary<string, List<Proto>> ByName = new Dictionary<string, List<Proto>>();

    static readonly Regex TypedefRe = new Regex("^typedef struct (\\w+) \\1;", RegexOptions.Multiline);
    static readonly Regex ProtoRe = new Regex("^(?:static\\s+)?(?:inline\\s+)?(?<ret>[A-Za-z_][^()\\n]*?)\\s*\\b(?<name>[A-Za-z_]\\w*)\\((?<params>[^()\\n]*)\\)[ \\t]*(?:;|\\{)", RegexOptions.Multiline);
    static readonly HashSet<string> NotRet = new HashSet<string> { "return", "else", "goto", "case", "typedef", "break", "continue", "do", "if", "while", "for" };

    public static ForeignInfo Parse(string c)
    {
      var f = new ForeignInfo();
      foreach (Match m in TypedefRe.Matches(c)) f.Types.Add(m.Groups[1].Value);
      foreach (Match m in ProtoRe.Matches(c)) {
        string ret = m.Groups["ret"].Value.Trim();
        if (NotRet.Contains(ret.Split(' ')[0])) continue;
        var pr = new Proto { Ret = ret, Name = m.Groups["name"].Value };
        string ps = m.Groups["params"].Value.Trim();
        if (ps.Length > 0 && ps != "void") pr.Params.AddRange(ps.Split(',').Select(x => x.Trim()));
        if (!f.ByName.TryGetValue(pr.Name, out var list)) f.ByName[pr.Name] = list = new List<Proto>();
        if (!list.Any(x => x.Ret == pr.Ret && string.Join(",", x.Params) == string.Join(",", pr.Params))) list.Add(pr);
      }
      return f;
    }

    /** Type_member and Type_member_N: cpprust numbers the overloads of a name. */
    public List<Proto> Members(string type, string member)
    {
      var re = new Regex("^" + Regex.Escape(type + "_" + member) + "(_\\d+)?$");
      var r = new List<Proto>();
      foreach (var kv in ByName) if (re.IsMatch(kv.Key)) r.AddRange(kv.Value);
      return r;
    }

    public bool IsInst(Proto p, string type)
    {
      return p.Params.Count > 0 && Regex.IsMatch(p.Params[0], "^(?:const\\s+)?" + Regex.Escape(type) + "\\s*\\*\\s*this$");
    }

    public bool HasDrop(string type) { return ByName.ContainsKey(type + "_drop"); }

    /** The parameters after `this`, as the caller sees them: a pointer to one of the types is a C++ reference, so the argument goes by address. */
    public List<Param> Declared(Proto p, bool inst)
    {
      var r = new List<Param>();
      foreach (var x in p.Params.Skip(inst ? 1 : 0)) {
        var m = Regex.Match(x, "^(?:const\\s+)?(\\w+)\\s*\\*\\s*\\w*$");
        r.Add(new Param { Type = x, Name = "", IsRef = (m.Success && Types.Contains(m.Groups[1].Value)) || ( Regex.IsMatch(x, "^(?:unsigned\\s+)?(?:int|long long|long|short|double|float|bool|unsigned|size_t|int64_t|uint64_t|int32_t|uint32_t)\\s*\\*\\s*\\w*$")) });
      }
      return r;
    }
  }

  class Ctx
  {
    public ClassInfo Cls;
    public bool IsStatic = true;
    public string Ret = "void";
    public List<Dictionary<string, Local>> Scopes = new List<Dictionary<string, Local>>();
    public Local Find(string n)
    {
      for (int i = Scopes.Count - 1; i >= 0; i--) if (Scopes[i].TryGetValue(n, out var l)) return l;
      return null;
    }
  }

  // ------------------------------------------------------------------ lowering

  readonly Dictionary<string, ClassInfo> classes = new Dictionary<string, ClassInfo>();
  readonly List<ClassInfo> order = new List<ClassInfo>();
  readonly Dictionary<string, Member> helpers = new Dictionary<string, Member>();
  readonly List<object> items = new List<object>();          //the program's classes and helper functions, in order
  int retCounter = 0;
  string entryClass;                   //the class whose Main main() calls

  static readonly HashSet<string> PrimWords = new HashSet<string> {
    "int", "long", "short", "char", "bool", "float", "double", "void", "unsigned", "signed", "size_t",
    "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t", "const"
  };

  static readonly Regex PieceInclude = new Regex("^[ \\t]*#[ \\t]*include[ \\t]*\"([^\"]+\\.cpp)\"[ \\t]*\\r?\\n?", RegexOptions.Multiline);

  readonly HashSet<string> declared = new HashSet<string>();     //every class the program names, so a class may mention itself or one below it

  ForeignInfo foreign;                 //null in phase 1 (only the skeleton is wanted)

  public Sections Lower(string path, string foreignPath)
  {
    if (foreignPath != null) foreign = ForeignInfo.Parse(File.ReadAllText(foreignPath));
    string dir = Path.GetDirectoryName(Path.GetFullPath(path));
    string raw = File.ReadAllText(path);
    var pieces = PieceInclude.Matches(raw);
    if (pieces.Count == 0) throw new LowerUnsupported("no `#include \"Type.cpp\"` in the aggregate: nothing to lower");
    string prefix = raw.Substring(0, pieces[0].Index);                                   //the headers, the wrapv pragma, the helpers: the surroundings' own
    var last = pieces[pieces.Count - 1];
    string mainRaw = raw.Substring(last.Index + last.Length);
    var toks = Expand(Lex(raw.Substring(pieces[0].Index)), dir, 0);
    programText = string.Concat(toks.Select(x => x.S));
    foreach (Match dm in Regex.Matches(programText, "\\b(?:class|struct)\\s+(\\w+)\\s*\\{")) declared.Add(dm.Groups[1].Value);               // (what comes before is the surroundings' own: it is in the skeleton)
    var defs = new StringBuilder();
    string mainText = null;
    int p = 0;
    bool seenType = false;
    while (p < toks.Count) {
      var t = toks[p];
      if (!IsSig(t)) { p++; continue; }
      if (t.Kind == K.Pre) {
        if (t.S.StartsWith("#line")) throw new LowerUnsupported("`#line` (a type that was moved above its source position)");
        if (seenType) throw new LowerUnsupported("a preprocessor line between types: " + t.S);
        p++;
        continue;
      }
      if (!seenType && t.Kind == K.Ident && t.S == "_Pragma") {
        while (p < toks.Count && !Is(toks[p], ")")) p++;
        p++;
        continue;
      }
      if (Is(t, "class") || Is(t, "struct")) {
        seenType = true;
        var ci = ParseClass(toks, ref p);
        classes[ci.Name] = ci;
        order.Add(ci);
        items.Add(ci);
        continue;
      }
      if (seenType && Is(t, "static")) {                                           // a helper function between the types
        var fn = ParseFree(toks, ref p);
        items.Add(fn);
        helpers[fn.Name] = fn;
        continue;
      }
      if (!seenType) throw new LowerUnsupported("something before the first type that is not an include or the pragma (a helper?): " + t.S);
      mainText = ParseMain(toks, ref p);
    }
    if (mainText == null) throw new LowerUnsupported("no `main` in the aggregate");
    var sec = new Sections();
    bool boolText = toks.Any(x => x.Kind == K.Ident && (x.S == "bool" || x.S == "true" || x.S == "false"));
    var word = new Regex("\\bbool\\b");
    bool boolHead = order.Any(ci => ci.Members.Any(m => m.IsMethod && ((m.Ret != null && word.IsMatch(m.Ret)) || m.Params.Any(x => word.IsMatch(x.Type)))));   // a prototype says bool
    sec.Skeleton = Skeleton(prefix, mainRaw, boolText, boolHead);
    if (foreign == null) return sec;                                              // phase 1
    foreach (var ci in order) {
      sec.Fwd += "struct " + ci.Name + ";\ntypedef struct " + ci.Name + " " + ci.Name + ";\n";
      foreach (var m in ci.Members.Where(x => x.IsMethod)) sec.Proto += Signature(ci, m) + ";\n";
      if (ClassHasDrop(ci)) sec.Proto += "static void " + ci.Name + "_copy(" + ci.Name + " *this, const " + ci.Name + " *o);\nstatic void " + ci.Name + "__assign(" + ci.Name + " *this, const " + ci.Name + " *o);\n";
      if (IsArena(ci)) foreach (var c in ci.Ctors) sec.Proto += "static " + ci.Name + " *" + AllocName(ci, c) + "(" + (c.Params.Count == 0 ? "void" : string.Join(", ", c.Params.Select(LowerParam))) + ");\n";
      foreach (var suffix in new[] { "", "_P" }) {
        if (!(suffix == "" ? vecClasses : ptrVecClasses).Contains(ci.Name)) continue;
        sec.Fwd += "/*CCS-VEC-FWD:" + ci.Name + suffix + "*/\n";
        sec.Proto += "/*CCS-VEC-PROTO:" + ci.Name + suffix + "*/\n";
      }
    }
    if (Regex.IsMatch(programText, "(?<![\\w.>])(?:new\\s+\\w+|delete\\b)")) defs.Append("/*CCS-HEAP*/\n");        // `new` and `delete` make cpprust declare malloc and free first thing
    foreach (var it in items) defs.Append(it is ClassInfo cc ? EmitClass(cc) : EmitFree((Member)it));
    defs.Append(mainText);
    sec.Defs = defs.ToString();
    return sec;
  }

  /** `#include "Type.cpp"` pieces are spliced in place, as cpprust does; every other include stays where it is. */
  List<Tok> Expand(List<Tok> toks, string dir, int depth)
  {
    if (depth > 32) throw new LowerUnsupported("includes nest too deep");
    var r = new List<Tok>();
    foreach (var t in toks) {
      if (t.Kind == K.Pre) {
        var m = Regex.Match(t.S, "^#\\s*include\\s*\"([^\"]+\\.cpp)\"");
        if (m.Success) {
          string f = Path.Combine(dir, m.Groups[1].Value);
          if (!File.Exists(f)) throw new LowerUnsupported("cannot read " + f);
          r.AddRange(Expand(Lex(File.ReadAllText(f)), Path.GetDirectoryName(f), depth + 1));
          continue;
        }
        if (Regex.IsMatch(t.S, "^#\\s*include\\s*\"")) throw new LowerUnsupported("a program that needs a library header yet: " + t.S);
      }
      r.Add(t);
    }
    return r;
  }

  // ---- types

  /** The type that starts at t[p]: [const] primitive words | a class name | std::vector<...>, then `*`s.  Returns its end (exclusive), or -1. */
  int TypeEnd(List<Tok> t, int p)
  {
    int q = NextSig(t, p);
    if (q < 0 || t[q].Kind != K.Ident) return -1;
    if (PrimWords.Contains(t[q].S)) {
      bool onlyConst = t[q].S == "const";
      q++;
      while (true) {
        int r = NextSig(t, q);
        if (r >= 0 && t[r].Kind == K.Ident && PrimWords.Contains(t[r].S)) { onlyConst = onlyConst && t[r].S == "const"; q = r + 1; } else break;
      }
      if (onlyConst) {                                                              // const fastring
        int r = NextSig(t, q);
        if (r >= 0 && t[r].Kind == K.Ident && IsTypeName(t[r].S)) q = r + 1;
      }
    } else if (t[q].S == "std") {
      int r = NextSig(t, q + 1);
      if (r < 0 || !Is(t[r], "::")) return -1;
      r = NextSig(t, r + 1);
      if (r < 0 || !Is(t[r], "vector")) return -1;
      r = NextSig(t, r + 1);
      if (r < 0 || !Is(t[r], "<")) return -1;
      int depth = 1;
      r++;
      while (r < t.Count && depth > 0) { if (Is(t[r], "<")) depth++; else if (Is(t[r], ">")) depth--; r++; }
      q = r;
    } else if (IsTypeName(t[q].S)) {
      q++;
    } else {
      int r1 = NextSig(t, q + 1);
      int r2 = r1 >= 0 && Is(t[r1], "::") ? NextSig(t, r1 + 1) : -1;
      if (r2 < 0 || t[r2].Kind != K.Ident || !IsTypeName(t[q].S + "_" + t[r2].S)) return -1;
      q = r2 + 1;
    }
    while (true) {
      int r = NextSig(t, q);
      if (r >= 0 && Is(t[r], "*")) q = r + 1; else break;
    }
    return q;
  }

  /** A class of the program, or (phase 2) a library type; in phase 1 any name: only the shape of the members is read then. */
  bool IsTypeName(string n)
  {
    return classes.ContainsKey(n) || declared.Contains(n) || (foreign != null ? foreign.Types.Contains(n) : true);
  }

  Local MakeLocal(string raw, bool isPtr)
  {
    string ty = LowerType(raw, out string cls, out string vec);
    string bare = Regex.Replace(ty, "^const\\s+", "");
    string fg = cls == null && vec == null && foreign != null && foreign.Types.Contains(bare) ? bare : null;
    var pm = Regex.Match(ty, "^(\\w+) ?\\*$");
    if (cls == null && vec == null && pm.Success && classes.ContainsKey(pm.Groups[1].Value))                         // Node *: a reference to an arena object
      return new Local { Type = ty, Class = pm.Groups[1].Value, IsPtr = true, Pointer = true, PtrRef = isPtr };
    string elem = null;
    if (vec != null) { var em = Regex.Match(raw, "^\\s*std\\s*::\\s*vector\\s*<\\s*(\\w+)\\s*>\\s*$"); if (em.Success && (classes.ContainsKey(em.Groups[1].Value) || (foreign != null && foreign.Types.Contains(em.Groups[1].Value)))) elem = em.Groups[1].Value; }
    return new Local { Type = ty, Class = cls, Vector = vec, Foreign = fg, IsPtr = isPtr, Elem = elem };
  }

  /** The C spelling of a type written in the subset: std::vector<const char *> is the instance cpprust names vector_const_char_P. */
  string LowerType(string raw, out string cls, out string vec)
  {
    cls = null; vec = null;
    string s = Regex.Replace(raw.Trim(), "\\s+", " ");
    if (!s.StartsWith("std")) s = Regex.Replace(s, "\\s*::\\s*", "_");
    var m = Regex.Match(s, "^std\\s*::\\s*vector\\s*<(.*)>$");
    if (m.Success) {
      string arg = m.Groups[1].Value.Trim();
      string ea = Regex.Replace(arg, "\\s+", " ");
      var pm = Regex.Match(ea, "^(\\w+) \\*$");
      bool ptrElem = pm.Success && classes.ContainsKey(pm.Groups[1].Value);
      bool classElem = classes.ContainsKey(ea) || (foreign != null && foreign.Types.Contains(ea));
      if (ea != "const char *" && !ptrElem && !classElem && !(IsPrimitive(ea) && !ea.Contains("const"))) throw new LowerUnsupported("`" + s + "`: instantiating the standard containers is not lowered here yet");
      if (classElem && foreign != null && classes.ContainsKey(ea) && ClassHasDrop(classes[ea])) throw new LowerUnsupported("`" + s + "`: class " + ea + " owns resources");
      vec = "vector_" + Regex.Replace(arg.Replace("*", "P").Replace(" ", "_"), "_+", "_");
      return vec;
    }
    if (classes.ContainsKey(s)) cls = s;
    return s;
  }

  static bool IsPrimitive(string type)
  {
    var ws = type.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    return ws.Length > 0 && ws.All(w => PrimWords.Contains(w) || w == "*");
  }

  // ---- classes

  ClassInfo ParseClass(List<Tok> t, ref int p)
  {
    p = NextSig(t, p + 1);
    var ci = new ClassInfo { Name = t[p].S };
    p = NextSig(t, p + 1);
    if (!Is(t[p], "{")) throw new LowerUnsupported("class " + ci.Name + ": a base class or a forward declaration");
    p++;
    while (true) {
      p = NextSig(t, p);
      if (p < 0) throw new LowerUnsupported("class " + ci.Name + ": no closing brace");
      if (Is(t[p], "}")) { p++; break; }
      if (Is(t[p], "public") || Is(t[p], "private") || Is(t[p], "protected")) throw new LowerUnsupported("class " + ci.Name + ": access specifiers");
      ParseMember(t, ref p, ci);
    }
    int semi = NextSig(t, p);
    if (semi >= 0 && Is(t[semi], ";")) p = semi + 1;
    if (!ci.Ctors.Any() && ci.Fields.Any(f => f.Init != null || !IsPrimitive(f.Type)))
      ci.Members.Add(new Member { IsMethod = true, IsCtor = true, Synthesized = true, Name = ci.Name });     // (cpprust writes it after the other members)
    return ci;
  }

  void ParseMember(List<Tok> t, ref int p, ClassInfo ci)
  {
    bool isStatic = false;
    if (Is(t[p], "static")) { isStatic = true; p = NextSig(t, p + 1); }
    foreach (var bad in new[] { "virtual", "template", "operator", "~", "enum", "typedef", "using" })
      if (Is(t[p], bad)) throw new LowerUnsupported("class " + ci.Name + ": `" + bad + "` member");
    int n1 = NextSig(t, p + 1);
    if (t[p].Kind == K.Ident && t[p].S == ci.Name && n1 >= 0 && Is(t[n1], "(")) {          // a constructor
      var m = new Member { IsMethod = true, IsCtor = true, Name = ci.Name };
      int close = MatchParen(t, n1);
      m.Params = ParseParams(t, n1 + 1, close);
      int b = NextSig(t, close + 1);
      if (b < 0 || !Is(t[b], "{")) throw new LowerUnsupported("class " + ci.Name + ": a constructor without a body, or with an initialiser list");
      int e = MatchBrace(t, b);
      m.Body = t.GetRange(b, e - b + 1);
      p = e + 1;
      ci.Members.Add(m);
      return;
    }
    int te = TypeEnd(t, p);
    if (te < 0) throw new LowerUnsupported("class " + ci.Name + ": a member whose type is not understood yet: " + Join(t, p, Math.Min(t.Count, p + 6)).Trim());
    string rawType = Join(t, p, te).Trim();
    int nameAt = NextSig(t, te);
    if (nameAt < 0 || t[nameAt].Kind != K.Ident) throw new LowerUnsupported("class " + ci.Name + ": a member without a name");
    string name = t[nameAt].S;
    int after = NextSig(t, nameAt + 1);
    if (after < 0) throw new LowerUnsupported("class " + ci.Name + ": truncated member " + name);
    if (Is(t[after], "(")) {                                                                    // a method
      var m = new Member { IsMethod = true, Name = name, Static = isStatic, Ret = rawType };
      int close = MatchParen(t, after);
      m.Params = ParseParams(t, after + 1, close);
      int b = NextSig(t, close + 1);
      if (b >= 0 && Is(t[b], "const")) throw new LowerUnsupported("class " + ci.Name + ": a const method");
      if (b < 0 || !Is(t[b], "{")) throw new LowerUnsupported("class " + ci.Name + ": method " + name + " without a body");
      int e = MatchBrace(t, b);
      m.Body = t.GetRange(b, e - b + 1);
      p = e + 1;
      if (ci.Methods.Any(x => x.Name == name)) throw new LowerUnsupported("class " + ci.Name + ": overloaded method " + name);
      if (!isStatic && Regex.IsMatch(rawType, "^\\w+\\s*\\*$") && declared.Contains(Regex.Match(rawType, "^\\w+").Value)) throw new LowerUnsupported("class " + ci.Name + ": method " + name + " returns a pointer to a class (cpprust adds a by-value wrapper for it)");
      ci.Members.Add(m);
      return;
    }
    var f = new Member { IsMethod = false, Name = name, Type = rawType, Static = isStatic };
    if (!IsPrimitive(f.Type) && !Regex.IsMatch(f.Type, "^(?:std\\s*::\\s*vector\\s*<[^<>]*>|[A-Za-z_][\\w:]*)(?:\\s*\\*)?$")) throw new LowerUnsupported("class " + ci.Name + ": field " + name + " of type `" + f.Type + "`");
    if (Is(t[after], "=")) {
      int semi = StmtEnd(t, after + 1);
      f.Init = Join(t, after + 1, semi).Trim();
      p = semi + 1;
    } else if (Is(t[after], ";")) {
      p = after + 1;
    } else throw new LowerUnsupported("class " + ci.Name + ": field " + name + ": " + t[after].S);
    ci.Members.Add(f);
  }

  List<Param> ParseParams(List<Tok> t, int a, int b)
  {
    var r = new List<Param>();
    var parts = new List<(int, int)>();
    int start = a, depth = 0;
    for (int i = a; i <= b; i++) {
      if (i == b || (depth == 0 && Is(t[i], ","))) {
        if (Join(t, start, i).Trim().Length > 0) parts.Add((start, i));
        start = i + 1;
      } else if (Is(t[i], "<") || Is(t[i], "(")) depth++;
      else if (Is(t[i], ">") || Is(t[i], ")")) depth--;
    }
    foreach (var (s, e) in parts) {
      string text = Join(t, s, e).Trim();
      if (text.Contains("=")) throw new LowerUnsupported("default arguments");
      bool isRef = text.Contains("&");
      text = text.Replace("&", " ").Trim();
      int sp = text.LastIndexOfAny(new[] { ' ', '*', '\t', '\n' });
      if (sp < 0) throw new LowerUnsupported("a parameter without a name: " + text);
      r.Add(new Param { Type = text.Substring(0, sp + 1).Trim(), Name = text.Substring(sp + 1), IsRef = isRef });
    }
    return r;
  }

  string LowerParam(Param p)
  {
    string ty = LowerType(p.Type, out _, out _);
    return p.IsRef ? ty + " *" + p.Name : ty + " " + p.Name;
  }

  /** `T_new` for the only constructor and for the no-argument one; with several, the others are `T_new_<argument count>` (cpprust resolves by count). */
  /** A field that owns something: a vector, or a library object with a destructor.  Anything else that is not a primitive is refused. */
  bool Owning(ClassInfo ci, Member f)
  {
    if (IsPrimitive(f.Type)) return false;
    var l = MakeLocal(f.Type, false);
    if (l.Pointer) return false;
    if (l.Vector != null) return true;
    if (l.Foreign != null && foreign.HasDrop(l.Foreign)) return true;
    throw new LowerUnsupported("class " + ci.Name + ": field " + f.Name + " of type `" + f.Type + "`");
  }

  /** An arena class: `[MaxInstances(N)]` in C#, a `static const int __max_instances` here.  A reference to it is a pointer into N static slots. */
  bool IsArena(ClassInfo ci) { return ci.Statics.Any(x => x.Name == "__max_instances"); }

  string AllocName(ClassInfo ci, Member ctor) { return FuncName(ci, ctor).Replace("_new", "__alloc"); }

  bool ClassHasDrop(ClassInfo ci) { return ci.Fields.Any(f => Owning(ci, f)); }

  string FieldType(Member f) { return IsPrimitive(f.Type) ? f.Type : MakeLocal(f.Type, false).Type; }

  string FuncName(ClassInfo ci, Member m)
  {
    if (!m.IsCtor) return ci.Name + "_" + m.Name;
    var ctors = ci.Ctors.ToList();
    if (ctors.Count(c => c.Params.Count == m.Params.Count) > 1) throw new LowerUnsupported("class " + ci.Name + ": two constructors with " + m.Params.Count + " parameters");
    return ci.Name + (ctors.Count == 1 || m.Params.Count == 0 ? "_new" : "_new_" + m.Params.Count);
  }

  string Signature(ClassInfo ci, Member m)
  {
    var ps = new List<string>();
    if (!m.Static) ps.Add(ci.Name + " *this");
    ps.AddRange(m.Params.Select(LowerParam));
    string ret = m.IsCtor ? "void" : LowerType(m.Ret, out _, out _);
    string name = FuncName(ci, m);
    return "static " + ret + " " + name + "(" + (ps.Count == 0 ? "void" : string.Join(", ", ps)) + ")";
  }

  Dictionary<string, Local> ParamScope(Member m)
  {
    var scope = new Dictionary<string, Local>();
    foreach (var pr in m.Params) {
      var l = MakeLocal(pr.Type, pr.IsRef);
      if (!pr.IsRef && l.Foreign != null) l.Droppable = foreign.HasDrop(l.Foreign);      // a by-value library object is the callee's to destroy
      scope[pr.Name] = l;
    }
    return scope;
  }

  string EmitClass(ClassInfo ci)
  {
    var sb = new StringBuilder();
    var fields = ci.Fields.ToList();
    if (ptrVecClasses.Contains(ci.Name)) sb.Append("/*CCS-VEC-STRUCT:" + ci.Name + "_P*/\n");
    foreach (var st in ci.Statics) sb.Append("static ").Append(FieldType(st)).Append(' ').Append(ci.Name).Append('_').Append(st.Name).Append(st.Init != null ? " = " + st.Init : "").Append(";\n");
    var owning = fields.Where(f => Owning(ci, f)).ToList();
    foreach (var f in fields.Where(f => f.Init != null && !IsPrimitive(f.Type) && !f.Type.TrimEnd().EndsWith("*"))) throw new LowerUnsupported("class " + ci.Name + ": field " + f.Name + " with an initialiser");
    if (fields.Count == 0) sb.Append("struct " + ci.Name + " { char _cpp_empty; };\n");
    else sb.Append("struct " + ci.Name + " { " + string.Join(" ", fields.Select(f => FieldType(f) + " " + f.Name + ";")) + " };\n");
    // owned fields are constructed first, then the field initialisers run (in every constructor)
    string news = string.Concat(owning.Select(f => { var l = MakeLocal(f.Type, false); return (l.Vector ?? l.Foreign) + "_new(&this->" + f.Name + "); "; }));
    string inits = string.Concat(fields.Where(f => f.Init != null).Select(f => "this->" + f.Name + " = " + Regex.Replace(f.Init, "\\bNULL\\b", "((void *)0)") + "; "));
    foreach (var m in ci.Members.Where(x => x.IsMethod)) {
      string lead = news + (inits.Length > 0 ? " " + inits.TrimEnd() : "");
      if (m.Synthesized) { sb.Append(Signature(ci, m)).Append(" {").Append(news).Append("  ").Append(inits.TrimEnd()).Append("}\n"); continue; }
      var cx = new Ctx { Cls = ci, IsStatic = m.Static, Ret = m.IsCtor ? "void" : LowerType(m.Ret, out _, out _) };
      cx.Scopes.Add(ParamScope(m));
      int bp = 0;
      sb.Append(Signature(ci, m)).Append(' ').Append(Block(m.Body, ref bp, cx, "", true, m.IsCtor ? lead : null)).Append('\n');
    }
    if (owning.Count > 0) {
      string c = ci.Name, drops = string.Join(" ", Enumerable.Reverse(owning).Select(f => { var l = MakeLocal(f.Type, false); return (l.Vector ?? l.Foreign) + "_drop(&this->" + f.Name + ");"; }));
      string copies = string.Join(" ", fields.Select(f => {
        if (!Owning(ci, f)) return "this->" + f.Name + " = o->" + f.Name + ";";
        var l = MakeLocal(f.Type, false);
        return (l.Vector ?? l.Foreign) + "_copy(&this->" + f.Name + ", &o->" + f.Name + ");";
      }));
      sb.Append("static void " + c + "_drop(" + c + " *this) { " + drops + " }\n");
      sb.Append("static void " + c + "_copy(" + c + " *this, const " + c + " *o) { " + copies + "}\n");
      sb.Append("static void " + c + "__assign(" + c + " *this, const " + c + " *o) { if (this != o) { " + drops + " " + copies + " }}\n");
    }
    if (IsArena(ci)) {
      if (ClassHasDrop(ci)) throw new LowerUnsupported("arena class " + ci.Name + " owns resources");
      var maxField = ci.Statics.First(x => x.Name == "__max_instances");
      string n = maxField.Init, c0 = ci.Name;
      sb.Append("static " + c0 + " " + c0 + "__arena[" + n + "]; static int " + c0 + "__arena_n;\n");
      sb.Append("static void " + c0 + "__arena_reset(void) { int k; for (k = 0; k < " + c0 + "__arena_n; k = k + 1) { } " + c0 + "__arena_n = 0; }\n");
      foreach (var ct in ci.Ctors)
        sb.Append("static " + c0 + " *" + AllocName(ci, ct) + "(" + (ct.Params.Count == 0 ? "void" : string.Join(", ", ct.Params.Select(LowerParam))) + ") { " + c0 + " *p; if (" + c0 + "__arena_n >= " + n + ") { abort(); } p = &" + c0 + "__arena[" + c0 + "__arena_n]; " + c0 + "__arena_n = " + c0 + "__arena_n + 1; memset(p, 0, sizeof(" + c0 + ")); if (p) { " + FuncName(ci, ct) + "(p" + string.Concat(ct.Params.Select(q => ", " + q.Name)) + "); } return p; }\n");
    }
    foreach (var suffix in new[] { "", "_P" })
      if ((suffix == "" ? vecClasses : ptrVecClasses).Contains(ci.Name)) sb.Append("/*CCS-VEC-DEFS:" + ci.Name + suffix + "*/\n");
    sb.Append("\n;\n");
    return sb.ToString();
  }

  // ---- helper functions

  /** `static RET name(params) { ... }` between the types: a free function the C# compiler wrote (an array helper). */
  Member ParseFree(List<Tok> t, ref int p)
  {
    p = NextSig(t, p + 1);                                                       // past `static`
    int te = TypeEnd(t, p);
    if (te < 0) throw new LowerUnsupported("a helper function with a return type not understood yet: " + Join(t, p, Math.Min(t.Count, p + 6)).Trim());
    var m = new Member { IsMethod = true, Static = true, Ret = Join(t, p, te).Trim() };
    int nameAt = NextSig(t, te);
    int open = nameAt < 0 ? -1 : NextSig(t, nameAt + 1);
    if (open < 0 || t[nameAt].Kind != K.Ident || !Is(t[open], "(")) throw new LowerUnsupported("a helper that is not a function");
    m.Name = t[nameAt].S;
    int close = MatchParen(t, open);
    m.Params = ParseParams(t, open + 1, close);
    int b = NextSig(t, close + 1);
    if (b < 0 || !Is(t[b], "{")) throw new LowerUnsupported("helper " + m.Name + " without a body");
    int e = MatchBrace(t, b);
    m.Body = t.GetRange(b, e - b + 1);
    p = e + 1;
    return m;
  }

  string EmitFree(Member m)
  {
    var cx = new Ctx { Cls = null, IsStatic = true, Ret = LowerType(m.Ret, out _, out _) };
    cx.Scopes.Add(ParamScope(m));
    int bp = 0;
    string ps = m.Params.Count == 0 ? "void" : string.Join(", ", m.Params.Select(LowerParam));
    return "static " + cx.Ret + " " + m.Name + "(" + ps + ") " + Block(m.Body, ref bp, cx, "", true) + "\n";
  }

  // ---- main

  /** int main(int argc, char **argv) { ... }, which the back end writes itself. */
  string ParseMain(List<Tok> t, ref int p)
  {
    int start = p;
    int open = start;
    while (open < t.Count && !Is(t[open], "(")) open++;
    string head = Join(t, start, open).Trim();
    if (head != "int main") throw new LowerUnsupported("a free function other than main: " + head);
    int close = MatchParen(t, open);
    int b = NextSig(t, close + 1);
    int e = MatchBrace(t, b);
    var body = t.GetRange(b, e - b + 1);
    p = e + 1;
    for (int i = 0; i + 2 < body.Count; i++) {                                     // Entry::Main(...)
      if (Is(body[i], "::")) {
        int m = NextSig(body, i + 1);
        int c = i - 1;
        while (c >= 0 && !IsSig(body[c])) c--;
        if (m >= 0 && Is(body[m], "Main") && c >= 0) { entryClass = body[c].S; break; }
      }
    }
    if (entryClass == null || !classes.ContainsKey(entryClass)) throw new LowerUnsupported("main does not call a Main of the program's classes");
    if (foreign == null) return "";                                                // phase 1: no bodies
    var cx = new Ctx { Ret = "int" };
    cx.Scopes.Add(new Dictionary<string, Local> {
      ["argc"] = new Local { Type = "int" },
      ["argv"] = new Local { Type = "char **" },
    });
    int bp = 0;
    return Join(t, start, b) + Block(body, ref bp, cx, "") + "\n";
  }

  /** The aggregate with the program's types replaced by one slot class, and main calling it. */
  string programText;
  readonly List<string> ptrVecClasses = new List<string>();  //program classes some std::vector<Cls *> holds (arena classes)
  readonly List<string> vecClasses = new List<string>();     //program classes some std::vector holds: cpprust writes the instance right after the class

  string Skeleton(string prefix, string mainRaw, bool boolText, bool boolHead)
  {
    var main = classes[entryClass].Methods.First(m => m.Name == "Main");
    string ps = string.Join(", ", main.Params.Select(x => x.Type + (x.IsRef ? " & " : " ") + x.Name));
    // cpprust adds <stdbool.h> when the text says bool/true/false, and again at the top when `bool` is in a prototype: the slot says the same
    string use = boolText ? "bool CcsB = false; " : "";
    string body = LowerType(main.Ret, out _, out _) == "void" ? "{ " + use + "}" : "{ " + use + "return 0; }";
    string probe = boolHead ? " static bool CcsProbe() { return true; }" : "";
    // the containers the program names that the surroundings do not: used here so cpprust instantiates them, in the order the program first names them
    var seen = new List<string>();
    foreach (Match vm in Regex.Matches(programText, "std\\s*::\\s*vector\\s*<([^<>]*)>")) {
      string ea = Regex.Replace(vm.Groups[1].Value.Trim(), "\\s+", " ");
      if (ea == "const char *" || seen.Contains(ea) || Regex.IsMatch(prefix, "std\\s*::\\s*vector\\s*<\\s*" + Regex.Escape(ea).Replace("\\ ", "\\s+") + "\\s*>")) continue;
      seen.Add(ea);
    }
    // a vector of a program class is stamped from a stand-in class of the same name and shape (owning or not): the back end moves the text to the class
    var stubs = new StringBuilder();
    var stubbed = new HashSet<string>();
    for (int k = 0; k < seen.Count; k++) {
      var em = Regex.Match(seen[k], "^(\\w+)( \\*)?$");
      if (!em.Success || !classes.TryGetValue(em.Groups[1].Value, out var ec)) continue;
      bool ptr = em.Groups[2].Success;
      if (stubbed.Add(ec.Name)) {
        bool own = ec.Fields.Any(f => !IsPrimitive(f.Type) && !Regex.IsMatch(f.Type, "\\*$"));
        stubs.Append("class CcsE_" + ec.Name + " { int v; " + (own ? "std::vector<int> a; " : "") + "};\n");
      }
      if (ptr) ptrVecClasses.Add(ec.Name); else vecClasses.Add(ec.Name);
      seen[k] = "CcsE_" + ec.Name + (ptr ? " *" : "");
    }
    string uses = seen.Count == 0 ? "" : "static void CcsUse(" + string.Join(", ", seen.Select((e, i) => "std::vector<" + e + "> & u" + i)) + ") { } ";
    string slot = "class " + Slot + " { " + uses + "static " + main.Ret + " Main(" + ps + ") " + body + probe + " };\n";
    return prefix + stubs + slot + Regex.Replace(mainRaw, "\\b" + Regex.Escape(entryClass) + "\\s*::\\s*Main\\b", Slot + "::Main");
  }

  // ---- bodies

  /** `{ ... }` at body[p]: its statements lowered.  `inits` is text that goes first inside (a constructor's field initialisers). */
  bool lastReturn;
  Dictionary<string, Local> pendingScope;     //locals the next block starts out with (a range-for's variable)

  string Block(List<Tok> body, ref int p, Ctx cx, string inits, bool fn = false, string raw = null)
  {
    var sb = new StringBuilder();
    sb.Append('{');
    if (raw != null) sb.Append(raw);
    else if (inits.Length > 0) sb.Append(' ').Append(inits.TrimEnd());                  // right after the brace; what followed it stays as it was
    p++;
    cx.Scopes.Add(pendingScope ?? new Dictionary<string, Local>());
    pendingScope = null;
    while (true) {
      if (p >= body.Count) throw new LowerUnsupported("unbalanced brace in a body");
      var t = body[p];
      if (Is(t, "}")) break;
      if (!IsSig(t)) { sb.Append(t.S); p++; continue; }
      lastReturn = Is(t, "return");
      Stmt(body, ref p, cx, sb);
    }
    var ends = new List<string>();                                                  // what dies at the closing brace: this scope, and a function's parameters
    for (int k = cx.Scopes.Count - 1; k >= (fn ? 0 : cx.Scopes.Count - 1); k--)
      foreach (var kv in Enumerable.Reverse(cx.Scopes[k].ToList()))
        if (kv.Value.Droppable) ends.Add((kv.Value.Vector ?? kv.Value.Class ?? kv.Value.Foreign) + "_drop(&" + kv.Key + ");");
    if (ends.Count > 0 && !lastReturn) {
      int n = sb.Length;
      while (n > 0 && char.IsWhiteSpace(sb[n - 1])) n--;
      string ws = sb.ToString(n, sb.Length - n);
      sb.Length = n;
      foreach (var d in ends) sb.Append(' ').Append(d);
      sb.Append(ws);
    }
    lastReturn = false;
    cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
    sb.Append('}');
    p++;
    return sb.ToString();
  }

  /** The white space and comments at t[p..], copied. */
  static void CopyWs(List<Tok> t, ref int p, StringBuilder sb)
  {
    while (p < t.Count && !IsSig(t[p])) { sb.Append(t[p].S); p++; }
  }

  void Stmt(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    var tok = t[p];
    if (Is(tok, "{")) { sb.Append(Block(t, ref p, cx, "")); return; }
    if (Is(tok, "return")) { Return(t, ref p, cx, sb); return; }
    if (Is(tok, "if")) { If(t, ref p, cx, sb); return; }
    if (Is(tok, "while")) { While(t, ref p, cx, sb); return; }
    if (Is(tok, "for")) { For(t, ref p, cx, sb); return; }
    foreach (var bad in new[] { "switch", "do", "goto", "try", "throw", "else", "case", "default" })
      if (Is(tok, bad)) throw new LowerUnsupported("`" + bad + "` statement");
    int te = TypeEnd(t, p);
    if (te >= 0) {
      int nm = NextSig(t, te);
      if (nm >= 0 && t[nm].Kind == K.Ident) {
        int after = NextSig(t, nm + 1);
        if (after >= 0 && (Is(t[after], ";") || Is(t[after], "(") || Is(t[after], "="))) { Decl(t, ref p, cx, sb, te, nm, after); return; }
      }
    }
    int end = StmtEnd(t, p);                                                    // an expression statement
    int p0 = p;
    {                                                                            // x = std::vector<T>();  builds a temporary first, ahead of the line it is on
      var vm = Regex.Match(Join(t, p, end).Trim(), "^(\\w+)\\s*=\\s*std\\s*::\\s*vector\\s*<([^<>]*)>\\s*\\(\\s*\\)$");
      if (vm.Success) {
        string tn = "std::vector<" + vm.Groups[2].Value + ">";
        string vname = LowerType(tn, out _, out string vv);
        string tmp = "__cpp_tmp" + (tmpCounter++);
        int n = sb.Length;
        while (n > 0 && char.IsWhiteSpace(sb[n - 1])) n--;
        string ws = sb.ToString(n, sb.Length - n);
        sb.Length = n;
        sb.Append(vv + " " + tmp + "; " + vv + "_new(&" + tmp + "); ").Append(ws);
        cx.Scopes[cx.Scopes.Count - 1][tmp] = new Local { Type = vv, Vector = vv, Droppable = true };
        sb.Append(Expr(t, p, NextSig(t, p + 1), cx).Trim()).Append(" = ").Append(tmp).Append(';');
        p = end + 1;
        return;
      }
    }
    if (t[p].Kind == K.Ident && foreign != null) {                              // dst = src; on library objects: an assignment function
      int ls = p;
      if (Is(t[p], "this")) { int a1 = NextSig(t, p + 1), a2 = a1 < 0 ? -1 : NextSig(t, a1 + 1); if (a2 >= 0 && Is(t[a1], "->") && t[a2].Kind == K.Ident) ls = a2; }
      int eq = NextSig(t, ls + 1);
      int rs = eq < 0 ? -1 : NextSig(t, eq + 1);
      if (eq >= 0 && Is(t[eq], "=") && rs >= 0 && t[rs].Kind == K.Ident && NextSig(t, rs + 1) == end) {
        Local dl = ls == p ? cx.Find(t[ls].S) : null;
        if (dl == null && cx.Cls != null && !cx.IsStatic) {
          string fname = t[ls].S;
          var fld = cx.Cls.Fields.FirstOrDefault(f => f.Name == fname && !IsPrimitive(f.Type));
          if (fld != null) dl = MakeLocal(fld.Type, false);
        }
        var sl = cx.Find(t[rs].S);
        if (dl != null && dl.Foreign != null && foreign.HasDrop(dl.Foreign)) {
          if (sl == null || sl.Foreign != dl.Foreign) throw new LowerUnsupported("assigning `" + t[rs].S + "` to a library object");
          sb.Append(dl.Foreign).Append("__assign(&").Append(Expr(t, p, eq, cx).Trim()).Append(", ").Append(sl.IsPtr ? t[rs].S : "&" + t[rs].S).Append(");");
          p = end + 1;
          return;
        }
      }
    }
    sb.Append(Expr(t, p, end, cx)).Append(';');
    p = end + 1;
  }

  /** The statement after a condition: a block, or one statement. */
  void Body(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    CopyWs(t, ref p, sb);
    if (p >= t.Count) throw new LowerUnsupported("a statement is missing its body");
    cx.Scopes.Add(new Dictionary<string, Local>());
    Stmt(t, ref p, cx, sb);
    cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
  }

  void If(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int open = NextSig(t, p + 1);
    int close = MatchParen(t, open);
    sb.Append("if").Append(Join(t, p + 1, open)).Append('(').Append(Expr(t, open + 1, close, cx)).Append(')');
    p = close + 1;
    Body(t, ref p, cx, sb);
    int e = NextSig(t, p);
    if (e >= 0 && Is(t[e], "else")) {
      while (p < e) { sb.Append(t[p].S); p++; }
      sb.Append("else");
      p = e + 1;
      CopyWs(t, ref p, sb);
      if (p < t.Count && Is(t[p], "if")) If(t, ref p, cx, sb);
      else Body(t, ref p, cx, sb);
    }
  }

  void While(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int open = NextSig(t, p + 1);
    int close = MatchParen(t, open);
    sb.Append("while").Append(Join(t, p + 1, open)).Append('(').Append(Expr(t, open + 1, close, cx)).Append(')');
    p = close + 1;
    Body(t, ref p, cx, sb);
  }

  void For(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int open = NextSig(t, p + 1);
    int close = MatchParen(t, open);
    int colon = -1;
    for (int k = open + 1, d = 0; k < close; k++) {
      if (Is(t[k], "(") || Is(t[k], "[") || Is(t[k], "<")) d++; else if (Is(t[k], ")") || Is(t[k], "]") || Is(t[k], ">")) d--;
      else if (Is(t[k], ";")) break;
      else if (d == 0 && Is(t[k], ":")) { colon = k; break; }
    }
    if (colon >= 0) { RangeFor(t, ref p, open, colon, close, cx, sb); return; }
    int s1 = StmtEnd(t, open + 1);
    int s2 = StmtEnd(t, s1 + 1);
    cx.Scopes.Add(new Dictionary<string, Local>());                            // the loop variable lives in the loop
    int te = TypeEnd(t, open + 1);
    string init;
    if (te >= 0 && te < s1 && IsPrimitive(Join(t, open + 1, te).Trim())) {      // for (int i = 0; ...)
      int nm = NextSig(t, te);
      cx.Scopes[cx.Scopes.Count - 1][t[nm].S] = new Local { Type = Join(t, open + 1, te).Trim() };
      init = Join(t, open + 1, nm) + t[nm].S + Expr(t, nm + 1, s1, cx);
    } else init = Expr(t, open + 1, s1, cx);
    sb.Append("for").Append(Join(t, p + 1, open)).Append('(').Append(init).Append(';').Append(Expr(t, s1 + 1, s2, cx)).Append(';').Append(Expr(t, s2 + 1, close, cx)).Append(')');
    p = close + 1;
    Body(t, ref p, cx, sb);
    cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
  }

  /** `&x` for an object of library type `fg` that `text` names (a local, a field of this, `this->f`), or null if it names something else. */
  string CopySource(string text, string fg, Ctx cx)
  {
    if (Regex.IsMatch(text, "^[A-Za-z_]\\w*$")) {
      var l = cx.Find(text);
      if (l != null) return l.Foreign == fg ? (l.IsPtr ? text : "&" + text) : null;
      if (cx.Cls != null && !cx.IsStatic) {
        var f = cx.Cls.Fields.FirstOrDefault(x => x.Name == text && !IsPrimitive(x.Type));
        if (f != null && MakeLocal(f.Type, false).Foreign == fg) return "&this->" + text;
      }
      return null;
    }
    var m = Regex.Match(text, "^this\\s*->\\s*(\\w+)$");
    if (m.Success && cx.Cls != null) {
      var f = cx.Cls.Fields.FirstOrDefault(x => x.Name == m.Groups[1].Value && !IsPrimitive(x.Type));
      if (f != null && MakeLocal(f.Type, false).Foreign == fg) return "&this->" + m.Groups[1].Value;
    }
    return null;
  }

  int itCounter;

  /** for (T v : vec) body  ->  an index loop over the vector, `v` copied out first thing in the body (as cpprust writes it). */
  void RangeFor(List<Tok> t, ref int p, int open, int colon, int close, Ctx cx, StringBuilder sb)
  {
    int te = TypeEnd(t, open + 1);
    int nm = te < 0 ? -1 : NextSig(t, te);
    bool byRef = false;
    if (nm >= 0 && nm < colon && Is(t[nm], "&")) { byRef = true; nm = NextSig(t, nm + 1); }
    string elemTy = te < 0 ? "" : Join(t, open + 1, te).Trim();
    bool elemClass = classes.ContainsKey(elemTy);
    bool elemLib = foreign != null && foreign.Types.Contains(elemTy) && !byRef && foreign.HasDrop(elemTy);
    if (te < 0 || nm < 0 || nm >= colon || !(IsPrimitive(elemTy) || (elemClass && byRef) || elemLib)) throw new LowerUnsupported("a range-for over something other than primitives, or objects by reference");
    string over = Join(t, colon + 1, close).Trim();
    var vl = cx.Find(over);
    string otext = over;
    if (vl == null && cx.Cls != null && !cx.IsStatic) {                         // a field of this
      var fld = cx.Cls.Fields.FirstOrDefault(f => f.Name == over && !IsPrimitive(f.Type));
      if (fld != null) { vl = MakeLocal(fld.Type, false); otext = "this->" + over; }
    }
    if (vl == null || vl.Vector == null) throw new LowerUnsupported("a range-for over `" + over + "`");
    string ty = Join(t, open + 1, te).Trim(), v = t[nm].S, it = "_cpp_it" + (itCounter++);
    string recv = vl.IsPtr ? otext : "&" + otext;
    sb.Append("for").Append(Join(t, p + 1, open)).Append("(int ").Append(it).Append(" = 0; ").Append(it).Append(" < ").Append(vl.Vector).Append("_size(").Append(recv).Append("); ")
      .Append(it).Append(" = ").Append(it).Append(" + 1)");
    p = close + 1;
    CopyWs(t, ref p, sb);
    if (p >= t.Count || !Is(t[p], "{")) throw new LowerUnsupported("a range-for without braces");
    string element = "(*" + vl.Vector + "__index(" + recv + ", " + it + "))";
    int bp = p;
    if (elemLib) {                                                                  // a library object by value: a copy, destroyed at the end of each pass
      pendingScope = new Dictionary<string, Local> { [v] = new Local { Type = ty, Foreign = ty, Droppable = true } };
      string cinit = ty + " " + v + "; " + ty + "_copy(&" + v + ", &(*" + vl.Vector + "__index(" + (vl.IsPtr ? "(" + otext + ")" : "&(" + otext + ")") + ", " + it + ")));";
      sb.Append(Block(t, ref bp, cx, cinit));
    } else {
      var scope = new Dictionary<string, Local> { [v] = elemClass ? new Local { Type = ty, Class = ty, Alias = element } : new Local { Type = ty } };
      cx.Scopes.Add(scope);
      string init = ty + " " + v + " = " + element + ";";
      sb.Append(Block(t, ref bp, cx, elemClass ? "" : init));
      cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
    }
    p = bp;
  }

  void Decl(List<Tok> t, ref int p, Ctx cx, StringBuilder sb, int te, int nm, int after)
  {
    string rawType = Join(t, p, te).Trim();
    string name = t[nm].S;
    var local = MakeLocal(rawType, false);
    string ty = local.Type, cls = local.Class, vec = local.Vector, fg = local.Foreign;
    int semi = StmtEnd(t, after);
    if (local.Pointer) {                                                           // Node * n = ...;
      sb.Append(rawType).Append(' ').Append(name);
      if (Is(t[after], "=")) sb.Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim());
      else if (!Is(t[after], ";")) throw new LowerUnsupported("a declaration of `" + name + "`");
      sb.Append(';');
      cx.Scopes[cx.Scopes.Count - 1][name] = local;
      p = semi + 1;
      return;
    }
    if (fg != null) {                                                              // a library type: fastring s; fastring s = f(..); fastring s(..);
      sb.Append(ty).Append(' ').Append(name);
      if (Is(t[after], "=")) {
        string rhs = Expr(t, after + 1, semi, cx).Trim();
        string rt = Join(t, after + 1, semi).Trim();
        var src = Regex.IsMatch(rt, "^[A-Za-z_]\\w*$") ? cx.Find(rt) : null;
        if (src != null && src.Foreign == fg && !src.IsPtr) {                                          // a copy of another object
          sb.Append(';').Append(' ').Append(fg).Append("_copy(&").Append(name).Append(", &").Append(rt).Append(");");
          local.Droppable = foreign.HasDrop(fg);
          cx.Scopes[cx.Scopes.Count - 1][name] = local;
          p = semi + 1;
          return;
        }
        if (!Regex.IsMatch(Join(t, after + 1, semi), "^\\s*[A-Za-z_][\\w:.]*\\s*\\(")) throw new LowerUnsupported("`" + rawType + " " + name + " = ...`: initialising a library object from something other than a call");
        sb.Append(" = ").Append(rhs).Append(';');
      } else {
        sb.Append(';');
        int argc = Is(t[after], "(") ? ArgCount(t, after + 1, MatchParen(t, after)) : 0;
        string copyOf = argc == 1 ? CopySource(Join(t, after + 1, MatchParen(t, after)).Trim(), fg, cx) : null;
        if (copyOf != null) {                                                       // T x(other): a copy
          sb.Append(' ').Append(fg).Append("_copy(&").Append(name).Append(", ").Append(copyOf).Append(");");
          local.Droppable = foreign.HasDrop(fg);
          cx.Scopes[cx.Scopes.Count - 1][name] = local;
          p = semi + 1;
          return;
        }
        var ctors = foreign.Members(fg, "new");
        if (ctors.Count > 0 || argc > 0) {
          var pr = Pick(ctors, true, argc, fg, "new");
          string args = Is(t[after], "(") ? Args(t, after + 1, MatchParen(t, after), cx, foreign.Declared(pr, true)) : "";
          sb.Append(' ').Append(pr.Name).Append("(&").Append(name).Append(args.Length > 0 ? ", " + args.TrimStart() : "").Append(");");
        }
      }
      local.Droppable = foreign.HasDrop(fg);
    } else if (cls != null) {
      var ci = classes[cls];
      if (Is(t[after], "=")) {                                                      // Node n = f(..): a value from a call
        string rt = Join(t, after + 1, semi).Trim();
        if (!Regex.IsMatch(rt, "^[A-Za-z_][\\w:]*\\s*\\(") || classes.ContainsKey(Regex.Match(rt, "^[A-Za-z_]\\w*(?=\\s*\\()").Value)) throw new LowerUnsupported("`" + rawType + " " + name + " = ...`: initialising an object from something other than a call");
        if (ClassHasDrop(ci)) throw new LowerUnsupported("class " + cls + " owns resources: a value from a call");
        sb.Append(ty).Append(' ').Append(name).Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim()).Append(';');
      } else {
        int argc = Is(t[after], "(") ? ArgCount(t, after + 1, MatchParen(t, after)) : 0;
        var ctors = ci.Ctors.ToList();
        var pick = ctors.FirstOrDefault(c => c.Params.Count == argc);
        if (pick == null && (ctors.Count > 0 || argc > 0)) throw new LowerUnsupported("class " + cls + " has no constructor taking " + argc + " arguments");
        sb.Append(ty).Append(' ').Append(name).Append(';');
        if (pick != null) {
          string args = Is(t[after], "(") ? Args(t, after + 1, MatchParen(t, after), cx, pick.Params) : "";
          sb.Append(' ').Append(FuncName(ci, pick)).Append("(&").Append(name);
          if (args.Length > 0) sb.Append(", ").Append(args.TrimStart());
          sb.Append(");");
        }
      }
      local.Droppable = ClassHasDrop(ci);
    } else if (vec != null) {
      if (Is(t[after], "=")) {                                                      // a vector made by a library or helper function
        string rt = Join(t, after + 1, semi).Trim();
        if (!Regex.IsMatch(rt, "^[A-Za-z_][\\w:]*\\s*\\(")) throw new LowerUnsupported("a `std::vector` initialised from something other than a call");
        sb.Append(ty).Append(' ').Append(name).Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim()).Append(';');
      } else if (!Is(t[after], ";")) throw new LowerUnsupported("a `std::vector` with an initialiser");
      else sb.Append(ty).Append(' ').Append(name).Append("; ").Append(vec).Append("_new(&").Append(name).Append(");");
      local.Droppable = true;
    } else {
      sb.Append(Join(t, p, nm).Trim()).Append(' ').Append(name);
      if (Is(t[after], "=")) sb.Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim());
      else if (!Is(t[after], ";")) throw new LowerUnsupported("a declaration of `" + name + "`");
      sb.Append(';');
    }
    cx.Scopes[cx.Scopes.Count - 1][name] = local;
    p = semi + 1;
  }

  void Return(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int semi = StmtEnd(t, p + 1);
    var cm = Regex.Match(Join(t, p + 1, semi).Trim(), "^([A-Za-z_]\\w*)\\s*\\(");
    if (cm.Success && classes.TryGetValue(cm.Groups[1].Value, out var rc)) {            // return Node(args);
      var rt = Join(t, p + 1, semi).Trim();
      int po = rt.IndexOf('(');
      var ct = Lex(rt);
      int open0 = ct.FindIndex(x => Is(x, "(")), close0 = MatchParen(ct, open0);
      int argc = ArgCount(ct, open0 + 1, close0);
      var pick = rc.Ctors.FirstOrDefault(c => c.Params.Count == argc);
      if (pick == null) throw new LowerUnsupported("class " + rc.Name + " has no constructor taking " + argc + " arguments");
      if (ClassHasDrop(rc)) throw new LowerUnsupported("class " + rc.Name + " owns resources: returned by a constructor call");
      if (cx.Scopes.Any(sc => sc.Values.Any(v => v.Droppable))) throw new LowerUnsupported("returning a constructor call with live objects to destroy");
      string tmp0 = "__cpp_ret" + (ctorRetCounter++);
      string a0 = Args(ct, open0 + 1, close0, cx, pick.Params);
      sb.Append("{ ").Append(rc.Name).Append(' ').Append(tmp0).Append("; ").Append(FuncName(rc, pick)).Append("(&").Append(tmp0).Append(a0.Length > 0 ? ", " + a0 : "").Append("); return ").Append(tmp0).Append("; }");
      p = semi + 1;
      return;
    }
    string expr = Expr(t, p + 1, semi, cx).Trim();
    var drops = new List<string>();
    var moved = Regex.IsMatch(expr, "^[A-Za-z_]\\w*$") ? expr : null;                      // returning a local moves it out
    for (int i = cx.Scopes.Count - 1; i >= 0; i--)
      foreach (var kv in Enumerable.Reverse(cx.Scopes[i].ToList()))
        if (kv.Value.Droppable && kv.Key != moved) drops.Add((kv.Value.Vector ?? kv.Value.Class ?? kv.Value.Foreign) + "_drop(&" + kv.Key + ");");
    bool movedOwned = moved != null && cx.Find(moved) != null && cx.Find(moved).Droppable;       // an owning local that is returned still goes through a temporary
    if (drops.Count == 0 && !movedOwned) {
      sb.Append("return");
      if (expr.Length > 0) sb.Append(' ').Append(expr);
      sb.Append(';');
    } else if (expr.Length == 0) {
      sb.Append("{ ").Append(string.Join(" ", drops)).Append(" return; }");
    } else {
      // cpprust numbers these across the whole unit, after everything it lowered before the program: the caller (see direct_c in
      // crust/test_direct_c.py) turns this placeholder into the next free number.  Step 2, which lowers the whole unit, counts them itself.
      string tmp = "__CCS_RET" + (++retCounter) + "__";
      sb.Append("{ ").Append(cx.Ret).Append(' ').Append(tmp).Append(" = (").Append(expr).Append("); ").Append(drops.Count > 0 ? string.Join(" ", drops) + " " : "").Append("return ").Append(tmp).Append("; }");
    }
    p = semi + 1;
  }

  // ---- expressions

  static bool PrevIsAccess(List<Tok> t, int a, int i)
  {
    int j = i - 1;
    while (j >= a && !IsSig(t[j])) j--;
    return j >= a && (Is(t[j], ".") || Is(t[j], "->") || Is(t[j], "::"));
  }

  /** Tokens [a, b) of an expression, lowered: `this->` on fields, method calls as functions, `Cls::M` as `Cls_M`, `&` where a callee takes a reference. */
  string Expr(List<Tok> t, int a, int b, Ctx cx)
  {
    var sb = new StringBuilder();
    int i = a;
    while (i < b) {
      var tok = t[i];
      if (tok.Kind != K.Ident) { sb.Append(tok.S); i++; continue; }
      string id = tok.S;
      int n1 = NextSig(t, i + 1);
      bool afterAccess = PrevIsAccess(t, a, i);
      if (n1 > b) n1 = -1;
      if (!afterAccess && classes.TryGetValue(id, out var qc) && n1 >= 0 && Is(t[n1], "::")) {            // Cls::M(args)
        int mi = NextSig(t, n1 + 1);
        int op = mi < 0 ? -1 : NextSig(t, mi + 1);
        if (mi >= 0 && qc.Statics.Any(x => x.Name == t[mi].S) && (op < 0 || op >= b || !Is(t[op], "("))) { sb.Append(id).Append('_').Append(t[mi].S); i = mi + 1; continue; }
        if (mi < 0 || op < 0 || !Is(t[op], "(")) throw new LowerUnsupported(id + "::" + (mi >= 0 ? t[mi].S : "") + " without a call");
        var meth = qc.Methods.FirstOrDefault(m => m.Name == t[mi].S);
        if (meth == null) throw new LowerUnsupported(id + "::" + t[mi].S + ": no such method");
        if (!meth.Static) throw new LowerUnsupported(id + "::" + t[mi].S + ": a qualified call of an instance method");
        int close = MatchParen(t, op);
        sb.Append(id).Append('_').Append(meth.Name).Append('(').Append(Args(t, op + 1, close, cx, meth.Params)).Append(')');
        i = close + 1;
        continue;
      }
      if (!afterAccess && foreign != null && cx.Find(id) == null && foreign.Types.Contains(id) && n1 >= 0 && Is(t[n1], "::")) {        // Type::M(args), a library type's
        int mi = NextSig(t, n1 + 1);
        int op = mi < 0 ? -1 : NextSig(t, mi + 1);
        if (mi < 0 || op < 0 || !Is(t[op], "(")) throw new LowerUnsupported(id + "::" + (mi >= 0 ? t[mi].S : "") + " without a call");
        int close = MatchParen(t, op);
        var pr = Pick(foreign.Members(id, t[mi].S), false, ArgCount(t, op + 1, close), id, t[mi].S);
        sb.Append(pr.Name).Append('(').Append(Args(t, op + 1, close, cx, foreign.Declared(pr, false))).Append(')');
        i = close + 1;
        continue;
      }
      var loc = afterAccess ? null : cx.Find(id);
      if (loc != null && loc.Alias != null) {                                                            // a reference loop variable: the element itself
        int an = NextSig(t, i + 1);
        int am = an < 0 ? -1 : NextSig(t, an + 1);
        int ao = am < 0 ? -1 : NextSig(t, am + 1);
        if (an < 0 || an >= b || !Is(t[an], ".") || am < 0 || (ao >= 0 && ao < b && Is(t[ao], "("))) throw new LowerUnsupported("`" + id + "`, a reference loop variable, used other than as `" + id + ".field`");
        sb.Append(loc.Alias).Append('.');
        i = am;
        continue;
      }
      if (loc != null && (loc.Class != null || loc.Vector != null || loc.Foreign != null)) {
        string idt = loc.PtrRef ? "(*" + id + ")" : id;
        i = ObjectUse(t, i + 1, b, loc, idt, cx, sb, loc.IsPtr && loc.Class == null && loc.Vector == null ? "(*" + id + ")" : idt);
        continue;
      }
      if (loc != null) { sb.Append(loc.IsPtr && loc.Class == null && loc.Vector == null ? "(*" + id + ")" : id); i++; continue; }
      if (!afterAccess && cx.Cls != null) {                                                              // a member of this class, written bare
        bool call = n1 >= 0 && Is(t[n1], "(");
        if (!call) {
          var sfld = cx.Cls.Statics.FirstOrDefault(x => x.Name == id);
          if (sfld != null && !IsPrimitive(sfld.Type)) {
            string gname = cx.Cls.Name + "_" + id;
            i = ObjectUse(t, i + 1, b, MakeLocal(sfld.Type, false), gname, cx, sb, gname);
            continue;
          }
          if (sfld != null) { sb.Append(cx.Cls.Name).Append('_').Append(id); i++; continue; }
          var ofld = cx.Cls.Fields.FirstOrDefault(x => x.Name == id && !IsPrimitive(x.Type));
          if (ofld != null) {
            if (cx.IsStatic) throw new LowerUnsupported("field " + id + " used in a static method");
            i = ObjectUse(t, i + 1, b, MakeLocal(ofld.Type, false), "this->" + id, cx, sb, "this->" + id);
            continue;
          }
          if (cx.Cls.Fields.Any(x => x.Name == id)) {
            if (cx.IsStatic) throw new LowerUnsupported("field " + id + " used in a static method");
            sb.Append("this->").Append(id);
            i++;
            continue;
          }
        } else {
          var meth = cx.Cls.Methods.FirstOrDefault(m => m.Name == id);
          if (meth != null) {
            int close = MatchParen(t, n1);
            string a2 = Args(t, n1 + 1, close, cx, meth.Params);
            if (meth.Static) sb.Append(cx.Cls.Name).Append('_').Append(id).Append('(').Append(a2).Append(')');
            else sb.Append(cx.Cls.Name).Append('_').Append(id).Append("(this").Append(a2.Length > 0 ? ", " + a2.TrimStart() : "").Append(')');
            i = close + 1;
            continue;
          }
        }
      }
      if (!afterAccess && loc == null && n1 >= 0 && n1 < b && Is(t[n1], "(") && helpers.TryGetValue(id, out var hf) && (cx.Cls == null || !cx.Cls.Methods.Any(m => m.Name == id))) {     // a helper function
        int close = MatchParen(t, n1);
        sb.Append(id).Append('(').Append(Args(t, n1 + 1, close, cx, hf.Params)).Append(')');
        i = close + 1;
        continue;
      }
      if (!afterAccess && foreign != null && loc == null && n1 >= 0 && Is(t[n1], "(") && foreign.ByName.TryGetValue(id, out var fp) && !foreign.Types.Contains(id)) {   // a library function
        if (fp.Count != 1) throw new LowerUnsupported(id + ": several prototypes");
        int close = MatchParen(t, n1);
        sb.Append(id).Append('(').Append(Args(t, n1 + 1, close, cx, foreign.Declared(fp[0], false))).Append(')');
        i = close + 1;
        continue;
      }
      if (!afterAccess && cx.Find(id) == null && classes.ContainsKey(id) && n1 >= 0 && Is(t[n1], "(")) throw new LowerUnsupported("a constructor call `" + id + "(...)` inside an expression");
      if (!afterAccess && id == "NULL") { sb.Append("((void *)0)"); i++; continue; }
      if (!afterAccess && id == "new" && n1 >= 0 && n1 < b && t[n1].Kind == K.Ident && classes.TryGetValue(t[n1].S, out var nc) && IsArena(nc)) {      // new Cls(args): the next slot of its arena
        int op = NextSig(t, n1 + 1);
        if (op < 0 || op >= b || !Is(t[op], "(")) throw new LowerUnsupported("`new " + nc.Name + "` without a constructor call");
        int close = MatchParen(t, op);
        int argc = ArgCount(t, op + 1, close);
        var pick = nc.Ctors.FirstOrDefault(c => c.Params.Count == argc);
        if (pick == null) throw new LowerUnsupported("class " + nc.Name + " has no constructor taking " + argc + " arguments");
        string a3 = Args(t, op + 1, close, cx, pick.Params);
        sb.Append(AllocName(nc, pick)).Append('(').Append(a3.TrimStart()).Append(')');
        i = close + 1;
        continue;
      }
      if (!afterAccess && (id == "new" || id == "delete" || id == "template" || (id == "this" && (cx.Cls == null || cx.IsStatic)))) throw new LowerUnsupported("`" + id + "` in an expression");
      if (!afterAccess && id == "std") throw new LowerUnsupported("`std::` in an expression: the standard library is cpprust's here yet");
      sb.Append(id);
      i++;
    }
    return sb.ToString();
  }

  /** An object-typed expression (`text`: a local, `this->f`, `b.f`) and what may follow it: `[i]`, `.m(args)`, `.field`.  Returns the index after what it consumed. */
  int ObjectUse(List<Tok> t, int after, int b, Local l, string text, Ctx cx, StringBuilder sb, string bare)
  {
    int n = NextSig(t, after);
    if (n < 0 || n >= b) { sb.Append(bare); return after; }
    string recv = l.IsPtr ? text : "&" + text;
    if (l.Vector != null && Is(t[n], "[")) {
      int close = n, depth = 0;
      for (int k = n; k < b; k++) { if (Is(t[k], "[")) depth++; else if (Is(t[k], "]") && --depth == 0) { close = k; break; } }
      string el = "(*" + l.Vector + "__index(" + recv + ", " + Expr(t, n + 1, close, cx).Trim() + "))";
      if (l.Elem != null) {
        var el2 = new Local { Type = l.Elem, Class = classes.ContainsKey(l.Elem) ? l.Elem : null, Foreign = classes.ContainsKey(l.Elem) ? null : l.Elem };
        return ObjectUse(t, close + 1, b, el2, el, cx, sb, el);
      }
      sb.Append(el);
      return close + 1;
    }
    if (Is(t[n], ".") || Is(t[n], "->")) {
      int mi = NextSig(t, n + 1);
      int op = mi < 0 ? -1 : NextSig(t, mi + 1);
      if (op >= 0 && op < b && Is(t[op], "(")) {
        int close = MatchParen(t, op);
        if (l.Class != null) {
          var meth = classes[l.Class].Methods.FirstOrDefault(m => m.Name == t[mi].S);
          if (meth == null) throw new LowerUnsupported(l.Class + "." + t[mi].S + ": no such method");
          string a2 = Args(t, op + 1, close, cx, meth.Params);
          sb.Append(l.Class).Append('_').Append(meth.Name).Append('(').Append(recv).Append(a2.Length > 0 ? ", " + a2.TrimStart() : "").Append(')');
        } else if (l.Vector != null) {
          List<Param> vps = null;
          if (l.Elem != null) {
            if (t[mi].S != "push_back" && t[mi].S != "size" && t[mi].S != "clear" && t[mi].S != "empty" && t[mi].S != "reserve" && t[mi].S != "pop_back") throw new LowerUnsupported("`" + t[mi].S + "` on a vector of objects");
            if (t[mi].S == "push_back") vps = new List<Param> { new Param { Type = "const " + l.Elem + " *", Name = "v", IsRef = true } };
          }
          string a2 = Args(t, op + 1, close, cx, vps);
          sb.Append(l.Vector).Append('_').Append(t[mi].S == "erase" ? "erase_" + ArgCount(t, op + 1, close) : t[mi].S).Append('(').Append(recv).Append(a2.Length > 0 ? ", " + a2.TrimStart() : "").Append(')');
        } else if (l.Foreign != null) {
          var pr = Pick(foreign.Members(l.Foreign, t[mi].S), true, ArgCount(t, op + 1, close), l.Foreign, t[mi].S);
          string a2 = Args(t, op + 1, close, cx, foreign.Declared(pr, true));
          sb.Append(pr.Name).Append('(').Append(recv).Append(a2.Length > 0 ? ", " + a2.TrimStart() : "").Append(')');
        } else throw new LowerUnsupported("a method call on `" + text + "` of type " + l.Type);
        return close + 1;
      }
      string acc = l.IsPtr ? "->" : ".";
      if (l.Class != null && mi >= 0) {
        var fld = classes[l.Class].Fields.FirstOrDefault(f => f.Name == t[mi].S);
        if (fld != null && !IsPrimitive(fld.Type)) {
          string ft = text + acc + t[mi].S;
          return ObjectUse(t, mi + 1, b, MakeLocal(fld.Type, false), ft, cx, sb, ft);
        }
      }
      sb.Append(text).Append(acc);
      return mi;
    }
    sb.Append(bare);
    return after;
  }

  static int ArgCount(List<Tok> t, int a, int b)
  {
    int n = 0, start = a, depth = 0;
    for (int i = a; i <= b; i++) {
      bool end = i == b;
      if (!end) {
        if (depth == 0 && Is(t[i], ",")) end = true;
        else if (Is(t[i], "(") || Is(t[i], "[") || Is(t[i], "{")) depth++;
        else if (Is(t[i], ")") || Is(t[i], "]") || Is(t[i], "}")) depth--;
      }
      if (!end) continue;
      if (Join(t, start, i).Trim().Length > 0) n++;
      start = i + 1;
    }
    return n;
  }

  /** The library function that takes this many arguments (cpprust numbers overloads; telling them apart by type is not lowered here yet). */
  Proto Pick(List<Proto> cands, bool inst, int argc, string type, string what)
  {
    var ok = cands.Where(x => foreign.IsInst(x, type) == inst && x.Params.Count - (inst ? 1 : 0) == argc).ToList();
    if (ok.Count == 1) return ok[0];
    throw new LowerUnsupported(type + (inst ? "." : "::") + what + (ok.Count == 0 ? ": no such function with " + argc + " argument(s) in the library" : ": several overloads take " + argc + " argument(s)"));
  }

  /** The arguments of a call, lowered, joined with ", ".  An argument for a reference parameter is passed by address. */
  int baCounter, ctorRetCounter, tmpCounter;

  string Args(List<Tok> t, int a, int b, Ctx cx, List<Param> ps)
  {
    var parts = new List<string>();
    int start = a, depth = 0;
    for (int i = a; i <= b; i++) {
      bool end = i == b;
      if (!end) {
        if (depth == 0 && Is(t[i], ",")) end = true;
        else if (Is(t[i], "(") || Is(t[i], "[") || Is(t[i], "{")) depth++;
        else if (Is(t[i], ")") || Is(t[i], "]") || Is(t[i], "}")) depth--;
      }
      if (!end) continue;
      string text = Join(t, start, i).Trim();
      if (text.Length > 0) {
        int k = parts.Count;
        string low;
        if (ps != null && k < ps.Count && ps[k].IsRef) {
          var l = cx.Find(text);
          if (l != null) low = l.IsPtr && !(l.Pointer && !l.PtrRef) ? text : "&" + text;
          else if (cx.Cls != null && !cx.IsStatic && cx.Cls.Fields.Any(f => f.Name == text)) low = "&this->" + text;                  // a field of this
          else {
            var fm = Regex.Match(text, "^(\\w+)\\.(\\w+)$");                                                                         // local.field
            var ol = fm.Success ? cx.Find(fm.Groups[1].Value) : null;
            if (ol == null || ol.Class == null || !classes[ol.Class].Fields.Any(f => f.Name == fm.Groups[2].Value)) throw new LowerUnsupported("a reference argument that is not a plain variable: " + text);
            low = "&" + fm.Groups[1].Value + (ol.IsPtr ? "->" : ".") + fm.Groups[2].Value;
          }
        } else {
          low = Expr(t, start, i, cx).Trim();
          var l = ps != null && k < ps.Count && ps[k].Name != "" && !ps[k].IsRef && Regex.IsMatch(text, "^[A-Za-z_]\\w*$") ? cx.Find(text) : null;
          if (l != null && l.Foreign != null && l.Droppable && !l.IsPtr) {                      // a by-value argument is a copy
            string bn = "_cpp_ba" + (++baCounter);
            low = (parts.Count == 0 ? " " : "") + "({ " + l.Foreign + " " + bn + "; " + l.Foreign + "_copy(&" + bn + ", &(" + text + ")); " + bn + "; })";
          }
        }
        parts.Add(low);
      }
      start = i + 1;
    }
    return string.Join(", ", parts);
  }
}
