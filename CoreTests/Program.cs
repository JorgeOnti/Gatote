using System;
using System.IO;
using FractalTicTacToe.Core;

namespace CoreTests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║      SUITE DE PRUEBAS UNITARIAS Y DE INTEGRACIÓN • FRACTAL CORE     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");

            RunTest("01. Estado Inicial y Hash Zobrist Base", TestInitialState);
            RunTest("02. Redirección Recíproca y Validación de Legalidad", TestReciprocalRedirectionAndValidation);
            RunTest("03. Fronteras, Índices Fuera de Rango y Entradas Malformadas", TestBoundaryAndMalformedInputs);
            RunTest("04. Los 8 Patrones de Victoria (Filas, Columnas, Diagonales)", TestAllEightWinPatternsAcrossBoards);
            RunTest("05. Condición de Muerte Súbita Inmediata", TestSuddenDeathVictory);
            RunTest("06. Excepción de Tiro Libre por Tablero Saturado", TestFreeMoveOnSaturatedBoard);
            RunTest("07. Empate Global de 81 Casillas sin 3 en Raya", TestFull81CellGlobalDraw);
            RunTest("08. Consistencia Incremental de Hash Zobrist y Transposiciones", TestZobristIncrementalConsistencyAndTransposition);
            RunTest("09. Historial Undo/Redo en O(1) e Invalidación de Rama Futura", TestUndoAndRedoMoveConsistency);
            RunTest("10. Serialización e Importación FEN y Transcripción PGN", TestFenAndMoveHistorySerialization);
            RunTest("11. Sistema de Puntuación (Piso, Asimetría, Factor K y Empates)", TestRatingSystemEdgeCasesAndMatchRecord);
            RunTest("12. Minimax: Remate Inmediato de Muerte Súbita", TestMinimaxImmediateSuddenDeathWin);
            RunTest("13. Minimax: Evasión de Tableros Minados (Landmines)", TestMinimaxAvoidsSendingOpponentToLandmineBoard);
            RunTest("14. Tabla de Transposición Zobrist: Hits y Podas Directas", TestTranspositionTableHitAndSpeedup);
            RunTest("15. Iterative Deepening por Límite de Tiempo y Dificultades", TestIterativeDeepeningTimeLimitAndDifficulties);
            RunTest("16. Controlador Asíncrono de Sesión Móvil (GameSessionController)", TestGameSessionControllerAsyncFlow);
            RunTest("17. Matchmaking por Ventana de Puntos, Persistencia y Takeback", TestOnlineMatchmakingAndMutualConsentTakeback);
            RunTest("18. Máquina de Estados del Protocolo Cliente-Servidor Online", TestOnlineMultiplayerClientProtocolStateMachine);
            RunTest("19. Reloj de Partida con Incremento Fischer y Derrota por Tiempo", TestMatchClockFischerIncrementAndWinningLine);
            RunTest("20. Navegador de Repeticiones y Sala de Análisis (MatchReplayNavigator)", TestMatchReplayNavigatorStepByStep);
            RunTest("21. Cumplimiento de Tiendas (DELETE_ACCOUNT, Privacidad y Audio PCM)", TestStoreComplianceAccountDeletionAndSoundSynthesizer);
            RunTest("22. Minimax: Redirección Multi-Tablero, Auto-Redirección y Trampas", TestMinimaxMultiBoardRedirectionLookaheadAndTraps);
            RunTest("23. Clasificación Humana de Jugadas en Sala de Análisis", TestHumanReadableMoveQualityAndAnalysisLabels);
            RunTest("24. Caída de Servidor sin Congelamiento y Salas Privadas por Código", TestServerCrashGracefulCancelAndPrivateRoomCode);

            Console.WriteLine("\n✔ ¡Las 24 suites de pruebas unitarias y de integración pasaron exitosamente (100% PASS)!");
            return 0;
        }

        private static void RunTest(string testName, Action testAction)
        {
            Console.Write($"  [TEST] {testName,-64} ... ");
            testAction();
            Console.WriteLine("OK");
        }

        private static void TestInitialState()
        {
            var state = new GameState();
            Assert(state.CurrentPlayer == Player.X, "El jugador inicial debe ser X.");
            Assert(state.ActiveBoardIndex == -1, "El tablero inicial debe ser tiro libre (-1).");
            Assert(!state.IsGameOver, "El juego no debe iniciar terminado.");
            Assert(state.Winner == Player.None, "No debe haber ganador inicial.");
            Assert(state.GetLegalMoves().Count == 81, "Deben existir 81 movimientos legales al inicio.");
            Assert(state.ZobristKey == ZobristHasher.ComputeFullHash(state), "El ZobristKey inicial debe coincidir con el cálculo completo.");
        }

        private static void TestReciprocalRedirectionAndValidation()
        {
            var state = new GameState();

            Assert(state.MakeMove(new Move(4, 2)), "El primer tiro libre (4, 2) debe ser válido.");
            Assert(state.CurrentPlayer == Player.O, "El turno debe cambiar a O.");
            Assert(state.ActiveBoardIndex == 2, "ActiveBoardIndex debe ser 2.");
            Assert(state.GetLegalMoves().Count == 9, "O debe tener 9 opciones en el Tablero 2.");

            Assert(!state.MakeMove(new Move(0, 0)), "No se debe permitir tirar fuera del tablero activo.");

            Assert(state.MakeMove(new Move(2, 4)), "El tiro en el tablero activo (2, 4) debe ser válido.");
            Assert(state.CurrentPlayer == Player.X, "El turno debe volver a X.");
            Assert(state.ActiveBoardIndex == 4, "ActiveBoardIndex debe ser 4.");
            Assert(state.GetLegalMoves().Count == 8, "El Tablero 4 debe tener 8 celdas disponibles.");

            Assert(!state.MakeMove(new Move(4, 2)), "No se debe permitir tirar en una celda ocupada.");
        }

        private static void TestBoundaryAndMalformedInputs()
        {
            var state = new GameState();

            // Coordenadas fuera de rango (0..8)
            Assert(!state.IsValidMove(new Move(-1, 0)), "Tablero negativo debe ser inválido.");
            Assert(!state.IsValidMove(new Move(0, -1)), "Celda negativa debe ser inválida.");
            Assert(!state.IsValidMove(new Move(9, 0)), "Tablero 9 debe ser inválido.");
            Assert(!state.IsValidMove(new Move(0, 9)), "Celda 9 debe ser inválida.");
            Assert(!state.MakeMove(new Move(99, -5)), "MakeMove fuera de rango debe retornar false sin lanzar excepción.");

            AssertThrows<ArgumentOutOfRangeException>(() => state.GetCell(-1, 0), "GetCell con tablero -1 debe lanzar ArgumentOutOfRangeException.");
            AssertThrows<ArgumentOutOfRangeException>(() => state.GetCell(0, 9), "GetCell con celda 9 debe lanzar ArgumentOutOfRangeException.");

            // Cadenas FEN malformadas
            AssertThrows<ArgumentException>(() => GameSerializer.FromFen(""), "FEN vacío debe fallar.");
            AssertThrows<FormatException>(() => GameSerializer.FromFen("9/9/9 X -1 *"), "FEN con menos de 9 sub-tableros debe fallar.");
            AssertThrows<FormatException>(() => GameSerializer.FromFen("9/9/9/9/9/9/9/9/8 X -1 *"), "FEN con sub-tablero incompleto (8 celdas) debe fallar.");
            AssertThrows<FormatException>(() => GameSerializer.FromFen("9/9/9/9/9/9/9/9/9 Z -1 *"), "FEN con jugador inválido 'Z' debe fallar.");

            // Transcripción con jugada ilegal (viola redirección)
            AssertThrows<InvalidOperationException>(
                () => GameSerializer.ImportMoveHistory("42-00"),
                "ImportMoveHistory debe rechazar secuencias que violen la redirección obligatoria.");
        }

        private static void TestAllEightWinPatternsAcrossBoards()
        {
            int[][] patterns =
            {
                new[] { 0, 1, 2 }, // Fila superior
                new[] { 3, 4, 5 }, // Fila central
                new[] { 6, 7, 8 }, // Fila inferior
                new[] { 0, 3, 6 }, // Columna izquierda
                new[] { 1, 4, 7 }, // Columna central
                new[] { 2, 5, 8 }, // Columna derecha
                new[] { 0, 4, 8 }, // Diagonal principal
                new[] { 2, 4, 6 }  // Diagonal secundaria
            };

            for (int pIdx = 0; pIdx < patterns.Length; pIdx++)
            {
                int targetBoard = pIdx; // Probamos cada patrón en un sub-tablero distinto (0..7)
                int[] line = patterns[pIdx];

                // Construimos una instantánea FEN donde X tiene las dos primeras celdas de la línea en targetBoard
                // y es turno de X con tiro libre para colocar la tercera celda:
                var cells = new Player[GameState.TotalCells];
                cells[(targetBoard * 9) + line[0]] = Player.X;
                cells[(targetBoard * 9) + line[1]] = Player.X;

                var state = new GameState();
                string fenBefore = BuildFenFromCells(cells, Player.X, targetBoard);
                state = GameSerializer.FromFen(fenBefore);

                Assert(state.MakeMove(new Move(targetBoard, line[2])), $"El patrón #{pIdx} debe permitir colocar la 3ra ficha en ({targetBoard}, {line[2]}).");
                Assert(state.IsGameOver && state.Winner == Player.X, $"El patrón #{pIdx} en el tablero {targetBoard} debe declarar victoria por Muerte Súbita.");
                Assert(state.TryGetWinningLine(out int wb, out int wc0, out int wc1, out int wc2), $"TryGetWinningLine debe detectar el patrón #{pIdx}.");
                Assert(wb == targetBoard && wc0 == line[0] && wc1 == line[1] && wc2 == line[2], "Las coordenadas de la línea ganadora deben coincidir.");
            }
        }

        private static void TestSuddenDeathVictory()
        {
            var suddenDeath = new GameState();
            Assert(suddenDeath.MakeMove(new Move(0, 1)), "X juega (0,1) -> O va a 1");
            Assert(suddenDeath.MakeMove(new Move(1, 0)), "O juega (1,0) -> X va a 0");
            Assert(suddenDeath.MakeMove(new Move(0, 2)), "X juega (0,2) -> O va a 2");
            Assert(suddenDeath.MakeMove(new Move(2, 0)), "O juega (2,0) -> X va a 0");
            Assert(suddenDeath.MakeMove(new Move(0, 0)), "X juega (0,0) -> Completa 0-1-2 en Tablero 0");

            Assert(suddenDeath.IsGameOver, "La partida debe terminar inmediatamente por muerte súbita.");
            Assert(suddenDeath.Winner == Player.X, "El ganador debe ser X.");
            Assert(suddenDeath.ActiveBoardIndex == -1, "ActiveBoardIndex debe ser -1 al terminar.");
            Assert(suddenDeath.GetLegalMoves().Count == 0, "No deben existir movimientos legales tras finalizar.");
            Assert(!suddenDeath.MakeMove(new Move(0, 4)), "No se deben aceptar movimientos tras terminar la partida.");
        }

        private static void TestFreeMoveOnSaturatedBoard()
        {
            var state = new GameState();

            Assert(state.MakeMove(new Move(0, 2)), "X(0,2)");
            Assert(state.MakeMove(new Move(2, 0)), "O(2,0) -> envía a X a 0");
            Assert(state.MakeMove(new Move(0, 3)), "X(0,3) -> envía a O a 3");
            Assert(state.MakeMove(new Move(3, 0)), "O(3,0) -> envía a X a 0");
            Assert(state.MakeMove(new Move(0, 7)), "X(0,7) -> envía a O a 7");
            Assert(state.MakeMove(new Move(7, 0)), "O(7,0) -> envía a X a 0");
            Assert(state.MakeMove(new Move(0, 8)), "X(0,8) -> envía a O a 8");
            Assert(state.MakeMove(new Move(8, 0)), "O(8,0) -> envía a X a 0");
            Assert(state.MakeMove(new Move(0, 0)), "X(0,0) -> envía a O a 0");
            Assert(state.MakeMove(new Move(0, 1)), "O(0,1) -> envía a X a 1");
            Assert(state.MakeMove(new Move(1, 0)), "X(1,0) -> envía a O a 0");
            Assert(state.MakeMove(new Move(0, 4)), "O(0,4) -> envía a X a 4");
            Assert(state.MakeMove(new Move(4, 0)), "X(4,0) -> envía a O a 0");
            Assert(state.MakeMove(new Move(0, 5)), "O(0,5) -> envía a X a 5");
            Assert(state.MakeMove(new Move(5, 0)), "X(5,0) -> envía a O a 0");
            Assert(state.MakeMove(new Move(0, 6)), "O(0,6) -> llena Tablero 0 y envía a X a 6");

            Assert(!state.IsGameOver, "El juego no debe haber terminado (Tablero 0 está lleno en empate local).");
            Assert(state.ActiveBoardIndex == 6, "X debe estar en el Tablero 6.");

            Assert(state.MakeMove(new Move(6, 0)), "X(6,0) apunta al Tablero 0 saturado.");
            Assert(state.ActiveBoardIndex == -1, "Como el Tablero 0 está saturado, O debe recibir TIRO LIBRE (-1).");
            Assert(state.ZobristKey == ZobristHasher.ComputeFullHash(state), "El hash Zobrist tras saturación debe ser exacto.");
        }

        private static void TestFull81CellGlobalDraw()
        {
            // Patrón de empate local sin 3 en raya en cada sub-tablero:
            // X O X
            // X O O
            // O X X  -> "XOX/XOO/OXX" = "XOXXOOOXX"
            // Dejamos únicamente la última celda del Tablero 8 vacía ("XOXXOOOX1") con turno de X en el Tablero 8:
            string nearFullFen =
                "XOXXOOOXX/XOXXOOOXX/XOXXOOOXX/XOXXOOOXX/XOXXOOOXX/XOXXOOOXX/XOXXOOOXX/XOXXOOOXX/XOXXOOOX1 X 8 *";

            GameState state = GameSerializer.FromFen(nearFullFen);
            Assert(!state.IsGameOver && state.MoveCount == 80, "Deben existir 80 fichas colocadas antes del último tiro.");
            Assert(state.GetLegalMoves().Count == 1 && state.GetLegalMoves()[0] == new Move(8, 8), "Solo debe quedar el movimiento (8,8).");

            Assert(state.MakeMove(new Move(8, 8)), "El movimiento #81 (8,8) debe ser válido.");
            Assert(state.IsGameOver && state.Winner == Player.None, "Al llenarse las 81 casillas sin 3 en raya, debe declararse Empate Global.");
            Assert(!state.TryGetWinningLine(out _, out _, out _, out _), "En un empate global no debe existir línea ganadora.");
            Assert(GameSerializer.ToFen(state).EndsWith(" D"), "El FEN de empate global debe terminar con el indicador 'D'.");
        }

        private static void TestZobristIncrementalConsistencyAndTransposition()
        {
            var t1 = new GameState();
            t1.MakeMove(new Move(0, 1));
            t1.MakeMove(new Move(1, 0));
            t1.MakeMove(new Move(0, 2));
            t1.MakeMove(new Move(2, 0));
            t1.MakeMove(new Move(0, 4));

            var t2 = new GameState();
            t2.MakeMove(new Move(0, 2));
            t2.MakeMove(new Move(2, 0));
            t2.MakeMove(new Move(0, 1));
            t2.MakeMove(new Move(1, 0));
            t2.MakeMove(new Move(0, 4));

            Assert(t1.ZobristKey == t2.ZobristKey, "Dos secuencias transpuestas al mismo estado deben tener idéntico ZobristKey.");
            Assert(t1.ZobristKey == ZobristHasher.ComputeFullHash(t1), "El hash incremental debe coincidir con ComputeFullHash.");
        }

        private static void TestUndoAndRedoMoveConsistency()
        {
            var state = new GameState();
            ulong initialHash = state.ZobristKey;

            state.MakeMove(new Move(0, 1));
            ulong hashAfter1 = state.ZobristKey;
            state.MakeMove(new Move(1, 0));
            state.MakeMove(new Move(0, 2));
            state.MakeMove(new Move(2, 0));
            ulong hashBeforeWinningMove = state.ZobristKey;
            state.MakeMove(new Move(0, 0));

            Assert(state.IsGameOver && state.Winner == Player.X, "X debe haber ganado antes de Undo.");
            Assert(state.CanUndo && !state.CanRedo, "Debe poder hacer Undo y no Redo.");

            Assert(state.UndoMove(), "Undo del tiro ganador debe ser exitoso.");
            Assert(!state.IsGameOver && state.Winner == Player.None, "Tras Undo ya no debe estar terminada la partida.");
            Assert(state.CurrentPlayer == Player.X && state.ActiveBoardIndex == 0, "Debe volver a ser turno de X en el Tablero 0.");
            Assert(state.ZobristKey == hashBeforeWinningMove, "El ZobristKey debe restaurarse exactamente tras Undo.");
            Assert(state.CanRedo, "Debe existir 1 movimiento disponible en Redo.");

            Assert(state.RedoMove(), "Redo del tiro ganador debe ser exitoso.");
            Assert(state.IsGameOver && state.Winner == Player.X, "Tras Redo, X vuelve a ser ganador.");
            Assert(!state.CanRedo, "La pila de Redo debe quedar vacía.");

            for (int i = 0; i < 4; i++)
            {
                Assert(state.UndoMove(), $"Undo paso {i}");
            }
            Assert(state.MoveCount == 1 && state.ZobristKey == hashAfter1, "El estado tras 4 Undos debe coincidir con el movimiento 1.");
            Assert(state.RedoCount == 4, "Deben existir 4 movimientos en la pila de Redo.");

            Assert(state.MakeMove(new Move(1, 4)), "Nuevo movimiento alternativo (1, 4)");
            Assert(!state.CanRedo && state.RedoCount == 0, "Hacer un movimiento nuevo debe invalidar la pila de Redo.");

            Assert(state.UndoMove() && state.UndoMove(), "Deshacer hasta el origen");
            Assert(!state.CanUndo && state.MoveCount == 0 && state.ZobristKey == initialHash, "El tablero debe regresar intacto al estado inicial.");
        }

        private static void TestFenAndMoveHistorySerialization()
        {
            var original = new GameState();
            original.MakeMove(new Move(4, 4));
            original.MakeMove(new Move(4, 0));
            original.MakeMove(new Move(0, 8));
            original.MakeMove(new Move(8, 2));

            string fen = GameSerializer.ToFen(original);
            GameState fromFen = GameSerializer.FromFen(fen);

            Assert(fromFen.ZobristKey == original.ZobristKey, "El estado reconstruido desde FEN debe tener el mismo ZobristKey.");
            Assert(fromFen.CurrentPlayer == original.CurrentPlayer, "El jugador activo desde FEN debe coincidir.");
            Assert(fromFen.ActiveBoardIndex == original.ActiveBoardIndex, "El tablero activo desde FEN debe coincidir.");
            Assert(fromFen.MoveCount == original.MoveCount, "El conteo de fichas desde FEN debe coincidir.");

            string transcript = GameSerializer.ExportMoveHistory(original);
            Assert(transcript == "44-40-08-82", $"Transcripción esperada '44-40-08-82', obtenida '{transcript}'.");

            GameState fromTranscript = GameSerializer.ImportMoveHistory(transcript);
            Assert(fromTranscript.ZobristKey == original.ZobristKey, "El estado importado por transcripción debe tener idéntico ZobristKey.");
            Assert(fromTranscript.CanUndo && fromTranscript.UndoCount == 4, "La importación por transcripción debe preservar la pila de Undo.");
        }

        private static void TestRatingSystemEdgeCasesAndMatchRecord()
        {
            // 1. Partida equilibrada provisional (1200 vs 1200, K=40)
            var playerX = new PlayerProfile("id-x", "Alice", 1200);
            var playerO = new PlayerProfile("id-o", "Bob", 1200);

            var game = GameSerializer.ImportMoveHistory("01-10-02-20-00");
            MatchRecord record = MatchRecord.CreateFromGame(game, playerX, playerO, updateProfilesIfGameOver: true);
            Assert(record.PlayerXEloBefore == 1200 && record.PlayerOEloBefore == 1200, "Rating inicial debe ser 1200.");
            Assert(record.PlayerXEloAfter == 1220 && record.PlayerOEloAfter == 1180, "Con K=40 provisional, el ganador sube +20 y el perdedor baja -20.");

            // 2. Victoria sorpresa (Upset): Jugador de 1000 vence a Maestro de 1800
            EloMatchUpdate upset = EloRatingCalculator.CalculateMatchRatingChange(1000, 20, 1800, 20, Player.X);
            Assert(upset.DeltaX >= 22, $"El jugador débil que vence a uno muy superior debe ganar casi todo el factor K=24 (ganó {upset.DeltaX}).");

            // 3. Empate asimétrico: Jugador de 1000 empata contra jugador de 1800 -> el de 1000 gana puntos y el de 1800 pierde puntos
            EloMatchUpdate unequalDraw = EloRatingCalculator.CalculateMatchRatingChange(1000, 20, 1800, 20, Player.None);
            Assert(unequalDraw.DeltaX > 0 && unequalDraw.DeltaO < 0, "En un empate asimétrico, el jugador de menor rating debe subir y el mayor debe bajar.");

            // 4. Piso mínimo de Rating (MinimumRatingFloor = 100)
            EloMatchUpdate floorCheck = EloRatingCalculator.CalculateMatchRatingChange(100, 2, 1200, 2, Player.O);
            Assert(floorCheck.NewRatingX == EloRatingCalculator.MinimumRatingFloor, "El rating nunca debe caer por debajo del piso mínimo (100).");

            // 5. Serialización PGN
            string pgnText = record.SerializeToText();
            MatchRecord restoredRecord = MatchRecord.DeserializeFromText(pgnText);
            Assert(restoredRecord.PlayerXName == "Alice" && restoredRecord.PlayerOName == "Bob", "Nombres en PGN deben conservarse.");
            GameState restoredGame = restoredRecord.ReconstructGameState();
            Assert(restoredGame.IsGameOver && restoredGame.Winner == Player.X && restoredGame.MoveCount == 5, "La partida reconstruida desde el MatchRecord debe ser idéntica.");
        }

        private static void TestMinimaxImmediateSuddenDeathWin()
        {
            var state = new GameState();
            state.MakeMove(new Move(0, 1));
            state.MakeMove(new Move(1, 0));
            state.MakeMove(new Move(0, 2));
            state.MakeMove(new Move(2, 0));

            var bot = new MinimaxBot(BotDifficulty.Hard);
            Move bestMove = bot.GetBestMove(state);

            Assert(bestMove == new Move(0, 0), $"El bot debe elegir el tiro ganador inmediato (0, 0), pero eligió {bestMove}.");
        }

        private static void TestMinimaxAvoidsSendingOpponentToLandmineBoard()
        {
            var state = new GameState();
            state.MakeMove(new Move(0, 1));
            state.MakeMove(new Move(1, 0));
            state.MakeMove(new Move(0, 2));

            var bot = new MinimaxBot(BotDifficulty.Hard);
            Move bestMove = bot.GetBestMove(state);

            Assert(bestMove.BoardIndex == 2, "El bot O debe jugar en el Tablero 2 activo.");
            Assert(bestMove.CellIndex != 0, $"El bot O NO debe jugar en la celda 0 porque enviaría a X al Tablero 0 minado; eligió {bestMove}.");
        }

        private static void TestTranspositionTableHitAndSpeedup()
        {
            var state = new GameState();
            state.MakeMove(new Move(4, 4));
            state.MakeMove(new Move(4, 0));

            var botWithTT = new MinimaxBot(maxDepth: 6, timeLimitMs: 5000, useTranspositionTable: true);
            MinimaxSearchResult resTT = botWithTT.FindBestMove(state);

            Assert(resTT.MaxDepthReached == 6, $"Debe alcanzar profundidad 6, alcanzó {resTT.MaxDepthReached}.");
            Assert(resTT.TranspositionHits > 0, "La Tabla de Transposición debe registrar hits durante Iterative Deepening.");

            MinimaxSearchResult cachedRes = botWithTT.FindBestMove(state);
            Assert(cachedRes.BestMove == resTT.BestMove, "La jugada óptima con TT caliente debe ser consistente.");
            Assert(cachedRes.TranspositionCutoffs > 0, "Debe producir podas directas desde la Tabla de Transposición.");
        }

        private static void TestIterativeDeepeningTimeLimitAndDifficulties()
        {
            var state = new GameState();
            state.MakeMove(new Move(4, 4));

            var fastBot = new MinimaxBot(maxDepth: 9, timeLimitMs: 25, useTranspositionTable: true);
            MinimaxSearchResult result = fastBot.FindBestMove(state);

            Assert(result.MaxDepthReached >= 1, "Iterative Deepening siempre debe completar al menos profundidad 1.");
            Assert(state.IsValidMove(result.BestMove), "El movimiento retornado bajo límite de tiempo debe ser legal.");

            foreach (BotDifficulty diff in new[] { BotDifficulty.Easy, BotDifficulty.Medium, BotDifficulty.Hard })
            {
                var presetBot = new MinimaxBot(diff);
                Move move = presetBot.GetBestMove(state);
                Assert(state.IsValidMove(move), $"El nivel de dificultad {diff} debe retornar un movimiento válido.");
            }
        }

        private static void TestGameSessionControllerAsyncFlow()
        {
            var session = new GameSessionController(SessionMode.HumanVsBotX, BotDifficulty.Easy);
            int stateChangedEvents = 0;
            session.StateChanged += () => stateChangedEvents++;

            Assert(session.TryPlayHumanMove(4, 4), "El movimiento humano inicial (4,4) debe ser aceptado.");
            Assert(session.IsCurrentTurnBot(), "Después del tiro humano debe ser turno del Bot O.");

            MinimaxSearchResult? botRes = session.PlayBotTurnAsync().GetAwaiter().GetResult();
            Assert(botRes.HasValue, "PlayBotTurnAsync debe retornar un resultado de búsqueda.");
            Assert(session.State.MoveCount == 2, "Deben existir 2 movimientos en el tablero (Humano + Bot).");
            Assert(!session.IsCurrentTurnBot(), "El turno debe regresar al humano (X).");

            Assert(session.Undo(), "Undo debe ejecutarse correctamente.");
            Assert(session.State.MoveCount == 0 && session.State.CurrentPlayer == Player.X, "Undo contra Bot debe regresar al turno de X.");
            Assert(session.Redo(), "Redo debe restaurar ambos movimientos.");
            Assert(session.State.MoveCount == 2, "Tras Redo deben existir 2 movimientos nuevamente.");
            Assert(stateChangedEvents >= 4, "El controlador debe emitir eventos StateChanged para actualizar la UI de Godot.");
        }

        private static void TestOnlineMatchmakingAndMutualConsentTakeback()
        {
            var matchmaking = new MatchmakingService();

            // 1. Verificar Registro y Login con contraseña
            Assert(matchmaking.RegisterAccount("Alice", "pass1234", out PlayerProfile alice, out _), "Registro de Alice exitoso.");
            Assert(!matchmaking.RegisterAccount("Alice", "otraClave", out _, out _), "No debe permitir registrar un usuario duplicado.");
            Assert(!matchmaking.AuthenticateAccount("Alice", "claveIncorrecta", out _, out _), "Debe rechazar contraseña incorrecta.");
            Assert(matchmaking.AuthenticateAccount("Alice", "pass1234", out PlayerProfile authAlice, out _) && authAlice.PlayerId == alice.PlayerId, "Login de Alice con contraseña correcta.");

            PlayerProfile bob = matchmaking.GetOrCreateAccount("Bob");
            PlayerProfile charlie5Plus0 = matchmaking.GetOrCreateAccount("Charlie");
            PlayerProfile grandmaster = matchmaking.GetOrCreateAccount("Grandmaster");
            grandmaster.EloRating = 2100;

            // 2. Verificar aislamiento estricto por Modo de Tiempo (3+2 vs 5+0 NO deben sincronizarse)
            Assert(matchmaking.EnqueueOrMatchPlayer(alice.PlayerId, TimeControlConfig.Blitz3Plus2, maxEloGap: 300) == null, "Alice (1200) entra en la cola Blitz 3+2.");
            Assert(matchmaking.EnqueueOrMatchPlayer(charlie5Plus0.PlayerId, TimeControlConfig.Rapid5Plus0, maxEloGap: 300) == null, "Charlie (1200) en Rápida 5+0 NO debe emparejarse con Alice en Blitz 3+2.");
            Assert(matchmaking.EnqueueOrMatchPlayer(grandmaster.PlayerId, TimeControlConfig.Blitz3Plus2, maxEloGap: 300) == null, "Grandmaster (2100) NO debe emparejarse con Alice (1200) cuando maxEloGap=300.");

            OnlineMatchRoom room = matchmaking.EnqueueOrMatchPlayer(bob.PlayerId, TimeControlConfig.Blitz3Plus2, maxEloGap: 300);
            Assert(room != null, "Bob (1200) buscando en el mismo modo Blitz 3+2 sí debe emparejarse con Alice (1200).");
            Assert(room.TimeControl.ToCode() == "3+2", "La sala debe crearse con el modo Blitz 3+2.");

            string idX = room.PlayerX.PlayerId;
            string idO = room.PlayerO.PlayerId;

            Assert(!room.SubmitMove(idO, new Move(4, 4), out _), "El servidor debe rechazar un tiro fuera de turno.");
            Assert(room.SubmitMove(idX, new Move(4, 4), out _), "X juega (4, 4) legalmente.");

            Assert(room.RequestTakeback(idX, out TakebackRequest req1, out _), "X solicita Takeback.");
            Assert(req1.PliesToUndo == 1, "Como X acaba de tirar, el Takeback debe ser de 1 medio turno (1 ply).");

            Assert(!room.RespondToTakeback(idX, accept: true, out _), "El propio solicitante NO puede aceptar su propio Takeback.");
            Assert(room.RespondToTakeback(idO, accept: false, out _), "O rechaza el Takeback.");
            Assert(room.State.MoveCount == 1 && !room.PendingTakeback.HasValue, "Tras el rechazo de O, el tablero conserva el movimiento.");

            Assert(room.RequestTakeback(idX, out _, out _), "X vuelve a solicitar Takeback.");
            Assert(room.RespondToTakeback(idO, accept: true, out _), "O acepta el Takeback.");
            Assert(room.State.MoveCount == 0 && room.State.CurrentPlayer == Player.X, "Al aceptar O, la jugada se deshace y vuelve a ser turno de X.");

            Assert(room.SubmitMove(idX, new Move(4, 4), out _), "X juega (4, 4).");
            Assert(room.SubmitMove(idO, new Move(4, 0), out _), "O juega (4, 0).");
            Assert(room.RequestTakeback(idX, out TakebackRequest req2, out _), "X solicita regresar a su turno anterior (2 plies).");
            Assert(req2.PliesToUndo == 2, "Debe solicitar deshacer 2 medios turnos.");
            Assert(room.RespondToTakeback(idO, accept: true, out _), "O acepta deshacer los 2 medios turnos.");
            Assert(room.State.MoveCount == 0 && room.State.CurrentPlayer == Player.X, "El tablero regresa a 0 movimientos y turno de X.");

            // Verificar que si ya hubo movimientos y alguien se rinde o agota tiempo, cuenta como victoria y actualiza puntos
            Assert(room.SubmitMove(idX, new Move(4, 4), out _), "X realiza el primer movimiento.");
            Assert(room.Resign(idO, out _), "O se rinde.");
            Assert(room.IsMatchOver && room.EffectiveWinner == Player.X && room.EndReason == MatchEndReason.Resignation, "X gana por rendición de O.");
            Assert(room.State.IsGameOver && room.State.Winner == Player.X, "El GameState interno de la sala debe quedar marcado como IsGameOver con Winner=X.");

            // Verificar persistencia en directorio temporal (incluyendo contraseña cifrada)
            string tempDir = Path.Combine(Path.GetTempPath(), "FractalTTT_Test_" + Guid.NewGuid().ToString("N"));
            try
            {
                matchmaking.SaveToDirectory(tempDir);
                var reloaded = new MatchmakingService();
                reloaded.LoadFromDirectory(tempDir);
                Assert(reloaded.GetLeaderboard().Count == 4, "Deben cargarse las 4 cuentas desde disco.");
                Assert(reloaded.AuthenticateAccount("Alice", "pass1234", out _, out _), "Debe conservar el hash de contraseña de Alice tras recargar.");
                Assert(reloaded.GetPlayerMatchHistory("Alice").Count == 1, "Debe cargarse el historial de partidas de Alice.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void TestOnlineMultiplayerClientProtocolStateMachine()
        {
            var matchmaking = new MatchmakingService();
            PlayerProfile alice = matchmaking.GetOrCreateAccount("Alice");
            PlayerProfile bob = matchmaking.GetOrCreateAccount("Bob");

            matchmaking.EnqueueOrMatchPlayer(alice.PlayerId, TimeControlConfig.Blitz3Plus2);
            OnlineMatchRoom room = matchmaking.EnqueueOrMatchPlayer(bob.PlayerId, TimeControlConfig.Blitz3Plus2);

            using (var clientX = new OnlineMultiplayerClient())
            using (var clientO = new OnlineMultiplayerClient())
            {
                clientX.ProcessServerPacket(MultiplayerProtocol.BuildAuthOk(room.PlayerX));
                clientO.ProcessServerPacket(MultiplayerProtocol.BuildAuthOk(room.PlayerO));

                clientX.ProcessServerPacket(MultiplayerProtocol.BuildMatchStart(room, Player.X));
                clientO.ProcessServerPacket(MultiplayerProtocol.BuildMatchStart(room, Player.O));

                Assert(clientX.IsInActiveMatch && clientX.AssignedRole == Player.X, "Cliente X debe estar activo con rol X.");
                Assert(clientO.IsInActiveMatch && clientO.AssignedRole == Player.O, "Cliente O debe estar activo con rol O.");

                // Verificar regla estilo Lichess: antes del primer tiro corre el contador de 30s, NO el reloj principal
                Assert(clientX.Clock.IsWaitingForFirstMove && !clientX.Clock.IsRunning, "El reloj principal no debe correr antes del primer movimiento.");
                clientX.TickClock(10.0);
                Assert(Math.Abs(clientX.Clock.RemainingSecondsX - 180.0) < 0.001, "El reloj principal de X debe seguir intacto en 180s antes del primer tiro.");
                Assert(Math.Abs(clientX.Clock.FirstMoveSecondsRemaining - 20.0) < 0.001, "El temporizador de primer movimiento debe bajar a 20s.");

                room.SubmitMove(room.PlayerX.PlayerId, new Move(4, 4), out _);
                string syncPacket = MultiplayerProtocol.BuildStateSync(room, new Move(4, 4));
                clientX.ProcessServerPacket(syncPacket);
                clientO.ProcessServerPacket(syncPacket);

                Assert(!clientX.Clock.IsWaitingForFirstMove && clientX.Clock.IsRunning, "Tras el primer movimiento se desactiva la cuenta de 30s y arranca el reloj principal.");
                Assert(clientO.State.MoveCount == 1 && clientO.State.ActiveBoardIndex == 4, "Cliente O debe sincronizar el movimiento (4,4).");

                room.RequestTakeback(room.PlayerX.PlayerId, out TakebackRequest tbReq, out _);
                string offerPacket = MultiplayerProtocol.BuildTakebackOffered(room, tbReq);
                clientX.ProcessServerPacket(offerPacket);
                clientO.ProcessServerPacket(offerPacket);

                Assert(clientX.IsWaitingForOpponentTakebackResponse, "Cliente X debe quedar esperando respuesta del rival.");
                Assert(clientO.HasIncomingTakebackOffer && clientO.IncomingTakebackPlies == 1, "Cliente O debe mostrar el banner de confirmación de Takeback.");

                room.RespondToTakeback(room.PlayerO.PlayerId, accept: true, out _);
                string resolvedPacket = MultiplayerProtocol.BuildTakebackResolved(room, accepted: true);
                clientX.ProcessServerPacket(resolvedPacket);
                clientO.ProcessServerPacket(resolvedPacket);

                Assert(!clientO.HasIncomingTakebackOffer && !clientX.IsWaitingForOpponentTakebackResponse, "El estado de Takeback pendiente debe limpiarse.");
                Assert(clientX.State.MoveCount == 0 && clientO.State.MoveCount == 0, "Ambos clientes deben haber revertido la jugada sincronizadamente.");

                // Simular un movimiento y luego derrota por Tiempo Agotado (Timeout) de O en línea
                room.SubmitMove(room.PlayerX.PlayerId, new Move(4, 4), out _);
                string syncAfterRedo = MultiplayerProtocol.BuildStateSync(room, new Move(4, 4));
                clientX.ProcessServerPacket(syncAfterRedo);
                clientO.ProcessServerPacket(syncAfterRedo);

                Assert(room.ForfeitByDisconnectOrTimeout(room.PlayerO.PlayerId, isTimeout: true), "O pierde por tiempo agotado.");
                string matchEndPacket = MultiplayerProtocol.BuildMatchEnd(room, room.CompletedMatchRecord, room.CompletedEloUpdate.Value);
                clientX.ProcessServerPacket(matchEndPacket);
                clientO.ProcessServerPacket(matchEndPacket);

                Assert(clientX.State.IsGameOver && clientX.State.Winner == Player.X, "Cliente X debe ver la partida terminada y a X como ganador por tiempo.");
                Assert(!clientX.IsInActiveMatch && !clientO.IsInActiveMatch, "IsInActiveMatch debe ser false al terminar para deshabilitar Rendirse.");
                Assert(clientX.LastRatingUpdate.HasValue && clientX.LastRatingUpdate.Value.DeltaX > 0, "Cliente X debe recibir el delta positivo de puntos (+pts).");

                // Verificar flujo de Revancha: X solicita revancha -> O recibe notificación -> O rechaza
                room.RegisterRematchVote(room.PlayerX.PlayerId, out _, out bool bothReady);
                Assert(!bothReady, "Con un solo voto aún no inicia la revancha.");
                string rematchOfferPkt = MultiplayerProtocol.BuildRematchOffered(room, Player.X);
                clientX.ProcessServerPacket(rematchOfferPkt);
                clientO.ProcessServerPacket(rematchOfferPkt);
                Assert(clientX.HasRequestedRematch && !clientX.HasOpponentOfferedRematch, "Cliente X sabe que solicitó revancha.");
                Assert(clientO.HasOpponentOfferedRematch && !clientO.HasRequestedRematch, "Cliente O recibe la notificación de aceptar/rechazar revancha.");

                room.ClearRematchVotes();
                string rematchDeclinedPkt = MultiplayerProtocol.BuildRematchDeclined(room, Player.O);
                clientX.ProcessServerPacket(rematchDeclinedPkt);
                clientO.ProcessServerPacket(rematchDeclinedPkt);
                Assert(!clientO.HasOpponentOfferedRematch && clientX.RematchDeclinedByOpponent, "Tras rechazar O, se limpia la oferta y X ve que fue rechazada.");

                // Verificar que si expiran los 30s para el primer movimiento, se bloquea el tablero de inmediato
                matchmaking.EnqueueOrMatchPlayer(alice.PlayerId, TimeControlConfig.Bullet1Plus1);
                Assert(matchmaking.EnqueueOrMatchPlayer(bob.PlayerId, TimeControlConfig.Blitz3Plus0) == null, "1+1 y 3+0 JAMÁS deben emparejarse entre sí.");
                OnlineMatchRoom roomBullet = matchmaking.EnqueueOrMatchPlayer(bob.PlayerId, TimeControlConfig.Bullet1Plus1);
                Assert(roomBullet != null && roomBullet.TimeControl.ToCode() == "1+1", "Al buscar ambos en 1+1 sí se emparejan.");

                clientX.ProcessServerPacket(MultiplayerProtocol.BuildMatchStart(roomBullet, Player.X));
                clientX.TickClock(30.5);
                Assert(clientX.State.IsGameOver && clientX.LastEndReason == MatchEndReason.Aborted, "Al expirar los 30s sin primer tiro, el cliente concluye y bloquea el tablero de inmediato.");
                Assert(!clientX.TrySendMove(4, 4), "No debe permitir hacer el primer movimiento después de expirar los 30s.");
            }
        }

        private static void TestMatchClockFischerIncrementAndWinningLine()
        {
            GameState winGame = GameSerializer.ImportMoveHistory("01-10-02-20-00");
            Assert(winGame.TryGetWinningLine(out int winBoard, out int c0, out int c1, out int c2), "Debe detectar la línea ganadora.");
            Assert(winBoard == 0 && c0 == 0 && c1 == 1 && c2 == 2, $"Línea ganadora esperada (Tablero 0: 0,1,2), obtenida ({winBoard}: {c0},{c1},{c2}).");

            // Verificar que el modo contra IA por defecto no tiene límite de tiempo
            var aiSession = new GameSessionController(SessionMode.HumanVsBotX, BotDifficulty.Hard);
            Assert(aiSession.Clock.Config.IsUntimed, "El modo contra IA debe ser sin límite de tiempo (IsUntimed).");

            var session = new GameSessionController(
                SessionMode.PassAndPlay2P,
                BotDifficulty.Easy,
                timeControl: TimeControlConfig.Blitz3Plus2);

            session.Clock.Start(Player.X);
            session.TickClock(10.0);
            Assert(Math.Abs(session.Clock.RemainingSecondsX - 170.0) < 0.001, "A X deben quedarle 170s.");

            Assert(session.TryPlayHumanMove(4, 4), "X juega (4,4)");
            Assert(Math.Abs(session.Clock.RemainingSecondsX - 172.0) < 0.001, "X debe recibir +2s de incremento Fischer (172s).");
            Assert(session.Clock.ActivePlayer == Player.O, "El reloj activo debe cambiar a O.");

            Assert(session.TickClock(180.5), "El tick que agota el reloj de O debe retornar true.");
            Assert(session.State.IsGameOver && session.State.Winner == Player.X, "X debe ganar por tiempo agotado de O.");
            Assert(session.LastEndReason == MatchEndReason.Timeout, "El motivo de fin de partida debe ser Timeout.");
            Assert(!session.LastEloUpdate.HasValue && session.ProfileX.EloRating == 1200, "Las partidas locales no deben sumar ni restar puntos competitivos.");
        }

        private static void TestMatchReplayNavigatorStepByStep()
        {
            var pX = new PlayerProfile("x", "Alice", 1200);
            var pO = new PlayerProfile("o", "Bob", 1200);
            GameState finishedGame = GameSerializer.ImportMoveHistory("01-10-02-20-00");
            MatchRecord rec = MatchRecord.CreateFromGame(finishedGame, pX, pO);

            var replay = new MatchReplayNavigator(rec);
            Assert(replay.TotalPlies == 5 && replay.CurrentPly == 5, "Al cargar un MatchRecord debe iniciar en el último movimiento (5/5).");
            Assert(replay.CurrentState.IsGameOver && replay.CurrentState.Winner == Player.X, "En el turno 5/5 X es el ganador.");
            Assert(replay.GetCurrentEvaluationForX() == MinimaxBot.WinScore, "La evaluación en el final debe ser +WinScore.");

            replay.JumpToStart();
            Assert(replay.CurrentPly == 0 && !replay.CanStepBackward && replay.CanStepForward, "En el inicio el turno es 0/5.");

            Assert(replay.StepForward(), "Avanzar al turno 1/5.");
            Assert(replay.CurrentPly == 1 && replay.CurrentState.GetLastMove() == new Move(0, 1), "El movimiento 1 debe ser (0,1).");

            replay.JumpToPly(3);
            Assert(replay.CurrentPly == 3 && replay.CurrentState.GetLastMove() == new Move(0, 2), "Al saltar al turno 3 el último tiro debe ser (0,2).");

            replay.JumpToEnd();
            Assert(replay.CurrentPly == 5 && !replay.CanStepForward, "Al saltar al final debe regresar a 5/5.");
        }

        private static void TestStoreComplianceAccountDeletionAndSoundSynthesizer()
        {
            // 1. Verificar eliminación permanente de cuenta (Apple App Store Guideline 5.1.1(v) y Google Play Data Safety)
            var mm = new MatchmakingService();
            Assert(mm.RegisterAccount("TempPlayer", "secret123", out PlayerProfile created, out _), "Debe registrar TempPlayer.");
            Assert(created != null && created.Username == "TempPlayer", "Perfil creado debe ser TempPlayer.");

            Assert(!mm.DeleteAccount("TempPlayer", "wrongpass", out string badPassErr), "No debe eliminar cuenta con contraseña incorrecta.");
            Assert(!string.IsNullOrEmpty(badPassErr), "Debe devolver mensaje de error al fallar contraseña.");

            Assert(mm.DeleteAccount("TempPlayer", "secret123", out string delErr), $"Debe eliminar cuenta con contraseña correcta ({delErr}).");
            Assert(!mm.AuthenticateAccount("TempPlayer", "secret123", out _, out _), "La cuenta eliminada ya no debe poder iniciar sesión.");

            // 2. Verificar protocolo ACCOUNT_DELETED en OnlineMultiplayerClient
            using (var client = new OnlineMultiplayerClient())
            {
                string deletedEventUser = null;
                client.AccountDeleted += u => deletedEventUser = u;
                client.ProcessServerPacket(MultiplayerProtocol.BuildAccountDeleted("TempPlayer"));
                Assert(deletedEventUser == "TempPlayer" && !client.IsAuthenticated, "El cliente debe procesar ACCOUNT_DELETED y limpiar sesión.");
            }

            // 3. Verificar sintetizador procedural de audio WAV/PCM y texto de Política de Privacidad
            foreach (GameSoundEffect sfx in Enum.GetValues(typeof(GameSoundEffect)))
            {
                byte[] wav = SoundEffectsSynthesizer.GenerateWavFileBytes(sfx);
                byte[] pcm = SoundEffectsSynthesizer.GeneratePcm16Bytes(sfx);
                Assert(wav.Length > 44 && pcm.Length == wav.Length - 44, $"El efecto {sfx} debe generar cabecera WAV de 44 bytes + muestras PCM.");
                Assert(wav[0] == (byte)'R' && wav[1] == (byte)'I' && wav[2] == (byte)'F' && wav[3] == (byte)'F', "Cabecera RIFF válida.");
                Assert(wav[8] == (byte)'W' && wav[9] == (byte)'A' && wav[10] == (byte)'V' && wav[11] == (byte)'E', "Formato WAVE válido.");
            }

            Assert(!string.IsNullOrWhiteSpace(SoundEffectsSynthesizer.PrivacyPolicyInAppText) &&
                   SoundEffectsSynthesizer.PrivacyPolicyInAppText.Contains("Eliminar Cuenta"),
                   "El texto de Política de Privacidad integrado debe documentar la eliminación de cuenta.");
        }

        private static void TestMinimaxMultiBoardRedirectionLookaheadAndTraps()
        {
            // Caso 1: El humano (X) ya tiene 2 en raya o 2 piezas peligrosas en varios sub-tableros (1, 2, 3, 5, 6, 7, 8).
            // El bot (O) está forzado a tirar en el sub-tablero 4. Solo enviar al sub-tablero 0 es seguro.
            var cells = new Player[81];
            foreach (int dangerBoard in new[] { 1, 2, 3, 5, 6, 7, 8 })
            {
                cells[dangerBoard * 9 + 0] = Player.X;
                cells[dangerBoard * 9 + 4] = Player.X; // Amenaza diagonal 0-4-8 para X
            }

            // Además, en el tablero 4 (donde tira O), si O jugara la celda 4 (auto-redirección 4->4),
            // supongamos que X también tiene 4*9+1 y 4*9+2 (amenaza 0-1-2 en el propio tablero 4).
            cells[4 * 9 + 1] = Player.X;
            cells[4 * 9 + 2] = Player.X;

            var state = new GameState();
            state.LoadFromSnapshot(cells, Player.O, activeBoardIndex: 4, isGameOver: false, winner: Player.None);

            foreach (BotDifficulty diff in new[] { BotDifficulty.Easy, BotDifficulty.Medium, BotDifficulty.Hard, BotDifficulty.Master })
            {
                var bot = new MinimaxBot(diff);
                Move chosen = bot.GetBestMove(state);
                Assert(chosen.BoardIndex == 4 && chosen.CellIndex == 0,
                    $"En dificultad {diff}, la IA debe elegir (4,0) para bloquear a X en el tablero 4 y enviarlo al único tablero seguro #0, pero eligió {chosen}.");
            }

            // Caso 2: Evitar enviar al rival a un sub-tablero donde ya tiene 2 piezas armadas (aunque aún no sean 2 en raya directo)
            // cuando existe un sub-tablero completamente vacío.
            var setupCells = new Player[81];
            setupCells[2 * 9 + 0] = Player.X;
            setupCells[2 * 9 + 7] = Player.X;
            var setupState = new GameState();
            setupState.LoadFromSnapshot(setupCells, Player.O, activeBoardIndex: 4, isGameOver: false, winner: Player.None);

            var hardBot = new MinimaxBot(BotDifficulty.Hard);
            Move safeSetupMove = hardBot.GetBestMove(setupState);
            Assert(safeSetupMove.CellIndex != 2,
                $"La IA debe evitar enviar a X al sub-tablero 2 donde X ya tiene 2 piezas armadas (eligió {safeSetupMove}).");
        }

        private static void TestHumanReadableMoveQualityAndAnalysisLabels()
        {
            var pX = new PlayerProfile("x", "JugadorX", 1200);
            var pO = new PlayerProfile("o", "JugadorO", 1200);
            // Transcripción donde X gana en el movimiento 5: 01-10-02-20-00
            // Nota: en el turno 4, O juega (2,0) que envía a X de regreso al tablero 0 donde X ya tiene (0,1) y (0,2) -> Error grave de O
            GameState game = GameSerializer.ImportMoveHistory("01-10-02-20-00");
            MatchRecord rec = MatchRecord.CreateFromGame(game, pX, pO);
            var nav = new MatchReplayNavigator(rec);

            // En el movimiento 5/5 (victoria de X con 0,0)
            string finalQuality = nav.GetCurrentMoveQuality(out string finalExplanation);
            Assert(finalQuality == "Jugada ganadora", $"El último tiro ganador debe clasificarse como 'Jugada ganadora', fue '{finalQuality}'.");
            Assert(!string.IsNullOrWhiteSpace(finalExplanation), "Debe incluir explicación en español sencillo.");
            Assert(nav.GetCurrentPositionStatus().Contains("Victoria"), "El estado final debe indicar Victoria.");

            // En el movimiento 4/5 (O jugó 2,0 enviando a X al tablero 0 donde X tenía 2 en línea)
            nav.JumpToPly(4);
            string blunderQuality = nav.GetCurrentMoveQuality(out string blunderExplanation);
            Assert(blunderQuality == "Error grave", $"Enviar al rival a un tablero donde gana en 1 turno debe ser 'Error grave', fue '{blunderQuality}' ({blunderExplanation}).");

            // En el movimiento 1/5 (apertura normal de X)
            nav.JumpToPly(1);
            string openingQuality = nav.GetCurrentMoveQuality(out _);
            Assert(openingQuality == "Muy buen movimiento" || openingQuality == "Buen movimiento",
                $"Una apertura segura debe clasificarse como buen o muy buen movimiento, fue '{openingQuality}'.");
        }

        private static void TestServerCrashGracefulCancelAndPrivateRoomCode()
        {
            // 1. Verificar Salas Privadas con código de 4 caracteres en MatchmakingService
            var mm = new MatchmakingService();
            mm.RegisterAccount("HostUser", "pass123", out PlayerProfile host, out _);
            mm.RegisterAccount("GuestUser", "pass123", out PlayerProfile guest, out _);
            host.EloRating = 1350;

            string code = mm.CreatePrivateLobby(host.PlayerId, TimeControlConfig.Rapid5Plus0);
            Assert(!string.IsNullOrEmpty(code) && code.Length == 4, $"El código de sala privada debe tener 4 caracteres, fue '{code}'.");

            OnlineMatchRoom room = mm.JoinPrivateLobby(guest.PlayerId, code.ToLowerInvariant(), out string joinErr);
            Assert(room != null && string.IsNullOrEmpty(joinErr), $"GuestUser debe poder unirse con el código '{code}' ({joinErr}).");
            Assert(room.TimeControl.InitialSeconds == 300, "La sala privada debe respetar el modo de tiempo del anfitrión (5+0).");

            // 2. Verificar que si el servidor se cae a mitad de partida, el cliente cancela la partida sin puntos y dispara ServerConnectionLost
            using (var client = new OnlineMultiplayerClient())
            {
                client.ProcessServerPacket(MultiplayerProtocol.BuildAuthOk(host));
                client.ProcessServerPacket(MultiplayerProtocol.BuildMatchStart(room, Player.X));
                room.SubmitMove(room.PlayerX.PlayerId, new Move(4, 4), out _);
                client.ProcessServerPacket(MultiplayerProtocol.BuildStateSync(room, new Move(4, 4)));

                Assert(client.IsInActiveMatch, "El cliente debe estar en partida activa antes de la caída del servidor.");

                string disconnectMessage = null;
                client.ServerConnectionLost += msg => disconnectMessage = msg;

                client.HandleUnexpectedServerDisconnect();

                Assert(!client.IsInActiveMatch, "Al caerse el servidor, IsInActiveMatch debe pasar a false inmediatamente.");
                Assert(!client.IsInQueue, "Al caerse el servidor, IsInQueue debe ser false.");
                Assert(client.LastEndReason == MatchEndReason.Aborted, "La partida interrumpida por caída del servidor debe quedar Aborted.");
                Assert(!client.LastRatingUpdate.HasValue, "Nadie debe ganar ni perder puntos cuando el servidor se cae.");
                Assert(client.LocalElo == 1350, "El puntaje del jugador debe permanecer intacto (1350).");
                Assert(!string.IsNullOrEmpty(disconnectMessage), "Debe disparar el evento ServerConnectionLost con un mensaje para redirigir a Inicio.");

                // 3. Verificar reanudación de sesión ante micro-cortes de red móvil (SESSION_RESUMED)
                string resumedRoomId = null;
                client.SessionResumed += rId => resumedRoomId = rId;
                client.ProcessServerPacket(MultiplayerProtocol.BuildSessionResumed(room, Player.X));
                Assert(resumedRoomId == room.RoomId && client.IsInActiveMatch, "El cliente debe restaurar la partida activa al recibir SESSION_RESUMED.");
                Assert(client.State.MoveCount == 1 && client.State.GetLastMove() == new Move(4, 4), "El estado del tablero debe restaurar el movimiento (4,4).");
            }
        }

        private static string BuildFenFromCells(Player[] cells, Player turn, int activeBoard)
        {
            var temp = new GameState();
            temp.LoadFromSnapshot(cells, turn, activeBoard, isGameOver: false, winner: Player.None);
            return GameSerializer.ToFen(temp);
        }

        private static void AssertThrows<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Fallo de aserción ({message}): se esperaba {typeof(TException).Name} pero se lanzó {ex.GetType().Name}.");
            }

            throw new InvalidOperationException($"Fallo de aserción ({message}): no se lanzó ninguna excepción (se esperaba {typeof(TException).Name}).");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Fallo de aserción: {message}");
            }
        }
    }
}
