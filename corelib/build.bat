@echo off
cd src
csc -noconfig -nostdlib -unsafe -runtimemetadataversion:v4.0 -t:library -out:..\system.dll -refonly -nowarn:0626 -recurse:*.cs
cd ..
copy system.dll ..\lib
..\bin\CCSharpCompiler src System --library --corelib --home=.. --qt5 --release --no-npe-checks --no-abe-checks
ninja
copy *.lib ..\lib
copy cpp\*.hpp ..\include
