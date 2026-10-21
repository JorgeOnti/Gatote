# ⚡ Gatote Fractal • Muerte Súbita

[![CI de .NET con Pruebas Unitarias](https://github.com/JorgeOnti/Gatote/actions/workflows/dotnet.yml/badge.svg)](https://github.com/JorgeOnti/Gatote/actions/workflows/dotnet.yml)

Juego competitivo 2D por turnos inspirado en el gato fractal (*Tic-Tac-Toe 9x9*) con **regla de Muerte Súbita**, motor de inteligencia artificial **Minimax con Poda Alfa-Beta + Tabla de Transposición Zobrist**, servidor multijugador autoritativo en tiempo real y aplicación móvil en **Godot 4 (.NET / C#)**.

---

## 🎯 1. Reglas del Juego

1. **Tablero Fractal (9×9):**
   - El tablero global está compuesto por una cuadrícula macro de **3×3 sub-tableros** (indexados del `0` al `8`).
   - Cada sub-tablero contiene a su vez **9 casillas individuales** (indexadas del `0` al `8`), sumando **81 casillas** en total.
2. **Dinámica de Redirección Recíproca:**
   - El **Jugador 1 (`X`)** inicia la partida con **Tiro Libre** en cualquier casilla de cualquier sub-tablero.
   - En todos los turnos subsecuentes, la casilla relativa (`CellIndex` de `0` a `8`) elegida por el jugador actual **determina obligatoriamente el sub-tablero macro (`BoardIndex` de `0` a `8`)** donde deberá jugar su rival en el siguiente turno.
   - **Excepción de Tiro Libre por Saturación:** Si un jugador es enviado a un sub-tablero que ya tiene sus 9 casillas ocupadas, obtiene **Tiro Libre** en cualquier casilla vacía disponible del tablero global.
3. **Condición de Victoria por Muerte Súbita:**
   - A diferencia del *Ultimate Tic-Tac-Toe* tradicional, la partida **termina de forma instantánea** en cuanto cualquier jugador completa **una sola línea de 3 en raya** (horizontal, vertical o diagonal) en **cualquier sub-tablero pequeño**.
   - Si se llenan las 81 casillas sin que nadie logre un 3 en raya, la partida concluye en **Empate Global**.

---

## 🏗️ 2. Arquitectura del Proyecto

La solución está diseñada bajo **Arquitectura Limpia (*Clean Architecture*)** con separación estricta entre la capa de dominio pura, el servidor de red y la capa de presentación móvil:

```text
Gatote/
├── FractalTicTacToe.Core/         # Capa de Dominio Pura (C# sin dependencias externas)
│   ├── GameState.cs               # Tablero plano de 81 celdas, Undo/Redo O(1) y Hash Zobrist incremental
│   ├── MinimaxBot.cs              # Motor IA: Negamax + Alfa-Beta + Iterative Deepening + Heurística de Campos Minados
│   ├── TranspositionTable.cs      # Tabla de Transposición Zobrist de 2^18 entradas sin GC allocations
│   ├── MatchClock.cs              # Reloj Fischer dual + Temporizador de 30s para el primer movimiento
│   ├── EloRatingCalculator.cs     # Sistema de puntuación competitiva (pts) y Ligas Fractales
│   ├── MatchmakingService.cs      # Registro/Login con SHA-256, cola filtrada por modo de tiempo e historial
│   ├── OnlineMatchRoom.cs         # Sala autoritativa Anti-Cheat, Takeback mutuo, Timeout, Abort y Revancha
│   ├── OnlineMultiplayerClient.cs # Cliente TCP asíncrono para Godot 4 y consola
│   └── MatchReplayNavigator.cs    # Motor de sala de análisis y repetición jugada por jugada
├── FractalTicTacToe.Godot/        # Aplicación Móvil (Portrait 9:16) en Godot 4 (.NET / C#)
│   └── Scripts/MobileGameScreen.cs# Interfaz multi-pantalla (Login/Registro, Inicio, Tablero, Análisis y Perfil)
├── FractalTicTacToe.Server/       # Servidor TCP dedicado autoritativo (Puerto 7777)
├── FractalTicTacToe.ConsoleApp/   # Cliente interactivo en consola con telemetría en vivo
└── CoreTests/                     # Suite automatizada de 20 pruebas unitarias y de integración
```

---

## 📱 3. Experiencia de Usuario Móvil (Godot 4)

- **🔐 Autenticación de Cuentas:** Pantalla inicial para **Iniciar Sesión** o **Registrarse** como nuevo usuario con contraseña cifrada (`SHA-256`).
- **🏠 Menú Principal de 3 Pestañas (`Inicio`, `Análisis`, `Perfil`):**
  - **Contra IA (Sin límite de tiempo):** Selección directa entre 4 niveles de dificultad (`🌱 Fácil`, `⚔ Medio`, `🔥 Difícil`, `👑 Maestro`) con opción de **💡 Pista IA** exclusiva para este modo.
  - **En Línea (Competitivo con Puntos):** Emparejamiento estricto por control de tiempo (`⚡ Bullet 1+1`, `🔥 Blitz 3+0`, `⏱ Blitz 3+2`, `🏛 Rápida 5+0`) con pantalla de espera en vivo, **30 segundos para realizar el primer movimiento** (estilo Lichess) antes de arrancar el reloj principal, solicitud de **Deshacer Jugada (*Takeback*)** con consentimiento del rival y **Revancha**.
  - **2 Jugadores Local:** Modo *Pass & Play* en el mismo teléfono.
- **🎬 Sala de Análisis:** Carga por defecto la **última partida jugada**, permitiendo avanzar o retroceder jugada por jugada (`⏮`, `◀`, `▶`, `⏭`) con barra de evaluación heurística en vivo.
- **🏆 Escalafón de Ligas Competitivas (Exclusivo Online):**
  - Las partidas contra la IA o de 2 jugadores en el mismo teléfono son amistosas y no alteran el puntaje.
  - En partidas en línea se suman o restan puntos (`pts`) escalando desde **Bronce** hasta **Gran Maestro Fractal (`2100+ pts`)**.

---

## 🚀 4. Cómo Compilar y Ejecutar

### Requisitos Previos
- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**
- **[Godot Engine 4.x (.NET Edition)](https://godotengine.org/)** (para abrir y exportar la app móvil)

### 1. Ejecutar las Pruebas Unitarias y de Integración
```bash
dotnet run --project CoreTests/CoreTests.csproj --configuration Release
```

### 2. Iniciar el Servidor Multijugador Online (Puerto `7777`)
```bash
dotnet run --project FractalTicTacToe.Server/FractalTicTacToe.Server.csproj
```

### 3. Abrir la Aplicación Móvil en Godot 4 (.NET)
1. Abre **Godot 4 (.NET)**.
2. Haz clic en **Importar** y selecciona `FractalTicTacToe.Godot/project.godot`.
3. Presiona **`F5`** (puedes lanzar 2 instancias desde *Debug -> Customize Run Instances* para probar el modo multijugador en línea en tiempo real).
