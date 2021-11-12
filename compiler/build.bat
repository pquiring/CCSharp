::csc src\CCSharpCompiler.cs -r:Microsoft.CodeAnalysis.CSharp.dll -r:Microsoft.CodeAnalysis.dll -r:System.Collections.Immutable.dll -r:netstandard.dll -debug+
cd src
call build.bat
cd ..
copy_bin.bat
