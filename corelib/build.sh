#!/bin/bash
cd src
csc -noconfig -nostdlib -unsafe -runtimemetadataversion:v4.0 -t:library -out:../system.dll -refonly -nowarn:0626 -recurse:*.cs
cd ..
cp system.dll ../lib
../bin/CCSharpCompiler.exe src System --library --corelib --home=.. --qt5 --release --no-npe-checks --no-abe-checks
ninja
cp System.a ../lib/libSystem.a
cp cpp/*.hpp ../include
