@echo off
setlocal enabledelayedexpansion

:: =============================================================================
:: update_fork.bat — Merge upstream into fork and apply fork-specific fixups
::
:: Usage:
::   tools\update_fork.bat [upstream-ref]
::
:: Examples:
::   tools\update_fork.bat                    &:: merges upstream/main
::   tools\update_fork.bat v9.6.0             &:: merges a specific tag
::   tools\update_fork.bat upstream/beta      &:: merges a specific branch
:: =============================================================================

set "REPO_ROOT=%~dp0.."
set "CONFIGURATOR_DIR=%REPO_ROOT%\MCPForUnity\Editor\Clients\Configurators"
set "KEEP_CONFIGURATOR=ClaudeCodeConfigurator"

set "UPSTREAM_REF=%~1"
if "%UPSTREAM_REF%"=="" set "UPSTREAM_REF=upstream/main"

:: --- Pre-flight checks -------------------------------------------------------

for /f "delims=" %%i in ('git -C "%REPO_ROOT%" status --porcelain') do (
    echo Error: working tree is not clean. Commit or stash changes first.
    exit /b 1
)

for /f "delims=" %%i in ('git -C "%REPO_ROOT%" branch --show-current') do set "CURRENT_BRANCH=%%i"
if not "%CURRENT_BRANCH%"=="main" (
    echo Error: expected to be on 'main', currently on '%CURRENT_BRANCH%'.
    exit /b 1
)

:: --- Handle --fixup-only -----------------------------------------------------

if "%UPSTREAM_REF%"=="--fixup-only" (
    call :apply_fixups
    exit /b %errorlevel%
)

:: --- Fetch & merge upstream ---------------------------------------------------

echo Fetching upstream...
git -C "%REPO_ROOT%" fetch upstream
if errorlevel 1 exit /b 1

echo Merging %UPSTREAM_REF%...
git -C "%REPO_ROOT%" merge %UPSTREAM_REF% -m "Merge %UPSTREAM_REF% into fork"
if errorlevel 1 (
    echo.
    echo Merge conflicts detected. Resolve them, then run:
    echo   git commit
    echo   tools\update_fork.bat --fixup-only
    exit /b 1
)

call :apply_fixups
echo.
echo Done. Review with: git log --oneline -5
exit /b 0

:: --- Fork fixups -------------------------------------------------------------

:apply_fixups

:: 1. Remove non-ClaudeCode configurators
set "removed=0"
if exist "%CONFIGURATOR_DIR%" (
    for %%f in ("%CONFIGURATOR_DIR%\*") do (
        echo %%~nxf | findstr /i /b "%KEEP_CONFIGURATOR%" >nul
        if errorlevel 1 (
            git -C "%REPO_ROOT%" rm -f "%%f" 2>nul
            set /a removed+=1
        )
    )
)
if %removed% gtr 0 (
    echo Removed %removed% non-ClaudeCode configurator file(s).
) else (
    echo No extra configurators to remove.
)

:: 2. Detect upstream version and apply fork suffix
for /f "delims=" %%v in ('python3 -c "import json,pathlib; v=json.loads(pathlib.Path('%REPO_ROOT:\=/%/MCPForUnity/package.json').read_text())['version']; print(v.split('-fork')[0])"') do set "UPSTREAM_VERSION=%%v"

set "FORK_VERSION=%UPSTREAM_VERSION%-fork.1"
echo Setting fork version: %FORK_VERSION%
python3 "%REPO_ROOT%\tools\update_versions.py" --version "%FORK_VERSION%"

:: 3. Regenerate uv.lock
echo Regenerating uv.lock...
pushd "%REPO_ROOT%\Server"
uv lock 2>nul || echo Warning: uv lock failed (uv may not be installed)
popd

:: 4. Stage and commit fixups
git -C "%REPO_ROOT%" add -A
git -C "%REPO_ROOT%" diff --cached --quiet
if errorlevel 1 (
    git -C "%REPO_ROOT%" commit -m "Apply fork fixups: remove non-ClaudeCode configurators, set version %FORK_VERSION%"
    echo Fork fixups committed.
) else (
    echo No fixups needed.
)

exit /b 0
