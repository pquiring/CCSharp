/**

  CC# Cross-Compiler

  Converts C# to C++ using Roslyn.

  Author : Peter Quiring

*/

using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

namespace CCSharpCompiler;

  class Program
  {
    public static string csFolder;
    public static string cppFolder;
    public static string hppFile;
    public static List<Source> files = new List<Source>();
    public static bool printTree = false;
    public static bool printToString = false;
    public static bool printTokens = false;
    public static string version = "0.1";
    public static bool library;
    public static bool shared;
    public static bool service;
    public static bool windows;
    public static bool linux;
    public static bool console;
    public static String serviceName;
    public static string target;
    public static string ext_obj, ext_lib, ext_exe, path_sep;
    public static bool corelib;
    public static bool qt5lib;
    public static string main;
    public static string home = ".";
    public static StringBuilder ninja_header = new StringBuilder();
    public static StringBuilder ninja_cpp = new StringBuilder();
    public static StringBuilder ninja_target = new StringBuilder();
    public static bool debug = false;
    public static bool no_npe_checks = false;
    public static bool no_abe_checks = false;
    public static List<string> refs = new List<string>();
    public static List<string> libs = new List<string>();

    public static CSharpCompilation compiler;

    static void Main(string[] args)
    {
      if (args.Length < 2) {
        Console.WriteLine("CC# Compiler/" + version);
        Console.WriteLine("Usage : CCSharpCompiler cs_folder project_name [options]");
        Console.WriteLine("options:");
        Console.WriteLine("  --library");
        Console.WriteLine("    create a library");
        Console.WriteLine("  --shared");
        Console.WriteLine("    create a shared library (dll,so)");
        Console.WriteLine("  --main=class");
        Console.WriteLine("    define class that contains static void Main(String[] args)");
        Console.WriteLine("  --service=name");
        Console.WriteLine("    create a Windows Service");
        Console.WriteLine("  --ref=dll");
        Console.WriteLine("    add a reference to a library");
        Console.WriteLine("  --qt5");
        Console.WriteLine("    add Qt5 libraries");
        Console.WriteLine("  --home=folder");
        Console.WriteLine("    define where CC# home folder is (to find include and lib folders)");
        Console.WriteLine("  --print[=tokens,tostring,all]");
        Console.WriteLine("    print out C# Roslyn SyntaxNode trees");
        Console.WriteLine("  --release | --debug");
        Console.WriteLine("    generate release or debug type");
        Console.WriteLine("  --no-npe-checks");
        Console.WriteLine("    disable NPE checks");
        Console.WriteLine("  --no-abe-checks");
        Console.WriteLine("    disable ABE checks");
        Console.WriteLine("  --console");
        Console.WriteLine("    create console app");
        return;
      }

      for(int a=2;a<args.Length;a++) {
        int idx = args[a].IndexOf("=");
        String arg;
        String value;
        if (idx == -1) {
          arg = args[a];
          value = "";
        } else {
          arg = args[a].Substring(0, idx);
          value = args[a].Substring(idx + 1);
        }
        if (arg == "--library") {
          library = true;
        }
        if (arg == "--shared") {
          shared = true;
        }
        if (arg == "--service") {
          service = true;
          if (value.Length == 0) {
            Console.WriteLine("Error:--service requires a name");
            return;
          }
          serviceName = value;
        }
        if (arg == "--corelib") {
          corelib = true;
        }
        if (arg == "--qt5") {
          qt5lib = true;
        }
        if (arg == "--main") {
          if (value.Length == 0) {
            Console.WriteLine("Error:--main requires a class");
            return;
          }
          main = value.Replace(".", "::");
        }
        if (arg == "--console") {
          console = true;
        }
        if (arg == "--no-npe-checks") {
          no_npe_checks = true;
        }
        if (arg == "--no-abe-checks") {
          no_abe_checks = true;
        }
        if (arg == "--ref") {
          if (value.Length == 0) {
            Console.WriteLine("Error:--ref requires a file");
            return;
          }
          value = value.Replace("\\", "/");
          refs.Add(value);
          int i1 = value.LastIndexOf("/");
          if (i1 != -1) {
            value = value.Substring(i1+1);
          }
          int i2 = value.IndexOf(".");
          value = value.Substring(0, i2);
          libs.Add(value);
        }
        if (arg == "--home") {
          if (value.Length == 0) {
            Console.WriteLine("Error:--home requires a path");
            return;
          }
          home = value.Replace("\\", "/");
        }
        if (arg == "--print") {
          printTree = true;
          switch (value) {
            case "all": printToString = true; printTokens = true; break;
            case "tokens": printTokens = true; break;
            case "tostring": printToString = true; break;
          }
        }
        if (arg == "--debug") {
          debug = true;
        }
        if (arg == "--release") {
          debug = false;
        }
      }
      if (shared && !library) {
        Console.WriteLine("Error:--shared requires --library");
        return;
      }
      if (corelib && !library) {
        Console.WriteLine("Error:--corelib requires --library");
        return;
      }
      if (shared && main == null) {
        Console.WriteLine("Error:--shared requires --main");
        return;
      }
      if (service && library) {
        Console.WriteLine("Error:--service can not be a --library");
        return;
      }
      if (service && main == null) {
        Console.WriteLine("Error:--service requires --main");
        return;
      }
      if (!library && main == null) {
        Console.WriteLine("Error:application requires --main");
        return;
      }
      csFolder = args[0];
      target = args[1];
      cppFolder = "cpp";
      hppFile = target + ".hpp";
      windows = IsWindows();
      linux = !windows;
      if (windows) {
        ext_obj = ".obj";
        ext_lib = ".lib";
        ext_exe = ".exe";
        path_sep = "\\";
      } else {
        ext_obj = ".o";
        ext_lib = ".a";
        ext_exe = "";
        path_sep = "/";
      }
      if (windows) {
        BuildNinjaWindows();
      } else {
        BuildNinjaLinux();
      }
      new Program().Process();
      Console.WriteLine("CCSharp generated " + target);
    }

    public static bool IsWindows()
    {
      return Environment.GetEnvironmentVariable("WINVER") != null;
    }

    void Process()
    {
      compiler = CSharpCompilation.Create("C#");
      if (true) {
        //allow unsafe operations
        CSharpCompilationOptions options = compiler.Options.WithAllowUnsafe(true);
        compiler = compiler.WithOptions(options);
      }
      if (library) {
        CSharpCompilationOptions options = compiler.Options.WithOutputKind(OutputKind.DynamicallyLinkedLibrary);
        compiler = compiler.WithOptions(options);
      }
      foreach(var lib in refs) {
        Console.WriteLine("Adding Reference:" + lib);
        compiler = compiler.AddReferences(MetadataReference.CreateFromFile(lib));
      }
      //TODO : use DiagnosticSuppressor to suppress CS0626(extern without DllImport) and CS0227(unsafe usage) (not working yet - need help!)
      //this doesn't work! Ignore them when printing diagnostics
      //compiler = (CSharpCompilation)compiler.WithAnalyzers(ImmutableArray.Create(new DiagnosticAnalyzer[] {new DiagnosticSuppressorCS0626(), new DiagnosticSuppressorCS0227()})).Compilation;
      AddFolder(csFolder);
      foreach(Source node in files)
      {
        node.model = compiler.GetSemanticModel(node.tree);
        if (printTree) {
          PrintNodes(node, node.tree.GetRoot().ChildNodes(), 0, true);
        }
      }
      //generate assembly
      EmitResult results = compiler.Emit(File.Create(target + ".dll"));
      foreach(Diagnostic diag in results.Diagnostics) {
        switch (diag.Id) {
          case "CS0626": continue;  //extern without DllImport
        }
        if (diag.Location.SourceTree != null) {
          Console.WriteLine(FindTree(diag.Location.SourceTree));
        }
        Console.WriteLine(diag.ToString());
      }
      if (!results.Success) {
        Console.WriteLine("Compiler Errors Detected!");
        Environment.Exit(1);
      }
      try {
        new Generate().GenerateSources();
      } catch (Exception e) {
        Console.WriteLine(e.ToString());
      }
    }

    void AddFolder(string folder)
    {
      string[] files = Directory.GetFiles(folder);
      foreach(var file in files) {
        if (file.EndsWith(".cs") && !file.Contains("NETCoreApp")) AddFile(file);
      }
      string[] folders = Directory.GetDirectories(folder);
      foreach(var subfolder in folders) {
        AddFolder(subfolder);
      }
    }

    void AddFile(string file)
    {
      if (file.IndexOf("AssemblyInfo") != -1) return;
      if (linux) {
        file = file.Replace("\\", "/");
      }
      Console.WriteLine("build:" + file);
      Source node = new Source();
      node.src = System.IO.File.ReadAllText(file);
      node.csFile = file;
      node.nativeFile = file.Substring(0, file.Length - 3) + ".cpp";
      string baseFile = file.Substring(csFolder.Length + 1);
      baseFile = baseFile.Substring(0, baseFile.Length - 3).Replace(".", "_").Replace(path_sep, "_");
      node.cppFile = cppFolder + path_sep + baseFile + ".cpp";
      node.clss = new List<Class>();
      ninja_cpp.Append("build obj/" + baseFile + ext_obj + " : cpp " + node.cppFile + "\r\n");
      ninja_target.Append(" obj/" + baseFile + ext_obj);
      node.tree = CSharpSyntaxTree.ParseText(node.src);
      compiler = compiler.AddSyntaxTrees(node.tree);
      files.Add(node);
    }

    string FindTree(SyntaxTree tree) {
      foreach(var node in files) {
        if (node.tree == tree) {
          return node.csFile;
        }
      }
      return "?";
    }

    void PrintNodes(Source file, IEnumerable<SyntaxNode> nodes, int lvl, bool showDiag)
    {
      int idx = 0;
      if (showDiag) {
        String diags = "";
        foreach(var diag in file.model.GetDiagnostics()) {
          diags += ",diag=" + diag.ToString();
        }
        foreach(var diag in file.model.GetSyntaxDiagnostics()) {
          diags += ",syntaxdiag=" + diag.ToString();
        }
        foreach(var diag in file.model.GetDeclarationDiagnostics()) {
          diags += ",decldiag=" + diag.ToString();
        }
        foreach(var diag in file.model.GetMethodBodyDiagnostics()) {
          diags += ",methoddiag=" + diag.ToString();
        }
        if (diags.Length > 0) {
          Console.WriteLine("Errors in file:" + file.csFile);
          Console.WriteLine(diags);
        }
      }
      foreach(var node in nodes) {
        PrintNode(file, node, lvl, idx);
        if (printTokens) PrintTokens(file, node.ChildTokens(), lvl);
        PrintNodes(file, node.ChildNodes(), lvl+1, false);
        idx++;
      }
    }

    public static void PrintNode(Source file, SyntaxNode node, int lvl, int idx) {
      for(int a=0;a<lvl;a++) {
        Console.Write("  ");
      }
      String ln = "node[" + lvl + "][" + idx + "]=" + node.Kind();
      ISymbol decl = file.model.GetDeclaredSymbol(node);
      if (decl != null) {
        ln += ",DeclSymbol=" + decl.ToString();
        ln += ",DeclSymbol.Name=" + decl.Name;
        ln += ",DeclSymbol.Kind=" + decl.Kind;
        ln += ",DeclSymbol.IsStatic=" + decl.IsStatic;
        ln += ",DeclSymbol.IsAbstract=" + decl.IsAbstract;
        ITypeSymbol containing = decl.ContainingType;
        if (containing != null) {
          ln += ",DeclSymbol.ContainingType.TypeKind=" + containing.TypeKind;
        }
      }
      ISymbol symbol = file.model.GetSymbolInfo(node).Symbol;
      if (symbol != null) {
        ln += ",Symbol=" + symbol.ToString();
        ln += ",Symbol.Name=" + symbol.Name;
        ln += ",Symbol.Kind=" + symbol.Kind;
        ln += ",Symbol.IsStatic=" + symbol.IsStatic;
        ITypeSymbol containing = symbol.ContainingType;
        if (containing != null) {
          ln += ",Symbol.ContainingType.TypeKind=" + containing.TypeKind;
        }
      }
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type != null) {
        ln += ",Type=" + type.ToString();
        ln += ",Type.IsStatic=" + type.IsStatic;
        ln += ",Type.Kind=" + type.Kind;
        ln += ",Type.TypeKind=" + type.TypeKind;
        ln += ",Type.OrgDef=" + type.OriginalDefinition;
        ln += ",Type.SpecialType=" + type.SpecialType;
        INamedTypeSymbol baseType = type.BaseType;
        if (baseType != null) {
          ln += ",Type.BaseType=" + baseType;
          ln += ",Type.BaseType.Name=" + baseType.Name;
          ln += ",Type.BaseType.TypeKind=" + baseType.TypeKind;
          ln += ",Type.BaseType.OrgDef=" + baseType.OriginalDefinition;
          ln += ",Type.BaseType.SpecialType=" + baseType.SpecialType;
        }
      }
      Object value = file.model.GetConstantValue(node).Value;
      if (value != null) {
        ln += ",Constant=" + value.ToString().Replace("\r", "").Replace("\n", "");
      }
      if (printToString) {
        ln += ",ToString=" + node.ToString().Replace("\r", "").Replace("\n", "");
      }
      Console.WriteLine(ln);
    }

    void PrintTokens(Source file, IEnumerable<SyntaxToken> tokens, int lvl)
    {
      int idx = 0;
      foreach(var token in tokens) {
        for(int a=0;a<lvl+2;a++) {
          Console.Write("  ");
        }
        Console.WriteLine("token[" + lvl + "][" + idx + "]=" + token + ":" + token.Kind());
        idx++;
      }
    }

    public static void BuildNinjaWindows() {
      ninja_header.Append("cflags = /nologo /EHsc /MD /std:c++17");
      if (debug) {
        ninja_header.Append(" /Zi");
      } else {
        ninja_header.Append(" /O2");
      }
      if (no_npe_checks) {
        ninja_header.Append(" /DCCSHARP_NO_NPE_CHECKS");
      }
      if (no_abe_checks) {
        ninja_header.Append(" /DCCSHARP_NO_ABE_CHECKS");
      }
      ninja_header.Append(" /I " + home + "\\include");
      ninja_header.Append(" /I .");
      ninja_header.Append("\r\n");
      ninja_header.Append("linkflags = /link /LIBPATH:" + home + "\\lib");
      if (Program.console) {
        ninja_header.Append(" /subsystem:console");
      }
      ninja_header.Append("\r\n");
      ninja_header.Append("libs =");
      if (qt5lib) {
        ninja_header.Append(" qt5core.lib qt5network.lib");
      }
      foreach(string lib in libs) {
        ninja_header.Append(" ");
        ninja_header.Append(lib);
        ninja_header.Append(".lib");
      }
      ninja_header.Append("\r\n");
      ninja_header.Append("dllflags = /LD");
      if (debug) {
        ninja_header.Append("d");
      }
      ninja_header.Append("\r\n");
      ninja_header.Append("rule cpp\r\n");
      ninja_header.Append("  command = cl.exe $cflags /c $in /Fo:$out");
      if (debug) {
        ninja_header.Append(" /Fd$out.pdb");
      }
      ninja_header.Append(" $linkflags");
      ninja_header.Append("\r\n");
      ninja_header.Append("rule exe\r\n");
      ninja_header.Append("  command = cl.exe $cflags $in $libs /Fe:$out");
      if (debug) {
        ninja_header.Append(" /Fd$out.pdb");
      }
      ninja_header.Append(" $linkflags");
      ninja_header.Append("\r\n");
      ninja_header.Append("rule dll\r\n");
      ninja_header.Append("  command = cl.exe $cflags $dllflags $in $libs /Fe:$out");
      if (debug) {
        ninja_header.Append(" /Fd$out.pdb");
      }
      ninja_header.Append(" $linkflags");
      ninja_header.Append("\r\n");
      ninja_header.Append("rule lib\r\n");
      ninja_header.Append("  command = lib.exe $in /out:$out\r\n");

      if (library) {
        ninja_target.Append("build " + target + ".lib : lib");
      } else {
        ninja_target.Append("build " + target + ".exe : exe");
      }
    }

    public static void BuildNinjaLinux() {
      ninja_header.Append("cflags = -std=c++17 -fPIC");
      if (debug) {
        ninja_header.Append(" -g");
      } else {
        ninja_header.Append(" -O");
      }
      if (no_npe_checks) {
        ninja_header.Append(" -DCCSHARP_NO_NPE_CHECKS");
      }
      if (no_abe_checks) {
        ninja_header.Append(" -DCCSHARP_NO_ABE_CHECKS");
      }
      if (Program.qt5lib) {
        ninja_header.Append(" -I /usr/include/x86_64-linux-gnu/qt5/QtCore");
        ninja_header.Append(" -I /usr/include/x86_64-linux-gnu/qt5");
      }
      ninja_header.Append(" -I " + home + "/include");
      ninja_header.Append(" -I .");
      ninja_header.Append("\n");
      ninja_header.Append("linkflags = -lstdc++ -lpthread -L " + home + "/lib");
      if (Program.console) {
        ninja_header.Append("");
      }
      if (qt5lib) {
        ninja_header.Append(" -L /usr/lib/x86_64-linux-gnu");
      }
      ninja_header.Append("\n");
      ninja_header.Append("libs =");
      if (qt5lib) {
        ninja_header.Append(" -lQt5Core -lQt5Network");
      }
      foreach(string lib in libs) {
        ninja_header.Append(" -l");
        ninja_header.Append(lib);
      }
      ninja_header.Append("\n");
      ninja_header.Append("dllflags = ");
      if (debug) {
        ninja_header.Append("");
      }
      ninja_header.Append("\n");
      ninja_header.Append("rule cpp\n");
      ninja_header.Append("  command = gcc $cflags $linkflags -c $in -o $out\n");
      ninja_header.Append("rule exe\n");
      ninja_header.Append("  command = gcc $cflags $linkflags $in $libs -o $out\n");
      ninja_header.Append("rule dll\n");
      ninja_header.Append("  command = gcc $cflags $linkflags $dllflags $in $libs -o $out\n");
      ninja_header.Append("rule lib\n");
      ninja_header.Append("  command = ar qf $out $in\n");

      if (library) {
        ninja_target.Append("build " + target + ".a : lib");
      } else {
        ninja_target.Append("build " + target + " : exe");
      }
    }

  }

  class Source
  {
    public string csFile;
    public string cppFile;
    public string nativeFile;
    public string src;
    public SyntaxTree tree;
    public SemanticModel model;
    public List<Class> clss;

    public bool UpToDate() {
      if (Program.linux) return false;  //test
      if (!File.Exists(cppFile)) return false;
      {
        FileInfo src = new FileInfo(csFile);
        FileInfo dest = new FileInfo(cppFile);
        if (src.LastWriteTime > dest.LastWriteTime) {
          return false;
        }
      }
      //check native file is exists
      if (File.Exists(nativeFile)) {
        FileInfo src = new FileInfo(nativeFile);
        FileInfo dest = new FileInfo(cppFile);
        if (src.LastWriteTime > dest.LastWriteTime) {
          return false;
        }
      }
      return true;
    }
  }

  class Generate
  {
    public static Source file;
    public static int errors = 0;

    private FileStream fs;
    private string Namespace = "";
    private readonly Class NoClass = new Class();  //for classless delegates
    private readonly Class OpClass = new Class();  //for operators
    private readonly List<Class> clss = new List<Class>();

    public static Class cls;
    public Method method;
    public Method init;
    public Field field;

    public void GenerateSources()
    {
      if (Program.printTree) {
        Console.WriteLine();
      }
      foreach(Source file in Program.files) {
        GenerateSource(file);
      }
      if (errors > 0) {
        Console.WriteLine("Errors:" + errors);
        Environment.Exit(1);
      }
      Directory.CreateDirectory("cpp");
      OpenOutput("cpp/" + Program.hppFile);
      WriteForward();
      /** In C++ you can not use an undefined class, so they must be sorted by usage. */
      BuildClasses();
      CheckClasses();
      if (errors > 0) {
        Console.WriteLine("Errors:" + errors);
        Environment.Exit(1);
      }
      if (Program.corelib) {
        //move some base classes to the top
        SortCoreLib();
      }
      while (SortClasses()) {};
      //TODO : sort inner classes
      WriteNoClassTypes();
      WriteClasses();
      WriteOperators();
      WriteEndIf();
      CloseOutput();
      foreach(Source file in Program.files) {
        //check if file needs to be updated
        if (file.UpToDate()) continue;
        CCSharpCompiler.Generate.file = file;
        OpenOutput(file.cppFile);
        WriteIncludes();
        IncludeCPPCode();
        WriteStaticFields();
        WriteMethods();
        CloseOutput();
      }
      if (Program.library) {
        if (File.Exists("library.cpp")) {
          WriteLibrary();
        }
      }
      Program.ninja_cpp.Append("build obj/ctor.obj : cpp cpp/ctor.cpp\r\n");
      Program.ninja_target.Append(" obj/ctor.obj");
      OpenOutput("cpp/ctor.cpp");
      WriteIncludes();
      WriteStaticFieldsInit();
      CloseOutput();
      if (Program.main != null) {
        Program.ninja_cpp.Append("build obj/main.obj : cpp cpp/main.cpp\r\n");
        Program.ninja_target.Append(" obj/main.obj");
        OpenOutput("cpp/main.cpp");
        if (!Program.library) {
          WriteMain();
        } else {
          WriteLibraryMain();
        }
        CloseOutput();
      }
      OpenOutput("build.ninja");
      WriteNinja();
      CloseOutput();
    }

    private void WriteNinja() {
      Program.ninja_target.Append("\r\n");
      byte[] bytes_header = new UTF8Encoding().GetBytes(Program.ninja_header.ToString());
      fs.Write(bytes_header, 0, bytes_header.Length);
      byte[] bytes_cpp = new UTF8Encoding().GetBytes(Program.ninja_cpp.ToString());
      fs.Write(bytes_cpp, 0, bytes_cpp.Length);
      byte[] bytes_target = new UTF8Encoding().GetBytes(Program.ninja_target.ToString());
      fs.Write(bytes_target, 0, bytes_target.Length);
    }

    private void GenerateSource(Source file)
    {
      SyntaxNode root = file.tree.GetRoot();
      if (Program.printTree) {
        Console.WriteLine("Compiling:" + file.csFile);
      }
      OutputFile(file);
    }

    private void OpenOutput(string filename) {
      fs = System.IO.File.Open(filename, FileMode.Create);
      byte[] bytes;
      if (filename.EndsWith(".txt") || filename.EndsWith(".ninja"))
        bytes = new UTF8Encoding().GetBytes("# cs2cpp : Machine generated code : Do not edit!\r\n");
      else
        bytes = new UTF8Encoding().GetBytes("// cs2cpp : Machine generated code : Do not edit!\r\n");
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteForward() {
      StringBuilder sb = new StringBuilder();
      sb.Append("#ifndef __" + Program.target + "__\r\n");
      sb.Append("#define __" + Program.target + "__\r\n");
      sb.Append("#include <Core.hpp>\r\n");
      if (Program.library) {
        if (File.Exists("library.hpp")) {
          sb.Append(System.IO.File.ReadAllText("library.hpp"));
        }
      }
      foreach(var lib in Program.libs) {
        sb.Append("#include <" + lib + ".hpp>\r\n");
      }
      foreach(var file in Program.files) {
        foreach(var cls in file.clss) {
          sb.Append(cls.GetReflectionExtern());
          if (cls.Namespace != "") sb.Append(OpenNamespace(cls.Namespace));
          sb.Append(cls.GetForwardDeclaration());
          if (cls.Namespace != "") sb.Append(CloseNamespace(cls.Namespace));
        }
      }
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteEndIf() {
      byte[] bytes = new UTF8Encoding().GetBytes("#endif\r\n");
      fs.Write(bytes, 0, bytes.Length);
    }

    public static string OpenNamespace(String Namespace) {
      StringBuilder sb = new StringBuilder();
      String[] strs = Namespace.Split(new String[] {"::"}, 0);
      for(int a=0;a<strs.Length;a++) {
        sb.Append("namespace " + strs[a] + "{");
      }
      sb.Append("\r\n");
      return sb.ToString();
    }

    public static string CloseNamespace(String Namespace) {
      StringBuilder sb = new StringBuilder();
      String[] strs = Namespace.Split(new String[] {"::"}, 0);
      for(int a=0;a<strs.Length;a++) {
        sb.Append("}");
      }
      sb.Append("\r\n");
      return sb.ToString();
    }

    private void BuildClasses() {
      int fcnt = Program.files.Count;
      for(int fidx = 0;fidx<fcnt;fidx++) {
        Source file = Program.files[fidx];
        List<Class> fclss = file.clss;
        int cnt = fclss.Count;
        for(int idx=0;idx<cnt;idx++) {
          clss.Add(fclss[idx]);
        }
      }
    }

    /** Check classes for a cross references. */
    private void CheckClasses() {
      int cnt = clss.Count;
      for(int idx=0;idx<cnt;idx++) {
        Class cls1 = clss[idx];
        string clsfull = cls1.nsfullname;
        int ucnt1 = cls1.uses.Count;
        for(int uidx1=0;uidx1<ucnt1;uidx1++) {
          string use = cls1.uses[uidx1];
          for(int idx2=0;idx2<cnt;idx2++) {
            if (clss[idx2].nsfullname == use) {
              Class cls2 = clss[idx2];
              int ucnt2 = cls2.uses.Count;
              for(int uidx2=0;uidx2<ucnt2;uidx2++) {
                if (cls2.uses[uidx2] == clsfull) {
                  Console.WriteLine("Error:Cross reference detected:" + cls1.nsfullname.Replace("::", ".") + " with " + cls2.nsfullname.Replace("::", "."));
                  errors++;
                }
              }
            }
          }
        }
      }
    }

    private void SortCoreLib() {
      int cnt = clss.Count;
      for(int idx1=0;idx1<cnt;idx1++) {
        Class cls1 = clss[idx1];
        if (cls1.nsfullname == "Core::ThreadLock") {
          clss.RemoveAt(idx1);
          clss.Insert(0, cls1);
          break;
        }
      }
      for(int idx1=0;idx1<cnt;idx1++) {
        Class cls1 = clss[idx1];
        if (cls1.nsfullname.StartsWith("Core::FixedArray$T") && cls1.nsfullname.Contains("Enumerator")) {
          clss.RemoveAt(idx1);
          clss.Insert(0, cls1);
        }
      }
      for(int idx1=0;idx1<cnt;idx1++) {
        Class cls1 = clss[idx1];
        if (cls1.nsfullname.StartsWith("Core::FixedArray$T") && !cls1.nsfullname.Contains("Enumerator")) {
          clss.RemoveAt(idx1);
          clss.Insert(0, cls1);
        }
      }
    }

    private bool SortClasses() {
      int cnt = clss.Count;
      for(int idx1=0;idx1<cnt;idx1++) {
        Class cls1 = clss[idx1];
        int ucnt1 = cls1.uses.Count;
        for(int uidx1=0;uidx1<ucnt1;uidx1++) {
          string use = cls1.uses[uidx1];
          for(int idx2=idx1+1;idx2<cnt;idx2++) {
            if (clss[idx2].nsfullname == use) {
              //need to move idx2 before idx
              Class tmp = clss[idx2];
              clss.RemoveAt(idx2);
              clss.Insert(idx1, tmp);
              return true;
            }
          }
        }
      }
      return false;
    }

    private void WriteNoClassTypes() {
      StringBuilder sb = new StringBuilder();
      foreach(var dgate in NoClass.methods) {
        if (dgate.Namespace.Length > 0) {
          sb.Append(OpenNamespace(dgate.Namespace));
        }
        sb.Append(dgate.GetMethodDeclaration());
        sb.Append(";\r\n");
        if (dgate.Namespace.Length > 0) sb.Append(CloseNamespace(dgate.Namespace));
      }
      foreach(var e in NoClass.enums) {
        if (e.Namespace.Length > 0) {
          sb.Append(OpenNamespace(e.Namespace));
        }
        sb.Append(GetEnumStruct(e) + e.name + ";\r\n");
        if (e.Namespace.Length > 0) sb.Append(CloseNamespace(e.Namespace));
      }
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteOperators() {
      StringBuilder sb = new StringBuilder();
      foreach(var op in OpClass.methods) {
        sb.Append(op.GetMethodDeclaration());
        sb.Append(";\r\n");
      }
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    /** Create default ctor if class has no ctors. */
    private void CreateDefaultCtor(Class cls) {
      if (!cls.hasctor && !cls.isInterface) {
      CCSharpCompiler.Generate.cls = cls;
        CtorNode(null);
      }
      foreach(var inner in cls.inners) {
        CreateDefaultCtor(inner);
      }
    }

    private void WriteClasses() {
      StringBuilder sb = new StringBuilder();
      foreach(var cls in clss) {
        CreateDefaultCtor(cls);
        if (cls.Namespace != "") sb.Append(OpenNamespace(cls.Namespace));
        sb.Append(cls.GetClassDeclaration());
        if (cls.Namespace != "") sb.Append(CloseNamespace(cls.Namespace));
        string hppfile = "src/" + cls.name + ".hpp";
        if (File.Exists(hppfile)) {
          String hpp = File.ReadAllText(hppfile);
          sb.Append(hpp);
        }
      }
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteIncludes() {
      StringBuilder sb = new StringBuilder();
      sb.Append("#include \"" + Program.hppFile + "\"\r\n");
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteStaticFields() {
      StringBuilder sb = new StringBuilder();
      foreach(var cls in file.clss) {
        if (cls.Namespace != "") sb.Append(OpenNamespace(cls.Namespace));
        sb.Append(cls.GetStaticFields());
        if (cls.Namespace != "") sb.Append(CloseNamespace(cls.Namespace));
      }
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteStaticFieldsInit() {
      StringBuilder sb = new StringBuilder();
      sb.Append("namespace Core {\r\n");
      sb.Append("void Library_" + Program.target + "_ctor() {\r\n");
      foreach(var file in Program.files) {
        foreach(var cls in file.clss) {
          sb.Append(cls.GetStaticFieldsInit());
        }
      }
      sb.Append("}};\r\n");
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void IncludeCPPCode() {
      if (!File.Exists(CCSharpCompiler.Generate.file.nativeFile)) {
        return;
      }
      StringBuilder sb = new StringBuilder();
      sb.Append("#include \"" + CCSharpCompiler.Generate.file.nativeFile + "\"\r\n");
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteMethods() {
      StringBuilder sb = new StringBuilder();
      foreach(var cls in file.clss) {
        sb.Append(cls.GetReflectionData());
        if (cls.Namespace != "") sb.Append(OpenNamespace(cls.Namespace));
        if (!cls.isGeneric) {
          sb.Append(cls.GetMethodsDefinitions());
        }
        if (cls.Namespace != "") sb.Append(CloseNamespace(cls.Namespace));
      }
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteLibrary() {
      StringBuilder sb = new StringBuilder();
      sb.Append(System.IO.File.ReadAllText("library.cpp"));
      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void WriteMain() {
      StringBuilder sb = new StringBuilder();

      sb.Append("namespace Core {\r\n");
      foreach(var lib in Program.libs) {
        sb.Append("extern void Library_" + lib + "_ctor();\r\n");
      }
      sb.Append("extern void Library_" + Program.target + "_ctor();\r\n");
      sb.Append("}\r\n");

      sb.Append("#include \"" + Program.target + ".hpp\"\r\n");
      if (Program.service) {
        sb.Append("#include <windows.h>\r\n");
      }

      if (Program.service) {
        sb.Append("SERVICE_STATUS_HANDLE ServiceHandle;\r\n");

        sb.Append("void ServiceStatus(int state) {\r\n");
        sb.Append("  SERVICE_STATUS ss;\r\n");
        sb.Append("  ss.dwServiceType = SERVICE_WIN32;\r\n");
        sb.Append("  ss.dwWin32ExitCode = 0;\r\n");
        sb.Append("  ss.dwCurrentState = state;\r\n");
        sb.Append("  ss.dwControlsAccepted = SERVICE_ACCEPT_STOP;\r\n");
        sb.Append("  ss.dwWin32ExitCode = 0;\r\n");
        sb.Append("  ss.dwServiceSpecificExitCode = 0;\r\n");
        sb.Append("  ss.dwCheckPoint = 0;\r\n");
        sb.Append("  ss.dwWaitHint = 0;\r\n");
        sb.Append("  SetServiceStatus(ServiceHandle, &ss);\r\n");
        sb.Append("}\r\n");

        sb.Append("void __stdcall ServiceControl(int OpCode) {\r\n");
        sb.Append("  switch (OpCode) {\r\n");
        sb.Append("  case SERVICE_CONTROL_STOP:\r\n");
        sb.Append("    ServiceStatus(SERVICE_STOPPED);\r\n");
        sb.Append("    " + Program.main + "::ServiceStop();\r\n");
        sb.Append("    break;\r\n");
        sb.Append("  }\r\n");
        sb.Append("}\r\n");

        sb.Append("void __stdcall ServiceMain(int argc, char **argv) {\r\n");
        sb.Append("  ServiceHandle = RegisterServiceCtrlHandler(\"" + Program.serviceName + "\", (void (__stdcall *)(unsigned long))ServiceControl);\r\n");
        sb.Append("  ServiceStatus(SERVICE_RUNNING);\r\n");
        sb.Append(WriteInvokeMain("ServiceStart"));
        sb.Append("}\r\n");
      }

      sb.Append("namespace System { namespace Core {\r\n");;
      sb.Append("int g_argc;\r\n");
      sb.Append("const char **g_argv;\r\n");
      sb.Append("}}\r\n");
      sb.Append("int main(int argc, const char **argv) {\r\n");
      sb.Append("void* local = nullptr;\r\n");
      sb.Append("Core::g_argc = argc;\r\n");
      sb.Append("Core::g_argv = argv;\r\n");
      sb.Append("Core::Object::GC_init(&local);\r\n");
      foreach(var lib in Program.libs) {
        sb.Append("Core::Library_" + lib + "_ctor();\r\n");
      }
      sb.Append("Core::Library_" + Program.target + "_ctor();\r\n");
      if (!Program.service) {
        sb.Append(WriteInvokeMain("Main"));
      } else {
        sb.Append("void *ServiceTable[4];\r\n");
        sb.Append("ServiceTable[0] = (void*)\"" + Program.serviceName + "\";\r\n");
        sb.Append("ServiceTable[1] = (void*)ServiceMain;\r\n");
        sb.Append("ServiceTable[2] = nullptr;\r\n");
        sb.Append("ServiceTable[3] = nullptr;\r\n");
        sb.Append("StartServiceCtrlDispatcher((LPSERVICE_TABLE_ENTRY)&ServiceTable);\r\n");  //does not return until service has been stopped
      }
      sb.Append("return 0;\r\n");
      sb.Append("}\r\n");

      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private String WriteInvokeMain(String name) {
      StringBuilder sb = new StringBuilder();
      sb.Append("Core::FixedArray$T<System::String*> *args = new(argc-1) Core::FixedArray$T<System::String*>(System::String::$GetType());\r\n");
      sb.Append("for(int a=1;a<argc;a++) {args->at(a-1) = Core::utf8ToString(argv[a]);}\r\n");
      if (!Program.debug) {
        sb.Append("try {\r\n");
      }
      sb.Append(Program.main + "::" + name + "(args);\r\n");
      if (!Program.debug) {
        sb.Append("} catch (System::Exception *ex) {System::Console::WriteLine(Core::addstr(Core::utf16ToString(u\"Exception caught:\"), ex->ToString()));}\r\n");
        sb.Append("catch (...) {System::Console::WriteLine(Core::utf16ToString(u\"Unknown exception thrown\"));}\r\n");
      }
      return sb.ToString();
    }

    private void WriteLibraryMain() {
      StringBuilder sb = new StringBuilder();

      sb.Append("#include \"" + Program.target + ".hpp\"\r\n");

      sb.Append("namespace System { namespace Core {\r\n");;
      sb.Append("int g_argc;\r\n");
      sb.Append("const char **g_argv;\r\n");
      sb.Append("}}\r\n");
      sb.Append("extern \"C\" {\r\n");
      sb.Append("__declspec(dllexport)");
      sb.Append("void LibraryMain(System::Object *obj) {\r\n");
      sb.Append(Program.main + "::LibraryMain(obj);}\r\n");
      sb.Append("}\r\n");

      byte[] bytes = new UTF8Encoding().GetBytes(sb.ToString());
      fs.Write(bytes, 0, bytes.Length);
    }

    private void CloseOutput() {
      fs.Close();
    }

    private string NodeToString(SyntaxNode node) {
      if (node == null) return "null";
      return node.Kind().ToString() + ":hash=" + node.GetHashCode();
    }

    private string TypeToString(INamedTypeSymbol type) {
      if (type == null) return "null";
      return type.ToString() + ":hash=" + type.GetHashCode();
    }

    private void OutputFile(Source file)
    {
      CCSharpCompiler.Generate.file = file;
      SyntaxNode root = file.tree.GetRoot();
      IEnumerable<SyntaxNode> nodes = root.ChildNodes();
      foreach(var child in nodes) {
        TopLevelNode(child);
      }
    }

    private void TopLevelNode(SyntaxNode node) {
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      switch (node.Kind()) {
        case SyntaxKind.InterfaceDeclaration:
        case SyntaxKind.ClassDeclaration:
        case SyntaxKind.StructDeclaration:
          Class topcls = new Class();
          file.clss.Add(topcls);
          ClassNode(node, topcls, NoClass, node.Kind() == SyntaxKind.InterfaceDeclaration);
          cls = NoClass;
          break;
        case SyntaxKind.NamespaceDeclaration:
          string name = GetChildNode(node).ToString().Replace(".", "::");  //IdentifierName or QualifiedName
          string org = Namespace;
          if (Namespace.Length > 0) Namespace += "::";
          Namespace += name.ToString().Replace(".", "::");
          foreach(var child in nodes) {
            TopLevelNode(child);
          }
          Namespace = org;
          break;
        case SyntaxKind.UsingDirective:
          break;
        case SyntaxKind.DelegateDeclaration:
          cls = NoClass;
          MethodNode(node, false, true, null);
          break;
        case SyntaxKind.EnumDeclaration:
          cls = NoClass;
          Enum e = new Enum(GetDeclaredSymbol(node), Namespace);
          SyntaxNode attrList = GetChildNode(node);
          cls.enums.Add(e);
          break;
      }
    }

    private static StringBuilder Convert = new StringBuilder();

    //convert reserved C++ names
    public static String ConvertName(String name) {
      if (name.Contains("::")) {
        string[] parts = name.Split(new String[]{"::"}, 0);
        Convert.Clear();
        for(int a=0;a<parts.Length;a++) {
          if (a > 0) Convert.Append("::");
          Convert.Append(ConvertName(parts[a]));
        }
        return Convert.ToString();
      }
      switch (name) {
        case "near": return "$near";
        case "far": return "$far";
        case "delete": return "$delete";
        case "slots": return "$slots";
        case "BUFSIZ": return "$BUFSIZ";
        case "TRUE": return "$TRUE";
        case "FALSE": return "$FALSE";
        case "string": return "String";
      }
      return name;
    }

    public static String ConvertOperatorName(String name) {
      return "$" + name;
/*
      switch (name) {
        case "op_LessThan": return "<";
        case "op_GreaterThan": return ">";
        case "op_LessThanOrEqual": return "<=";
        case "op_GreaterThanOrEqual": return ">=";
        case "op_Equality": return "==";
      }
      return "??";
*/
    }

    private void ClassNode(SyntaxNode node, Class inner, Class outter, bool Interface) {
      cls = inner;
      cls.node = node;
      cls.model = file.model;
      cls.fullname = outter.fullname;
      if (cls.fullname.Length > 0) {
        cls.fullname += "::";
      }
      cls.name = ConvertName(file.model.GetDeclaredSymbol(node).Name);
      cls.fullname += cls.name;
      cls.Namespace = Namespace;
      if (cls.Namespace.Length > 0) {
        cls.nsfullname = cls.Namespace + "::";
      }
      cls.nsfullname += cls.fullname;
      cls.isInterface = Interface;
      init = new Method();
      init.cls = cls;
      init.type.Set("void");
      init.type.cls = cls;
      init.type.isPrimitive = true;
      init.type.isPublic = true;
      init.name = "$init";
      init.type.SetTypes();
      cls.methods.Add(init);
      GetFlags(cls, file.model.GetDeclaredSymbol(node));
      foreach(var child in node.ChildNodes()) {
        switch (child.Kind()) {
          case SyntaxKind.FieldDeclaration:
            FieldNode(child);
            break;
          case SyntaxKind.PropertyDeclaration:
            PropertyNode(child);
            break;
          case SyntaxKind.ConstructorDeclaration:
            CtorNode(child);
            break;
          case SyntaxKind.DestructorDeclaration:
            MethodNode(child, true, false, null);
            break;
          case SyntaxKind.MethodDeclaration:
            MethodNode(child, false, false, null);
            break;
          case SyntaxKind.DelegateDeclaration:
            MethodNode(child, false, true, null);
            break;
          case SyntaxKind.BaseList:
            BaseListNode(child);
            break;
          case SyntaxKind.AttributeList:
            break;
          case SyntaxKind.EnumDeclaration:
            Enum e = new Enum(GetDeclaredSymbol(child), cls.Namespace);
            SyntaxNode attrList = GetChildNode(child);
            cls.enums.Add(e);
            break;
          case SyntaxKind.TypeParameterList:
            cls.isGeneric = true;
            cls.name += "$T";
            cls.fullname += "$T";
            cls.nsfullname += "$T";
            TypeParameterListNode(child, cls.GenericArgs);
            break;
          case SyntaxKind.InterfaceDeclaration:
          case SyntaxKind.ClassDeclaration:
          case SyntaxKind.StructDeclaration:
            Class _otter = cls;
            Class _inner = new Class();
            _otter.inners.Add(_inner);
            _inner.outter = _otter;
            ClassNode(child, _inner, _otter, node.Kind() == SyntaxKind.InterfaceDeclaration);
            cls = _otter;
            break;
          case SyntaxKind.ConversionOperatorDeclaration:
            //TODO
            break;
          case SyntaxKind.OperatorDeclaration:
            MethodNode(child, false, false, null, true);
            break;
          case SyntaxKind.TypeParameterConstraintClause:
            //ignored
            break;
          default:
            Console.WriteLine("Unknown Declaration:" + child.Kind());
            Environment.Exit(1);
            break;
        }
      }
      if (cls.nsfullname == "System::Object") {
        cls.bases.Add(new Type(null, "Core::Object"));
      } else {
        if (cls.bases.Count == 0 && !cls.isInterface) {
          cls.bases.Add(new Type(null, "System::Object"));
        }
        cls.AddUsage("System::Object");
      }
    }

    private void TypeParameterListNode(SyntaxNode node, List<Type> GenericArgs) {
      foreach(var child in node.ChildNodes()) {
        switch (child.Kind()) {
          case SyntaxKind.TypeParameter:
            GenericArgs.Add(new Type(child, GetDeclaredSymbol(child)));
            break;
        }
      }
    }

    private void BaseListNode(SyntaxNode node) {
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      foreach(var child in nodes) {
        switch (child.Kind()) {
          case SyntaxKind.SimpleBaseType:
            SyntaxNode baseNode = GetChildNode(child);
            Type baseType = null;
            switch (baseNode.Kind()) {
              case SyntaxKind.IdentifierName:
              case SyntaxKind.QualifiedName:
              case SyntaxKind.GenericName:
                baseType = new Type(baseNode);
                break;
              default:
                Console.WriteLine("Unknown BaseListNode:" + baseNode.Kind());
                break;
            }
            if (IsClass(baseNode))
              cls.bases.Add(baseType);
            else
              cls.ifaces.Add(baseType);
            cls.AddUsage(baseType.GetSymbol());
            break;
        }
      }
    }

    private void GetFlags(Flags flags, ISymbol symbol) {
      if (symbol == null) return;
      flags.isStatic = symbol.IsStatic;
      flags.isAbstract = symbol.IsAbstract;
      flags.isVirtual = symbol.IsVirtual;
      flags.isExtern = symbol.IsExtern;
      flags.isOverride = symbol.IsOverride;
      flags.isDefinition = symbol.IsDefinition;
      flags.isSealed = symbol.IsSealed;
      //Accessibility namespace is getting erroronously referenced here instead of the enum
      Microsoft.CodeAnalysis.Accessibility access = symbol.DeclaredAccessibility;
      if (access == Microsoft.CodeAnalysis.Accessibility.Private) flags.isPrivate = true;
      if (access == Microsoft.CodeAnalysis.Accessibility.Protected) flags.isProtected = true;
      if (access == Microsoft.CodeAnalysis.Accessibility.Public) flags.isPublic = true;
    }

    private void FieldNode(SyntaxNode node) {
      field = new Field();
      field.cls = cls;
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      foreach(var child in nodes) {
        switch (child.Kind()) {
          case SyntaxKind.AttributeList:
            break;
          case SyntaxKind.VariableDeclaration:
            field.variables = VariableDeclaration(child, field, true);
            foreach(var v in field.variables) {
              if (v.equals != null) FieldEquals(v);
            }
            break;
        }
      }
      cls.fields.Add(field);
    }

    private void FieldEquals(Variable v) {
      method = v.method;
      if (field.isStatic) {
        if (cls.Namespace.Length > 0) {
          method.Append(cls.Namespace);
          method.Append("::");
        }
        method.Append(cls.fullname);
        method.Append("::");
      }
      method.Append(v.name);
      method.Append(" = ");
      SyntaxNode equalsChild = GetChildNode(v.equals);
      if (equalsChild.Kind() == SyntaxKind.ArrayInitializerExpression) {
        NewArrayInitNode(equalsChild, field, field.arrays);
      } else {
        ExpressionNode(equalsChild);
      }
      method.Append(";\r\n");
    }

    private void PropertyNode(SyntaxNode node) {
      // type, AccessorList -> {GetAccessorDeclaration, SetAccessorDeclaration}
      // type, ExplicitInterfaceSpecifier -> {...}
      field = new Field();
      field.cls = cls;
      field.isProperty = true;
      Variable v = new Variable();
      field.variables.Add(v);  //property decl can only have 1 variable
      ISymbol symbol = file.model.GetDeclaredSymbol(node);
      v.name = symbol.Name;
      field.isPublic = true;
      VariableDeclaration(node, field);
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      foreach(var child in nodes) {
        switch (child.Kind()) {
          case SyntaxKind.AccessorList:
            foreach(var _etter in child.ChildNodes()) {
              switch (_etter.Kind()) {
                case SyntaxKind.GetAccessorDeclaration:
                  MethodNode(_etter, false, false, "$get_" + v.name);
                  method.type.CopyType(field);
                  method.type.CopyFlags(field);
                  method.type.SetTypes();
                  method.type.isVirtual = true;
                  if (method.src.Length == 0) {
                    method.Append("{return " + v.name + ";}");
                  }
                  field.get_Property = true;
                  break;
                case SyntaxKind.SetAccessorDeclaration:
                  MethodNode(_etter, false, false, "$set_" + v.name);
                  Argument arg = new Argument();
                  arg.type = field;
                  arg.name.name = "value";
                  method.args.Add(arg);
                  method.type.Set("void");
                  method.type.SetTypes();
                  method.type.isVirtual = true;
                  if (method.src.Length == 0) {
                    method.Append("{" + v.name + " = value;}");
                  }
                  field.set_Property = true;
                  break;
              }
            }
            break;
          case SyntaxKind.ExplicitInterfaceSpecifier:
            //TODO
            break;
        }
      }
      cls.fields.Add(field);
      //add default getter/setter
      if (!field.get_Property) {
        method = new Method();
        method.cls = cls;
        method.name = "$get_" + v.name;
        method.type.CopyType(field);
        method.type.CopyFlags(field);
        method.type.SetTypes();
        method.type.isVirtual = true;
        method.Append("{return " + v.name + ".Value;}\r\n");
        cls.methods.Add(method);
      }
      if (!field.set_Property) {
        method = new Method();
        method.cls = cls;
        method.name = "$set_" + v.name;
        Argument arg = new Argument();
        arg.type = field;
        arg.name.name = "value";
        method.args.Add(arg);
        method.type.Set("void");
        method.type.SetTypes();
        method.type.isVirtual = true;
        method.Append("{" + v.name + ".Value = value;}\r\n");
        cls.methods.Add(method);
      }
      //call Property<T>.Init() in ctor
      method = v.method;
      method.Append(v.name + ".Init("
        + "std::bind(&" + cls.fullname + "::$get_" + v.name + ", this),"
        + "std::bind(&" + cls.fullname + "::$set_" + v.name + ", this, std::placeholders::_1)"
        + ");\r\n");
    }

    private List<Variable> VariableDeclaration(SyntaxNode node, Type type, bool field = false) {
      List<Variable> vars = new List<Variable>();
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      foreach(var child in nodes) {
        switch (child.Kind()) {
          case SyntaxKind.ArrayType:
            type.isArray = true;
            VariableDeclaration(child, type);
            break;
          case SyntaxKind.ArrayRankSpecifier:
            //assume it's OmittedArraySizeExpression
            type.arrays++;
            if (type.arrays > 3) {
              Console.WriteLine("Error:Array Dimensions not supported:" + type.arrays);
              WriteFileLine(node);
              errors++;
            }
            break;
          case SyntaxKind.PointerType:
            type.isPtr = true;
            type.ptrs++;
            VariableDeclaration(child, type);
            break;
          case SyntaxKind.PredefinedType:
          case SyntaxKind.IdentifierName:
          case SyntaxKind.QualifiedName:
          case SyntaxKind.GenericName:
            type.Set(child);
            type.SetTypes();
            break;
          case SyntaxKind.VariableDeclarator:
            GetFlags(type, file.model.GetDeclaredSymbol(child));
            ISymbol symbol2 = file.model.GetDeclaredSymbol(child);
            Variable var = new Variable();
            vars.Add(var);
            if (symbol2 != null) {
              var.name = ConvertName(symbol2.Name);
            }
            var.equals = GetChildNode(child);
            break;
        }
      }
      return vars;
    }

    public void CtorNode(SyntaxNode node) {
      method = new Method();
      cls.methods.Add(method);
      method.cls = cls;
      method.name = cls.name;
      method.type.Set("");
      method.type.cls = cls;
      method.type.isPrimitive = true;
      method.ctor = true;
      cls.hasctor = true;
      if (node != null) {
        GetFlags(method.type, file.model.GetDeclaredSymbol(node));
        IEnumerable<SyntaxNode> nodes = node.ChildNodes();
        //nodes : parameter list, [baseCtor], block
        foreach(var child in nodes) {
          switch (child.Kind()) {
            case SyntaxKind.AttributeList:
              break;
            case SyntaxKind.ParameterList:
              ParameterListNode(child);
              break;
            case SyntaxKind.BaseConstructorInitializer:
              SyntaxNode argList = GetChildNode(child);
              if (cls.bases.Count == 0) {
                Console.WriteLine("Error:BaseConstructorInitializer:bases.Count==0" + cls.name);
              }
              method.Append(cls.bases[0].GetCPPType());
              method.Append("(");
              OutArgList(argList);
              method.Append(")\r\n");
              method.basector = method.src.ToString();
              method.src.Length = 0;
              break;
            case SyntaxKind.Block:
              BlockNode(child, true, false, true);
              break;
          }
        }
      } else {
        method.src.Append("{$init();}");
      }
      method.type.SetTypes();
    }

    private void MethodNode(SyntaxNode node, bool dtor, bool isDelegate, String name, bool isOperator = false) {
      method = new Method();
      method.cls = cls;
      method.type.cls = cls;
      method.isOperator = isOperator;
      if (isDelegate) {
        method.isDelegate = true;
        method.Namespace = Namespace;
      }
      if (name != null) {
        method.name = name;
      } else {
        method.node = node;
        method.symbol = file.model.GetDeclaredSymbol(node);
        if (dtor) {
          method.name = "~" + cls.name;
          method.type.Set("");
        } else {
          if (isOperator) {
            method.name = ConvertOperatorName(method.symbol.Name);
          } else {
            method.name = ConvertName(method.symbol.Name);
          }
        }
      }
      GetFlags(method.type, file.model.GetDeclaredSymbol(node));
      if (dtor) {
        method.type.isProtected = false;
        method.type.isVirtual = true;
        method.type.isPublic = true;
      }
      MethodType(node);
      method.type.SetTypes();
      if (isOperator) {
        OpClass.methods.Add(method);
      } else {
        cls.methods.Add(method);
      }
    }

    private void MethodType(SyntaxNode node) {
      //nodes : [return type], parameter list, block
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      foreach(var child in nodes) {
        switch (child.Kind()) {
          case SyntaxKind.PredefinedType:
          case SyntaxKind.IdentifierName:
          case SyntaxKind.QualifiedName:
          case SyntaxKind.GenericName:
            method.type.Set(child);
            method.type.SetTypes();
            break;
          case SyntaxKind.ParameterList:
            ParameterListNode(child);
            break;
          case SyntaxKind.Block:
            BlockNode(child, true);
            break;
          case SyntaxKind.AttributeList:
            break;
          case SyntaxKind.ArrayType:
            method.type.isArray = true;
            foreach(var arrayType in child.ChildNodes()) {
              ParameterNode(arrayType, method.type);
            }
            break;
          case SyntaxKind.TypeParameterList:
            method.isGeneric = true;
            method.name += "$T";
            TypeParameterListNode(child, method.GenericArgs);
            break;
          case SyntaxKind.PointerType:
            method.type.isPtr = true;
            method.type.ptrs++;
            MethodType(child);
            break;
          default:
            Console.WriteLine("Unsupported Method Node:" + child.Kind());
            break;
        }
      }
    }

    private void ParameterListNode(SyntaxNode node) {
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      foreach(var param in nodes) {
        switch (param.Kind()) {
          case SyntaxKind.Parameter:
            SyntaxNode par = GetChildNode(param, 1);
            Type type = new Type();
            if (par != null) {
              ParameterNode(par, type);
            } else {
              type.Set("auto");
            }
            Argument arg = new Argument();
            arg.name.name = ConvertName(file.model.GetDeclaredSymbol(param).Name.Replace(".", "::"));
            arg.type = type;
            method.args.Add(arg);
            SyntaxNode equals = GetChildNode(param, 2);
            if (equals != null && equals.Kind() == SyntaxKind.EqualsValueClause) {
              Method orgmethod = method;
              method = arg.name.method;
              ExpressionNode(GetChildNode(equals));
              method = orgmethod;
            }
            break;
        }
      }
    }

    private void ParameterNode(SyntaxNode node, Type type) {
      switch (node.Kind()) {
        case SyntaxKind.PredefinedType:
        case SyntaxKind.IdentifierName:
        case SyntaxKind.QualifiedName:
        case SyntaxKind.GenericName:
          type.Set(node);
          type.SetTypes();
          break;
        case SyntaxKind.ArrayType:
          type.isArray = true;
          ParameterNode(GetChildNode(node), type);
          IEnumerable<SyntaxNode> ranks = node.DescendantNodes();
          foreach(var rank in ranks) {
            if (rank.Kind() == SyntaxKind.ArrayRankSpecifier) {
              type.arrays++;
              if (type.arrays > 3) {
                Console.WriteLine("Error:Array Dimensions not supported:" + type.arrays);
                WriteFileLine(node);
                errors++;
              }
            }
          }
          break;
        case SyntaxKind.ArrayRankSpecifier:
          type.arrays++;
          if (type.arrays > 3) {
            Console.WriteLine("Error:Array Dimensions not supported:" + type.arrays);
            WriteFileLine(node);
            errors++;
          }
          break;
        case SyntaxKind.PointerType:
          type.isPtr = true;
          type.ptrs++;
          var child = GetChildNode(node);
          ParameterNode(child, type);
          break;
        default:
          Console.WriteLine("Unknown arg type:" + node.Kind());
          break;
      }
    }

    private void BlockNode(SyntaxNode node, bool top = false, bool throwFinally = false, bool ctor = false) {
      method.Append("{\r\n");
      if (ctor) {
        method.Append("$init();\r\n");
      }
      if (top) {
        if (method.type.isObject) {
          method.Append(method.type.GetTypeDeclaration());
          method.Append(" $ret;\r\n");
        }
      }
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      foreach(var child in nodes) {
        StatementNode(child);
      }
      if (throwFinally) {
        method.Append("throw new System::FinallyException();");
      }
      method.Append("}\r\n");
    }

    private void StatementNode(SyntaxNode node, bool top = false, bool throwFinally = false) {
      switch (node.Kind()) {
        case SyntaxKind.Block:
        case SyntaxKind.UnsafeStatement:
          BlockNode(node, top, throwFinally);
          break;
        case SyntaxKind.ExpressionStatement:
          ExpressionNode(GetChildNode(node));
          method.Append(";\r\n");
          break;
        case SyntaxKind.LocalDeclarationStatement:
          ExpressionNode(GetChildNode(node));
          method.Append(";\r\n");
          break;
        case SyntaxKind.ReturnStatement:
          SyntaxNode returnValue = GetChildNode(node);
          bool useValue = method.type.isObject && returnValue != null;
          if (useValue) {
            method.Append("$ret = ");
          } else {
            method.Append("return ");
          }
          if (returnValue != null) ExpressionNode(returnValue);
          method.Append(";\r\n");
          if (useValue) {
            method.Append("return $ret;\r\n");
          }
          break;
        case SyntaxKind.WhileStatement:
          //while (expression) statement
          method.Append("while (");
          ExpressionNode(GetChildNode(node, 1));
          method.Append(")");
          StatementNode(GetChildNode(node, 2));
          break;
        case SyntaxKind.ForStatement:
          //for(initializers;condition;incrementors) statement
          ForStatementSyntax ffs = (ForStatementSyntax)node;
          bool HasDecl = ffs.Declaration != null;
          int InitCount = ffs.Initializers.Count;
          if (HasDecl) InitCount++;
          bool HasCond = ffs.Condition != null;
          int IncreCount = ffs.Incrementors.Count;
          method.Append("for(");
          int pos = 1;
          for(int idx=0;idx<InitCount;idx++) {
            if (idx > 0) method.Append(",");
            ExpressionNode(GetChildNode(node, pos++));
          }
          method.Append(";");
          if (HasCond) {
            ExpressionNode(GetChildNode(node, pos++));
          }
          method.Append(";");
          for(int idx=0;idx<IncreCount;idx++) {
            if (idx > 0) method.Append(",");
            ExpressionNode(GetChildNode(node, pos++));
          }
          method.Append(")");
          StatementNode(GetChildNode(node, pos));
          break;
        case SyntaxKind.ForEachStatement:
          //foreach(var item in items) {}
          //node(item) -> type, items, block {}
          SyntaxNode foreachItem = node;
          String foreachName = file.model.GetDeclaredSymbol(foreachItem).ToString();
          SyntaxNode foreachTypeNode = GetChildNode(node, 1);
          ISymbol foreachTypeNodeSymbol = file.model.GetSymbolInfo(foreachTypeNode).Symbol;
          Type foreachType = new Type(foreachTypeNode);
          SyntaxNode foreachItems = GetChildNode(node, 2);
          SyntaxNode foreachBlock = GetChildNode(node, 3);
          String enumID = "$enum_" + cls.enumCnt++;
          method.Append("{");
          method.Append(foreachType.GetTypeDeclaration());  //var type
          method.Append(" ");
          method.Append(foreachName);  //var name : item
          method.Append(";\r\n");
          method.Append("System::IEnumerator");
          method.Append(foreachType.GetTypeDeclaration());  //var type
          method.Append("> *" + enumID + " = ");
          ExpressionNode(foreachItems);  //items
          method.Append("->GetEnumerator();\r\n");
          method.Append("while (");
          method.Append(enumID + "->MoveNext()) {\r\n");
          method.Append(foreachName + " = ");  //var name : item =
          method.Append(enumID + "->$get_Current();\r\n");
          StatementNode(foreachBlock);
          method.Append("}}\r\n");
          break;
        case SyntaxKind.DoStatement:
          //do statement/block while (expression)
          method.Append("do ");
          StatementNode(GetChildNode(node, 1));
          method.Append(" while (");
          ExpressionNode(GetChildNode(node, 2));
          method.Append(");\r\n");
          break;
        case SyntaxKind.IfStatement:
          //if (expression) statement [else statement]
          method.Append("if (");
          ExpressionNode(GetChildNode(node, 1));
          method.Append(")");
          StatementNode(GetChildNode(node, 2));
          SyntaxNode elseClause = GetChildNode(node, 3);
          if (elseClause != null && elseClause.Kind() == SyntaxKind.ElseClause) {
            method.Append(" else ");
            StatementNode(GetChildNode(elseClause, 1));
          }
          break;
        case SyntaxKind.TryStatement:
          //statement CatchClause ... FinallyClause
          int cnt = GetChildCount(node);
          bool hasFinally = false;
          for(int a=2;a<=cnt;a++) {
            SyntaxNode child = GetChildNode(node, a);
            if (child.Kind() == SyntaxKind.FinallyClause) {
              hasFinally = true;
            }
          }
          if (hasFinally) {
            method.Append("try {");
          }
          method.Append("try ");
          SyntaxNode tryBlock = GetChildNode(node, 1);
          if (tryBlock.Kind() == SyntaxKind.Block) {
            BlockNode(tryBlock, false, hasFinally);
          } else {
            StatementNode(tryBlock);
          }
          for(int a=2;a<=cnt;a++) {
            SyntaxNode child = GetChildNode(node, a);
            switch (child.Kind()) {
              case SyntaxKind.CatchClause:
                int cc = GetChildCount(child);
                if (cc == 2) {
                  //catch (Exception ?)
                  SyntaxNode catchDecl = GetChildNode(child, 1);
                  method.Append(" catch(");
                  ExpressionNode(GetChildNode(catchDecl));  //exception type
                  method.Append(" *");
                  method.Append(file.model.GetDeclaredSymbol(catchDecl).Name);  //exception variable name
                  method.Append(")");
                  SyntaxNode catchBlock = GetChildNode(child, 2);
                  StatementNode(catchBlock, false, hasFinally);
                } else {
                  //catch all
                  method.Append(" catch (...)");
                  SyntaxNode catchBlock = GetChildNode(child, 1);
                  StatementNode(catchBlock, false, hasFinally);
                }
                break;
              case SyntaxKind.FinallyClause:
                method.Append("} catch(System::FinallyException *$finally" + cls.finallyCnt++ + ") ");
                StatementNode(GetChildNode(child));
                break;
            }
          }
          break;
        case SyntaxKind.ThrowStatement:
          int tc = GetChildCount(node);
          if (tc == 1) {
            method.Append("throw ");
            ExpressionNode(GetChildNode(node));
          } else {
            method.Append("std::rethrow_exception(std::current_exception())");
          }
          method.Append(";");
          break;
        case SyntaxKind.FixedStatement:
          method.inFixedBlock = true;
          method.Append("{\r\n");
          foreach(var child in node.ChildNodes()) {
            switch (child.Kind()) {
              case SyntaxKind.VariableDeclaration:
                Type type = new Type();
                List<Variable> vars = VariableDeclaration(child, type);
                method.Append(type.GetTypeDeclaration());
                method.Append(" ");
                foreach(var variable in vars) {
                  method.Append(variable.name);
                  SyntaxNode equals = variable.equals;
                  if (equals != null) {
                    method.Append(" = ");
                    SyntaxNode equalsChild = GetChildNode(equals);
                    if (equalsChild.Kind() == SyntaxKind.ArrayInitializerExpression) {
                      NewArrayInitNode(equalsChild, type, type.arrays);
                    } else {
                      ExpressionNode(equalsChild);
                    }
                  }
                  method.Append(".get()->data();\r\n");
                }
                break;
              case SyntaxKind.Block:
                BlockNode(child);
                break;
            }
          }
          method.Append("}\r\n");
          method.inFixedBlock = false;
          break;
        case SyntaxKind.LockStatement:
          //lock, block
          SyntaxNode lockId = GetChildNode(node, 1);
          string lockIdName = GetTypeName(lockId);
          if (lockIdName != "Core::ThreadLock") {
            Console.WriteLine("Error:lock {} must use Core.ThreadLock (Type=" + lockIdName + " id=" + GetSymbol(lockId) + ")");
            WriteFileLine(lockId);
            errors++;
            break;
          }
          SyntaxNode lockBlock = GetChildNode(node, 2);
          string holder = "$lock" + cls.lockCnt++;
          method.Append("{$LockHolder " + holder + "(");
          ExpressionNode(lockId);
          method.Append(");"); // + holder + ".Condition();" + holder + ".Signal())");
          BlockNode(lockBlock);
          method.Append("}\r\n");
          break;
        case SyntaxKind.SwitchStatement:
          // var, [SwitchSection...]
          SyntaxNode var = GetChildNode(node);
          if (IsString(var)) {
            SwitchString(node);
            break;
          }
          method.Append("switch (");
          ExpressionNode(var);
          method.Append(") {\r\n");
          method.currentSwitch++;
          method.switchIDs[method.currentSwitch] = method.nextSwitchID++;
          int caseIdx = 0;
          bool block = false;
          foreach(var section in node.ChildNodes()) {
            if (section.Kind() != SyntaxKind.SwitchSection) continue;
            foreach(var child in section.ChildNodes()) {
              switch (child.Kind()) {
                case SyntaxKind.CaseSwitchLabel:
                  method.Append("case ");
                  method.Append(ConstantNode(GetChildNode(child), false));
                  method.Append(":\r\n");
                  method.Append("$case_" + method.switchIDs[method.currentSwitch] + "_" + caseIdx++);
                  method.Append(":\r\n");
                  break;
                case SyntaxKind.DefaultSwitchLabel:
                  method.Append("default:\r\n");
                  method.Append("$default_" + method.switchIDs[method.currentSwitch]);
                  method.Append(":\r\n");
                  break;
                default:
                  if (!block) {
                    method.Append("{\r\n");
                    block = true;
                  }
                  StatementNode(child);
                  break;
              }
            }
            method.Append("}\r\n");
            block = false;
          }
          method.currentSwitch--;
          method.Append("}\r\n");
          break;
        case SyntaxKind.BreakStatement:
          method.Append("break;\r\n");
          break;
        case SyntaxKind.ContinueStatement:
          method.Append("continue;\r\n");
          break;
        case SyntaxKind.GotoCaseStatement:
          String value = file.model.GetConstantValue(GetChildNode(node)).Value.ToString();
          String index = FindCase(node, value);
          method.Append("goto $case_" + + method.switchIDs[method.currentSwitch] + "_" + index + ";\r\n");
          break;
        case SyntaxKind.GotoDefaultStatement:
          method.Append("goto $default_" + method.switchIDs[method.currentSwitch] + ";\r\n");
          break;
        default:
          Console.WriteLine("Error:Statement not supported:" + node.Kind());
          WriteFileLine(node);
          Environment.Exit(0);
          break;
      }
    }

    public String FindCase(SyntaxNode node, String value) {
      //first find parent SwitchStatement
      while (node.Kind() != SyntaxKind.SwitchStatement) {
        node = node.Parent;
      }
      int caseIdx = 0;
      //interate over SwitchSection/CaseSwitchLabel to find matching value
      foreach(var section in node.ChildNodes()) {
        if (section.Kind() != SyntaxKind.SwitchSection) continue;
        SyntaxNode caseSwitch = GetChildNode(section);
        if (caseSwitch.Kind() != SyntaxKind.CaseSwitchLabel) continue;
        SyntaxNode caseValue = GetChildNode(caseSwitch);
        String caseConst = file.model.GetConstantValue(caseValue).Value.ToString();
        if (caseConst == value) return "" + caseIdx;
        caseIdx++;
      }
      Console.WriteLine("Failed to find goto case target");
      Environment.Exit(0);
      return null;
    }

    public void SwitchString(SyntaxNode node) {
      //SwitchStatement -> [SwitchSection -> [CaseSwitchLabel, [Default] ...] [Statements...] ...]
      SyntaxNode var = GetChildNode(node);
      String ssid = "$ss_" + cls.switchStringCnt++;
      method.Append("bool " + ssid + " = false;\r\n");  //set to true if case block used (else default is used)
      method.Append("while (true) {\r\n");
      SyntaxNode defaultSection = null;
      foreach(var section in node.ChildNodes()) {
        if (section.Kind() != SyntaxKind.SwitchSection) continue;
        if (HasDefault(section)) {
          defaultSection = section;
        } else {
          SwitchStringSection(var, section, ssid);
        }
      }
      if (defaultSection != null) {
        SwitchStringSection(var, defaultSection, ssid);
      }
      method.Append("}\r\n");  //end of while loop
    }

    public bool HasDefault(SyntaxNode section) {
      foreach(var child in section.ChildNodes()) {
        if (child.Kind() == SyntaxKind.DefaultSwitchLabel) return true;
      }
      return false;
    }

    public void SwitchStringSection(SyntaxNode var, SyntaxNode section, String ssid) {
      bool first = true;
      bool statements = false;
      foreach(var child in section.ChildNodes()) {
        switch (child.Kind()) {
          case SyntaxKind.CaseSwitchLabel:
            if (first) {
              method.Append("if (");
              first = false;
            } else {
              method.Append("||");
            }
            method.Append("(");
            ExpressionNode(var);
            method.Append("->Equals(");
            ExpressionNode(GetChildNode(child));
            method.Append("))");
            break;
          case SyntaxKind.DefaultSwitchLabel:
            if (first) {
              method.Append("if (");
              first = false;
            } else {
              method.Append("||");
            }
            method.Append("(!" + ssid + ")");
            break;
          default:
            //statements
            if (!statements) {
              method.Append(") {\r\n");
              method.Append(ssid + " = true;\r\n");
              statements = true;
            }
            StatementNode(child);
            break;
        }
      }
      method.Append("}\r\n");  //end of statements
    }

    private int GetNumArgs(SyntaxNode node) {
      ISymbol symbol = file.model.GetSymbolInfo(node).Symbol;
      String str = symbol.ToString();  //delegate(args...)
      if (str.EndsWith("()")) return 0;
      String[] args = str.Split(new String[] {","}, 0);
      return args.Length;
    }

    private static String ConvertChar(String value) {
      if (value == "\\") return "'\\\\'";
      if (value == "\t") return "'\\t'";
      if (value == "\r") return "'\\r'";
      if (value == "\n") return "'\\n'";
      return "'" + value + "'";
    }

    public static String ConstantNode(SyntaxNode node, bool typeCastEnum = true) {
      Object obj = file.model.GetConstantValue(node).Value;
      if (obj == null) return null;
      String value = obj.ToString();
      String valueType = GetTypeName(node);
      switch (valueType) {
        case "char":
          value = ConvertChar(value);
          break;
        case "float":
          if (value.IndexOf(".") == -1) value += ".0";
          value += "f";
          break;
        case "double":
          if (value.IndexOf(".") == -1) value += ".0";
          break;
        case "long":
          value += "LL";
          break;
        case "string":
          value = "u\"" + value.Replace("\0", "\\0").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
          break;
      }
      if (!typeCastEnum) return value;
      ISymbol symbol = file.model.GetSymbolInfo(node).Symbol;
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type.TypeKind == TypeKind.Enum) {
        value = "(" + type.ToString().Replace(".", "::") + ")" + value;
      }
      return value;
    }

    private void ExpressionNode(SyntaxNode node, bool useName = false) {
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      Type type;
      String constValue = ConstantNode(node);
      switch (node.Kind()) {
        case SyntaxKind.IdentifierName:
        case SyntaxKind.PredefinedType:
        case SyntaxKind.QualifiedName:
        case SyntaxKind.GenericName:
          if (constValue != null) {
            method.Append(constValue);
            return;
          }
          type = new Type(node, useName);
          method.Append(type.GetCPPType());
          if (IsProperty(node)) {
            method.Append(".Value");
          }
          break;
        case SyntaxKind.SimpleMemberAccessExpression:
          if (constValue != null) {
            method.Append(constValue);
            return;
          }
          SyntaxNode left = GetChildNode(node, 1);
          SyntaxNode right = GetChildNode(node, 2);
          if (IsStatic(right) || left.Kind() == SyntaxKind.BaseExpression || IsEnum(left) || IsNamespace(left) || (IsNamedType(left) && IsNamedType(right))) {
            ExpressionNode(left);
            method.Append("::");
            ExpressionNode(right, true);
          } else {
            method.Append("$check(");
            ExpressionNode(left, true);
            method.Append(")->");
            ExpressionNode(right, true);
          }
          break;
        case SyntaxKind.VariableDeclaration:
          //local variable
          type = new Type();
          List<Variable> vars = VariableDeclaration(node, type);
          foreach(var variable in vars) {
            method.Append(type.GetTypeDeclaration());
            method.Append(" ");
            method.Append(variable.name);
            SyntaxNode equals = variable.equals;
            if (equals != null) {
              method.Append(" = ");
              SyntaxNode equalsChild = GetChildNode(equals);
              if (equalsChild.Kind() == SyntaxKind.ArrayInitializerExpression) {
                NewArrayInitNode(equalsChild, type, type.arrays);
              } else {
                ExpressionNode(equalsChild);
              }
            }
            if (vars.Count > 1) method.Append(";");
          }
          break;
        case SyntaxKind.SimpleAssignmentExpression:
          AssignNode(node);
          break;
        case SyntaxKind.InvocationExpression:
          InvokeNode(node);
          break;
        case SyntaxKind.ArrayCreationExpression:
          NewArrayNode(node);
          break;
        case SyntaxKind.NullLiteralExpression:
          method.Append("nullptr");
          break;
        case SyntaxKind.NumericLiteralExpression:
          method.Append(ConstantNode(node));
          break;
        case SyntaxKind.TrueLiteralExpression:
          method.Append("true");
          break;
        case SyntaxKind.FalseLiteralExpression:
          method.Append("false");
          break;
        case SyntaxKind.StringLiteralExpression:
          method.Append("Core::utf16ToString(");
          method.Append(ConstantNode(node));
          method.Append(")");
          break;
        case SyntaxKind.CharacterLiteralExpression:
          method.Append("(char16)");
          method.Append(ConstantNode(node));
          break;
        case SyntaxKind.BaseExpression:
          method.Append(cls.bases[0].GetCPPType());
          break;
        case SyntaxKind.ObjectCreationExpression:
          InvokeNode(node, true);
          break;
        case SyntaxKind.CastExpression:
          CastNode(node);
          break;
        case SyntaxKind.ElementAccessExpression:
          //IdentifierNode, BracketedArgumentList -> {Argument, ...}
          SyntaxNode array = GetChildNode(node, 1);
          SyntaxNode index = GetChildNode(node, 2);
          ExpressionNode(array);
          method.Append("->at(");
          ExpressionNode(index);
          method.Append(")");
          break;
        case SyntaxKind.BracketedArgumentList:
          foreach(var child in nodes) {
            ExpressionNode(child);
          }
          break;
        case SyntaxKind.Argument:
          ExpressionNode(GetChildNode(node));
          break;
        case SyntaxKind.AddExpression:
          SyntaxNode addleft = GetChildNode(node, 1);
          SyntaxNode addright = GetChildNode(node, 2);
          if (IsString(addleft) || IsString(addright)) {
            method.Append("Core::addstr(");
          } else {
            method.Append("Core::addnum(");
          }
          ExpressionNode(addleft);
          method.Append(",");
          switch (GetTypeName(addright)) {
            case "char": method.Append("(char16)"); break;
            case "short": method.Append("(int32)"); break;
            case "ushort": method.Append("(uint32)"); break;
            case "byte": method.Append("(uint32)"); break;
            case "sbyte": method.Append("(int32)"); break;
          }
          ExpressionNode(addright);
          method.Append(")");
          break;
        case SyntaxKind.SubtractExpression:
          BinaryNode(node, "-");
          break;
        case SyntaxKind.MultiplyExpression:
          BinaryNode(node, "*");
          break;
        case SyntaxKind.DivideExpression:
          BinaryNode(node, "/");
          break;
        case SyntaxKind.ModuloExpression:
          ModNode(node, "%");
          break;
        case SyntaxKind.ModuloAssignmentExpression:
          ModAssignNode(node, "%");
          break;
        case SyntaxKind.LessThanExpression:
          BinaryNode(node, "<");
          break;
        case SyntaxKind.LessThanOrEqualExpression:
          BinaryNode(node, "<=");
          break;
        case SyntaxKind.GreaterThanExpression:
          BinaryNode(node, ">");
          break;
        case SyntaxKind.GreaterThanOrEqualExpression:
          BinaryNode(node, ">=");
          break;
        case SyntaxKind.EqualsExpression:
          EqualsNode(node, "==");
          break;
        case SyntaxKind.NotEqualsExpression:
          EqualsNode(node, "!=");
          break;
        case SyntaxKind.LeftShiftExpression:
          BinaryNode(node, "<<");
          break;
        case SyntaxKind.RightShiftExpression:
          BinaryNode(node, ">>");
          break;
        case SyntaxKind.AddAssignmentExpression:
          SyntaxNode addassignleft = GetChildNode(node, 1);
          SyntaxNode addassignright = GetChildNode(node, 2);
          ExpressionNode(addassignleft, true);
          if (IsString(addassignleft) || IsString(addassignright)) {
            method.Append("= Core::addstr(");
          } else {
            method.Append("= Core::addnum(");
          }
          ExpressionNode(addassignleft);
          method.Append(",");
          switch (GetTypeName(addassignright)) {
            case "char": method.Append("(char16)"); break;
            case "short": method.Append("(int32)"); break;
            case "ushort": method.Append("(uint32)"); break;
            case "byte": method.Append("(uint32)"); break;
            case "sbyte": method.Append("(int32)"); break;
          }
          ExpressionNode(addassignright);
          method.Append(")");
          break;
        case SyntaxKind.SubtractAssignmentExpression:
          BinaryAssignNode(node, "-");
          break;
        case SyntaxKind.MultiplyAssignmentExpression:
          BinaryAssignNode(node, "*");
          break;
        case SyntaxKind.DivideAssignmentExpression:
          BinaryAssignNode(node, "/");
          break;
        case SyntaxKind.OrAssignmentExpression:
          BinaryAssignNode(node, "|");
          break;
        case SyntaxKind.AndAssignmentExpression:
          BinaryAssignNode(node, "&");
          break;
        case SyntaxKind.ExclusiveOrAssignmentExpression:
          BinaryAssignNode(node, "^");
          break;
        case SyntaxKind.ExclusiveOrExpression:
          BinaryNode(node, "^");
          break;
        case SyntaxKind.LogicalNotExpression:
          method.Append("!");
          ExpressionNode(GetChildNode(node));
          break;
        case SyntaxKind.LogicalOrExpression:
          BinaryNode(node, "||");
          break;
        case SyntaxKind.LogicalAndExpression:
          BinaryNode(node, "&&");
          break;
        case SyntaxKind.BitwiseOrExpression:
          if (IsEnum(GetChildNode(node))) {
            //enums convert to int which must be type casted back to enum type
            method.Append("(");
            method.Append(GetTypeName(GetChildNode(node)));
            method.Append(")");
          }
          method.Append("(");
          BinaryNode(node, "|");
          method.Append(")");
          break;
        case SyntaxKind.BitwiseAndExpression:
          BinaryNode(node, "&");
          break;
        case SyntaxKind.BitwiseNotExpression:
          method.Append("!");
          ExpressionNode(GetChildNode(node));
          break;
        case SyntaxKind.LeftShiftAssignmentExpression:
          BinaryAssignNode(node, "<<");
          break;
        case SyntaxKind.RightShiftAssignmentExpression:
          BinaryAssignNode(node, ">>");
          break;
        case SyntaxKind.ParenthesizedExpression:
          method.Append("(");
          ExpressionNode(GetChildNode(node));
          method.Append(")");
          break;
        case SyntaxKind.PostIncrementExpression:
          ExpressionNode(GetChildNode(node));
          method.Append("++");
          break;
        case SyntaxKind.PreIncrementExpression:
          method.Append("++");
          ExpressionNode(GetChildNode(node));
          break;
        case SyntaxKind.PostDecrementExpression:
          ExpressionNode(GetChildNode(node), true);
          method.Append("--");
          break;
        case SyntaxKind.PreDecrementExpression:
          method.Append("--");
          ExpressionNode(GetChildNode(node), true);
          break;
        case SyntaxKind.UnaryMinusExpression:
          method.Append("-");
          ExpressionNode(GetChildNode(node));
          break;
        case SyntaxKind.UnaryPlusExpression:
          method.Append("+");
          ExpressionNode(GetChildNode(node));
          break;
        case SyntaxKind.ThisExpression:
          method.Append("this");
          break;
        case SyntaxKind.PointerIndirectionExpression:
          method.Append("*");
          ExpressionNode(GetChildNode(node));
          break;
        case SyntaxKind.PointerMemberAccessExpression:
          SyntaxNode ptrleft = GetChildNode(node, 1);
          SyntaxNode ptrright = GetChildNode(node, 2);
          ExpressionNode(ptrleft);
          method.Append("->");
          ExpressionNode(ptrright);
          break;
        case SyntaxKind.ParenthesizedLambdaExpression:
          // ParameterList, Block
          SyntaxNode plist = GetChildNode(node, 1);
          SyntaxNode pblock = GetChildNode(node, 2);
          method.Append("[&]");
          //output parameter list
          bool firstLambda = true;
          method.Append("(");
          foreach(var param in plist.ChildNodes()) {
            switch (param.Kind()) {
              case SyntaxKind.Parameter:
                SyntaxNode par = GetChildNode(param);
                Type ptype = new Type();
                if (par != null) {
                  ParameterNode(par, ptype);
                } else {
                  //lambda without arg types
                  ptype.Set("auto");
                }
                Variable pvar = new Variable();
                pvar.name = file.model.GetDeclaredSymbol(param).Name.Replace(".", "::");
                if (!firstLambda) method.Append(","); else firstLambda = false;
                method.Append(ptype.GetTypeDeclaration());
                method.Append(" ");
                method.Append(pvar.name);
                break;
            }
          }
          method.Append(")");
          BlockNode(pblock);
          break;
        case SyntaxKind.DefaultExpression:
          SyntaxNode defType = GetChildNode(node);
          Type defSymbol = new Type(defType);
          method.Append(defSymbol.GetCPPType());
          method.Append("()");
          break;
        case SyntaxKind.TypeOfExpression:
          //typeof(X)
          SyntaxNode typeOf = GetChildNode(node);
          Type typeSymbol = new Type(typeOf);
          method.Append(typeSymbol.GetCoreType());
          break;
        case SyntaxKind.IsExpression:
          //X is Type
          SyntaxNode isObj = GetChildNode(node, 1);
          SyntaxNode isType = GetChildNode(node, 2);
          Type isTypeType = new Type(isType);
          ExpressionNode(isObj);
          method.Append("->GetType()");
          method.Append("->IsDerivedFrom(");
          method.Append(isTypeType.GetCoreType());
          break;
        case SyntaxKind.AsExpression:
          //X as Type
          SyntaxNode asObj = GetChildNode(node, 1);
          SyntaxNode asType = GetChildNode(node, 2);
          Type asTypeType = new Type(asType);
          method.Append(asTypeType.GetCoreType());
          method.Append("->IsDerivedFrom(");
          ExpressionNode(asObj);
          method.Append("->GetType()) ? dynamic_cast<" + asTypeType.Get_Symbol() + ">(");
          ExpressionNode(asObj);
          method.Append(") : nullptr)");
          break;
        case SyntaxKind.ConditionalExpression:
          // (cond ? val1 : val2)
          method.Append("(");
          ExpressionNode(GetChildNode(node, 1));
          method.Append("?");
          ExpressionNode(GetChildNode(node, 2));
          method.Append(":");
          ExpressionNode(GetChildNode(node, 3));
          method.Append(")");
          break;
        default:
          Console.WriteLine("Error:Unsupported expression:" + node.Kind());
          WriteFileLine(node);
          Environment.Exit(0);
          break;
      }
    }

    private void NewArrayInitNode(SyntaxNode node, Type type, int dims) {
      IEnumerable<SyntaxNode> list = node.ChildNodes();
      int idx = method.Length();
      for(int a=0;a<dims;a++) {
        method.Append("Core::FixedArray$T<");
      }
      method.Append(type.GetTypeDeclaration(false));
      for(int a=0;a<dims;a++) {
        if (a > 0) method.Append("*");
        method.Append(">");
      }
      method.Append("(");
      method.Append(type.GetCoreType());
      method.Append(",");
      method.Append("std::initializer_list<");
      dims--;
      for(int a=0;a<dims;a++) {
        method.Append("Core::FixedArray$T<");
      }
      method.Append(type.GetTypeDeclaration(false));
      for(int a=0;a<dims;a++) {
        if (a > 0) method.Append("*");
        method.Append(">");
      }
      if (dims > 0) {
        method.Append("*");
      }
      method.Append(">{");
      bool first = true;
      int cnt = 0;
      foreach(var elem in list) {
        cnt++;
        if (!first) method.Append(","); else first = false;
        ExpressionNode(elem);
      }
      method.Append("})");
      method.Insert(idx, " new(" + cnt + ") ");
    }

    private bool IsStatic(SyntaxNode node) {
      ISymbol symbol = file.model.GetSymbolInfo(node).Symbol;
      if (symbol != null) {
        return symbol.IsStatic;
      }
      ISymbol declsymbol = file.model.GetDeclaredSymbol(node);
      if (declsymbol != null) {
        return declsymbol.IsStatic;
      }
      ISymbol type = file.model.GetTypeInfo(node).Type;
      if (type != null) {
        return type.IsStatic;
      }
      Console.WriteLine("Error:isStatic():Symbol not found for:" + node.ToString());
      WriteFileLine(node);
      errors++;
      return true;
    }

    private bool IsClass(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      return (type.TypeKind == TypeKind.Class);
    }

    private bool IsEnum(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      if (type.TypeKind == TypeKind.Enum) return true;
      return false;
    }

    private bool IsProperty(SyntaxNode node) {
      if (method == null) return false;
      ISymbol symbol = file.model.GetSymbolInfo(node).Symbol;
      if (symbol == null) return false;
      if (symbol.Kind != SymbolKind.Property) return false;
      String name = symbol.Name;
      ISymbol parent = symbol.ContainingSymbol;
      if (parent == null) return false;
      String parentName = parent.Name;
      if (parentName != cls.name) return false;
      if (method.name == "$set_" + name) return true;
      if (method.name == "$get_" + name) return true;
      return false;
    }

    private bool IsExtern(SyntaxNode node) {
      ISymbol symbol = file.model.GetSymbolInfo(node).Symbol;
      if (symbol == null) return symbol.IsExtern;
      return false;
    }

    private bool IsDelegate(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) {
        return false;
      }
      return type.TypeKind == TypeKind.Delegate;
    }

    private bool IsMethod(SyntaxNode node) {
      ISymbol symbol = CCSharpCompiler.Generate.file.model.GetSymbolInfo(node).Symbol;
      if (symbol == null) return false;
      return symbol.Kind == SymbolKind.Method;
    }

    private bool IsNamedType(SyntaxNode node) {
      ISymbol symbol = CCSharpCompiler.Generate.file.model.GetSymbolInfo(node).Symbol;
      if (symbol == null) return false;
      return symbol.Kind == SymbolKind.NamedType;
    }

    private bool IsNamespace(SyntaxNode node) {
      ISymbol symbol = CCSharpCompiler.Generate.file.model.GetSymbolInfo(node).Symbol;
      if (symbol == null) return false;
      return symbol.Kind == SymbolKind.Namespace;
    }

    private bool IsString(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      switch (type.ToString()) {
        case "string": return true;
        case "System.String": return true;
      }
      return false;
    }

    private bool IsFloat(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      return type.ToString() == "float";
    }

    private bool IsDouble(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      return type.ToString() == "double";
    }

    private bool IsLong(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      return type.ToString() == "long";
    }

    private bool IsChar(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      return type.ToString() == "char";
    }

    private bool IsShort(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      return type.ToString() == "short";
    }

    private bool IsUShort(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return false;
      return type.ToString() == "ushort";
    }

    //returns true if member hides base class member with same name
    public static bool IsNew(Class cls, String member) {
      INamedTypeSymbol type = (INamedTypeSymbol)cls.model.GetDeclaredSymbol(cls.node);
      if (type == null) {
        return false;
      }
      type = type.BaseType;
      while (type != null) {
        ImmutableArray<ISymbol> members = type.GetMembers();
        foreach(var m in members) {
          if (m.Name.Equals(member)) {
            return true;
          }
        }
        type = type.BaseType;
      }
      return false;
    }

    private void BinaryNode(SyntaxNode node, string op) {
      ExpressionNode(GetChildNode(node, 1));
      method.Append(op);
      ExpressionNode(GetChildNode(node, 2));
    }

    private void EqualsNode(SyntaxNode node, string op) {
      SyntaxNode left = GetChildNode(node, 1);
      SyntaxNode right = GetChildNode(node, 2);
      bool useEquals = false;
      bool leftString = false;
      bool rightString = false;
      if (IsString(left)) leftString = true;
      if (IsString(right)) rightString = true;
      if (leftString && rightString) {
        useEquals = true;
      }
      if (GetTypeName(left) == "System::Type" && GetTypeName(right) == "System::Type") {
        useEquals = true;
      }
      if (useEquals) {
        if (op == "!=") method.Append("!");
        ExpressionNode(left);
        method.Append("->Equals(");
        ExpressionNode(right);
        method.Append(")");
      } else {
        BinaryNode(node, op);
      }
    }

    private void BinaryAssignNode(SyntaxNode node, string op) {
      ExpressionNode(GetChildNode(node, 1), true);
      method.Append(" = ");
      ExpressionNode(GetChildNode(node, 1));
      method.Append(op);
      ExpressionNode(GetChildNode(node, 2));
    }

    private String GetModType(SyntaxNode left, SyntaxNode right) {
      if (IsDouble(left)) return "d";
      if (IsDouble(right)) return "d";
      if (IsFloat(left)) return "f";
      if (IsFloat(right)) return "f";
      if (IsLong(left)) return "l";
      if (IsLong(right)) return "l";
      return "i";
    }

    //C++ does not support float % -- must use a special function
    private void ModNode(SyntaxNode node, string op) {
      SyntaxNode left = GetChildNode(node, 1);
      SyntaxNode right = GetChildNode(node, 2);
      method.Append("Core::mod");
      method.Append(GetModType(left, right));
      method.Append("(");
      ExpressionNode(left);
      method.Append(",");
      ExpressionNode(right);
      method.Append(")");
    }

    //C++ does not support float % -- must use a special function
    private void ModAssignNode(SyntaxNode node, string op) {
      SyntaxNode left = GetChildNode(node, 1);
      SyntaxNode right = GetChildNode(node, 2);
      ExpressionNode(left, true);
      method.Append("= Core::mod");
      method.Append(GetModType(left, right));
      method.Append("(");
      ExpressionNode(left);
      method.Append(",");
      ExpressionNode(right);
      method.Append(")");
    }

    private void CastNode(SyntaxNode node) {
      SyntaxNode castType = GetChildNode(node, 1);
      SyntaxNode value = GetChildNode(node, 2);
      //cast value to type
      //C# (type)value
      //C++ dynamic_cast<type>(value)
      Type type = new Type(castType);
      String typestr = type.GetTypeDeclaration();
      if (type.isObject) {
        method.Append("dynamic_cast<");
        method.Append(typestr);
        method.Append(">");
        method.Append("(");
        ExpressionNode(value);
        method.Append(")");
      } else {
        method.Append("static_cast<");
        method.Append(type.GetTypeDeclaration());
        method.Append(">");
        method.Append("(");
        ExpressionNode(value);
        method.Append(")");
      }
    }

    private void NewArrayNode(SyntaxNode node) {
      //node = ArrayCreationExpression -> {ArrayType -> {type, [[rank -> size] ...]} [, ArrayInitializerExpression -> {...}]}
      SyntaxNode arrayType = GetChildNode(node);
      IEnumerable<SyntaxNode> nodes = arrayType.ChildNodes();
      SyntaxNode typeNode = null;
      SyntaxNode sizeNode = null;
      int dims = 0;
      foreach(var child in nodes) {
        switch (child.Kind()) {
          case SyntaxKind.ArrayRankSpecifier:
            SyntaxNode rank = GetChildNode(child);
            dims++;
            switch (rank.Kind()) {
              case SyntaxKind.OmittedArraySizeExpression:
                break;
              default:
                if (sizeNode != null) {
                  Console.WriteLine("Error:multiple sizes for ArrayCreationExpression");
                  WriteFileLine(node);
                  errors++;
                }
                sizeNode = rank;  //*Expression
                break;
            }
            break;
          default:  //PredefinedType or IdentifierName
            typeNode = child;
            break;
        }
      }
      SyntaxNode initList = GetChildNode(node, 2);
      if (initList != null && initList.Kind() == SyntaxKind.ArrayInitializerExpression) {
        //method.Append("=");
        Type dataType = new Type(typeNode);
        NewArrayInitNode(initList, dataType, dims);
        return;
      }
      if (typeNode == null || sizeNode == null || dims == 0) {
        Console.WriteLine("Error:Invalid ArrayCreationExpression : " + typeNode + " : " + sizeNode);
        WriteFileLine(node);
        errors++;
        return;
      }
      method.Append(" new(");
      ExpressionNode(sizeNode);
      method.Append(")");
      for(int a=0;a<dims;a++) {
        method.Append("Core::FixedArray$T<");
      }
      Type type = new Type(typeNode);
      method.Append(type.GetTypeDeclaration());
      for(int a=0;a<dims;a++) {
        if (a > 0) method.Append("*");
        method.Append(">");
      }
      method.Append("(");
      method.Append(type.GetCoreType());
      method.Append(")");
    }

    private void AssignNode(SyntaxNode node) {
      //lvalue = rvalue
      SyntaxNode left = GetChildNode(node, 1);
      SyntaxNode right = GetChildNode(node, 2);
      ExpressionNode(left);
      method.Append(" = ");
      if (false && IsMethod(right)) {
        //assign method to delegate
        if (IsStatic(node)) {
          method.Append("&");
          ExpressionNode(right);
        } else {
          Type type = new Type(right);
          method.Append("std::bind(&");
          method.Append(type.GetCPPType());
          method.Append(", this");
          //add std::placeholders::_1, ... for # of arguments to delegate
          int numArgs = GetNumArgs(right);
          for(int a=0;a<numArgs;a++) {
            method.Append(", std::placeholders::_" + (a+1));
          }
          method.Append(")");
        }
      } else {
        ExpressionNode(right);
      }
    }

    private void InvokeNode(SyntaxNode node, bool New = false) {
      //IdentifierName/SimpleMemberAccessExpression/QualifiedName, ArgumentList
      SyntaxNode id = GetChildNode(node, 1);
      SyntaxNode args = GetChildNode(node, 2);
      if (New) {
        method.Append("(new ");
      }
      ExpressionNode(id, !New);
      method.Append("(");
      OutArgList(args);
      method.Append(")");
      if (New) {
        method.Append(")");
      }
    }

    //ArgumentList
    private void OutArgList(SyntaxNode node) {
      IEnumerable<SyntaxNode> nodes = node.ChildNodes();
      bool first = true;
      foreach(var child in nodes) {
        if (!first) method.Append(","); else first = false;
        switch (child.Kind()) {
          case SyntaxKind.Argument:
            ExpressionNode(GetChildNode(child));
            break;
        }
      }
    }

    public static SyntaxNode GetChildNode(SyntaxNode node, int idx = 1) {
      IEnumerator<SyntaxNode> e = node.ChildNodes().GetEnumerator();
      for(int a=0;a<idx;a++) {
        e.MoveNext();
      }
      return e.Current;
    }

    private int GetChildCount(SyntaxNode node) {
      IEnumerable<SyntaxNode> childs = node.ChildNodes();
      int cnt = 0;
      foreach(var child in childs) {
        cnt++;
      }
      return cnt;
    }

    private string GetSymbol(SyntaxNode node) {
      ISymbol symbol = file.model.GetSymbolInfo(node).Symbol;
      if (symbol != null) return symbol.Name.Replace(".", "::");
      return null;
    }

    private string GetDeclaredSymbol(SyntaxNode node) {
      ISymbol symbol = file.model.GetDeclaredSymbol(node);
      if (symbol != null) return symbol.Name.Replace(".", "::");
      return null;
    }

    private static string GetTypeName(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return "";
      return type.ToString().Replace(".", "::");
    }

    private TypeKind GetTypeKind(SyntaxNode node) {
      ITypeSymbol type = file.model.GetTypeInfo(node).Type;
      if (type == null) return TypeKind.Error;
      return type.TypeKind;
    }

    public static void WriteFileLine(SyntaxNode node) {
      FileLinePositionSpan span = file.tree.GetLineSpan(node.Span);
      Console.WriteLine("  in " + file.csFile + " @ " + (span.StartLinePosition.Line + 1));
    }

    public static String GetEnumStruct(Enum e) {
      StringBuilder str = new StringBuilder();
      str.Append("typedef struct " + e.name + "{\r\n");
      str.Append("int value;\r\n");
      str.Append(e.name + "() {value = 0;}\r\n");
      str.Append(e.name + "(int initValue) {value = initValue;}\r\n");
      str.Append("void operator=(int newValue) {value=newValue;}\r\n");
      str.Append("operator int() {return value;}\r\n");
      foreach(var qt in e.qtType) {
        str.Append("operator " + qt + "() {return (" + qt + ")value;}");
      }
      str.Append("bool operator==(int other) {return value!=other;}\r\n");
      str.Append("bool operator!=(int other) {return value!=other;}\r\n");
      str.Append("} ");
      return str.ToString();
    }
  }

  class Flags
  {
    public bool isPublic;
    public bool isPrivate;
    public bool isProtected;
    public bool isStatic;
    public bool isAbstract;
    public bool isVirtual;
    public bool isExtern;
    public bool isOverride;
    public bool isDefinition;
    public bool isSealed;
    public string GetFlags(bool cls, bool isGeneric) {
      StringBuilder sb = new StringBuilder();
      //if (Public) sb.Append("public:");
      //if (Private) sb.Append("private:");
      //if (Protected) sb.Append("protected:");
      //if (!isGeneric) sb.Append("public:");
      if (isStatic) sb.Append(" static");
      if (isAbstract) {
        if (!cls) {
          if (!isVirtual) {
            sb.Append(" virtual");
          }
        }
      }
      if (isVirtual) sb.Append(" virtual");
      return sb.ToString();
    }
    public void CopyFlags(Flags src) {
      isPublic = src.isPublic;
      isPrivate = src.isPrivate;
      isProtected = src.isProtected;
      isStatic = src.isStatic;
      isAbstract = src.isAbstract;
      isVirtual = src.isVirtual;
      isExtern = src.isExtern;
      isOverride = src.isOverride;
      isDefinition = src.isDefinition;
      isSealed = src.isSealed;
    }
  }

  class Enum {
    public Enum(string name, string Namespace) {
      this.name = name;
      this.Namespace = Namespace;
    }
    public string name;
    public string Namespace;
    public List<string> qtType = new List<string>();
  }

  class Class : Flags
  {
    public string name = "";
    public string fullname = "";  //inner classes
    public string Namespace = "";
    public string nsfullname = "";  //namespace + fullname
    public bool hasctor;
    public bool isInterface;
    public SyntaxNode node;
    public SemanticModel model;
    public List<Type> bases = new List<Type>();
    public List<string> cppbases = new List<string>();
    public List<Type> ifaces = new List<Type>();
    public List<Field> fields = new List<Field>();
    public List<Method> methods = new List<Method>();
    public List<Enum> enums = new List<Enum>();
    public List<Class> inners = new List<Class>();
    public List<string> usingBaseMembers = new List<string>();
    public Class outter;
    public int lockCnt;
    public int finallyCnt;
    public int enumCnt;
    public int switchStringCnt;
    public bool isGeneric;
    public List<Type> GenericArgs = new List<Type>();
    //uses are used to sort classes
    public List<string> uses = new List<string>();
    public void AddUsage(string cls) {
      int idx;
      idx = cls.IndexOf("<");
      if (idx != -1) cls = cls.Substring(0, idx);
      if (cls == nsfullname) return;  //do not add ref to this
      if (!uses.Contains(cls)) {
        uses.Add(cls);
      }
    }
    public string GetTypeDeclaration() {
      StringBuilder sb = new StringBuilder();
      sb.Append(name);
      if (isGeneric) {
        sb.Append("<");
        bool first = true;
        foreach(var arg in GenericArgs) {
          if (!first) sb.Append(","); else first = false;
          sb.Append(arg.GetCPPType());
        }
        sb.Append(">");
      }
      return sb.ToString();
    }
    public string FullName(String NameSpace, String name) {
      String ret = NameSpace.Replace("::", "_");
      if (ret.Length > 0) ret += "_";
      ret += name.Replace("::", "_");
      return ret;
    }
    public string GetReflectionExtern() {
      StringBuilder sb = new StringBuilder();
      String full_name = FullName(Namespace, fullname);
      sb.Append("namespace Core {\r\n");
      sb.Append("  extern Class Class_" + full_name + ";\r\n");
      sb.Append("  extern System::Type Type_" + full_name + ";\r\n");
      sb.Append("}\r\n");
      foreach(var inner in inners) {
        sb.Append(inner.GetReflectionExtern());
      }
      return sb.ToString();
    }
    public string GetForwardDeclaration() {
      StringBuilder sb = new StringBuilder();
      if (isGeneric) {
        sb.Append("template<");
        bool first = true;
        foreach(var arg in GenericArgs) {
          if (!first) sb.Append(","); else first = false;
          sb.Append("typename ");
          sb.Append(arg.GetTypeDeclaration());
        }
        sb.Append(">\r\n");
      }
      sb.Append("struct " + name);
      sb.Append(";\r\n");
      return sb.ToString();
    }
    public string GetReflectionData() {
      StringBuilder sb = new StringBuilder();
      bool first;
      int idx;
      String full_name = FullName(Namespace, fullname);
      sb.Append("namespace Core {\r\n");
      //reflection data : fields
      foreach(Field i in fields) {
        foreach(Variable v in i.variables) {
          sb.Append("static Field Field_" + full_name + "_" + v.name + "(\"" + v.name + "\");\r\n");
        }
      }
      //reflection data : methods
      idx = 0;
      foreach(Method m in methods) {
        if (m.name.StartsWith("$")) continue;
        String name = m.name;
        if (name.StartsWith("~")) {
          name = name.Replace("~", "$");
        }
        sb.Append("static Method Method_" + full_name + "_" + name + (idx++) + "(\"" + m.name + "\");\r\n");
      }
      //reflection data : class
      sb.Append("Class Class_" + full_name + "(");
      if (isInterface) {
        sb.Append("true,");
      } else {
        sb.Append("false,");
      }
      sb.Append("\"" + name + "\",");
      if (bases.Count == 0) {
        sb.Append("nullptr,");
      } else {
        sb.Append("&Class_" + bases[0].Get_Symbol() + ",");
      }
      //relection data : interfaces list
      sb.Append("{");
      first = true;
      foreach(var i in ifaces) {
        if (!first) sb.Append(","); else first = false;
        sb.Append("&Core::Class_" + i.Get_Symbol());
      }
      sb.Append("},");
      //reflection data : fields lists
      sb.Append("{");
      first = true;
      foreach(Field i in fields) {
        foreach(Variable v in i.variables) {
          if (!first) sb.Append(","); else first = false;
          sb.Append("&Core::Field_" + full_name + "_" + v.name);
        }
      }
      sb.Append("},");
      //reflection data : methods list
      sb.Append("{");
      first = true;
      idx = 0;
      foreach(Method m in methods) {
        if (m.name.StartsWith("$")) continue;
        if (!first) sb.Append(","); else first = false;
        String name = m.name;
        if (name.StartsWith("~")) {
          name = name.Replace("~", "$");
        }
        sb.Append("&Core::Method_" + full_name + "_" + name + (idx++));
      }
      sb.Append("}");
      //newInstance
      Method _new = null;
      if (!isGeneric) {
        foreach(Method m in methods) {
          if (m.name == m.cls.name) {
            if (m.args.Count == 0) {
              _new = m;
              break;
            }
          }
        }
      }
      if (_new == null || isAbstract) {
        sb.Append(",[] () {return nullptr;}");
      } else {
        sb.Append(",[] () {return new " + this.Namespace + "::" + this.fullname + "();}");
      }
      sb.Append(");\r\n");
      sb.Append("System::Type Type_" + full_name + "(&Core::Class_" + full_name + ");\r\n");
      sb.Append("};\r\n");  //namespace Core
      foreach(var inner in inners) {
        sb.Append(inner.GetReflectionData());
      }
      return sb.ToString();
    }
    public string GetClassDeclaration() {
      StringBuilder sb = new StringBuilder();
      bool first;
      String full_name = FullName(Namespace, fullname);
      if (isGeneric) {
        sb.Append("template< ");
        first = true;
        foreach(var arg in GenericArgs) {
          if (!first) sb.Append(","); else first = false;
          sb.Append("typename ");
          sb.Append(arg.GetTypeDeclaration());
        }
        sb.Append(">");
      }
      if (name != fullname) sb.Append(GetFlags(true, false));  //inner class
      sb.Append(" struct " + name);
      if (bases.Count > 0 || cppbases.Count > 0 || ifaces.Count > 0) {
        sb.Append(":");
        first = true;
        foreach(var basecls in bases) {
          if (!first) sb.Append(","); else first = false;
          sb.Append("public ");
          sb.Append(basecls.GetCPPType());
        }
        foreach(var cppcls in cppbases) {
          if (!first) sb.Append(","); else first = false;
          sb.Append("public ");
          sb.Append(cppcls);
        }
        foreach(var iface in ifaces) {
          if (!first) sb.Append(","); else first = false;
          sb.Append("public ");
          sb.Append(iface.GetCPPType());
        }
      }
      sb.Append("{\r\n");
      foreach(var inner in inners) {
        sb.Append(inner.GetClassDeclaration());
      }
      foreach(var e in enums) {
        sb.Append(Generate.GetEnumStruct(e) + e.name + ";\r\n");
      }
      foreach(var method in methods) {
        if (!method.isDelegate) continue;
        sb.Append(method.GetMethodDeclaration());
        if (isGeneric || method.isGeneric) {
          if (method.name == "$init") {
            sb.Append("{\r\n");
            foreach(var field in fields) {
              foreach(var v in field.variables) {
                if (v.method.Length() > 0 && !field.isStatic) {
                  sb.Append(v.method.src);
                }
              }
            }
            sb.Append("}\r\n");
          } else {
            if (!method.type.isAbstract) sb.Append(method.src);
          }
        }
        sb.Append(";\r\n");
      }
      foreach(var field in fields) {
        sb.Append(field.GetFieldDeclaration());
      }
      foreach(var method in methods) {
        if (method.isDelegate) continue;
        if (bases.Count > 0) {
          if (method.type.isOverride || Generate.IsNew(this, method.name) ) {
            if (!usingBaseMembers.Contains(method.name)) {
              usingBaseMembers.Add(method.name);
              //need to add using base::name so it does not hide base versions
              sb.Append("using ");
              sb.Append(bases[0].GetCPPType());
              sb.Append("::");
              sb.Append(method.name);
              sb.Append(";\r\n");
            }
          }
        }
        sb.Append(method.GetMethodDeclaration());
        if (isGeneric || method.isGeneric) {
          if (method.name == "$init") {
            sb.Append("{\r\n");
            foreach(var field in fields) {
              foreach(var v in field.variables) {
                if (v.method.Length() > 0 && !field.isStatic) {
                  sb.Append(v.method.src);
                }
              }
            }
            sb.Append("}\r\n");
          } else {
            if (!method.type.isAbstract) sb.Append(method.src);
          }
        }
        sb.Append(";\r\n");
      }
      if (!isInterface) {
        sb.Append("static System::Type* $GetType()");
        if (isGeneric) {
          sb.Append("{return &Core::Type_" + full_name + ";}\r\n");
        }
        sb.Append(";\r\n");
        if (nsfullname != "System::Object") {
          sb.Append("virtual System::Type* GetType()");
          if (isGeneric) {
            sb.Append("{ return $GetType(); }");
          }
          sb.Append(";\r\n");
        }
      }
      sb.Append("};\r\n");
      return sb.ToString();
    }
    public void GetInnerStaticFields(StringBuilder sb) {
      foreach(var inner in inners) {
        foreach(var field in inner.fields) {
          if (!field.isStatic) continue;
          foreach(var v in field.variables) {
            sb.Append(field.GetTypeDeclaration() + " " + inner.fullname + "::" + v.name);
            if (field.isNumeric) {
              sb.Append("= 0;\r\n");
            }  else {
              sb.Append(";\r\n");
            }
          }
        }
        inner.GetInnerStaticFields(sb);
      }
    }
    public void GetInnerStaticFieldsInit(StringBuilder sb) {
      foreach(var inner in inners) {
        foreach(var field in inner.fields) {
          if (!field.isStatic) continue;
          foreach(var v in field.variables) {
            if (field.isObject || field.isArray) {
              sb.Append("Core::Object::GC_add_static_ref((Core::Object**)&");
              sb.Append(field.cls.nsfullname);
              sb.Append("::");
              sb.Append(v.name);
              sb.Append(");\r\n");
            }
            sb.Append(v.method.src);
          }
        }
        inner.GetInnerStaticFields(sb);
      }
    }
    public string GetStaticFields() {
      StringBuilder sb = new StringBuilder();
      foreach(var field in fields) {
        if (!field.isStatic) continue;
        foreach(var v in field.variables) {
          sb.Append(field.GetTypeDeclaration() + " " + name + "::" + v.name);
          if (field.isNumeric) {
            sb.Append("= 0;\r\n");
          }  else {
            sb.Append(";\r\n");
          }
        }
      }
      GetInnerStaticFields(sb);
      return sb.ToString();
    }
    public string GetStaticFieldsInit() {
      StringBuilder sb = new StringBuilder();
      foreach(var field in fields) {
        if (!field.isStatic) continue;
        foreach(var v in field.variables) {
          if (field.isObject || field.isArray) {
            sb.Append("Core::Object::GC_add_static_ref((Core::Object**)&");
            sb.Append(field.cls.nsfullname);
            sb.Append("::");
            sb.Append(v.name);
            sb.Append(");\r\n");
          }
          sb.Append(v.method.src);
        }
      }
      GetInnerStaticFieldsInit(sb);
      return sb.ToString();
    }
    public string GetMethodsDefinitions() {
      StringBuilder sb = new StringBuilder();
      foreach(var method in methods) {
        if (method.isDelegate) continue;
        if (method.isGeneric) continue;
        if (method.type.isExtern) continue;
        if (method.type.isAbstract) {
          //C++ allows abstract methods to be defined and can be called
          //Need to invoke virtual function without scope specifier (BUG : Need to remove scope specifier in the first place)
          if (method.type.GetCSType() == "void") {
            method.Append("{" + method.name + method.GetArgsNames() + ";}");
          } else if (method.type.isPrimitive) {
            method.Append("{return " + method.name + method.GetArgsNames() + ";}");
          } else {
            method.Append("{return " + method.name + method.GetArgsNames() + ";}");
          }
        }
        sb.Append(method.type.GetTypeDeclaration());
        sb.Append(" ");
        sb.Append(method.cls.fullname);
        sb.Append("::");
        sb.Append(method.name);
        sb.Append(method.GetArgs(false));
        if (method.ctor) {
          if (method.basector != null) {
            sb.Append(":");
            sb.Append(method.basector);
          }
        }
        if (method.Length() == 0) method.Append("{}\r\n");
        if (method.name == "$init") {
          sb.Append("{\r\n");
          foreach(var field in method.cls.fields) {
            foreach(var v in field.variables) {
              if (v.method.Length() > 0 && !field.isStatic) {
                sb.Append(v.method.src);
              }
            }
          }
          sb.Append("}\r\n");
        } else {
          sb.Append(method.src);
        }
      }
      foreach(var inner in inners) {
        sb.Append(inner.GetMethodsDefinitions());
      }
      if (!isInterface) {
        String full_name = FullName(Namespace, fullname);
        //virtual GetType()
        sb.Append("System::Type* " + fullname + "::GetType() {");
        sb.Append("  return &Core::Type_" + full_name + ";\r\n");
        sb.Append("}\r\n");
        //static GetType()
        sb.Append("System::Type* " + fullname + "::$GetType() {");
        sb.Append("  return &Core::Type_" + full_name + ";\r\n");
        sb.Append("}\r\n");
      }
      return sb.ToString();
    }
    public string GetExternMethodsDefinitions() {
      StringBuilder sb = new StringBuilder();
      foreach(var method in methods) {
        if (!method.type.isExtern) continue;
        sb.Append(method.type.GetTypeDeclaration());
        sb.Append(" ");
        sb.Append(method.cls.fullname);
        sb.Append("_");
        sb.Append(method.name);
        sb.Append(method.GetArgs(false));
        sb.Append(";\r\n");
      }
      foreach(var inner in inners) {
        sb.Append(inner.GetExternMethodsDefinitions());
      }
      return sb.ToString();
    }
  }

  class Type : Flags {
    private String type;
    public TypeKind typekind;
    public SymbolKind symbolkind;
    public SyntaxNode node;
    public ISymbol symbol, declSymbol;
    public ITypeSymbol typeSymbol;
    public bool isGeneric;
    public bool isPrimitive;
    public bool isNumeric;
    public bool isArray;
    public int arrays;  //# of dimensions
    public bool isObject;
    public bool isDelegate;
    public bool isPtr;  //unsafe pointer
    public int ptrs;
    public Class cls;

    public virtual bool IsField() {return false;}
    public virtual bool IsMethod() {return false;}

    public Type() {}
    public Type(SyntaxNode node, bool useName = false) {
      Set(node, useName);
      SetTypes();
    }
    public Type(SyntaxNode node, String sym) {
      this.node = node;
      Set(sym);
      SetTypes();
    }
    public void CopyType(Type src) {
      type = src.type;
      typekind = src.typekind;
      symbolkind = src.symbolkind;
      node = src.node;
      symbol = src.symbol;
      declSymbol = src.declSymbol;
      typeSymbol = src.typeSymbol;
      isGeneric = src.isGeneric;
      isPrimitive = src.isPrimitive;
      isNumeric = src.isNumeric;
      isArray = src.isArray;
      arrays = src.arrays;
      isObject = src.isObject;
      isPtr = src.isPtr;
      ptrs = src.ptrs;
      cls = src.cls;
    }
    private String[] Split(String token) {
      String[] strs;
      int thisLength = type.Length;
      int tokenLength = token.Length;
      if (tokenLength == 0 || tokenLength >= thisLength) {
        strs = new String[1];
        strs[0] = type;
        return strs;
      }
      int cnt = 1;
      int off = 0;
      while (off < thisLength) {
        int idx = type.IndexOf(token, off);
        if (idx == -1) break;
        cnt++;
        off = idx + tokenLength;
      }
      strs = new String[cnt];
      cnt = 0;
      off = 0;
      while (off < thisLength) {
        int idx = type.IndexOf(token, off);
        if (idx == -1) {
          strs[cnt] = type.Substring(off);
          break;
        } else {
          strs[cnt] = type.Substring(off, idx - off);
        }
        cnt++;
        off = idx + tokenLength;
      }
      return strs;
    }
    private String Join(String[] parts) {
      StringBuilder sb = new StringBuilder();
      bool first = true;
      foreach(String part in parts) {
        if (first) first = false; else sb.Append("::");
        sb.Append(part);
      }
      return sb.ToString();
    }
    public void Set(String sym) {
      int idx = sym.IndexOf("(");
      if (idx != -1) {
        sym = sym.Substring(0, idx);
      }
      type = sym.Replace(".", "::");
    }
    public void Set(SyntaxNode node, bool useName = false) {
      this.node = node;
      if (node.Kind() == SyntaxKind.GenericName) {
        isGeneric = true;
      }
      while (node.Kind() == SyntaxKind.ArrayType) {
        isArray = true;
        foreach(var child in node.ChildNodes()) {
          if (child.Kind() == SyntaxKind.ArrayRankSpecifier) {
            arrays++;
            if (arrays > 3) {
              Console.WriteLine("Error:Array Dimensions not supported:" + arrays);
              Generate.WriteFileLine(node);
              Generate.errors++;
            }
          }
        }
        node = Generate.GetChildNode(node);
      }
      symbol = Generate.file.model.GetSymbolInfo(node).Symbol;
      if (symbol == null) {
        if (node.ToString() == "Length") {
//          Console.WriteLine("Warning:symbol==null:node=Length:Assuming template array");
          Set("Length");
          return;
        }
        Console.WriteLine("Error:symbol==null:" + node.Kind().ToString() + ":" + node.ToString());
        Generate.WriteFileLine(node);
        Generate.errors++;
        return;
      }
      switch (symbol.Kind) {
        case SymbolKind.Parameter:
          useName = true;
          break;
      }
      symbolkind = symbol.Kind;
      typeSymbol = Generate.file.model.GetTypeInfo(node).Type;
      if (typeSymbol != null) {
        typekind = typeSymbol.TypeKind;
        switch (typekind) {
          case TypeKind.Delegate:
//            if (Generate.cls.isGeneric) useName = true;  //for MSVC bug
            break;
        }
      }
      String value = Generate.ConstantNode(node);
      if (value != null) {
        Set(value);
      } else {
        if (useName) {
          Set(symbol.Name.Replace(".", "::"));
        } else {
          Set(symbol.ToString().Replace(".", "::"));
        }
      }
      if (node.Kind() == SyntaxKind.GenericName) {
        isGeneric = true;
        //replace generic args
        SyntaxNode typeArgList = Generate.GetChildNode(node);
        int idx1 = type.LastIndexOf('<');
        int idx2 = type.LastIndexOf('>');
        if (idx1 != -1 && idx2 != -1) {
          type = type.Substring(0, idx1+1) + type.Substring(idx2);
          String args = "";
          bool first = true;
          foreach(var child in typeArgList.ChildNodes()) {
            Type t = new Type(child);
            if (first) {first = false;} else {args += ",";}
            args += t.GetTypeDeclaration();
          }
          type = type.Insert(idx1+1, args);
        }
      } else {
        int idx = type.IndexOf('<');
        if (idx != -1) {
          isGeneric = true;
        }
      }
      if (isGeneric) {
        if (Program.linux || Program.windows) {
          //if nested template type, need to add typename ... template ... for gcc (msvc doesn't care)
          //see https://en.cppreference.com/w/cpp/language/dependent_name
          //this is why I no longer code in C++, too complex
          String[] parts = Split("::");
          int cnt = 0;
          for(int a=0;a<parts.Length;a++) {
            if (parts[a].Contains("<")) cnt++;
          }
          if (cnt > 1) {
            int pos = 0;
            for(int a=0;a<parts.Length;a++) {
              if (parts[a].Contains("<")) {
                if (cnt > 1) {
                  parts[pos] = "typename " + parts[pos];
                } else {
                  parts[pos] = "template " + parts[pos];
                }
                pos = a+1;
                cnt--;
              }
            }
            type = Join(parts);
          }
        }
      }
    }
    public void SetTypes() {
      isDelegate = typekind == TypeKind.Delegate;
      switch (type) {
        case "":
        case "void":
          isPrimitive = true;
          isNumeric = false;
          isObject = false;
          break;
        case "bool":
        case "byte":
        case "sbyte":
        case "short":
        case "ushort":
        case "int":
        case "uint":
        case "long":
        case "ulong":
        case "char":
        case "float":
        case "double":
          isPrimitive = true;
          isNumeric = true;
          isObject = false;
          break;
        default:
          isPrimitive = false;
          isObject = true;
          switch (typekind) {
            case TypeKind.Delegate: isObject = false; break;
            case TypeKind.Enum: isObject = false; break;
            case TypeKind.TypeParameter: isObject = false; break;
          }
          if (node != null) {
            switch (node.Kind()) {
              case SyntaxKind.TypeParameter: isObject = false; break;
            }
          }
          break;
      }
    }
    public bool IsSymbolMethod() {
      return symbolkind == SymbolKind.Method;
    }
    public string ConvertType() {
      switch (type) {
        case "byte": return "uint8";
        case "sbyte": return "int8";
        case "short": return "int16";
        case "ushort": return "uint16";
        case "int": return "int32";
        case "uint": return "uint32";
        case "long": return "int64";
        case "ulong": return "uint64";
        case "char": return "char16";
        case "string":
        case "System::string":
          return "System::String";
        case "object":
        case "System::object":
          return "System::Object";
        default: return Generate.ConvertName(type);
      }
    }
    public String GetSymbol() {
      //strips off <> template
      String sym = ConvertType();
      int idx = sym.IndexOf("<");
      if (idx != -1) {
        sym = sym.Substring(0, idx);
      }
      if (isGeneric) {
        sym += "$T";
      }
      return sym;
    }
    public String Get_Symbol() {
      return GetSymbol().Replace("::", "_");
    }
    public String GetName() {
      String sym = GetSymbol();
      int idx = sym.LastIndexOf("::");
      if (idx != -1) {
        return sym.Substring(idx+2);
      }
      return sym;
    }
    public string GetCSType() {
      return type;
    }
    public string GetCPPType() {
      //keeps <> template
      String id = ConvertType();
      if (isGeneric) {
        if (id.Contains("<")) {
          id = id.Replace("<", "$T<");
        } else {
          id += "$T";
        }
      }
      return id;
    }
    public string GetCoreType() {
      return "Core::GetType$T<" + GetCPPType() + ">()";
    }
    public string GetTypeDeclaration(bool inc_arrays = true) {
      StringBuilder sb = new StringBuilder();
      if (inc_arrays && arrays > 0) {
        for(int a=0;a<arrays;a++) {
          sb.Append("Core::FixedArray$T<");
        }
      }
      sb.Append(GetCPPType());
      if (isObject) sb.Append("*");
      if (isPtr) {
        for(int a=0;a<ptrs;a++) {
          sb.Append("*");
        }
      }
      if (inc_arrays && arrays > 0) {
        for(int a=0;a<arrays;a++) {
          if (a > 0) sb.Append("*");
          sb.Append(">");
        }
        sb.Append("*");
      }
      return sb.ToString();
    }
  }

  class Variable {
    public string name;
    public SyntaxNode equals;
    public Method method = new Method();  //equals code
  }

  class Argument {
    public Type type;
    public Variable name = new Variable();
  }

  class Field : Type
  {
    public override bool IsField() {return true;}
    public List<Variable> variables = new List<Variable>();
    public bool isProperty;
    public bool get_Property;
    public bool set_Property;

    public string GetFieldDeclaration() {
      StringBuilder sb = new StringBuilder();
      foreach(var v in variables) {
        sb.Append(GetFlags(false, false));
        if (isProperty) {
          sb.Append(" Core::Property<");
        }
        if (isArray) {
          sb.Append(" ");
          sb.Append(GetTypeDeclaration());
          sb.Append(" ");
          if (isProperty) {
            sb.Append(">");
          }
          sb.Append(v.name);
        } else {
          sb.Append(" ");
          sb.Append(GetTypeDeclaration());
          sb.Append(" ");
          if (isProperty) {
            sb.Append(">");
          }
          sb.Append(v.name);
        }
        if (!isStatic && !isProperty && !isDelegate) {
          if (isObject) {
            sb.Append(" = nullptr");
          } else {
            sb.Append(" = 0");
          }
        }
        sb.Append(";\r\n");
      }
      return sb.ToString();
    }
  }

  class Method
  {
    public Type type = new Type();
    public StringBuilder src = new StringBuilder();
    public int Length() {return src.Length;}
    public void Append(String str) {src.Append(str);}
    public void Insert(int idx, String str) {src.Insert(idx, str);}
    public String name;
    public SyntaxNode node;
    public ISymbol symbol;

    public bool ctor;
    public bool isDelegate;
    public bool isOperator;
    public bool isGeneric;
    public string Namespace;  //if classless delegate only
    public String basector;
    public List<Argument> args = new List<Argument>();
    public List<Type> GenericArgs = new List<Type>();
    public Class cls;
    public bool inFixedBlock;
    public int[] switchIDs = new int[32];  //up to 32 nested switch statements
    public int currentSwitch = -1;
    public int nextSwitchID = 0;

    public string GetArgs(bool decl) {
      StringBuilder sb = new StringBuilder();
      sb.Append("(");
      bool first = true;
      foreach(var arg in args) {
        if (!first) sb.Append(","); else first = false;
        sb.Append(arg.type.GetTypeDeclaration());
        sb.Append(" ");
        sb.Append(arg.name.name);
        if (decl && arg.name.method.src.Length > 0) {
          sb.Append(" = " );
          sb.Append(arg.name.method.src.ToString());
        }
      }
      sb.Append(")");
      return sb.ToString();
    }
    public string GetArgsNames() {
      StringBuilder sb = new StringBuilder();
      sb.Append("(");
      bool first = true;
      foreach(var arg in args) {
        if (!first) sb.Append(","); else first = false;
        sb.Append(arg.name.name);
      }
      sb.Append(")");
      return sb.ToString();
    }
    public string GetMethodDeclaration() {
      StringBuilder sb = new StringBuilder();
      if (isGeneric) {
        sb.Append("template<");
        bool first = true;
        foreach(var arg in GenericArgs) {
          if (!first) sb.Append(","); else first = false;
          sb.Append("typename ");
          sb.Append(arg.GetTypeDeclaration());
        }
        sb.Append(">\r\n");
      }
      if (isOperator) sb.Append("inline");
      if (!isDelegate && !isOperator) sb.Append(type.GetFlags(false, isGeneric));
      sb.Append(" ");
      if (isDelegate) sb.Append("typedef std::function<");
      sb.Append(type.GetTypeDeclaration());
      sb.Append(" ");
      if (isOperator) sb.Append(" operator");
      if (!isDelegate) sb.Append(name);
      sb.Append(GetArgs(true));
      if (isDelegate) {
        sb.Append(">");  //$delegate");
        sb.Append(name);
      }
      if (type.isAbstract) sb.Append("=0" + ";\r\n");
      if (isOperator) {
        sb.Append(src);
      }
      return sb.ToString();
    }
  }

  [DiagnosticAnalyzer(LanguageNames.CSharp)]
  public sealed class DiagnosticSuppressorCS0626 : DiagnosticSuppressor {
    public SuppressionDescriptor SuppressionDescriptor => new SuppressionDescriptor(
      id: "SPR0001",
      suppressedDiagnosticId: "CS0626",
      justification: "Blah Blah Blah");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(SuppressionDescriptor);

    public override void ReportSuppressions(SuppressionAnalysisContext context) {
      foreach (var diagnostic in context.ReportedDiagnostics) {
        context.ReportSuppression(Suppression.Create(SuppressionDescriptor, diagnostic));
      }
    }
  }

  [DiagnosticAnalyzer(LanguageNames.CSharp)]
  public sealed class DiagnosticSuppressorCS0227 : DiagnosticSuppressor {
    public SuppressionDescriptor SuppressionDescriptor => new SuppressionDescriptor(
      id: "SPR0002",
      suppressedDiagnosticId: "CS0227",
      justification: "Blah Blah Blah");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(SuppressionDescriptor);

    public override void ReportSuppressions(SuppressionAnalysisContext context) {
      foreach (var diagnostic in context.ReportedDiagnostics) {
        context.ReportSuppression(Suppression.Create(SuppressionDescriptor, diagnostic));
      }
    }
  }
  