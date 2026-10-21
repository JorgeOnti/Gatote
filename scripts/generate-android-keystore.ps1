# Script para generar el Keystore de firma de Google Play Store (gatote-release.keystore)
param(
    [string]$KeystorePath = "gatote-release.keystore",
    [string]$Alias = "gatote_key",
    [int]$ValidityDays = 10000
)

Write-Host "Generando Android Release Keystore para Gatote Fractal..." -ForegroundColor Cyan
keytool -genkeypair -v `
    -keystore $KeystorePath `
    -alias $Alias `
    -keyalg RSA `
    -keysize 2048 `
    -validity $ValidityDays

Write-Host "Keystore generado en: $KeystorePath" -ForegroundColor Green
