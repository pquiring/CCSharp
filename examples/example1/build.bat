@echo off
set HOME=..\..
cd src
::csc -noconfig -nostdlib -t:library -out:..\example.dll -r:%HOME%\..\lib\system.dll -recurse:*.cs -refonly
cd ..
%HOME%\bin\ccsharpcompiler.exe src Example --main=Example --ref=%HOME%\lib\System.dll --home=%HOME% --release --qt5
ninja
set HOME=
