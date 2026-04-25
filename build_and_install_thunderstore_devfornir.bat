@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM --- Config (edit if needed) ---
set "CONFIG=Debug"
set "PROFILE_NAME=DevForNIR"
set "REPO_DIR=%~dp0"
set "CSProj=%REPO_DIR%GeneticsArtifact.csproj"
set "BUILD_DLL=%REPO_DIR%bin\%CONFIG%\netstandard2.1\GeneticsArtifact.dll"

REM Thunderstore DataFolder root (default from your setup)
set "TS_DATAFOLDER=%APPDATA%\Thunderstore Mod Manager\DataFolder"

REM Optional: Thunderstore EXE path (set if auto-detect fails)
set "THUNDERSTORE_EXE="

REM --- Derived paths ---
set "PROFILE_DIR=%TS_DATAFOLDER%\RiskOfRain2\profiles\%PROFILE_NAME%"
set "BEPINEX_DIR=%PROFILE_DIR%\BepInEx"
set "PLUGINS_DIR=%BEPINEX_DIR%\plugins"

REM Two known locations where GeneticsArtifact.dll may be loaded from in this profile
set "DST1=%PLUGINS_DIR%\Unknown-GeneticsArtifact.dll\GeneticsArtifact.dll"
set "DST2=%PLUGINS_DIR%\Unknown-RoR2-StochasticGradientDescent_DDA\GeneticsArtifact.dll"

echo.
echo === Build: %CSProj% (%CONFIG%) ===
echo.

where dotnet >NUL 2>&1
if errorlevel 1 (
  echo [ERROR] dotnet not found in PATH.
  echo Install .NET SDK or fix PATH, then retry.
  exit /b 1
)

dotnet build "%CSProj%" -c "%CONFIG%"
if errorlevel 1 (
  echo [ERROR] Build failed.
  exit /b 1
)

if not exist "%BUILD_DLL%" (
  echo [ERROR] Built DLL not found: "%BUILD_DLL%"
  exit /b 1
)

if not exist "%PROFILE_DIR%" (
  echo [ERROR] Thunderstore profile not found: "%PROFILE_DIR%"
  echo Check TS_DATAFOLDER or PROFILE_NAME in this .bat.
  exit /b 1
)

echo.
echo === Install to Thunderstore profile: %PROFILE_NAME% ===
echo.

REM Ensure parent directories exist.
if not exist "%PLUGINS_DIR%" (
  mkdir "%PLUGINS_DIR%" >NUL 2>&1
)
if not exist "%PLUGINS_DIR%\Unknown-GeneticsArtifact.dll" (
  mkdir "%PLUGINS_DIR%\Unknown-GeneticsArtifact.dll" >NUL 2>&1
)
if not exist "%PLUGINS_DIR%\Unknown-RoR2-StochasticGradientDescent_DDA" (
  mkdir "%PLUGINS_DIR%\Unknown-RoR2-StochasticGradientDescent_DDA" >NUL 2>&1
)

copy /Y "%BUILD_DLL%" "%DST1%" >NUL
if errorlevel 1 (
  echo [ERROR] Copy failed: "%DST1%"
  exit /b 1
)

copy /Y "%BUILD_DLL%" "%DST2%" >NUL
if errorlevel 1 (
  echo [ERROR] Copy failed: "%DST2%"
  exit /b 1
)

for %%F in ("%DST1%" "%DST2%") do (
  echo [OK] Installed: %%~fF  (%%~zF bytes)
)

echo.
echo === Launch Thunderstore Mod Manager ===
echo.

if defined THUNDERSTORE_EXE (
  if exist "%THUNDERSTORE_EXE%" (
    start "" "%THUNDERSTORE_EXE%"
    goto :done
  )
)

REM Best-effort auto-detect common install locations.
set "CANDIDATE1=%LOCALAPPDATA%\Programs\Thunderstore Mod Manager\Thunderstore Mod Manager.exe"
set "CANDIDATE2=%LOCALAPPDATA%\Programs\Thunderstore Mod Manager\ThunderstoreModManager.exe"
set "CANDIDATE3=%PROGRAMFILES%\Thunderstore Mod Manager\Thunderstore Mod Manager.exe"
set "CANDIDATE4=%PROGRAMFILES(x86)%\Thunderstore Mod Manager\Thunderstore Mod Manager.exe"

for %%P in ("%CANDIDATE1%" "%CANDIDATE2%" "%CANDIDATE3%" "%CANDIDATE4%") do (
  if exist "%%~fP" (
    start "" "%%~fP"
    goto :done
  )
)

echo [WARN] Could not auto-detect Thunderstore exe.
echo Edit THUNDERSTORE_EXE at the top of this .bat to point to your Thunderstore Mod Manager executable.
goto :done

:done
echo.
echo === Done ===
echo DLL installed into Thunderstore profile "%PROFILE_NAME%".
echo Now start the game from Thunderstore using the "Modded" button.
echo.
pause
exit /b 0

