@echo off
echo ===================================================
echo Compilando los poderosos (ImGui Version)...
echo ===================================================
if not exist Horimiya mkdir Horimiya

echo Ejecutando dotnet build...
.\.dotnet\dotnet.exe build "src\ImGuiApp\ImGuiApp.csproj" -c Release
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] La compilacion ha fallado.
    pause
    exit /b %errorlevel%
)

echo Copiando archivos a la carpeta Horimiya...
xcopy /y /e "src\ImGuiApp\bin\Release\net48\*" "Horimiya\"

echo.
echo [EXITO] Compilacion exitosa. Ejecutable generado en 'Horimiya\'.
pause
