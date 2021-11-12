@echo off
..\bin\CCSharpCompiler src System --library --home=.. --print > print.txt
echo print.txt generated!

