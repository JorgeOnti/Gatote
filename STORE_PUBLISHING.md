# 📦 Guía de Publicación en Tiendas (Google Play Store y Apple App Store)

Esta guía resume los pasos listos en el repositorio para publicar **Gatote Fractal • Muerte Súbita** (`com.anahuac.gatotefractal`) y desplegar su servidor multijugador en la nube.

---

## 1. Arte Oficial Generado en el Repositorio

- **Ícono de la Aplicación (`512×512 PNG`):** [`Assets/gatote_icon.png`](./Assets/gatote_icon.png)
- **Logo / Splash Banner (`960×540 PNG`):** [`Assets/gatote_logo.png`](./Assets/gatote_logo.png)
- **Política de Privacidad (ES/EN):** [`PRIVACY_POLICY.md`](./PRIVACY_POLICY.md) (puedes usar la URL pública de este archivo en GitHub dentro de Google Play Console y App Store Connect).

---

## 2. Publicar en Google Play Store (`.aab` — Android App Bundle)

1. **Generar tu llave de firma (`gatote-release.keystore`):**
   ```powershell
   .\scripts\generate-android-keystore.ps1
   ```
2. **Compilar el paquete `.aab` firmado en modo `Release`:**
   ```powershell
   dotnet publish FractalTicTacToe.Maui/FractalTicTacToe.Maui.csproj `
     -f net8.0-android `
     -c Release `
     -p:AndroidKeyStore=true `
     -p:AndroidSigningKeyStore="..\gatote-release.keystore" `
     -p:AndroidSigningKeyAlias="gatote_key" `
     -p:AndroidSigningKeyPass="<TU_PASSWORD>" `
     -p:AndroidSigningStorePass="<TU_PASSWORD>"
   ```
   *(También puedes hacerlo desde Visual Studio 2022 haciendo clic derecho sobre `FractalTicTacToe.Maui` -> **Publish...** -> **Google Play**).*
3. El archivo firmado `com.anahuac.gatotefractal-Signed.aab` se generará en `FractalTicTacToe.Maui/bin/Release/net8.0-android/publish/`.

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
