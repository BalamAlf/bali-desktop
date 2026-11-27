param([switch]$Test, [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'No se encontró el compilador de .NET Framework de 64 bits.' }
$dist = $OutputDirectory
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exe = Join-Path $dist 'BaliDesktop.exe'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xml.dll','System.IO.Compression.dll','System.Net.Http.dll','System.Security.dll','System.Web.Extensions.dll','System.Xaml.dll','Microsoft.VisualBasic.dll','Microsoft.CSharp.dll',
    'WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\System.Speech.dll',
    'WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll','System.Runtime.WindowsRuntime.dll')
$refs = $references | ForEach-Object { '/reference:'+(Join-Path $framework $_) }
$metadata=Join-Path $env:WINDIR 'System32\WinMetadata'
$refs += @('Foundation','Media','Globalization','Graphics','Storage') | ForEach-Object { '/reference:'+(Join-Path $metadata ('Windows.'+$_+'.winmd')) }
$refs += '/reference:'+(Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll')
$source = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /warn:4 /optimize+ /target:winexe /platform:x64 "/out:$exe" "/win32manifest:$PSScriptRoot\Bali.manifest" @refs @source
if ($LASTEXITCODE -ne 0) { throw "La compilación falló ($LASTEXITCODE)." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$vendor=Join-Path $PSScriptRoot 'vendor'
$packages=@('pdfpig.0.1.12','microsoft.bcl.hashcode.6.0.0','system.memory.4.6.0','system.buffers.4.6.0','system.numerics.vectors.4.6.0','system.runtime.compilerservices.unsafe.6.1.0')
$licenses=Join-Path $dist 'licenses'
New-Item -Force -ItemType Directory $licenses | Out-Null
foreach($package in $packages) {
    $resolved=Join-Path $vendor ($package+'.nupkg')
    if(!(Test-Path -LiteralPath $resolved)) { throw "Falta el paquete local $package." }
    $zip=[IO.Compression.ZipFile]::OpenRead($resolved)
    try {
        $frameworks=@('net48','net472','net471','net47','net462','net461','net46','net45','netstandard2.0')
        $selected=@()
        foreach($target in $frameworks) {
            $selected=@($zip.Entries | Where-Object FullName -Match "^lib/$target/[^/]+\.dll$")
            if($selected.Count){break}
        }
        if(!$selected.Count){throw "No hay biblioteca compatible en $package."}
        foreach($entry in $selected) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $dist $entry.Name),$true) }
        foreach($entry in $zip.Entries | Where-Object { $_.FullName -match '^(LICENSE|THIRD-PARTY-NOTICES|ThirdPartyNotices)(\.txt|\.md)?$' }) {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $licenses ($package+'-'+$entry.Name)),$true)
        }
    } finally { $zip.Dispose() }
}
Copy-Item -LiteralPath (Join-Path $vendor 'PdfPig-LICENSE.txt'),(Join-Path $vendor 'PdfPig-NOTICES.txt') -Destination $licenses -Force
$speech = Join-Path $vendor 'speech'
if ((Test-Path -LiteralPath (Join-Path $speech 'bin\whisper-cli.exe')) -and (Test-Path -LiteralPath (Join-Path $speech 'ggml-tiny-q5_1.bin'))) {
    $localStt = Join-Path $dist 'stt'
    New-Item -Force -ItemType Directory $localStt | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $speech 'bin') -File | Copy-Item -Destination $localStt -Force
    Copy-Item -LiteralPath (Join-Path $speech 'ggml-tiny-q5_1.bin'),(Join-Path $speech 'LICENSE-whisper.txt') -Destination $localStt -Force
    $baseModel=Join-Path $speech 'ggml-base-q5_1.bin'
    if(Test-Path -LiteralPath $baseModel){Copy-Item -LiteralPath $baseModel -Destination $localStt -Force}
}
if ($Test) {
    $results = Join-Path $PSScriptRoot 'evidence'
    New-Item -ItemType Directory -Force -Path $results | Out-Null
    $p = Start-Process -FilePath $exe -ArgumentList "--test `"$results`"" -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $results 'unit.txt')
    if ($p.ExitCode -ne 0) { throw "Las pruebas fallaron ($($p.ExitCode))." }
}
