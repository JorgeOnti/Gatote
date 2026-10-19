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
            TestMinimaxImmediateSuddenDeathWin();
            TestMinimaxAvoidsSendingOpponentToLandmineBoard();
            TestTranspositionTableHitAndSpeedup();
            TestIterativeDeepeningTimeLimitAndDifficulties();

            Console.WriteLine("¡Todas las pruebas de dominio, Zobrist Hashing, Tabla de Transposición e Iterative Deepening pasaron exitosamente!");
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
            // Verificar que dos órdenes de movimientos distintos que llegan a la misma posición
            // producen exactamente el mismo ZobristKey incremental y coinciden con ComputeFullHash:
            // Ruta A: X(4, 1) -> O(1, 2) -> X(2, 3) -> O(3, 4)
            var pathA = new GameState();
            Assert(pathA.MakeMove(new Move(4, 1)), "A1");
            Assert(pathA.ZobristKey == ZobristHasher.ComputeFullHash(pathA), "Hash A1");
            Assert(pathA.MakeMove(new Move(1, 2)), "A2");
            Assert(pathA.ZobristKey == ZobristHasher.ComputeFullHash(pathA), "Hash A2");
            Assert(pathA.MakeMove(new Move(2, 3)), "A3");
            Assert(pathA.ZobristKey == ZobristHasher.ComputeFullHash(pathA), "Hash A3");
            Assert(pathA.MakeMove(new Move(3, 4)), "A4");
            Assert(pathA.ZobristKey == ZobristHasher.ComputeFullHash(pathA), "Hash A4");

            // Ruta B (Transposición): X(2, 3) -> O(3, 4) -> X(4, 1) -> O(1, 2)... pero para que ActiveBoardIndex final sea 4:
            // Necesitamos que las mismas fichas estén en: X en (4,1) y (2,3); O en (1,2) y (3,4), turno de X y ActiveBoardIndex = 4:
            // Ruta B: X inicia con tiro libre en (2, 1) -> no, X necesita (4,1) y (2,3).
            // Si X inicia en (4, 1) -> O va a 1.
            // Probemos una transposición de 5 movimientos donde X tiene {(0,1), (2,3)}, O tiene {(1,2), (3,0)}, y X juega (0,4):
            // Secuencia 1: X(0,1) -> O(1,2) -> X(2,3) -> O(3,0) -> X(0,4)
            var seq1 = new GameState();
            seq1.MakeMove(new Move(0, 1));
            seq1.MakeMove(new Move(1, 2));
            seq1.MakeMove(new Move(2, 3));
            seq1.MakeMove(new Move(3, 0));
            seq1.MakeMove(new Move(0, 4));

            // Secuencia 2: X(2,3) -> O(3,0) -> X(0,1) -> O(1,2) -> no, desde 2 sería X(2,4).
            // Para terminar en ActiveBoardIndex = 4 con las mismas fichas:
            // En Secuencia 1: X tiene {(0,1), (2,3), (0,4)}, O tiene {(1,2), (3,0)}, ActiveBoard = 4, Turno = O.
            // En Secuencia 2: X(0,4) -> O(4,2)? No, O necesita (3,0) y (1,2):
            // X(2,3) -> O(3,0) -> X(0,1) -> O(1,0)? Si O tiene (1,0) y (3,0):
            // Comparemos:
            // S1: X(0,1) -> O(1,3) -> X(3,2) -> O(2,0) -> X(0,4)  (X: (0,1),(3,2),(0,4) | O: (1,3),(2,0) | Active: 4)
            // S2: X(3,2) -> O(2,0) -> X(0,1) -> O(1,3) -> ¡Pero en S2 tras O(1,3) X está en 3, no en 0!
            // Para que el último tiro de X sea en el mismo tablero (0,4), el penúltimo tiro de O debe enviar a X a 0:
            // S1: X(0,1) -> O(1,2) -> X(2,3) -> O(3,0) -> X(0,4)
            // S2: X(0,3)? No, las fichas de X deben ser idénticas: {(0,1), (2,3), (0,4)}.
            // ¿Cómo puede X empezar en (0,4) o terminar en (0,4)?
            // ¡Con un ciclo en otro tablero!
            // S1: X(0,1) -> O(1,2) -> X(2,1) -> O(1,0) -> X(0,4)
            //     Fichas X: (0,1), (2,1), (0,4) | Fichas O: (1,2), (1,0) | ActiveBoard: 4, Turno: O
            // S2: X(2,1) -> O(1,0) -> X(0,1) -> O(1,2) -> ¡X está en 2, no en 0!
            // Para que ambos terminen con el mismo último movimiento (X en (0,4)), el ciclo intermedio en el Tablero 0 se puede permutar:
            // T1: X(0,1) -> O(1,0) -> X(0,2) -> O(2,0) -> X(0,4)
            //     Fichas X: (0,1), (0,2), (0,4) | Fichas O: (1,0), (2,0) | ActiveBoard: 4, Turno: O
            // T2: X(0,2) -> O(2,0) -> X(0,1) -> O(1,0) -> X(0,4)
            //     Fichas X: (0,2), (0,1), (0,4) | Fichas O: (2,0), (1,0) | ActiveBoard: 4, Turno: O
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
            state.MakeMove(new Move(0, 1)); // X en (0,1) -> O va a 1
            state.MakeMove(new Move(1, 0)); // O en (1,0) -> X va a 0
            state.MakeMove(new Move(0, 2)); // X en (0,2) -> O va a 2 (Tablero 0 queda minado con 2 en raya de X)

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

            // Segunda consulta sobre el mismo estado: debe aprovechar la tabla de transposición aún más rápido
            MinimaxSearchResult cachedRes = botWithTT.FindBestMove(state);
            Assert(cachedRes.BestMove == resTT.BestMove, "La jugada óptima con TT caliente debe ser consistente.");
            Assert(cachedRes.TranspositionCutoffs > 0, "Debe producir podas directas desde la Tabla de Transposición.");
        }

        private static void TestIterativeDeepeningTimeLimitAndDifficulties()
        {
            var state = new GameState();
            state.MakeMove(new Move(4, 4));

            // Probar límite de tiempo estricto (25 ms) con profundidad alta (9)
            var fastBot = new MinimaxBot(maxDepth: 9, timeLimitMs: 25, useTranspositionTable: true);
            MinimaxSearchResult result = fastBot.FindBestMove(state);

            Assert(result.MaxDepthReached >= 1, "Iterative Deepening siempre debe completar al menos profundidad 1.");
            Assert(state.IsValidMove(result.BestMove), "El movimiento retornado bajo límite de tiempo debe ser legal.");

            // Probar todos los niveles de dificultad preconfigurados
            foreach (BotDifficulty diff in new[] { BotDifficulty.Easy, BotDifficulty.Medium, BotDifficulty.Hard })
            {
                var presetBot = new MinimaxBot(diff);
                Move move = presetBot.GetBestMove(state);
                Assert(state.IsValidMove(move), $"El nivel de dificultad {diff} debe retornar un movimiento válido.");
            }
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
