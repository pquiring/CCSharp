CC#
===

C# -> C++ = CC#

  This is the next evolution in C# to C++ compiler based on the Q# project which tried to use attributes to inject C++ code
directly into the C# source which was a mess.
CC# instead uses extern functions which are defined in cpp source files, creating much cleaner code.
The compiler and assembly references are built directly with csc.exe (dotnet is no longer required).
The extern functions do not require a [DllImport ...] attribute so to avoid compiler warnings, include a <NoWarn>0626</NoWarn>
in your .csproj files, and add -nowarn:0626 on the command line for csc.exe.

Compiler
========

  The C# to C++ compiler is written in C# and uses the amazing Roslyn project to analyze code.
I've tried MANY times to use Java's compiler to build a similar project but it just doesn't expose enough symbol information,
and converting Java to C++ or C is impossible (even tried x64 assembly).  The Java language is just too abstract to implement.
Just look at the problems Oracle is having with Graal.

Supported C# features
---------------------

  Not all C# features are implemented yet.  But the basics are covered, including many fixes and improvements from Q#

Corelib
=======

  The Corelib already includes a garbage collector and basic classes (Thread, String, Console, etc.)
The corelib now uses standard C# names (like System.String and System.Object) since I've figured out how to
properly use the -nostdlib / -noconfig options.
You don't have to use Qt, you could use Gtk instead which provides a much easier to use C API but lacks in cross-platform support.
Q# relied too heavily on Qt which resulted in poor performance, so CC# will try to create classes in pure C# or use the C++ stdlib
as much as possible.

Windows
=======
  Download and install VS Build Tools.  Select C++ package.  Enough of C# compiler should be also installed along with it.
  Use the C# compiler in c:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\

Linux
=====
  To install C# compiler:
    Go to https://www.mono-project.com/download/stable/#download-lin to download latest version of Mono.
    The version in the default repo is probably too old.
    Note: The generated .exe / .dll files are no different than the ones generated in Windows.

Builing
=======
  Most building is done with scripts (scripts are for noobs) and ninja.  Will improve later.

Author : Peter Quiring (pquiring at gmail dot com)

Version : 0.0

Release Date : ?,? 2020
