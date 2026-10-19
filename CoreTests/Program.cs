using System;
using FractalTicTacToe.Core;

namespace CoreTests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("Ejecutando pruebas de dominio y Minimax para FractalTicTacToe.Core...");

            TestInitialState();
            TestReciprocalRedirectionAndValidation();
            TestSuddenDeathVictory();
            TestFreeMoveOnSaturatedBoard();
            TestMinimaxImmediateSuddenDeathWin();
            TestMinimaxAvoidsSendingOpponentToLandmineBoard();

            Console.WriteLine("¡Todas las pruebas de dominio y de IA Minimax pasaron exitosamente!");
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
        }

        private static void TestReciprocalRedirectionAndValidation()
        {
            var state = new GameState();

            // X tira en Tablero 4, Celda 2 -> O debe ser enviado al Tablero 2
            Assert(state.MakeMove(new Move(4, 2)), "El primer tiro libre (4, 2) debe ser válido.");
            Assert(state.CurrentPlayer == Player.O, "El turno debe cambiar a O.");
            Assert(state.ActiveBoardIndex == 2, "ActiveBoardIndex debe ser 2.");
            Assert(state.GetLegalMoves().Count == 9, "O debe tener 9 opciones en el Tablero 2.");

            // O intenta tirar en un tablero distinto al 2 -> inválido
            Assert(!state.MakeMove(new Move(0, 0)), "No se debe permitir tirar fuera del tablero activo.");

            // O tira en Tablero 2, Celda 4 -> X es enviado al Tablero 4 (que ya tiene 1 ficha en celda 2)
            Assert(state.MakeMove(new Move(2, 4)), "El tiro en el tablero activo (2, 4) debe ser válido.");
            Assert(state.CurrentPlayer == Player.X, "El turno debe volver a X.");
            Assert(state.ActiveBoardIndex == 4, "ActiveBoardIndex debe ser 4.");
            Assert(state.GetLegalMoves().Count == 8, "El Tablero 4 debe tener 8 celdas disponibles.");

            // X intenta tirar en la celda ocupada (4, 2) -> inválido
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

            // Llenar el Tablero 0 en empate local sin formar 3 en raya:
            // 0:X  1:O  2:X
            // 3:X  4:O  5:O
            // 6:O  7:X  8:X
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

            // X juega en (6, 0), que apunta al Tablero 0 ya saturado -> O recibe Tiro Libre (-1)
            Assert(state.MakeMove(new Move(6, 0)), "X(6,0) apunta al Tablero 0 saturado.");
            Assert(state.ActiveBoardIndex == -1, "Como el Tablero 0 está saturado, O debe recibir TIRO LIBRE (-1).");
        }

        private static void TestMinimaxImmediateSuddenDeathWin()
        {
            var state = new GameState();
            // X tiene (0,1) y (0,2), y es turno de X en el Tablero 0
            state.MakeMove(new Move(0, 1));
            state.MakeMove(new Move(1, 0));
            state.MakeMove(new Move(0, 2));
            state.MakeMove(new Move(2, 0));

            var bot = new MinimaxBot(maxDepth: 5);
            Move bestMove = bot.GetBestMove(state);

            Assert(bestMove == new Move(0, 0), $"El bot debe elegir el tiro ganador inmediato (0, 0), pero eligió {bestMove}.");
        }

        private static void TestMinimaxAvoidsSendingOpponentToLandmineBoard()
        {
            var state = new GameState();
            // X crea una amenaza mortal (2 en raya: celdas 1 y 2) en el Tablero 0:
            state.MakeMove(new Move(0, 1)); // X en (0,1) -> O va a 1
            state.MakeMove(new Move(1, 0)); // O en (1,0) -> X va a 0
            state.MakeMove(new Move(0, 2)); // X en (0,2) -> O va a 2 (Ahora el Tablero 0 es una mina para O: si X vuelve al 0, X gana en (0,0))

            // Es turno de O en el Tablero 2. Si O juega en la celda 0 (Move(2, 0)), envía a X al Tablero 0 y pierde en el acto.
            var bot = new MinimaxBot(maxDepth: 5);
            Move bestMove = bot.GetBestMove(state);

            Assert(bestMove.BoardIndex == 2, "El bot O debe jugar en el Tablero 2 activo.");
            Assert(bestMove.CellIndex != 0, $"El bot O NO debe jugar en la celda 0 porque enviaría a X al Tablero 0 minado; eligió {bestMove}.");
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
