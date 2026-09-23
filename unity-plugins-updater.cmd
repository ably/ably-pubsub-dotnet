@echo off
if "%~1"=="" (echo "Provide latest version number like unity-plugins-updater.cmd 2.0.0") else (
	dotnet tool restore
	dotnet cake cake-build/build.cake -- --target=Build.NetStandard --define=UNITY_PACKAGE
	dotnet cake cake-build/build.cake -- --target=Update.AblyUnity
	echo %~1 > unity\Assets\Ably\version.txt
)
