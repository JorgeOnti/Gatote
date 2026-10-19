using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FractalTicTacToe.Core
{
#if NET6_0_OR_GREATER
    /// <summary>
    /// Resultado detallado de una búsqueda Minimax con Iterative Deepening y Tabla de Transposición.
    /// </summary>
    public readonly record struct MinimaxSearchResult(
        Move BestMove,
        int Score,
        int NodesEvaluated,
        int MaxDepthReached,
        int TranspositionHits = 0,
        int TranspositionCutoffs = 0,
        long ElapsedMs = 0);
#else
    /// <summary>
    /// Resultado detallado de una búsqueda Minimax con Iterative Deepening y Tabla de Transposición.
    /// </summary>
    public readonly struct MinimaxSearchResult : IEquatable<MinimaxSearchResult>
    {
        public Move BestMove { get; }
        public int Score { get; }
        public int NodesEvaluated { get; }
        public int MaxDepthReached { get; }
        public int TranspositionHits { get; }
        public int TranspositionCutoffs { get; }
        public long ElapsedMs { get; }

        public MinimaxSearchResult(
            Move bestMove,
            int score,
            int nodesEvaluated,
            int maxDepthReached,
            int transpositionHits = 0,
            int transpositionCutoffs = 0,
            long elapsedMs = 0)
        {
            BestMove = bestMove;
            Score = score;
            NodesEvaluated = nodesEvaluated;
            MaxDepthReached = maxDepthReached;
            TranspositionHits = transpositionHits;
            TranspositionCutoffs = transpositionCutoffs;
            ElapsedMs = elapsedMs;
        }

        public bool Equals(MinimaxSearchResult other) =>
            BestMove.Equals(other.BestMove) &&
            Score == other.Score &&
            NodesEvaluated == other.NodesEvaluated &&
            MaxDepthReached == other.MaxDepthReached &&
            TranspositionHits == other.TranspositionHits &&
            TranspositionCutoffs == other.TranspositionCutoffs &&
            ElapsedMs == other.ElapsedMs;

        public override bool Equals(object obj) => obj is MinimaxSearchResult other && Equals(other);

        public override int GetHashCode() =>
            (((BestMove.GetHashCode() * 397) ^ Score) * 397 ^ NodesEvaluated) * 397 ^ MaxDepthReached;

        public override string ToString() =>
            $"Move: {BestMove}, Score: {Score}, Depth: {MaxDepthReached}, Nodes: {NodesEvaluated}, TT Hits: {TranspositionHits}, Time: {ElapsedMs}ms";
    }
#endif

    /// <summary>
    /// Agente de Inteligencia Artificial basado en Negamax/Minimax con poda Alfa-Beta,
    /// Make/Unmake en O(1), Tabla de Transposición Zobrist, Iterative Deepening y niveles de dificultad.
    /// </summary>
    public sealed class MinimaxBot
    {
        public const int WinScore = 100_000;
        private const int ImmediateThreatBonus = 45_000;
        private const int TwoInARowWeight = 400;
        private const int OneInARowWeight = 30;
        private const int UnsafeRedirectionPenalty = 250;
        private const int TimeCheckIntervalMask = 255; // Verificar reloj cada 256 nodos

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

        private readonly TranspositionTable _transpositionTable;
        private readonly int[] _botThreatsBuffer = new int[GameState.BoardCount];
        private readonly int[] _oppThreatsBuffer = new int[GameState.BoardCount];
        private readonly Stopwatch _searchTimer = new Stopwatch();

        private bool _searchAborted;
        private bool _enforceTimeLimitInCurrentIteration;

        public BotDifficulty Difficulty { get; }
        public int MaxDepth { get; }
        public int TimeLimitMs { get; }
        public bool UseTranspositionTable { get; }

        public int LastNodesEvaluated { get; private set; }
        public int LastSearchScore { get; private set; }
        public int LastDepthReached { get; private set; }
        public int LastTranspositionHits => _transpositionTable != null ? _transpositionTable.Hits : 0;
        public int LastTranspositionCutoffs => _transpositionTable != null ? _transpositionTable.Cutoffs : 0;

        /// <summary>
        /// Crea una instancia del bot a partir de un nivel de dificultad preconfigurado.
        /// </summary>
        public MinimaxBot(BotDifficulty difficulty)
        {
            Difficulty = difficulty;

            switch (difficulty)
            {
                case BotDifficulty.Easy:
                    MaxDepth = 2;
                    TimeLimitMs = 100;
                    UseTranspositionTable = false;
                    break;

                case BotDifficulty.Medium:
                    MaxDepth = 4;
                    TimeLimitMs = 350;
                    UseTranspositionTable = true;
                    break;

                case BotDifficulty.Hard:
                    MaxDepth = 6;
                    TimeLimitMs = 1000;
                    UseTranspositionTable = true;
                    break;

                case BotDifficulty.Master:
                    MaxDepth = 9;
                    TimeLimitMs = 2500;
                    UseTranspositionTable = true;
                    break;

                default:
                    MaxDepth = 6;
                    TimeLimitMs = 1000;
                    UseTranspositionTable = true;
                    break;
            }

            if (UseTranspositionTable)
            {
                _transpositionTable = new TranspositionTable();
            }
        }

        /// <summary>
        /// Crea una instancia personalizada del bot indicando profundidad máxima, límite de tiempo y uso de tabla Zobrist.
        /// </summary>
        public MinimaxBot(int maxDepth = 6, int timeLimitMs = 1500, bool useTranspositionTable = true)
        {
            if (maxDepth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth), "La profundidad máxima debe ser al menos 1.");
            }

            if (timeLimitMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeLimitMs), "El límite de tiempo no puede ser negativo.");
            }

            Difficulty = BotDifficulty.Custom;
            MaxDepth = maxDepth;
            TimeLimitMs = timeLimitMs;
            UseTranspositionTable = useTranspositionTable;

            if (UseTranspositionTable)
            {
                _transpositionTable = new TranspositionTable();
            }
        }

        /// <summary>
        /// Limpia por completo las entradas almacenadas en la tabla de transposición.
        /// </summary>
        public void ClearTranspositionTable()
        {
            _transpositionTable?.Clear();
        }

        /// <summary>
        /// Calcula el mejor movimiento legal para el jugador activo en el estado proporcionado.
        /// </summary>
        public Move GetBestMove(GameState state)
        {
            return FindBestMove(state).BestMove;
        }

        /// <summary>
        /// Ejecuta búsqueda con Iterative Deepening, poda Alfa-Beta, Make/Unmake in-place y Tabla Zobrist.
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

            Player rootPlayer = state.CurrentPlayer;
            LastNodesEvaluated = 0;
            _transpositionTable?.ResetMetrics();
            _searchTimer.Restart();

            int targetMaxDepth = state.MoveCount == 0 ? Math.Min(MaxDepth, 5) : MaxDepth;

            // 1. Atajo de victoria inmediata en 1 turno (Muerte Súbita)
            for (int i = 0; i < legalMoves.Count; i++)
            {
                Move candidate = legalMoves[i];
                if (WouldMoveWinImmediately(state, candidate, rootPlayer))
                {
                    _searchTimer.Stop();
                    LastNodesEvaluated = 1;
                    LastSearchScore = WinScore + (targetMaxDepth * 100);
                    LastDepthReached = 1;
                    return new MinimaxSearchResult(
                        candidate,
                        LastSearchScore,
                        1,
                        1,
                        LastTranspositionHits,
                        LastTranspositionCutoffs,
                        _searchTimer.ElapsedMilliseconds);
                }
            }

            // Si solo existe un movimiento legal forzado, retornarlo sin gastar tiempo de búsqueda
            if (legalMoves.Count == 1)
            {
                _searchTimer.Stop();
                LastNodesEvaluated = 1;
                LastSearchScore = 0;
                LastDepthReached = 1;
                return new MinimaxSearchResult(
                    legalMoves[0],
                    0,
                    1,
                    1,
                    0,
                    0,
                    _searchTimer.ElapsedMilliseconds);
            }

            // 2. Ordenamiento táctico inicial en la raíz
            Move? hashMove = null;
            if (_transpositionTable != null &&
                _transpositionTable.TryProbe(state.ZobristKey, out TranspositionEntry rootEntry))
            {
                hashMove = rootEntry.BestMove;
            }

            OrderMovesInPlace(state, legalMoves, rootPlayer, hashMove);

            // Clonamos una única vez para ejecutar MakeMove / UnmakeMoveForSearch en O(1) sin clonar en el árbol
            GameState searchState = state.Clone();

            Move bestMoveOverall = legalMoves[0];
            int bestScoreOverall = 0;
            int completedDepth = 0;

            // 3. Bucle de Iterative Deepening (profundidad 1 .. targetMaxDepth)
            for (int currentDepth = 1; currentDepth <= targetMaxDepth; currentDepth++)
            {
                _enforceTimeLimitInCurrentIteration = currentDepth > 1 && TimeLimitMs > 0;
                _searchAborted = false;

                if (_enforceTimeLimitInCurrentIteration && _searchTimer.ElapsedMilliseconds >= TimeLimitMs)
                {
                    break;
                }

                if (currentDepth > 1)
                {
                    PromoteMoveToFront(legalMoves, bestMoveOverall);
                }

                Move iterationBestMove = legalMoves[0];
                int iterationBestScore = int.MinValue + 1;
                int alpha = int.MinValue + 1;
                int beta = int.MaxValue;

                for (int i = 0; i < legalMoves.Count; i++)
                {
                    Move move = legalMoves[i];
                    searchState.MakeMove(move);

                    int score = -Negamax(
                        searchState,
                        currentDepth - 1,
                        -beta,
                        -alpha);

                    searchState.UnmakeMoveForSearch();

                    if (_searchAborted)
                    {
                        break;
                    }

                    if (score > iterationBestScore)
                    {
                        iterationBestScore = score;
                        iterationBestMove = move;
                    }

                    if (score > alpha)
                    {
                        alpha = score;
                    }
                }

                if (_searchAborted)
                {
                    break;
                }

                bestMoveOverall = iterationBestMove;
                bestScoreOverall = iterationBestScore;
                completedDepth = currentDepth;

                _transpositionTable?.Store(
                    state.ZobristKey,
                    currentDepth,
                    bestScoreOverall,
                    TranspositionFlag.Exact,
                    bestMoveOverall);

                if (bestScoreOverall >= WinScore - 1000 || bestScoreOverall <= -WinScore + 1000)
                {
                    break;
                }
            }

            _searchTimer.Stop();
            LastSearchScore = bestScoreOverall;
            LastDepthReached = completedDepth;

            return new MinimaxSearchResult(
                bestMoveOverall,
                bestScoreOverall,
                LastNodesEvaluated,
                completedDepth,
                LastTranspositionHits,
                LastTranspositionCutoffs,
                _searchTimer.ElapsedMilliseconds);
        }

        private int Negamax(GameState state, int depth, int alpha, int beta)
        {
            if (_enforceTimeLimitInCurrentIteration &&
                (LastNodesEvaluated & TimeCheckIntervalMask) == 0 &&
                _searchTimer.ElapsedMilliseconds >= TimeLimitMs)
            {
                _searchAborted = true;
                return 0;
            }

            LastNodesEvaluated++;

            if (state.IsGameOver)
            {
                if (state.Winner == Player.None)
                {
                    return 0;
                }

                return state.Winner == state.CurrentPlayer
                    ? WinScore + (depth * 100)
                    : -WinScore - (depth * 100);
            }

            int originalAlpha = alpha;
            Move? hashMove = null;

            if (_transpositionTable != null &&
                _transpositionTable.TryProbe(state.ZobristKey, out TranspositionEntry ttEntry))
            {
                hashMove = ttEntry.BestMove;

                if (ttEntry.Depth >= depth)
                {
                    if (ttEntry.Flag == TranspositionFlag.Exact)
                    {
                        _transpositionTable.RecordCutoff();
                        return ttEntry.Score;
                    }

                    if (ttEntry.Flag == TranspositionFlag.LowerBound && ttEntry.Score > alpha)
                    {
                        alpha = ttEntry.Score;
                    }
                    else if (ttEntry.Flag == TranspositionFlag.UpperBound && ttEntry.Score < beta)
                    {
                        beta = ttEntry.Score;
                    }

                    if (alpha >= beta)
                    {
                        _transpositionTable.RecordCutoff();
                        return ttEntry.Score;
                    }
                }
            }

            if (depth <= 0)
            {
                return EvaluateStateInternal(state, state.CurrentPlayer, _botThreatsBuffer, _oppThreatsBuffer);
            }

            List<Move> legalMoves = state.GetLegalMoves();

            if (legalMoves.Count > 1 && (depth >= 2 || hashMove.HasValue))
            {
                OrderMovesInPlace(state, legalMoves, state.CurrentPlayer, hashMove);
            }

            int bestScore = int.MinValue + 1;
            Move bestMove = legalMoves[0];

            for (int i = 0; i < legalMoves.Count; i++)
            {
                Move move = legalMoves[i];
                state.MakeMove(move);

                int score = -Negamax(state, depth - 1, -beta, -alpha);

                state.UnmakeMoveForSearch();

                if (_searchAborted)
                {
                    return 0;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }

                if (score > alpha)
                {
                    alpha = score;
                }

                if (alpha >= beta)
                {
                    break; // Poda Beta
                }
            }

            if (_transpositionTable != null && !_searchAborted)
            {
                TranspositionFlag flag;
                if (bestScore <= originalAlpha)
                {
                    flag = TranspositionFlag.UpperBound;
                }
                else if (bestScore >= beta)
                {
                    flag = TranspositionFlag.LowerBound;
                }
                else
                {
                    flag = TranspositionFlag.Exact;
                }

                _transpositionTable.Store(state.ZobristKey, depth, bestScore, flag, bestMove);
            }

            return bestScore;
        }

        /// <summary>
        /// Evalúa heurísticamente un estado no terminal desde la perspectiva de <paramref name="perspectivePlayer"/>.
        /// </summary>
        public static int EvaluateState(GameState state, Player perspectivePlayer)
        {
            int[] ownThreats = new int[GameState.BoardCount];
            int[] oppThreats = new int[GameState.BoardCount];
            return EvaluateStateInternal(state, perspectivePlayer, ownThreats, oppThreats);
        }

        private static int EvaluateStateInternal(
            GameState state,
            Player perspectivePlayer,
            int[] ownThreatsPerBoard,
            int[] oppThreatsPerBoard)
        {
            Player opponent = GetOpponent(perspectivePlayer);
            int totalScore = 0;
            int totalOwnThreats = 0;
            int totalOppThreats = 0;

            Array.Clear(ownThreatsPerBoard, 0, GameState.BoardCount);
            Array.Clear(oppThreatsPerBoard, 0, GameState.BoardCount);

            for (int board = 0; board < GameState.BoardCount; board++)
            {
                for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                {
                    Player occupant = state.GetCell(board, cell);
                    if (occupant == perspectivePlayer)
                    {
                        totalScore += CellPositionalWeights[cell];
                    }
                    else if (occupant == opponent)
                    {
                        totalScore -= CellPositionalWeights[cell];
                    }
                }

                for (int lineIdx = 0; lineIdx < WinLines.Length; lineIdx += 3)
                {
                    Player c0 = state.GetCell(board, WinLines[lineIdx]);
                    Player c1 = state.GetCell(board, WinLines[lineIdx + 1]);
                    Player c2 = state.GetCell(board, WinLines[lineIdx + 2]);

                    int ownCount = (c0 == perspectivePlayer ? 1 : 0) + (c1 == perspectivePlayer ? 1 : 0) + (c2 == perspectivePlayer ? 1 : 0);
                    int oppCount = (c0 == opponent ? 1 : 0) + (c1 == opponent ? 1 : 0) + (c2 == opponent ? 1 : 0);

                    if (ownCount > 0 && oppCount > 0)
                    {
                        continue;
                    }

                    if (ownCount == 2)
                    {
                        totalScore += TwoInARowWeight;
                        ownThreatsPerBoard[board]++;
                        totalOwnThreats++;
                    }
                    else if (ownCount == 1)
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

            int activeBoard = state.ActiveBoardIndex;
            if (state.CurrentPlayer == perspectivePlayer)
            {
                bool canWinNow = activeBoard == GameState.FreeMoveBoardIndex
                    ? totalOwnThreats > 0
                    : ownThreatsPerBoard[activeBoard] > 0;

                if (canWinNow)
                {
                    return ImmediateThreatBonus;
                }

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
                        if (state.GetCell(activeBoard, cell) == Player.None && ownThreatsPerBoard[cell] > 0)
                        {
                            totalScore += UnsafeRedirectionPenalty;
                        }
                    }
                }
            }

            return totalScore;
        }

        private static void PromoteMoveToFront(List<Move> moves, Move targetMove)
        {
            for (int i = 1; i < moves.Count; i++)
            {
                if (moves[i].Equals(targetMove))
                {
                    Move temp = moves[i];
                    for (int j = i; j > 0; j--)
                    {
                        moves[j] = moves[j - 1];
                    }
                    moves[0] = temp;
                    return;
                }
            }
        }

        private static void OrderMovesInPlace(GameState state, List<Move> moves, Player currentPlayer, Move? hashMove)
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
                Move move = moves[i];
                if (hashMove.HasValue && move.Equals(hashMove.Value))
                {
                    scores[i] = 2_000_000;
                }
                else
                {
                    scores[i] = ScoreMoveForOrdering(state, move, currentPlayer, opponent);
                }
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
            if (WouldMoveWinImmediately(state, move, currentPlayer))
            {
                return 1_000_000;
            }

            int score = (CellPositionalWeights[move.CellIndex] * 10) + CellPositionalWeights[move.BoardIndex];

            int targetBoard = move.CellIndex;
            if (DoesBoardHaveImmediateWin(state, targetBoard, opponent))
            {
                score -= 50_000;
            }

            if (WouldMoveWinImmediately(state, move, opponent))
            {
                score += 2_000;
            }

            return score;
        }

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
