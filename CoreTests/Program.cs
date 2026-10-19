using System;
using FractalTicTacToe.Core;

namespace CoreTests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("Ejecutando pruebas de dominio para FractalTicTacToe.Core...");

            TestInitialState();
            TestReciprocalRedirectionAndValidation();
            TestSuddenDeathVictory();
            TestFreeMoveOnSaturatedBoard();

            Console.WriteLine("¡Todas las pruebas de dominio pasaron exitosamente!");
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
            var state = new GameState();

            // Secuencia para que X gane con fila superior (0, 1, 2) en el Tablero 0:
            // 1. X: (0, 0) -> envía a O al Tablero 0
            Assert(state.MakeMove(new Move(0, 0)), "X (0,0)");
            // 2. O: (0, 3) -> envía a X al Tablero 3
            Assert(state.MakeMove(new Move(0, 3)), "O (0,3)");
            // 3. X: (3, 0) -> envía a O al Tablero 0
            Assert(state.MakeMove(new Move(3, 0)), "X (3,0)");
            // 4. O: (0, 4) -> envía a X al Tablero 4
            Assert(state.MakeMove(new Move(0, 4)), "O (0,4)");
            // 5. X: (4, 0) -> envía a O al Tablero 0
            Assert(state.MakeMove(new Move(4, 0)), "X (4,0)");
            // 6. O: (0, 6) -> envía a X al Tablero 6
            Assert(state.MakeMove(new Move(0, 6)), "O (0,6)");
            // 7. X: (6, 0) -> envía a O al Tablero 0
            Assert(state.MakeMove(new Move(6, 0)), "X (6,0)");
            // 8. O: (0, 7) -> envía a X al Tablero 7
            Assert(state.MakeMove(new Move(0, 7)), "O (0,7)");
            // 9. X: (7, 0) -> envía a O al Tablero 0
            Assert(state.MakeMove(new Move(7, 0)), "X (7,0)");

            // Espera: X necesita tirar en el Tablero 0 para completar (0,0), (0,1), (0,2).
            // Hagamos una secuencia directa enviando a X al tablero 0:
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
            // Llenar el Tablero 0 en empate local sin formar 3 en raya:
            // Índices del Tablero 0:
            // 0(X) 1(O) 2(X)
            // 3(X) 4(O) 5(O)
            // 6(O) 7(X) 8(X)
            // Celdas de X en Tablero 0: 0, 2, 3, 7, 8
            // Celdas de O en Tablero 0: 1, 4, 5, 6
            var state = new GameState();

            // 1. X juega (0, 0) -> O obligado a Tablero 0
            Assert(state.MakeMove(new Move(0, 0)), "1. X(0,0)");
            // 2. O juega (0, 1) -> X obligado a Tablero 1
            Assert(state.MakeMove(new Move(0, 1)), "2. O(0,1)");
            // 3. X juega (1, 0) -> O obligado a Tablero 0
            Assert(state.MakeMove(new Move(1, 0)), "3. X(1,0)");
            // 4. O juega (0, 4) -> X obligado a Tablero 4
            Assert(state.MakeMove(new Move(0, 4)), "4. O(0,4)");
            // 5. X juega (4, 0) -> O obligado a Tablero 0
            Assert(state.MakeMove(new Move(4, 0)), "5. X(4,0)");
            // 6. O juega (0, 5) -> X obligado a Tablero 5
            Assert(state.MakeMove(new Move(0, 5)), "6. O(0,5)");
            // 7. X juega (5, 0) -> O obligado a Tablero 0
            Assert(state.MakeMove(new Move(5, 0)), "7. X(5,0)");
            // 8. O juega (0, 6) -> X obligado a Tablero 6
            Assert(state.MakeMove(new Move(0, 6)), "8. O(0,6)");
            // 9. X juega (6, 0) -> O obligado a Tablero 0
            Assert(state.MakeMove(new Move(6, 0)), "9. X(6,0)");
            // Ahora O está en Tablero 0, pero queremos que X ponga en 2, 3, 7, 8 de Tablero 0.
            // O juega (0, 2) -> X va a 2 (entonces en Tablero 0 quedan libres 3, 7, 8)
            // Verifiquemos que con O en {1,2,4,5,6} no haya 3 en raya:
            // {1,4,7} no, {2,5,8} no, {2,4,6} ¡CUIDADO! 2-4-6 es diagonal!
            // Reiniciemos con una secuencia limpia donde nadie haga 3 en raya en Tablero 0:
            state = new GameState();

            // Queremos en Tablero 0:
            // 0:X  1:O  2:X
            // 3:X  4:O  5:O
            // 6:O  7:X  8:X
            // Movimientos dentro del Tablero 0 alternando entradas:
            // X entra a 0 y juega (0,1)? No, 1 es de O.
            // X inicia con tiro libre en (0, 2) -> O va a 2
            Assert(state.MakeMove(new Move(0, 2)), "X(0,2)");
            Assert(state.MakeMove(new Move(2, 0)), "O(2,0) -> envía a X a 0");
            Assert(state.MakeMove(new Move(0, 3)), "X(0,3) -> envía a O a 3");
            Assert(state.MakeMove(new Move(3, 0)), "O(3,0) -> envía a X a 0");
            Assert(state.MakeMove(new Move(0, 7)), "X(0,7) -> envía a O a 7");
            Assert(state.MakeMove(new Move(7, 0)), "O(7,0) -> envía a X a 0");
            Assert(state.MakeMove(new Move(0, 8)), "X(0,8) -> envía a O a 8");
            Assert(state.MakeMove(new Move(8, 0)), "O(8,0) -> envía a X a 0");
            // Ahora X tiene {2, 3, 7, 8} en Tablero 0 (ningún 3 en raya).
            // X juega (0, 0) -> ¡X tiene {0, 2, 3, 7, 8} (ningún 3 en raya) y envía a O al Tablero 0!
            Assert(state.MakeMove(new Move(0, 0)), "X(0,0) -> envía a O a 0");
            // Ahora O llena las 4 casillas restantes del Tablero 0: {1, 4, 5, 6}
            Assert(state.MakeMove(new Move(0, 1)), "O(0,1) -> envía a X a 1");
            Assert(state.MakeMove(new Move(1, 0)), "X(1,0) -> envía a O a 0");
            Assert(state.MakeMove(new Move(0, 4)), "O(0,4) -> envía a X a 4");
            Assert(state.MakeMove(new Move(4, 0)), "X(4,0) -> envía a O a 0");
            Assert(state.MakeMove(new Move(0, 5)), "O(0,5) -> envía a X a 5");
            Assert(state.MakeMove(new Move(5, 0)), "X(5,0) -> envía a O a 0");
            // Última casilla del Tablero 0 es la 6:
            Assert(state.MakeMove(new Move(0, 6)), "O(0,6) -> llena Tablero 0 y envía a X a 6");

            Assert(!state.IsGameOver, "El juego no debe haber terminado (Tablero 0 está lleno en empate local).");
            Assert(state.ActiveBoardIndex == 6, "X debe estar en el Tablero 6.");

            // Ahora X juega en (6, 0), lo cual redirige a O al Tablero 0 (que ya tiene las 9 casillas llenas)
            Assert(state.MakeMove(new Move(6, 0)), "X(6,0) apunta al Tablero 0 saturado.");
            Assert(state.ActiveBoardIndex == -1, "Como el Tablero 0 está saturado, O debe recibir TIRO LIBRE (-1).");
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
