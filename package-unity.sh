#!/bin/bash
echo "======================================================"
echo "   Cake Build - Creating ably.pubsub.*.unitypackage     "
echo "======================================================"
echo " "
if [ $# -eq 0 ]; then
    echo "Provide version number like: package-unity.sh 2.0.0"
else
    dotnet tool restore
    dotnet cake cake-build/build.cake -- --target=UnityPackage --version="$1"
fi
