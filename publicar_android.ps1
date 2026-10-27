param(
    [string]$KeystorePassword = "GatoteStore2026!"
)

$ErrorActionPreference = "Stop"
$mauiDir = Join-Path $PSScriptRoot "FractalTicTacToe.Maui"
$keystorePath = Join-Path $mauiDir "gatote-release.keystore"

Write-Host "=== EMPAQUETADO ANDROID RELEASE (.AAB + .APK) PARA GOOGLE PLAY ===" -ForegroundColor Cyan

if (-not (Test-Path $keystorePath)) {
    Write-Host "Generando keystore de firma en $keystorePath ..." -ForegroundColor Yellow
    keytool -genkeypair -v `
        -keystore $keystorePath `
        -alias gatote_key `
        -keyalg RSA `
        -keysize 2048 `
        -validity 10000 `
        -storepass $KeystorePassword `
        -keypass $KeystorePassword `
        -dname "CN=Gatote Fractal, OU=Mobile, O=Anahuac, L=Mexico, S=CDMX, C=MX"
}

Write-Host "Compilando paquetes firmados (.aab y .apk)..." -ForegroundColor Green
dotnet publish "$mauiDir\FractalTicTacToe.Maui.csproj" `
    -f net8.0-android `
    -c Release `
    -p:AndroidSigningStorePass=$KeystorePassword `
    -p:AndroidSigningKeyPass=$KeystorePassword

Write-Host "`nPaquetes generados en: $mauiDir\bin\Release\net8.0-android\publish\" -ForegroundColor Cyan
