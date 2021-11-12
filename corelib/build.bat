@echo off
cd src
::csc -noconfig -nostdlib -unsafe -runtimemetadataversion:v4.0 -t:library -out:..\system.dll -refonly -nowarn:0626 -recurse:*.cs
cd ..
..\bin\CCSharpCompiler src System --library --corelib --home=.. --qt5 --release --no-npe-checks --no-abe-checks
if %ERRORLEVEL% GTR 0 goto end
copy system.dll ..\lib
ninja
copy *.lib ..\lib
copy cpp\*.hpp ..\include
:end
