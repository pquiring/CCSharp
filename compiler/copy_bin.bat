@echo off
copy CCSharpCompiler.exe ..\bin
set SRC=\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn
copy "%SRC%\Microsoft.CodeAnalysis.CSharp.dll" ..\bin
copy "%SRC%\Microsoft.CodeAnalysis.dll" ..\bin
copy "%SRC%\System.Collections.Immutable.dll" ..\bin
copy "%SRC%\System.Reflection.Metadata.dll" ..\bin
set SRC=\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\amd64
copy "%SRC%\System.Memory.dll" ..\bin
copy "%SRC%\System.Runtime.CompilerServices.Unsafe.dll" ..\bin
copy "%SRC%\System.Numerics.Vectors.dll" ..\bin
