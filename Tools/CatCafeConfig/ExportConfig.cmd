@echo off
setlocal
cd /d "%~dp0\..\.."
where py >nul 2>nul
if %errorlevel%==0 (
  py -3 Tools\CatCafeConfig\export_config.py
) else (
  python Tools\CatCafeConfig\export_config.py
)
if errorlevel 1 pause
endlocal
