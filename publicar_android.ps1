param(
    [string]$KeystorePassword = "GatoteStore2026!"
)

$ErrorActionPreference = "Stop"
$mauiDir = Join-Path $PSScriptRoot "FractalTicTacToe.Maui"
$keystorePath = Join-Path $mauiDir "gatote-release.keystore"

Write-Host "=== EMPAQUETADO ANDROID RELEASE (.AAB + .APK) PARA GOOGLE PLAY ===" -ForegroundColor Cyan

function Find-Keytool {
    $cmd = Get-Command keytool -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $searchRoots = @(
        "$env:JAVA_HOME\bin",
        "C:\Program Files\Microsoft",
        "C:\Program Files\Android",
        "C:\Program Files\Java"
    )
    foreach ($root in $searchRoots) {
        if ($root -and (Test-Path $root)) {
            $found = Get-ChildItem -Path $root -Recurse -Filter "keytool.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($found) { return $found.FullName }
        }
    }
    throw "No se encontró keytool.exe. Instala el JDK de Java / Android Studio o agrégalo al PATH."
}

if (-not (Test-Path $keystorePath)) {
    $keytoolExe = Find-Keytool
    Write-Host "Generando keystore de firma en $keystorePath usando $keytoolExe ..." -ForegroundColor Yellow
    & $keytoolExe -genkeypair -v `
        -keystore $keystorePath `
        -alias gatote_key `
        -keyalg RSA `
        -keysize 2048 `
        -validity 10000 `
        -storepass $KeystorePassword `
        -keypass $KeystorePassword `
        -dname "CN=Gatote Fractal, OU=Mobile, O=Anahuac, L=Mexico, S=CDMX, C=MX"
} else {
    Write-Host "Keystore existente detectado en: $keystorePath" -ForegroundColor Green
}

Write-Host "`nCompilando paquetes firmados (.aab para Google Play y .apk para instalación directa)..." -ForegroundColor Green
dotnet publish "$mauiDir\FractalTicTacToe.Maui.csproj" `
    -f net8.0-android `
    -c Release `
    -p:AndroidPackageFormats="aab;apk" `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore="$keystorePath" `
    -p:AndroidSigningKeyAlias="gatote_key" `
    -p:AndroidSigningStorePass=$KeystorePassword `
    -p:AndroidSigningKeyPass=$KeystorePassword

$publishDir = Join-Path $mauiDir "bin\Release\net8.0-android\publish"
Write-Host "`n=== PAQUETES FIRMADOS LISTOS ===" -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Get-ChildItem -Path $publishDir -Include "*-Signed.apk", "*-Signed.aab" -Recurse | ForEach-Object {
        $sizeMb = [Math]::Round($_.Length / 1MB, 2)
        Write-Host "  -> $($_.Name) ($sizeMb MB)" -ForegroundColor Green
        Write-Host "     Ruta: $($_.FullName)" -ForegroundColor Gray
    }
} else {
    Write-Host "Ruta de salida: $publishDir" -ForegroundColor Cyan
}
