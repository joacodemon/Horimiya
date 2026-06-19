@echo off
echo ===================================================
echo Compilando los poderosos (ImGui Version)...
echo ===================================================
if not exist lospoderosisimos mkdir lospoderosisimos

echo Ejecutando dotnet build...
.\.dotnet\dotnet.exe build "src\ImGuiApp\ImGuiApp.csproj" -c Release
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] La compilacion ha fallado.
    pause
    exit /b %errorlevel%
)

echo Copiando archivos a la carpeta lospoderosisimos...
xcopy /y /e "src\ImGuiApp\bin\Release\net48\*" "lospoderosisimos\"

echo.
echo [EXITO] Compilacion exitosa. Ejecutable generado en 'lospoderosisimos\'.
pause
