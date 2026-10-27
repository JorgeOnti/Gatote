# 📦 Guía de Publicación en Tiendas (Google Play Store y Apple App Store)

Esta guía resume los pasos listos en el repositorio para publicar **Gatote Fractal • Muerte Súbita** (`com.anahuac.gatotefractal`) y desplegar su servidor multijugador en la nube.

---

## 1. Arte Oficial Generado en el Repositorio

- **Ícono de la Aplicación (`512×512 PNG`):** [`Assets/gatote_icon.png`](./Assets/gatote_icon.png)
- **Logo / Splash Banner (`960×540 PNG`):** [`Assets/gatote_logo.png`](./Assets/gatote_logo.png)
- **Política de Privacidad (ES/EN):** [`PRIVACY_POLICY.md`](./PRIVACY_POLICY.md) (puedes usar la URL pública de este archivo en GitHub dentro de Google Play Console y App Store Connect).

---

## 2. Publicar en Google Play Store (`.aab` y `.apk` Firmados)

### Opción A: Script Automático en 1 Comando (Local)
Ejecuta en PowerShell desde la raíz del proyecto:
```powershell
.\publicar_android.ps1
```
El script detecta `keytool.exe` automáticamente, usa la llave [`FractalTicTacToe.Maui/gatote-release.keystore`](./FractalTicTacToe.Maui/gatote-release.keystore) y genera ambos paquetes firmados en `FractalTicTacToe.Maui/bin/Release/net8.0-android/publish/`:
- **`com.anahuac.gatotefractal-Signed.aab`** (Android App Bundle listo para subir a **Google Play Console**).
- **`com.anahuac.gatotefractal-Signed.apk`** (APK firmado listo para instalar directamente en dispositivos Android).

### Opción B: Descarga Directa desde GitHub Actions (Sin instalar SDK local)
En cada `git push` a `master`, el workflow [`.github/workflows/android-release.yml`](./.github/workflows/android-release.yml) compila y firma en la nube ambos archivos (`.apk` y `.aab`) y los publica como artefacto descargable **`gatote-fractal-android-release`** en la pestaña **Actions** de tu repositorio de GitHub.

### Opción C: Comandos Manuales
1. **Generar tu llave de firma (`gatote-release.keystore`):**
   ```powershell
   .\scripts\generate-android-keystore.ps1
   ```
2. **Compilar los paquetes `.aab` y `.apk` firmados en modo `Release`:**
   ```powershell
   dotnet publish FractalTicTacToe.Maui/FractalTicTacToe.Maui.csproj `
     -f net8.0-android `
     -c Release `
     -p:AndroidPackageFormats="aab;apk" `
     -p:AndroidKeyStore=true `
     -p:AndroidSigningKeyStore="gatote-release.keystore" `
     -p:AndroidSigningKeyAlias="gatote_key" `
     -p:AndroidSigningKeyPass="GatoteStore2026!" `
     -p:AndroidSigningStorePass="GatoteStore2026!"
   ```

---

## 3. Publicar en Apple App Store (`.ipa` — iOS)

1. En Visual Studio 2022 (vinculado por red a una Mac con Xcode mediante *Pair to Mac*) o directamente en una Mac:
   ```bash
   dotnet publish FractalTicTacToe.Maui/FractalTicTacToe.Maui.csproj \
     -f net8.0-ios \
     -c Release \
     -p:ArchiveOnBuild=true \
     -p:RuntimeIdentifier=ios-arm64
   ```
2. Sube el `.ipa` generado a **App Store Connect** usando la app **Transporter** de Apple.
3. **Cumplimiento de Revisión de Apple (App Store Review Guidelines):**
   - **Guideline 5.1.1(v) (Account Deletion):** Incluido en `🏆 Perfil -> 🗑️ Eliminar Cuenta`.
   - **Guideline 5.1.1 (Privacy Policy):** Incluida tanto dentro de la app (`🔒 Privacidad`) como en [`PRIVACY_POLICY.md`](./PRIVACY_POLICY.md).
   - **Modo sin conexión / Invitado:** Incluido en la pantalla de acceso (`👤 Jugar como Invitado`).

---

## 4. Desplegar el Servidor Multijugador en la Nube (Docker)

Para que todos los jugadores en Android e iOS se conecten desde cualquier red Wi-Fi o 4G/5G:

```bash
# Construir e iniciar con Docker Compose en un VPS (DigitalOcean, AWS, Azure, etc.) o Fly.io / Railway:
docker compose up -d --build
```

El contenedor expone el puerto TCP `7777` y persiste automáticamente las cuentas (`accounts.tsv`) y partidas (`matches.pgn`) en el volumen `./ServerData`.
