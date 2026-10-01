@echo off
rem Inicia el Sage Bridge en una consola (solo desarrollo). Cerrar la ventana o Ctrl+C lo detiene.
rem En el servidor el Bridge corre como servicio de Windows «PsaSageBridge».
cd /d "%~dp0bin\Debug"
if not exist PsaWeb.SageBridge.exe (
  echo No se encontro bin\Debug\PsaWeb.SageBridge.exe: compile antes con  dotnet build bridge\PsaWeb.SageBridge.sln
  pause
  exit /b 1
)
title PSA Sage Bridge (desarrollo)
PsaWeb.SageBridge.exe --consola
