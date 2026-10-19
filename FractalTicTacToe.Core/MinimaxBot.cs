using System;
using System.Collections.Generic;

namespace FractalTicTacToe.Core
{
#if NET6_0_OR_GREATER
    /// <summary>
    /// Resultado detallado de una búsqueda Minimax con poda Alfa-Beta.
    /// </summary>
    public readonly record struct MinimaxSearchResult(
        Move BestMove,
        int Score,
        int NodesEvaluated,
        int MaxDepthReached);
#else
    /// <summary>
    /// Resultado detallado de una búsqueda Minimax con poda Alfa-Beta.
    /// </summary>
    public readonly struct MinimaxSearchResult : IEquatable<MinimaxSearchResult>
    {
        public Move BestMove { get; }
        public int Score { get; }
        public int NodesEvaluated { get; }
        public int MaxDepthReached { get; }

        public MinimaxSearchResult(Move bestMove, int score, int nodesEvaluated, int maxDepthReached)
        {
            BestMove = bestMove;
            Score = score;
            NodesEvaluated = nodesEvaluated;
            MaxDepthReached = maxDepthReached;
        }

        public bool Equals(MinimaxSearchResult other) =>
            BestMove.Equals(other.BestMove) &&
            Score == other.Score &&
            NodesEvaluated == other.NodesEvaluated &&
            MaxDepthReached == other.MaxDepthReached;

        public override bool Equals(object obj) => obj is MinimaxSearchResult other && Equals(other);

        public override int GetHashCode() =>
            (((BestMove.GetHashCode() * 397) ^ Score) * 397 ^ NodesEvaluated) * 397 ^ MaxDepthReached;

        public override string ToString() =>
            $"Move: {BestMove}, Score: {Score}, Nodes: {NodesEvaluated}, Depth: {MaxDepthReached}";
    }
#endif

    /// <summary>
    /// Agente de Inteligencia Artificial basado en Minimax con poda Alfa-Beta,
    /// ordenamiento heurístico de movimientos sin asignaciones y evaluación táctica para Muerte Súbita.
    /// </summary>
    public sealed class MinimaxBot
    {
        public const int WinScore = 100_000;
        private const int ImmediateThreatBonus = 45_000;
        private const int TwoInARowWeight = 400;
        private const int OneInARowWeight = 30;
        private const int UnsafeRedirectionPenalty = 250;

        // Pesos posicionales de cada celda (0..8) según el número de líneas ganadoras en las que participa:
        // Centro (4 líneas) = 16, Esquinas (3 líneas) = 9, Bordes (2 líneas) = 4
        private static readonly int[] CellPositionalWeights =
        {
            9,  4,  9,
            4, 16,  4,
            9,  4,  9
        };

        private static readonly int[] WinLines =
        {
            0, 1, 2,
            3, 4, 5,
            6, 7, 8,
            0, 3, 6,
            1, 4, 7,
            2, 5, 8,
            0, 4, 8,
            2, 4, 6
        };

        // Buffers reutilizables para evitar presión sobre el Garbage Collector en los nodos hoja
        private readonly int[] _botThreatsBuffer = new int[GameState.BoardCount];
        private readonly int[] _oppThreatsBuffer = new int[GameState.BoardCount];

        public int MaxDepth { get; }
        public int LastNodesEvaluated { get; private set; }
        public int LastSearchScore { get; private set; }

        public MinimaxBot(int maxDepth = 6)
        {
            if (maxDepth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth), "La profundidad máxima debe ser al menos 1.");
            }

