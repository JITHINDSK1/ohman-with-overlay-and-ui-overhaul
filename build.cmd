@echo off
:: Builds Ohman with the C# compiler that ships inside Windows (.NET Framework 4.8) - no SDK needed.
::   build.cmd            -> Ohman.exe (real; asks for administrator rights), preview\Ohman.exe, tools\omenprobe.exe
::   build.cmd preview    -> only preview\Ohman.exe (same UI, simulated hardware, no elevation)
:: The exe icon (app.ico) is drawn by the app itself so the mark has a single source of truth.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
set REFS=/r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xaml.dll /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsBase.dll"
set SRC=src\Meta.cs src\Support.cs src\Platform.cs src\Hardware.cs src\Hid.cs src\Lighting.cs src\Keyboard.cs src\Display.cs src\Update.cs src\Theme.cs src\Controls.cs src\Curve.cs src\Sensors.cs src\Engine.cs src\Profile.cs src\OverlayWindow.cs src\Ui.cs src\Program.cs
set RES=/resource:src\Ui.xaml,Ohman.Ui.xaml /resource:src\Ui.xaml,OMI.Ui.xaml /resource:fonts\IBMPlexSans-Regular.ttf,Ohman.fonts.IBMPlexSans-Regular.ttf /resource:fonts\IBMPlexSans-Regular.ttf,OMI.fonts.IBMPlexSans-Regular.ttf /resource:fonts\IBMPlexSans-Medium.ttf,Ohman.fonts.IBMPlexSans-Medium.ttf /resource:fonts\IBMPlexSans-Medium.ttf,OMI.fonts.IBMPlexSans-Medium.ttf /resource:fonts\IBMPlexSans-SemiBold.ttf,Ohman.fonts.IBMPlexSans-SemiBold.ttf /resource:fonts\IBMPlexSans-SemiBold.ttf,OMI.fonts.IBMPlexSans-SemiBold.ttf /resource:fonts\IBMPlexMono-Regular.ttf,Ohman.fonts.IBMPlexMono-Regular.ttf /resource:fonts\IBMPlexMono-Regular.ttf,OMI.fonts.IBMPlexMono-Regular.ttf /resource:fonts\IBMPlexMono-Medium.ttf,Ohman.fonts.IBMPlexMono-Medium.ttf /resource:fonts\IBMPlexMono-Medium.ttf,OMI.fonts.IBMPlexMono-Medium.ttf /resource:fonts\OFL.txt,Ohman.fonts.OFL.txt /resource:fonts\OFL.txt,OMI.fonts.OFL.txt
if not exist preview mkdir preview

:: 1. preview build without an icon, used to generate app.ico
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warn:1 /out:preview\OMI.exe /win32manifest:app.demo.manifest %RES% %REFS% %SRC%
if errorlevel 1 exit /b 1
preview\OMI.exe --make-ico app.ico
if exist app.ico (set ICO=/win32icon:app.ico) else (set ICO=)

:: 2. preview build with the icon
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warn:1 /out:preview\OMI.exe /win32manifest:app.demo.manifest %ICO% %RES% %REFS% %SRC%
if errorlevel 1 exit /b 1
if /i "%~1"=="preview" (echo Built preview\OMI.exe & exit /b 0)

:: 3. the real thing + the probe tool
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warn:1 /out:OMI.exe /win32manifest:app.manifest %ICO% %RES% %REFS% %SRC%
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /warn:1 /out:tools\omenprobe.exe /r:System.Management.dll tools\omenprobe.cs src\Meta.cs
if errorlevel 1 exit /b 1

:: 4. Sign OMI.exe before embedding into setup
if exist tools\sign.ps1 (
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign.ps1 -Files OMI.exe,preview\OMI.exe
)

:: 5. Build standalone installer OMI-Setup.exe
set SETUP_RES=/resource:OMI.exe,OMI.exe /resource:uninstall.cmd,uninstall.cmd
if exist JITHINDSK1.cer set SETUP_RES=%SETUP_RES% /resource:JITHINDSK1.cer,JITHINDSK1.cer
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warn:1 /out:OMI-Setup.exe /win32manifest:setup.manifest %ICO% /r:System.Windows.Forms.dll /r:System.Drawing.dll %SETUP_RES% src\Setup.cs
if errorlevel 1 exit /b 1

:: 6. Sign OMI-Setup.exe
if exist tools\sign.ps1 (
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign.ps1 -Files OMI-Setup.exe
)

echo Built OMI.exe, preview\OMI.exe, tools\omenprobe.exe, and OMI-Setup.exe
