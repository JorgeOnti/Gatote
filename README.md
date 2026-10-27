# ⚡ Gatote Fractal • Muerte Súbita

[![CI de .NET con Pruebas Unitarias](https://github.com/JorgeOnti/Gatote/actions/workflows/dotnet.yml/badge.svg)](https://github.com/JorgeOnti/Gatote/actions/workflows/dotnet.yml)

Juego competitivo 2D por turnos inspirado en el gato fractal (*Tic-Tac-Toe 9x9*) con **regla de Muerte Súbita**, motor de inteligencia artificial **Minimax con Poda Alfa-Beta + Tabla de Transposición Zobrist**, servidor multijugador autoritativo en tiempo real y aplicaciones móviles nativas en **.NET MAUI (Android / iOS / Windows)** y **Godot 4 (.NET / C#)**.

---

## 🎯 1. Reglas del Juego

1. **Tablero Fractal (9×9):**
   - El tablero global está compuesto por una cuadrícula macro de **3×3 sub-tableros** (indexados del `0` al `8`).
   - Cada sub-tablero contiene a su vez **9 casillas individuales** (indexadas del `0` a `8`), sumando **81 casillas** en total.
2. **Dinámica de Redirección Recíproca:**
   - El **Jugador 1 (`X`)** inicia la partida con **Tiro Libre** en cualquier casilla de cualquier sub-tablero.
   - En todos los turnos subsecuentes, la casilla relativa (`CellIndex` de `0` a `8`) elegida por el jugador actual **determina obligatoriamente el sub-tablero macro (`BoardIndex` de `0` a `8`)** donde deberá jugar su rival en el siguiente turno.
   - **Excepción de Tiro Libre por Saturación:** Si un jugador es enviado a un sub-tablero que ya tiene sus 9 casillas ocupadas, obtiene **Tiro Libre** en cualquier casilla vacía disponible del tablero global.
3. **Condición de Victoria por Muerte Súbita:**
   - A diferencia del *Ultimate Tic-Tac-Toe* tradicional, la partida **termina de forma instantánea** en cuanto cualquier jugador completa **una sola línea de 3 en raya** (horizontal, vertical o diagonal) en **cualquier sub-tablero pequeño**.
   - Si se llenan las 81 casillas sin que nadie logre un 3 en raya, la partida concluye en **Empate Global**.

---

## 🏗️ 2. Arquitectura del Proyecto

La solución está diseñada bajo **Arquitectura Limpia (*Clean Architecture*)** con separación estricta entre la capa de dominio pura, el servidor de red y las capas de presentación móvil (**.NET MAUI** y **Godot 4**):

```text
Gatote/
├── FractalTicTacToe.Core/         # Capa de Dominio Pura (C# .NET 8 sin dependencias externas)
│   ├── GameState.cs               # Tablero plano de 81 celdas, Undo/Redo O(1) y Hash Zobrist incremental
│   ├── MinimaxBot.cs              # Motor IA: Negamax + Alfa-Beta + Iterative Deepening + Heurística de Campos Minados
│   ├── TranspositionTable.cs      # Tabla de Transposición Zobrist de 2^18 entradas sin GC allocations
│   ├── MatchClock.cs              # Reloj Fischer dual + Temporizador de 30s para el primer movimiento
│   ├── EloRatingCalculator.cs     # Sistema de puntuación competitiva (pts) y Ligas Fractales
│   ├── MatchmakingService.cs      # Registro/Login/DeleteAccount con SHA-256, cola filtrada por modo de tiempo e historial
│   ├── OnlineMatchRoom.cs         # Sala autoritativa Anti-Cheat, Takeback mutuo, Timeout, Abort y Revancha
│   ├── OnlineMultiplayerClient.cs # Cliente TCP asíncrono compartido entre MAUI, Godot 4 y Consola
│   ├── MatchReplayNavigator.cs    # Motor de sala de análisis y clasificación humana de jugadas
│   ├── InteractiveTutorialController.cs # Controlador aislado para el tutorial interactivo de 3 pasos
│   ├── GameLogger.cs              # Sistema centralizado y thread-safe de bitácora (Logs en memoria y disco)
│   └── SoundEffectsSynthesizer.cs # Sintetizador procedural PCM/WAV de 16 bits en memoria + Política de Privacidad
├── Assets/                        # Arte comercial oficial del juego
│   ├── gatote_icon.png            # Icono oficial de la app móvil (512×512)
│   └── gatote_logo.png            # Logo oficial del juego para la pantalla Splash (960×540)
├── FractalTicTacToe.Maui/         # App Móvil Nativa .NET MAUI (Android e iOS) lista para Visual Studio 2022
│   └── MainPage.cs                # Interfaz multi-pantalla con Splash + Barra de Progreso, Haptics y Auto-Login
├── FractalTicTacToe.Godot/        # Aplicación Móvil (Portrait 9:16) en Godot 4 (.NET / C#)
│   └── Scripts/MobileGameScreen.cs# Interfaz multi-pantalla con Splash + Barra de Progreso y AudioStreamWav
├── FractalTicTacToe.Server/       # Servidor TCP dedicado autoritativo (Puerto 7777 / Variable PORT en nube)
├── FractalTicTacToe.ConsoleApp/   # Cliente interactivo en consola con telemetría en vivo
├── CoreTests/                     # Suite automatizada de 26 pruebas unitarias y de integración
├── Dockerfile & docker-compose.yml# Empaquetado en contenedor Docker para despliegue en la nube (Render/Railway/Fly.io)
├── PRIVACY_POLICY.md              # Política de Privacidad oficial para Google Play Store y Apple App Store
├── STORE_PUBLISHING.md            # Guía paso a paso de firma y publicación (.aab y .ipa)
├── FractalTicTacToe.sln           # Solución Core + Servidor + Tests (integrada con GitHub Actions CI)
└── FractalTicTacToe.Maui.sln      # Solución Visual Studio 2022 con .NET MAUI para Deploy directo a Android / iOS
```

---

## 📱 3. Experiencia de Usuario Móvil (.NET MAUI y Godot 4)

- **⚡ Pantalla Splash de Carga Inicial con Logo y Barra de Progreso:** Al abrir la aplicación, se muestra el **logo oficial de Gatote Fractal** acompañado de una **barra de progreso animada (`0% -> 100%`)** que inicializa el motor táctico, el sintetizador de audio y restaura la sesión guardada sin pantallas negras.
- **🔐 Inicio de Sesión Persistente (Auto-Login) y Modo Invitado:** La pantalla de **Iniciar Sesión / Registrarse** solo aparece para usuarios nuevos o tras cerrar sesión explícitamente. Incluye además el botón **`👤 Jugar como Invitado`** para partidas locales sin registro.
- **🔊 Efectos de Sonido Sintetizados y Vibración Háptica:** Audio procedural PCM de 16 bits generado en memoria para movimientos, alerta de reloj bajo (`< 10s`), victoria por Muerte Súbita y derrota, con botón de silencio **`🔊 / 🔇`** en el encabezado superior.
- **🔒 Cumplimiento con Google Play Store y Apple App Store:** Incluye visor integrado de **`🔒 Política de Privacidad`** y botón **`🗑️ Eliminar Cuenta Permanentemente`** en la pestaña **`🏆 Perfil`** (cumpliendo la directriz *Apple App Store Guideline 5.1.1(v)* y *Google Play Data Safety*).
- **🏠 Menú Principal de 3 Pestañas (`Inicio`, `Análisis`, `Perfil`):**
  - **Contra IA (Sin límite de tiempo):** Selección directa entre 4 niveles de dificultad (`🌱 Fácil`, `⚔ Medio`, `🔥 Difícil`, `👑 Maestro`) con opción de **💡 Pista IA** exclusiva para este modo.
  - **En Línea (Competitivo con Puntos):** Emparejamiento estricto por control de tiempo (`⚡ Bullet 1+1`, `🔥 Blitz 3+0`, `⏱ Blitz 3+2`, `🏛 Rápida 5+0`) con pantalla de espera en vivo, **30 segundos para realizar el primer movimiento** antes de arrancar el reloj principal, solicitud de **Deshacer Jugada (*Takeback*)** y **Revancha** con notificación de aceptar/rechazar para el rival.
  - **2 Jugadores Local:** Modo *Pass & Play* en el mismo teléfono.
- **🎬 Sala de Análisis:** Carga por defecto la **última partida jugada**, permitiendo avanzar o retroceder jugada por jugada (`⏮`, `◀`, `▶`, `⏭`) con barra de evaluación heurística en vivo.
- **🏆 Escalafón de Ligas Competitivas (Exclusivo Online):**
  - Las partidas contra la IA o de 2 jugadores en el mismo teléfono son amistosas y no alteran el puntaje.
  - En partidas en línea se suman o restan puntos (`pts`) escalando desde **Bronce** hasta **Gran Maestro Fractal (`2100+ pts`)**.

---

## 🚀 4. Cómo Compilar y Desplegar

### 1. Desplegar en tu Celular Android Físico desde Visual Studio 2022 (.NET MAUI)
1. Abre **`FractalTicTacToe.Maui.sln`** en **Visual Studio 2022** (con la carga de trabajo *.NET Multi-platform App UI development* instalada).
2. Conecta tu teléfono Android por USB (o depuración inalámbrica Wi-Fi) y selecciónalo como destino de ejecución en la barra superior de Visual Studio.
3. Presiona **`F5`** para compilar e instalar la app en tu Android.
4. **Para probar partidas En Línea desde tu Android físico contra tu PC:**
   - Inicia el servidor en tu PC (`dotnet run --project FractalTicTacToe.Server/FractalTicTacToe.Server.csproj`).
   - En un Android físico, la app preselecciona automáticamente la IP LAN Wi-Fi de tu PC (`192.168.1.84:7777`), o puedes tocar el botón **`📡 192.168.x.x`** en la esquina superior derecha para elegir o escribir la IP de tu PC en un toque.

### 2. Ejecutar las Pruebas Unitarias y de Integración
```bash
dotnet run --project CoreTests/CoreTests.csproj --configuration Release
```

### 3. Abrir en Godot 4 (.NET)
1. Abre **Godot 4 (.NET)**.
2. Haz clic en **Importar** y selecciona `FractalTicTacToe.Godot/project.godot`.
3. Presiona **`F5`**.
