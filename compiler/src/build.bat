dotnet publish -c release -r win-x64

copy bin\x64\release\net8.0\win-x64\*.exe ..\..\bin
copy bin\x64\release\net8.0\win-x64\*.dll ..\..\bin
copy bin\x64\release\net8.0\win-x64\*.json ..\..\bin
