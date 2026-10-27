# Script para generar el Keystore de firma de Google Play Store (gatote-release.keystore)
param(
    [string]$KeystorePath = "FractalTicTacToe.Maui\gatote-release.keystore",
    [string]$Alias = "gatote_key",
    [string]$Password = "GatoteStore2026!",
    [int]$ValidityDays = 10000
)

$cmd = Get-Command keytool -ErrorAction SilentlyContinue
$keytoolExe = if ($cmd) { $cmd.Source } else {
    Get-ChildItem -Path "C:\Program Files\Microsoft", "C:\Program Files\Android", "C:\Program Files\Java" -Recurse -Filter "keytool.exe" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
}

if (-not $keytoolExe) {
    throw "No se encontró keytool.exe. Instala el JDK de Java o Android Studio."
}

Write-Host "Generando Android Release Keystore para Gatote Fractal usando $keytoolExe..." -ForegroundColor Cyan
& $keytoolExe -genkeypair -v `
    -keystore $KeystorePath `
    -alias $Alias `
    -keyalg RSA `
    -keysize 2048 `
    -validity $ValidityDays `
    -storepass $Password `
    -keypass $Password `
    -dname "CN=Gatote Fractal, OU=Mobile, O=Anahuac, L=Mexico, S=CDMX, C=MX"

Write-Host "Keystore generado en: $KeystorePath" -ForegroundColor Green
