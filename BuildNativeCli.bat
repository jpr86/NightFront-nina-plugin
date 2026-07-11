@echo off
REM Builds the NightFront CLI as a native-image executable (todos/nina-safety-delay-plan.md,
REM CLI-deployment hardening) and copies it alongside the plugin DLL in NINA's plugin folder, so
REM NightFrontReplanInstruction can find it at a known, co-located path with no user-configured
REM NightFrontCliPath required.
REM
REM %1 = the plugin's installed output folder (%%localappdata%%\NINA\Plugins\3.0.0\$(TargetName))
REM
REM Deliberately takes no argument for its own/the Gradle project's location - %~dp0 (this
REM script's own directory) is used instead, since passing $(ProjectDir) as a quoted MSBuild Exec
REM argument breaks: it always ends in a trailing backslash, and `\"` right before a closing quote
REM doesn't terminate the quoted string the way you'd expect, corrupting the whole command line
REM ("- was unexpected at this time", caught testing this exact script).
REM
REM Relies on nina-plugin always being checked out as a submodule inside nightfrontapp, so the
REM Gradle project is reliably one level up.
REM
REM By default, never fails the plugin build if that assumption doesn't hold (e.g. nina-plugin
REM cloned standalone) or if the native build itself fails - this step is a nice-to-have on top of
REM an already-working plugin build for the ordinary dev inner loop, not a precondition for one;
REM NightFrontCliPath remains a manual fallback either way. Set the environment variable
REM NIGHTFRONT_STRICT_CLI_BUILD=1 (e.g. in a CI/release pipeline) to turn every one of these
REM "skip and warn" branches into a real build failure instead - so a broken native-image build
REM can't silently ship in a tagged release with no CLI bundled and no signal anywhere that it
REM happened.
REM
REM Gotchas for anyone editing this script, all confirmed by bisecting it directly:
REM - A literal `(` or `)` in an echo message inside an if (...) block breaks cmd's paren-nesting
REM   parser ("- was unexpected at this time") - write "exit code %BUILD_RESULT%", not
REM   "(exit %BUILD_RESULT%)".
REM - `call :SomeLabel` from inside an if (...) block fails with "The system cannot find the batch
REM   label specified", even though the same label works fine called from outside any block.
REM - `exit /b` from inside a nested if-inside-if block, when there is more script content after
REM   that inner block within the same outer block, silently loses its exit code (always reports 0
REM   to the calling process, no error) - this is why the three "skip and warn" sites below each
REM   `goto` a single, top-level (unnested) :MaybeFail block to actually exit, rather than calling
REM   exit /b directly from inside their own nested if.
setlocal
set PLUGIN_OUT_DIR=%~1
set GRADLE_PROJECT_DIR=%~dp0..

if not exist "%GRADLE_PROJECT_DIR%\gradlew.bat" (
    echo [BuildNativeCli] No sibling NightFrontApp checkout found at "%GRADLE_PROJECT_DIR%" - skipping native CLI build. NightFrontCliPath can still be set manually.
    goto :MaybeFail
)

echo [BuildNativeCli] Building the NightFront CLI native image...
pushd "%GRADLE_PROJECT_DIR%"
call .\gradlew.bat --quiet nativeCompile
set BUILD_RESULT=%ERRORLEVEL%
popd

if not "%BUILD_RESULT%"=="0" (
    echo [BuildNativeCli] nativeCompile failed with exit code %BUILD_RESULT% - skipping copy. NightFrontCliPath can still be set manually.
    goto :MaybeFail
)

set CLI_EXE=%GRADLE_PROJECT_DIR%\build\native\nativeCompile\nightfront-cli.exe
if not exist "%CLI_EXE%" (
    echo [BuildNativeCli] nativeCompile reported success but nightfront-cli.exe wasn't found at "%CLI_EXE%" - skipping copy.
    goto :MaybeFail
)

echo [BuildNativeCli] Copying nightfront-cli.exe to "%PLUGIN_OUT_DIR%"
copy /y "%CLI_EXE%" "%PLUGIN_OUT_DIR%\nightfront-cli.exe" >nul

exit /b 0

:MaybeFail
if "%NIGHTFRONT_STRICT_CLI_BUILD%"=="1" (
    echo [BuildNativeCli] NIGHTFRONT_STRICT_CLI_BUILD=1 - treating the above as a build failure.
    exit /b 1
)
exit /b 0
