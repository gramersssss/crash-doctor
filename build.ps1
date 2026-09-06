# Builds the distributable: a single self-contained CrashDoctor.exe (no .NET install needed on the user's PC)
# plus the ui\ and fixes\ folders, zipped for Nexus (manual install) and as a Vortex-installable layout.
param([string]$Version = "0.1.0")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:PATH = "C:\Program Files\dotnet;" + $env:PATH
$dist = Join-Path $root 'dist'; if (Test-Path $dist) { Get-ChildItem $dist -Recurse -Force | Remove-Item -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
$pub = Join-Path $dist 'publish'
& dotnet publish (Join-Path $root 'src\CrashDoctor.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -o $pub -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
# manual-install layout
$manual = Join-Path $dist "CrashDoctor-$Version"; New-Item -ItemType Directory -Force $manual | Out-Null
Copy-Item (Join-Path $pub 'CrashDoctor.exe') $manual
Copy-Item (Join-Path $pub 'ui') (Join-Path $manual 'ui') -Recurse
Copy-Item (Join-Path $pub 'fixes') (Join-Path $manual 'fixes') -Recurse
Get-ChildItem $pub -Filter '*.dll' | Copy-Item -Destination $manual   # WebView2Loader.dll and friends
Copy-Item (Join-Path $root 'README.md') (Join-Path $manual 'README.md')
Compress-Archive -Path "$manual\*" -DestinationPath (Join-Path $dist "CrashDoctor-$Version-manual.zip") -Force
# Vortex layout: files under bin\x64\tools\CrashDoctor\ so Vortex deploys them into the game folder
$vortex = Join-Path $dist "vortex\bin\x64\tools\CrashDoctor"; New-Item -ItemType Directory -Force $vortex | Out-Null
Copy-Item "$manual\*" $vortex -Recurse
Compress-Archive -Path (Join-Path $dist 'vortex\bin') -DestinationPath (Join-Path $dist "CrashDoctor-$Version-vortex.zip") -Force
Get-ChildItem $dist -File | ForEach-Object { "{0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
"exe: {0:N1} MB" -f ((Get-Item (Join-Path $manual 'CrashDoctor.exe')).Length / 1MB)
