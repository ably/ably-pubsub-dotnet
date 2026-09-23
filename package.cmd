@echo off
echo ======================================================
echo    Cake Build - Creating Ably.PubSub.*.nupkg
echo ======================================================
echo.
if "%1"=="" (
    echo Provide version number like: package.cmd 2.0.0
) else (
    dotnet tool restore
    dotnet cake cake-build/build.cake -- --target=Package --version=%1
)
