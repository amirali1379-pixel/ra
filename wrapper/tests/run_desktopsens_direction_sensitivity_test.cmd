@echo off
setlocal
cd /d "%~dp0"
where cl >nul 2>nul
if errorlevel 1 (
    echo ERROR: cl.exe was not found. Run this from a Visual Studio Developer Command Prompt.
    exit /b 2
)
cl /nologo /std:c++17 /EHsc /W4 /permissive- desktopsens_direction_sensitivity_test.cpp ..\desktopsens_runtime.cpp /Fe:desktopsens_direction_sensitivity_test.exe
if errorlevel 1 exit /b %errorlevel%
desktopsens_direction_sensitivity_test.exe
exit /b %errorlevel%