            MaxDepth = maxDepth;
        }

        /// <summary>
        /// Calcula el mejor movimiento legal para el jugador activo en el estado proporcionado.
        /// </summary>
        public Move GetBestMove(GameState state)
        {
            return FindBestMove(state).BestMove;
        }

        /// <summary>
        /// Ejecuta la búsqueda Minimax con poda Alfa-Beta y retorna métricas detalladas.
        /// </summary>
        public MinimaxSearchResult FindBestMove(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.IsGameOver)
            {
                throw new InvalidOperationException("No se puede calcular un movimiento en una partida terminada.");
            }

            List<Move> legalMoves = state.GetLegalMoves();
            if (legalMoves.Count == 0)
            {
                throw new InvalidOperationException("No existen movimientos legales disponibles.");
            }

            Player botPlayer = state.CurrentPlayer;
            LastNodesEvaluated = 0;

            // Si es la apertura con tiro libre (81 opciones), limitamos la profundidad inicial a 4
            // para mantener tiempos de respuesta instantáneos (< 50 ms) mientras priorizamos el centro.
            int effectiveDepth = state.MoveCount == 0 ? Math.Min(MaxDepth, 4) : MaxDepth;

            // 1. Atajo de victoria inmediata en 1 turno (Muerte Súbita)
            for (int i = 0; i < legalMoves.Count; i++)
            {
                Move candidate = legalMoves[i];
                if (WouldMoveWinImmediately(state, candidate, botPlayer))
                {
                    LastNodesEvaluated = 1;
                    LastSearchScore = WinScore + (effectiveDepth * 100);
                    return new MinimaxSearchResult(candidate, LastSearchScore, 1, 1);
                }
            }

            // 2. Ordenar movimientos en la raíz para maximizar podas Alfa-Beta
            OrderMovesInPlace(state, legalMoves, botPlayer);

            Move bestMove = legalMoves[0];
            int bestScore = int.MinValue;
            int alpha = int.MinValue + 1;
            int beta = int.MaxValue;

            for (int i = 0; i < legalMoves.Count; i++)
            {
                Move move = legalMoves[i];
                GameState nextState = state.Clone();
                nextState.MakeMove(move);

                int score = Minimax(
                    nextState,
                    effectiveDepth - 1,
                    alpha,
                    beta,
                    isMaximizing: false,
                    botPlayer: botPlayer);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }

                if (score > alpha)
                {
                    alpha = score;
                }
            }

            LastSearchScore = bestScore;
            return new MinimaxSearchResult(bestMove, bestScore, LastNodesEvaluated, effectiveDepth);
        }

        private int Minimax(
            GameState state,
            int depth,
            int alpha,
            int beta,
            bool isMaximizing,
            Player botPlayer)
        {
            LastNodesEvaluated++;

            // Caso base 1: Estado terminal (Victoria por muerte súbita o empate global)
            if (state.IsGameOver)
            {
                if (state.Winner == Player.None)
                {
                    return 0;
                }

                // Preferimos victorias más rápidas (+depth) y derrotas más lejanas (-depth)
                return state.Winner == botPlayer
                    ? WinScore + (depth * 100)
                    : -WinScore - (depth * 100);
            }

            // Caso base 2: Límite de profundidad alcanzado -> Evaluación heurística estática
            if (depth <= 0)
            {
                return EvaluateStateInternal(state, botPlayer, _botThreatsBuffer, _oppThreatsBuffer);
            }

            List<Move> legalMoves = state.GetLegalMoves();

            // Ordenamos los movimientos en nodos internos para maximizar los cortes Alfa-Beta
            if (depth >= 2 && legalMoves.Count > 1)
            {
                OrderMovesInPlace(state, legalMoves, state.CurrentPlayer);
            }

            if (isMaximizing)
            {
                int maxEval = int.MinValue;

                for (int i = 0; i < legalMoves.Count; i++)
                {
                    GameState child = state.Clone();
                    child.MakeMove(legalMoves[i]);

                    int eval = Minimax(child, depth - 1, alpha, beta, isMaximizing: false, botPlayer);
                    if (eval > maxEval)
                    {
                        maxEval = eval;
                    }

                    if (eval > alpha)
                    {
                        alpha = eval;
                    }

                    if (beta <= alpha)
                    {
                        break; // Poda Beta
                    }
                }

                return maxEval;
            }
            else
            {
                int minEval = int.MaxValue;

                for (int i = 0; i < legalMoves.Count; i++)
                {
                    GameState child = state.Clone();
                    child.MakeMove(legalMoves[i]);

                    int eval = Minimax(child, depth - 1, alpha, beta, isMaximizing: true, botPlayer);
                    if (eval < minEval)
                    {
                        minEval = eval;
                    }

                    if (eval < beta)
                    {
                        beta = eval;
                    }

                    if (beta <= alpha)
                    {
                        break; // Poda Alfa
                    }
                }

                return minEval;
            }
        }

        /// <summary>
        /// Evalúa heurísticamente un estado no terminal desde la perspectiva de <paramref name="botPlayer"/>.
        /// </summary>
        public static int EvaluateState(GameState state, Player botPlayer)
        {
            int[] botThreats = new int[GameState.BoardCount];
            int[] oppThreats = new int[GameState.BoardCount];
            return EvaluateStateInternal(state, botPlayer, botThreats, oppThreats);
        }

        private static int EvaluateStateInternal(
            GameState state,
            Player botPlayer,
            int[] botThreatsPerBoard,
            int[] oppThreatsPerBoard)
        {
            Player opponent = GetOpponent(botPlayer);
            int totalScore = 0;
            int totalBotThreats = 0;
            int totalOppThreats = 0;

            Array.Clear(botThreatsPerBoard, 0, GameState.BoardCount);
            Array.Clear(oppThreatsPerBoard, 0, GameState.BoardCount);

            for (int board = 0; board < GameState.BoardCount; board++)
            {
                // 1. Control posicional de celdas individuales (Centro > Esquinas > Bordes)
                for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                {
                    Player occupant = state.GetCell(board, cell);
                    if (occupant == botPlayer)
                    {
                        totalScore += CellPositionalWeights[cell];
                    }
                    else if (occupant == opponent)
                    {
                        totalScore -= CellPositionalWeights[cell];
                    }
                }

                // 2. Evaluación de las 8 líneas ganadoras de este sub-tablero
                for (int lineIdx = 0; lineIdx < WinLines.Length; lineIdx += 3)
                {
                    Player c0 = state.GetCell(board, WinLines[lineIdx]);
                    Player c1 = state.GetCell(board, WinLines[lineIdx + 1]);
                    Player c2 = state.GetCell(board, WinLines[lineIdx + 2]);

                    int botCount = (c0 == botPlayer ? 1 : 0) + (c1 == botPlayer ? 1 : 0) + (c2 == botPlayer ? 1 : 0);
                    int oppCount = (c0 == opponent ? 1 : 0) + (c1 == opponent ? 1 : 0) + (c2 == opponent ? 1 : 0);

                    // Si ambos jugadores están en la misma línea, está bloqueada (0 puntos)
                    if (botCount > 0 && oppCount > 0)
                    {
                        continue;
                    }

                    if (botCount == 2)
                    {
                        totalScore += TwoInARowWeight;
                        botThreatsPerBoard[board]++;
                        totalBotThreats++;
                    }
                    else if (botCount == 1)
                    {
                        totalScore += OneInARowWeight;
                    }
                    else if (oppCount == 2)
                    {
                        totalScore -= TwoInARowWeight;
                        oppThreatsPerBoard[board]++;
                        totalOppThreats++;
                    }
                    else if (oppCount == 1)
                    {
                        totalScore -= OneInARowWeight;
                    }
                }
            }

            // 3. Evaluación táctica del jugador con el turno actual (Tempo de Muerte Súbita)
            int activeBoard = state.ActiveBoardIndex;
            if (state.CurrentPlayer == botPlayer)
            {
                bool canWinNow = activeBoard == GameState.FreeMoveBoardIndex
                    ? totalBotThreats > 0
                    : botThreatsPerBoard[activeBoard] > 0;

                if (canWinNow)
                {
                    return ImmediateThreatBonus;
                }

                // Penalizar si desde el tablero activo muchas casillas redirigen a tableros minados por el rival
                if (activeBoard != GameState.FreeMoveBoardIndex)
                {
                    for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                    {
                        if (state.GetCell(activeBoard, cell) == Player.None && oppThreatsPerBoard[cell] > 0)
                        {
                            totalScore -= UnsafeRedirectionPenalty;
                        }
                    }
                }
            }
            else
            {
                bool opponentCanWinNow = activeBoard == GameState.FreeMoveBoardIndex
                    ? totalOppThreats > 0
                    : oppThreatsPerBoard[activeBoard] > 0;

                if (opponentCanWinNow)
                {
                    return -ImmediateThreatBonus;
                }

                if (activeBoard != GameState.FreeMoveBoardIndex)
                {
                    for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                    {
                        if (state.GetCell(activeBoard, cell) == Player.None && botThreatsPerBoard[cell] > 0)
                        {
                            totalScore += UnsafeRedirectionPenalty;
                        }
                    }
                }
            }

            return totalScore;
        }

        /// <summary>
        /// Ordena los movimientos candidatos in-place de mayor a menor prioridad táctica
        /// usando Insertion Sort para evitar asignaciones de delegados en el árbol de búsqueda.
        /// </summary>
        private static void OrderMovesInPlace(GameState state, List<Move> moves, Player currentPlayer)
        {
            int count = moves.Count;
            if (count <= 1)
            {
                return;
            }

            Player opponent = GetOpponent(currentPlayer);
            int[] scores = new int[count];

            for (int i = 0; i < count; i++)
            {
                scores[i] = ScoreMoveForOrdering(state, moves[i], currentPlayer, opponent);
            }

            for (int i = 1; i < count; i++)
            {
                Move currentMove = moves[i];
                int currentScore = scores[i];
                int j = i - 1;

                while (j >= 0 && scores[j] < currentScore)
                {
                    moves[j + 1] = moves[j];
                    scores[j + 1] = scores[j];
                    j--;
                }

                moves[j + 1] = currentMove;
                scores[j + 1] = currentScore;
            }
        }

        private static int ScoreMoveForOrdering(GameState state, Move move, Player currentPlayer, Player opponent)
        {
            // 1. Si el movimiento gana inmediatamente la partida, prioridad máxima absoluta
            if (WouldMoveWinImmediately(state, move, currentPlayer))
            {
                return 1_000_000;
            }

            // 2. Prioridad por peso posicional tanto de la celda como del tablero
            int score = (CellPositionalWeights[move.CellIndex] * 10) + CellPositionalWeights[move.BoardIndex];

            // 3. Penalizar fuertemente enviar al rival a un tablero donde el rival ya tiene 2 en raya
            int targetBoard = move.CellIndex;
            if (DoesBoardHaveImmediateWin(state, targetBoard, opponent))
            {
                score -= 50_000;
            }

            // 4. Bonificar si el tiro bloquea un 2 en raya del rival en el tablero actual
            if (WouldMoveWinImmediately(state, move, opponent))
            {
                score += 2_000;
            }

            return score;
        }

        /// <summary>
        /// Determina si colocar una ficha de <paramref name="player"/> en <paramref name="move"/>
        /// completaría un 3 en raya en ese sub-tablero.
        /// </summary>
        private static bool WouldMoveWinImmediately(GameState state, Move move, Player player)
        {
            int board = move.BoardIndex;
            int targetCell = move.CellIndex;

            for (int i = 0; i < WinLines.Length; i += 3)
            {
                int c0 = WinLines[i];
                int c1 = WinLines[i + 1];
                int c2 = WinLines[i + 2];

                if (c0 != targetCell && c1 != targetCell && c2 != targetCell)
                {
                    continue;
                }

                Player p0 = c0 == targetCell ? player : state.GetCell(board, c0);
                Player p1 = c1 == targetCell ? player : state.GetCell(board, c1);
                Player p2 = c2 == targetCell ? player : state.GetCell(board, c2);

                if (p0 == player && p1 == player && p2 == player)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Verifica si <paramref name="player"/> ya tiene alguna línea con 2 fichas propias y 1 casilla vacía
        /// dentro del sub-tablero <paramref name="boardIndex"/>.
        /// </summary>
        private static bool DoesBoardHaveImmediateWin(GameState state, int boardIndex, Player player)
        {
            for (int i = 0; i < WinLines.Length; i += 3)
            {
                Player p0 = state.GetCell(boardIndex, WinLines[i]);
                Player p1 = state.GetCell(boardIndex, WinLines[i + 1]);
                Player p2 = state.GetCell(boardIndex, WinLines[i + 2]);

                int playerCount = (p0 == player ? 1 : 0) + (p1 == player ? 1 : 0) + (p2 == player ? 1 : 0);
                int emptyCount = (p0 == Player.None ? 1 : 0) + (p1 == Player.None ? 1 : 0) + (p2 == Player.None ? 1 : 0);

                if (playerCount == 2 && emptyCount == 1)
                {
                    return true;
                }
            }

            return false;
        }

        private static Player GetOpponent(Player player) => player == Player.X ? Player.O : Player.X;
    }
}
