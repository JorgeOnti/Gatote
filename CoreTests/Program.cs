using System;
using FractalTicTacToe.Core;

namespace CoreTests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("Ejecutando suite de pruebas para FractalTicTacToe.Core...");

            TestInitialState();
            TestReciprocalRedirectionAndValidation();
            TestSuddenDeathVictory();
            TestFreeMoveOnSaturatedBoard();
            TestZobristIncrementalConsistencyAndTransposition();
            TestUndoAndRedoMoveConsistency();
            TestFenAndMoveHistorySerialization();
            TestEloRatingAndMatchRecord();
            TestMinimaxImmediateSuddenDeathWin();
            TestMinimaxAvoidsSendingOpponentToLandmineBoard();
            TestTranspositionTableHitAndSpeedup();
            TestIterativeDeepeningTimeLimitAndDifficulties();
            TestGameSessionControllerAsyncFlow();
            TestOnlineMatchmakingAndMutualConsentTakeback();

            Console.WriteLine("¡Todas las pruebas de dominio, Undo/Redo, Serialización FEN/PGN, ELO, Sesión Móvil, Multijugador Online (Takeback) y Minimax pasaron exitosamente!");
            return 0;
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

            // Llevamos la partida hasta victoria por Muerte Súbita en 5 tiros:
            state.MakeMove(new Move(0, 1));
            ulong hashAfter1 = state.ZobristKey;
            state.MakeMove(new Move(1, 0));
            state.MakeMove(new Move(0, 2));
            state.MakeMove(new Move(2, 0));
            ulong hashBeforeWinningMove = state.ZobristKey;
            state.MakeMove(new Move(0, 0)); // X gana por Muerte Súbita

            Assert(state.IsGameOver && state.Winner == Player.X, "X debe haber ganado antes de Undo.");
            Assert(state.CanUndo && !state.CanRedo, "Debe poder hacer Undo y no Redo.");

            // Deshacer el tiro ganador
            Assert(state.UndoMove(), "Undo del tiro ganador debe ser exitoso.");
            Assert(!state.IsGameOver && state.Winner == Player.None, "Tras Undo ya no debe estar terminada la partida.");
            Assert(state.CurrentPlayer == Player.X && state.ActiveBoardIndex == 0, "Debe volver a ser turno de X en el Tablero 0.");
            Assert(state.ZobristKey == hashBeforeWinningMove, "El ZobristKey debe restaurarse exactamente tras Undo.");
            Assert(state.CanRedo, "Debe existir 1 movimiento disponible en Redo.");

            // Rehacer el tiro ganador con RedoMove()
            Assert(state.RedoMove(), "Redo del tiro ganador debe ser exitoso.");
            Assert(state.IsGameOver && state.Winner == Player.X, "Tras Redo, X vuelve a ser ganador.");
            Assert(!state.CanRedo, "La pila de Redo debe quedar vacía.");

            // Deshacer 4 movimientos hasta quedar en el movimiento 1
            for (int i = 0; i < 4; i++)
            {
                Assert(state.UndoMove(), $"Undo paso {i}");
            }
            Assert(state.MoveCount == 1 && state.ZobristKey == hashAfter1, "El estado tras 4 Undos debe coincidir con el movimiento 1.");
            Assert(state.RedoCount == 4, "Deben existir 4 movimientos en la pila de Redo.");

            // Si ahora hacemos un movimiento nuevo distinto, la pila de Redo debe limpiarse
            Assert(state.MakeMove(new Move(1, 4)), "Nuevo movimiento alternativo (1, 4)");
            Assert(!state.CanRedo && state.RedoCount == 0, "Hacer un movimiento nuevo debe invalidar la pila de Redo.");

            // Deshacer los 2 movimientos restantes hasta el inicio
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

            // 1. Prueba de exportación/importación FEN
            string fen = GameSerializer.ToFen(original);
            GameState fromFen = GameSerializer.FromFen(fen);

            Assert(fromFen.ZobristKey == original.ZobristKey, "El estado reconstruido desde FEN debe tener el mismo ZobristKey.");
            Assert(fromFen.CurrentPlayer == original.CurrentPlayer, "El jugador activo desde FEN debe coincidir.");
            Assert(fromFen.ActiveBoardIndex == original.ActiveBoardIndex, "El tablero activo desde FEN debe coincidir.");
            Assert(fromFen.MoveCount == original.MoveCount, "El conteo de fichas desde FEN debe coincidir.");

            // 2. Prueba de exportación/importación de transcripción de movimientos ("44-40-08-82")
            string transcript = GameSerializer.ExportMoveHistory(original);
            Assert(transcript == "44-40-08-82", $"Transcripción esperada '44-40-08-82', obtenida '{transcript}'.");

            GameState fromTranscript = GameSerializer.ImportMoveHistory(transcript);
            Assert(fromTranscript.ZobristKey == original.ZobristKey, "El estado importado por transcripción debe tener idéntico ZobristKey.");
            Assert(fromTranscript.CanUndo && fromTranscript.UndoCount == 4, "La importación por transcripción debe preservar la pila de Undo.");
        }

        private static void TestEloRatingAndMatchRecord()
        {
            var playerX = new PlayerProfile("id-x", "Alice", 1200);
            var playerO = new PlayerProfile("id-o", "Bob", 1200);

            var game = GameSerializer.ImportMoveHistory("01-10-02-20-00"); // X gana en 5 tiros
            Assert(game.IsGameOver && game.Winner == Player.X, "X debe ganar la partida de prueba.");

            MatchRecord record = MatchRecord.CreateFromGame(game, playerX, playerO, updateProfilesIfGameOver: true);
            Assert(record.PlayerXEloBefore == 1200 && record.PlayerOEloBefore == 1200, "ELO inicial debe ser 1200.");
            Assert(record.PlayerXEloAfter == 1220 && record.PlayerOEloAfter == 1180, $"Con K=40 provisional, el ganador sube +20 (1220) y el perdedor baja -20 (1180). Obtenido: {record.PlayerXEloAfter}/{record.PlayerOEloAfter}");
            Assert(playerX.Wins == 1 && playerO.Losses == 1, "Los perfiles deben registrar la victoria y derrota.");

            // Serializar y deserializar el MatchRecord en texto PGN
            string pgnText = record.SerializeToText();
            MatchRecord restoredRecord = MatchRecord.DeserializeFromText(pgnText);
            Assert(restoredRecord.PlayerXName == "Alice" && restoredRecord.PlayerOName == "Bob", "Nombres en PGN deben conservarse.");
            Assert(restoredRecord.PlayerXEloAfter == 1220 && restoredRecord.PlayerOEloAfter == 1180, "Transición ELO en PGN debe conservarse.");

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

            // 1. Humano (X) juega en (4, 4)
            Assert(session.TryPlayHumanMove(4, 4), "El movimiento humano inicial (4,4) debe ser aceptado.");
            Assert(session.IsCurrentTurnBot(), "Después del tiro humano debe ser turno del Bot O.");

            // 2. Bot (O) calcula y juega asíncronamente en hilo de fondo
            MinimaxSearchResult? botRes = session.PlayBotTurnAsync().GetAwaiter().GetResult();
            Assert(botRes.HasValue, "PlayBotTurnAsync debe retornar un resultado de búsqueda.");
            Assert(session.State.MoveCount == 2, "Deben existir 2 movimientos en el tablero (Humano + Bot).");
            Assert(!session.IsCurrentTurnBot(), "El turno debe regresar al humano (X).");

            // 3. Undo en modo Humano vs Bot debe deshacer los 2 medios turnos para regresar al turno humano
            Assert(session.Undo(), "Undo debe ejecutarse correctamente.");
            Assert(session.State.MoveCount == 0 && session.State.CurrentPlayer == Player.X, "Undo contra Bot debe regresar al turno de X.");
            Assert(session.Redo(), "Redo debe restaurar ambos movimientos.");
            Assert(session.State.MoveCount == 2, "Tras Redo deben existir 2 movimientos nuevamente.");
            Assert(stateChangedEvents >= 4, "El controlador debe emitir eventos StateChanged para actualizar la UI de Godot.");
        }

        private static void TestOnlineMatchmakingAndMutualConsentTakeback()
        {
            var matchmaking = new MatchmakingService();
            PlayerProfile alice = matchmaking.GetOrCreateAccount("Alice");
            PlayerProfile bob = matchmaking.GetOrCreateAccount("Bob");

            Assert(matchmaking.EnqueueOrMatchPlayer(alice.PlayerId) == null, "Alice queda en espera al ser la primera en la cola.");
            OnlineMatchRoom room = matchmaking.EnqueueOrMatchPlayer(bob.PlayerId);
            Assert(room != null, "Al entrar Bob con el mismo ELO (1200), se debe crear una sala OnlineMatchRoom.");

            string idX = room.PlayerX.PlayerId;
            string idO = room.PlayerO.PlayerId;

            // 1. Validar autoridad de turno (O no puede tirar en el turno de X)
            Assert(!room.SubmitMove(idO, new Move(4, 4), out _), "El servidor debe rechazar un tiro fuera de turno.");

            // 2. X juega (4, 4) y solicita deshacer su jugada (1 ply)
            Assert(room.SubmitMove(idX, new Move(4, 4), out _), "X juega (4, 4) legalmente.");
            Assert(room.State.MoveCount == 1, "Debe haber 1 movimiento en la sala.");

            Assert(room.RequestTakeback(idX, out TakebackRequest req1, out _), "X solicita Takeback.");
            Assert(req1.PliesToUndo == 1, "Como X acaba de tirar, el Takeback debe ser de 1 medio turno (1 ply).");

            // 3. X intenta aceptar su propia solicitud -> el servidor debe impedirlo
            Assert(!room.RespondToTakeback(idX, accept: true, out _), "El propio solicitante NO puede aceptar su propio Takeback.");
            Assert(room.State.MoveCount == 1, "El movimiento sigue en pie.");

            // 4. O rechaza la solicitud -> el tablero NO se deshace
            Assert(room.RespondToTakeback(idO, accept: false, out _), "O rechaza el Takeback.");
            Assert(room.State.MoveCount == 1 && !room.PendingTakeback.HasValue, "Tras el rechazo de O, el tablero conserva el movimiento.");

            // 5. X vuelve a solicitar Takeback y ahora O lo ACEPTA -> se deshace la jugada
            Assert(room.RequestTakeback(idX, out _, out _), "X vuelve a solicitar Takeback.");
            Assert(room.RespondToTakeback(idO, accept: true, out _), "O acepta el Takeback.");
            Assert(room.State.MoveCount == 0 && room.State.CurrentPlayer == Player.X, "Al aceptar O, la jugada se deshace y vuelve a ser turno de X.");

            // 6. Prueba de Takeback de 2 medios turnos (cuando solicita quien tiene el turno actual):
            Assert(room.SubmitMove(idX, new Move(4, 4), out _), "X juega (4, 4).");
            Assert(room.SubmitMove(idO, new Move(4, 0), out _), "O juega (4, 0).");
            Assert(room.State.MoveCount == 2 && room.State.CurrentPlayer == Player.X, "Turno de X con 2 movimientos.");

            Assert(room.RequestTakeback(idX, out TakebackRequest req2, out _), "X solicita regresar a su turno anterior (2 plies).");
            Assert(req2.PliesToUndo == 2, "Debe solicitar deshacer 2 medios turnos.");
            Assert(room.RespondToTakeback(idO, accept: true, out _), "O acepta deshacer los 2 medios turnos.");
            Assert(room.State.MoveCount == 0 && room.State.CurrentPlayer == Player.X, "El tablero regresa a 0 movimientos y turno de X.");

            // 7. Rendición (Resign) actualiza el ELO y registra el MatchRecord en el historial de ambos
            Assert(room.Resign(idO, out _), "O se rinde.");
            Assert(room.IsMatchOver && room.EffectiveWinner == Player.X && room.EndReason == MatchEndReason.Resignation, "X gana por rendición de O.");
            Assert(matchmaking.GetPlayerMatchHistory("Alice").Count == 1, "El historial de Alice debe contener la partida concluida.");
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
