CCSharpCompiler
===============

Desc : Converts C# to C++ with extern native API.

Code Name : CC#

Usage : CCSharpCompiler cs_in_folder cpp_out_folder [--library | --main=Class]

Notes:
 - uses Roslyn to analyze code
 - extern functions will call C++ functions defined in .cpp sources
 - generic class and methods are now supported
 - generic (template) types have a $T added to their name (class and methods)
  - this allows class Array and class Array<T> to both exist in the same namespace
 - compiler generates a ninja build file to compile the generated cpp source files
 - for release build try adding --no-npe-checks and --no-abe-checks for blazing performance

Building:
 - build.bat
 - copy_bin.bat
