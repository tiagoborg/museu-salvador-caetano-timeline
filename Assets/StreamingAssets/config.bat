@echo off
setlocal

:: Set the target folder path
set "TARGET_FOLDER=%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\"

:: Ensure target folder exists
if not exist "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\" mkdir "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\"
:: Ensure settings file exists
if not exist "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\\settings.json" (
    copy "%~dp0config\settings.json" "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\" /Y
)

:: Ensure appSettings file exists
if not exist "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\\appSettings.json" (
    copy "%~dp0config\appSettings.json" "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\" /Y
)

:: Ensure appData file exists
if not exist "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\\appData.json" (
    copy "%~dp0config\appData.json" "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\" /Y
)

explorer "%userprofile%\AppData\LocalLow\Museu Salvador Caetano\Timeline\StreamingAssets\config\"
:: Set the shortcut name
set "SHORTCUT_NAME=Timeline Settings"

:: Get the Desktop path for the current user
set "DESKTOP_PATH=%USERPROFILE%\Desktop"

:: Check if Desktop path exists
if not exist "%DESKTOP_PATH%" (
    echo Error: Could not determine desktop path.
    pause
    exit /b
)

:: Create shortcut using VBScript directly in batch
echo Set oWS = WScript.CreateObject("WScript.Shell") > "%temp%\CreateShortcut.vbs"
echo sLinkFile = "%DESKTOP_PATH%\%SHORTCUT_NAME%.lnk" >> "%temp%\CreateShortcut.vbs"
echo Set oLink = oWS.CreateShortcut(sLinkFile) >> "%temp%\CreateShortcut.vbs"
echo oLink.TargetPath = "%TARGET_FOLDER%" >> "%temp%\CreateShortcut.vbs"
echo oLink.WorkingDirectory = "%TARGET_FOLDER%" >> "%temp%\CreateShortcut.vbs"
echo oLink.IconLocation = "%SystemRoot%\system32\SHELL32.dll,278" >> "%temp%\CreateShortcut.vbs"
echo oLink.Save >> "%temp%\CreateShortcut.vbs"

:: Run the VBScript
cscript //nologo "%temp%\CreateShortcut.vbs"

:: Delete the temporary VBScript
del "%temp%\CreateShortcut.vbs"

echo Shortcut created successfully!
exit