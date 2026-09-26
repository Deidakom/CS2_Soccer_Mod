@echo off
rem CS2 SoccerMod drop-in installer (Windows): adds Metamod's line to
rem game\csgo\gameinfo.gi. Run once after copying, and again after every CS2 update.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Root "%~dp0."
echo.
pause
