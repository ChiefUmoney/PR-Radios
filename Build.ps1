$ErrorActionPreference = 'Stop'
$radioRoot = $PSScriptRoot
New-Item -ItemType Directory -Force (Join-Path $radioRoot 'plugin') | Out-Null
$radioFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$radioRefs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $radioFramework $_) }
$radioRefs += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path "$radioFramework\WPF" $_) }
& "$radioFramework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$radioRoot\assets\PR-Radios.ico" "/resource:$radioRoot\assets\logo.png,PRRadios.Logo" "/resource:$radioRoot\assets\mobile-realistic.png,PRRadios.Mobile" "/resource:$radioRoot\assets\handheld-realistic.png,PRRadios.Handheld" "/win32manifest:$radioRoot\source\overlay.manifest" "/out:$radioRoot\PR-Radios.exe" @radioRefs "$radioRoot\source\Radio.cs"
if($LASTEXITCODE -ne 0) { throw 'Overlay compilation failed' }
$radioVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$radioVs = & $radioVswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$radioVs) { throw 'Install Visual Studio C++ Build Tools to build the bridge.' }
$radioCompile = '@echo off' + "`r`n" + 'call "' + $radioVs + '\VC\Auxiliary\Build\vcvars64.bat" >nul' + "`r`n" + 'cl /nologo /EHsc /std:c++17 /MT /O2 /LD /I "' + $radioRoot + '\source\sdk" "' + $radioRoot + '\source\bridge.cpp" /Fo"' + $radioRoot + '\source\bridge.obj" /link /OUT:"' + $radioRoot + '\plugin\pr_radios_bridge.dll" /IMPLIB:"' + $radioRoot + '\source\bridge.lib" advapi32.lib'
$radioCompilePath = Join-Path $radioRoot 'source\compile.cmd'
[IO.File]::WriteAllText($radioCompilePath,$radioCompile)
& $env:ComSpec /d /c $radioCompilePath
if($LASTEXITCODE -ne 0) { throw 'Bridge compilation failed' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$radioPackage=Join-Path $radioRoot 'PR-Radios-Bridge.ts3_plugin'
if(Test-Path -LiteralPath $radioPackage) { Remove-Item -LiteralPath $radioPackage }
$radioZip=[IO.Compression.ZipFile]::Open($radioPackage,[IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($radioZip,"$radioRoot\source\package.ini",'package.ini') | Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($radioZip,"$radioRoot\plugin\pr_radios_bridge.dll",'plugins/pr_radios_bridge.dll') | Out-Null
} finally { $radioZip.Dispose() }
Write-Output 'Built PR-Radios.exe and PR-Radios-Bridge.ts3_plugin.'
