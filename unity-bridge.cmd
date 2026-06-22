@echo off
rem unity-bridge - CLI launcher for the Unity MCP bridge.
rem Runs the already-built server dll directly (fast; no rebuild), finding dotnet
rem on PATH or falling back to the standard install location.
setlocal
set "DOTNET=dotnet"
where dotnet >nul 2>nul || set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
"%DOTNET%" "%~dp0server\bin\Debug\net8.0\unity-mcp-bridge-server.dll" %*
