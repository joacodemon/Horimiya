@echo off
echo ===================================================
echo Compilando los poderosos en C#...
echo ===================================================
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:los-poderosos.exe /resource:background.png /resource:logo.png /resource:password.png /resource:closing.png /optimize /r:System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll src\Utils\Win32.cs src\Config\MqttSettings.cs src\Config\AppConfig.cs src\UI\Controls.cs src\UI\CustomRandForm.cs src\UI\SplashForm.cs src\UI\LoginForm.cs src\UI\MainForm.cs src\Modules\Clicker.cs src\Modules\RightClicker.cs src\Modules\Recorder.cs src\Modules\Misc.cs src\Program.cs
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] La compilacion ha fallado.
    pause
    exit /b %errorlevel%
)
echo.
echo [EXITO] Compilacion exitosa. Se ha generado 'los-poderosos.exe'.
pause
