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
        private const int ImmediateThreatBonus = 80_000;
        private const int ZugzwangLossPenalty = 70_000;
        private const int TwoInARowWeight = 600;
        private const int DoubleThreatForkBonus = 1_500;
        private const int OneInARowWeight = 45;
        private const int ActiveBoardOnePieceSetupBonus = 650;
        private const int ActiveBoardMultiPieceSetupBonus = 1_600;
        private const int UnsafeRedirectionPenalty = 1_200;
        private const int ForcedSingleSafeMovePenalty = 1_800;
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
                    MaxDepth = 3;
                    TimeLimitMs = 150;
                    UseTranspositionTable = true;
                    break;

                case BotDifficulty.Medium:
                    MaxDepth = 5;
                    TimeLimitMs = 450;
                    UseTranspositionTable = true;
                    break;

                case BotDifficulty.Hard:
                    MaxDepth = 7;
                    TimeLimitMs = 1100;
                    UseTranspositionTable = true;
                    break;

                case BotDifficulty.Master:
                    MaxDepth = 9;
                    TimeLimitMs = 2500;
                    UseTranspositionTable = true;
                    break;

                default:
                    MaxDepth = 7;
                    TimeLimitMs = 1100;
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

            // 2.5 Filtro estricto de seguridad en la raíz:
            // Descartar jugadas suicidas que envían al rival a un gato donde gana en 1 turno (incluyendo
            // auto-redirección board == cell o redirección a un tablero lleno que otorga Tiro Libre).
            var safeMoves = new List<Move>(legalMoves.Count);
            var nonForcedTrapMoves = new List<Move>(legalMoves.Count);

            for (int i = 0; i < legalMoves.Count; i++)
            {
                Move candidate = legalMoves[i];
                searchState.MakeMove(candidate);
                bool allowsOpponentImmediateWin = CanCurrentPlayerWinInOneMove(searchState);

                if (!allowsOpponentImmediateWin)
                {
                    safeMoves.Add(candidate);

                    // Verificar también si el rival tiene una respuesta que nos obligue a perder en el siguiente turno
                    if (!DoesOpponentHaveForcedTrapReply(searchState))
                    {
                        nonForcedTrapMoves.Add(candidate);
                    }
                }

                searchState.UnmakeMoveForSearch();
            }

            if (nonForcedTrapMoves.Count > 0)
            {
                legalMoves = nonForcedTrapMoves;
            }
            else if (safeMoves.Count > 0)
            {
                legalMoves = safeMoves;
            }

            if (legalMoves.Count == 1)
            {
                _searchTimer.Stop();
                LastNodesEvaluated = Math.Max(1, LastNodesEvaluated);
                LastSearchScore = 0;
                LastDepthReached = 1;
                return new MinimaxSearchResult(
                    legalMoves[0],
                    0,
                    LastNodesEvaluated,
                    1,
                    LastTranspositionHits,
                    LastTranspositionCutoffs,
                    _searchTimer.ElapsedMilliseconds);
            }

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
                int ownPiecesInBoard = 0;
                int oppPiecesInBoard = 0;

                for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                {
                    Player occupant = state.GetCell(board, cell);
                    if (occupant == perspectivePlayer)
                    {
                        totalScore += CellPositionalWeights[cell];
                        ownPiecesInBoard++;
                    }
                    else if (occupant == opponent)
                    {
                        totalScore -= CellPositionalWeights[cell];
                        oppPiecesInBoard++;
                    }
                }

                int ownOpenLinesInBoard = 0;
                int oppOpenLinesInBoard = 0;

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
                        ownOpenLinesInBoard++;
                    }
                    else if (ownCount == 1)
                    {
                        totalScore += OneInARowWeight;
                        ownOpenLinesInBoard++;
                    }
                    else if (oppCount == 2)
                    {
                        totalScore -= TwoInARowWeight;
                        oppThreatsPerBoard[board]++;
                        totalOppThreats++;
                        oppOpenLinesInBoard++;
                    }
                    else if (oppCount == 1)
                    {
                        totalScore -= OneInARowWeight;
                        oppOpenLinesInBoard++;
                    }
                }

                // Bonificación por doble amenaza (Fork) dentro del mismo sub-tablero
                if (ownThreatsPerBoard[board] >= 2)
                {
                    totalScore += DoubleThreatForkBonus;
                }
                if (oppThreatsPerBoard[board] >= 2)
                {
                    totalScore -= DoubleThreatForkBonus;
                }

                // Evaluación de iniciativa y peligro del sub-tablero activo (el gato al que fue enviado el jugador en turno):
                // Si el jugador en turno fue enviado a un sub-tablero donde ya tiene fichas y líneas abiertas,
                // podrá armar 2 en raya o doble amenaza de inmediato.
                if (state.ActiveBoardIndex == board)
                {
                    if (state.CurrentPlayer == perspectivePlayer)
                    {
                        if (ownPiecesInBoard >= 2 && ownOpenLinesInBoard > 0)
                        {
                            totalScore += ActiveBoardMultiPieceSetupBonus + (ownOpenLinesInBoard * 120);
                        }
                        else if (ownPiecesInBoard == 1 && ownOpenLinesInBoard > 0)
                        {
                            totalScore += ActiveBoardOnePieceSetupBonus + (ownOpenLinesInBoard * 60);
                        }
                    }
                    else
                    {
                        if (oppPiecesInBoard >= 2 && oppOpenLinesInBoard > 0)
                        {
                            totalScore -= ActiveBoardMultiPieceSetupBonus + (oppOpenLinesInBoard * 120);
                        }
                        else if (oppPiecesInBoard == 1 && oppOpenLinesInBoard > 0)
                        {
                            totalScore -= ActiveBoardOnePieceSetupBonus + (oppOpenLinesInBoard * 60);
                        }
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
                    int safeMoveCount = 0;
                    int poisonedMoveCount = 0;

                    for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                    {
                        if (state.GetCell(activeBoard, cell) != Player.None)
                        {
                            continue;
                        }

                        bool targetFullAfterMove = cell == activeBoard
                            ? state.GetBoardEmptyCount(activeBoard) <= 1
                            : state.GetBoardEmptyCount(cell) == 0;

                        bool sendsOpponentToWin = targetFullAfterMove
                            ? totalOppThreats > 0
                            : oppThreatsPerBoard[cell] > 0;

                        if (sendsOpponentToWin)
                        {
                            poisonedMoveCount++;
                            totalScore -= UnsafeRedirectionPenalty;
                        }
                        else
                        {
                            safeMoveCount++;
                        }
                    }

                    if (safeMoveCount == 0 && poisonedMoveCount > 0)
                    {
                        return -ZugzwangLossPenalty;
                    }
                    if (safeMoveCount == 1 && poisonedMoveCount > 0)
                    {
                        totalScore -= ForcedSingleSafeMovePenalty;
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
                    int oppSafeMoveCount = 0;
                    int oppPoisonedMoveCount = 0;

                    for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                    {
                        if (state.GetCell(activeBoard, cell) != Player.None)
                        {
                            continue;
                        }

                        bool targetFullAfterMove = cell == activeBoard
                            ? state.GetBoardEmptyCount(activeBoard) <= 1
                            : state.GetBoardEmptyCount(cell) == 0;

                        bool sendsUsToWin = targetFullAfterMove
                            ? totalOwnThreats > 0
                            : ownThreatsPerBoard[cell] > 0;

                        if (sendsUsToWin)
                        {
                            oppPoisonedMoveCount++;
                            totalScore += UnsafeRedirectionPenalty;
                        }
                        else
                        {
                            oppSafeMoveCount++;
                        }
                    }

                    if (oppSafeMoveCount == 0 && oppPoisonedMoveCount > 0)
                    {
                        return ZugzwangLossPenalty;
                    }
                    if (oppSafeMoveCount == 1 && oppPoisonedMoveCount > 0)
                    {
                        totalScore += ForcedSingleSafeMovePenalty;
                    }
                }
            }

            return totalScore;
        }

        private static bool CanCurrentPlayerWinInOneMove(GameState state)
        {
            if (state.IsGameOver)
            {
                return false;
            }

            Player turnPlayer = state.CurrentPlayer;
            int activeBoard = state.ActiveBoardIndex;

            if (activeBoard == GameState.FreeMoveBoardIndex)
            {
                for (int b = 0; b < GameState.BoardCount; b++)
                {
                    if (DoesBoardHaveImmediateWin(state, b, turnPlayer))
                    {
                        return true;
                    }
                }
                return false;
            }

            return DoesBoardHaveImmediateWin(state, activeBoard, turnPlayer);
        }

        /// <summary>
        /// Verifica si en <paramref name="stateAfterBotMove"/> (turno del oponente), el oponente dispone de
        /// algún movimiento legal que deje al bot en Zugzwang (donde todas las respuestas del bot regalan victoria inmediata)
        /// o gane en el acto.
        /// </summary>
        private static bool DoesOpponentHaveForcedTrapReply(GameState stateAfterBotMove)
        {
            if (stateAfterBotMove.IsGameOver)
            {
                return false;
            }

            List<Move> oppReplies = stateAfterBotMove.GetLegalMoves();
            for (int i = 0; i < oppReplies.Count; i++)
            {
                Move oppMove = oppReplies[i];
                stateAfterBotMove.MakeMove(oppMove);

                bool oppWonOrTrappedBot = false;
                if (stateAfterBotMove.IsGameOver)
                {
                    oppWonOrTrappedBot = stateAfterBotMove.Winner != Player.None;
                }
                else if (!CanCurrentPlayerWinInOneMove(stateAfterBotMove))
                {
                    // Revisar si todas las respuestas legales del bot regalan victoria inmediata al oponente
                    List<Move> botResponses = stateAfterBotMove.GetLegalMoves();
                    bool hasAtLeastOneSafeResponse = false;

                    for (int j = 0; j < botResponses.Count; j++)
                    {
                        stateAfterBotMove.MakeMove(botResponses[j]);
                        bool oppWinsNext = CanCurrentPlayerWinInOneMove(stateAfterBotMove);
                        stateAfterBotMove.UnmakeMoveForSearch();

                        if (!oppWinsNext)
                        {
                            hasAtLeastOneSafeResponse = true;
                            break;
                        }
                    }

                    if (!hasAtLeastOneSafeResponse && botResponses.Count > 0)
                    {
                        oppWonOrTrappedBot = true;
                    }
                }

                stateAfterBotMove.UnmakeMoveForSearch();

                if (oppWonOrTrappedBot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Clasifica la calidad de un movimiento realizado en lenguaje sencillo para el jugador
        /// ("Jugada ganadora", "Muy buen movimiento", "Buen movimiento", "Jugada imprecisa", "Mal movimiento", "Error grave").
        /// </summary>
        public static string ClassifyMoveQuality(GameState stateBeforeMove, Move playedMove, out string explanation)
        {
            if (stateBeforeMove == null)
            {
                throw new ArgumentNullException(nameof(stateBeforeMove));
            }

            Player mover = stateBeforeMove.CurrentPlayer;
            Player opponent = GetOpponent(mover);

            if (WouldMoveWinImmediately(stateBeforeMove, playedMove, mover))
            {
                explanation = "Completa tres en raya y gana la partida.";
                return "Jugada ganadora";
            }

            if (CanCurrentPlayerWinInOneMove(stateBeforeMove))
            {
                explanation = "Había una casilla disponible para ganar en este turno.";
                return "Oportunidad perdida";
            }

            GameState afterPlayed = stateBeforeMove.Clone();
            if (!afterPlayed.MakeMove(playedMove))
            {
                explanation = "Movimiento fuera de secuencia.";
                return "Movimiento";
            }

            // Verificar si existía al menos una jugada segura antes de tirar
            List<Move> allLegal = stateBeforeMove.GetLegalMoves();
            bool hadSafeAlternative = false;
            GameState probe = stateBeforeMove.Clone();
            for (int i = 0; i < allLegal.Count; i++)
            {
                probe.MakeMove(allLegal[i]);
                if (!CanCurrentPlayerWinInOneMove(probe))
                {
                    hadSafeAlternative = true;
                    probe.UnmakeMoveForSearch();
                    break;
                }
                probe.UnmakeMoveForSearch();
            }

            if (CanCurrentPlayerWinInOneMove(afterPlayed) && hadSafeAlternative)
            {
                explanation = $"Envía al rival al cuadro {playedMove.CellIndex + 1}, donde ya tiene dos en raya para ganar.";
                return "Error grave";
            }

            if (DoesOpponentHaveForcedTrapReply(afterPlayed) && hadSafeAlternative)
            {
                explanation = $"Permite que el rival fuerce una trampa ganadora desde el cuadro {playedMove.CellIndex + 1}.";
                return "Mal movimiento";
            }

            var analyzer = new MinimaxBot(maxDepth: 4, timeLimitMs: 120, useTranspositionTable: true);
            MinimaxSearchResult best = analyzer.FindBestMove(stateBeforeMove);

            if (playedMove.Equals(best.BestMove))
            {
                explanation = $"Mejor opción: juega en el cuadro {playedMove.BoardIndex + 1} y envía al rival al cuadro seguro {playedMove.CellIndex + 1}.";
                return "Muy buen movimiento";
            }

            int evalAfterPlayed = -EvaluateState(afterPlayed, opponent);
            GameState afterBest = stateBeforeMove.Clone();
            afterBest.MakeMove(best.BestMove);
            int evalAfterBest = -EvaluateState(afterBest, opponent);

            int drop = evalAfterBest - evalAfterPlayed;

            int oppPiecesInTarget = CountPlayerPiecesInBoard(afterPlayed, playedMove.CellIndex, opponent);
            int oppThreatsInTarget = CountPlayerOpenLinesWithCount(afterPlayed, playedMove.CellIndex, opponent, 1);

            if (drop >= 1300 || (oppPiecesInTarget >= 2 && oppThreatsInTarget >= 2 && drop >= 600))
            {
                explanation = $"Envía al rival al cuadro {playedMove.CellIndex + 1}, donde ya tiene piezas listas para atacar.";
                return "Mal movimiento";
            }

            if (drop >= 450)
            {
                explanation = $"Cede algo de iniciativa al enviar al rival al cuadro {playedMove.CellIndex + 1}.";
                return "Jugada imprecisa";
            }

            if (drop <= 120)
            {
                explanation = $"Mantiene buena posición y envía al rival al cuadro {playedMove.CellIndex + 1}.";
                return "Muy buen movimiento";
            }

            explanation = $"Jugada segura que envía al rival al cuadro {playedMove.CellIndex + 1}.";
            return "Buen movimiento";
        }

        /// <summary>
        /// Traduce la evaluación heurística en una frase corta y clara sin tecnicismos.
        /// </summary>
        public static string DescribePositionStatus(GameState state, int evalForX)
        {
            if (state == null || state.MoveCount == 0)
            {
                return "Partida equilibrada";
            }

            if (state.IsGameOver)
            {
                if (state.Winner == Player.X) return "Victoria para X";
                if (state.Winner == Player.O) return "Victoria para O";
                return "Empate";
            }

            if (evalForX >= ImmediateThreatBonus / 2) return "Posición ganadora para X";
            if (evalForX <= -ImmediateThreatBonus / 2) return "Posición ganadora para O";
            if (evalForX >= 1400) return "Ventaja clara para X";
            if (evalForX <= -1400) return "Ventaja clara para O";
            if (evalForX >= 350) return "Ligera ventaja para X";
            if (evalForX <= -350) return "Ligera ventaja para O";
            return "Partida equilibrada";
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
            bool targetBecomesFull = targetBoard == move.BoardIndex
                ? state.GetBoardEmptyCount(targetBoard) <= 1
                : state.GetBoardEmptyCount(targetBoard) == 0;

            if (targetBecomesFull)
            {
                // Si el tablero destino está lleno, el oponente tendrá Tiro Libre en cualquier tablero
                for (int b = 0; b < GameState.BoardCount; b++)
                {
                    if (DoesBoardHaveImmediateWin(state, b, opponent))
                    {
                        return -500_000;
                    }
                }
                score -= 8_000;
            }
            else if (DoesBoardHaveImmediateWin(state, targetBoard, opponent))
            {
                return -500_000;
            }

            // Penalizar enviar al rival a un gato donde ya tiene fichas colocadas y líneas abiertas
            int oppPiecesInTarget = CountPlayerPiecesInBoard(state, targetBoard, opponent);
            int ownPiecesInTarget = CountPlayerPiecesInBoard(state, targetBoard, currentPlayer);

            if (oppPiecesInTarget >= 2)
            {
                score -= 9_000;
            }
            else if (oppPiecesInTarget == 1)
            {
                score -= 3_000;
            }
            else
            {
                score += 1_500;
            }

            if (ownPiecesInTarget > oppPiecesInTarget)
            {
                score += 600;
            }

            if (WouldMoveWinImmediately(state, move, opponent))
            {
                score += 1_200;
            }

            return score;
        }

        private static int CountPlayerPiecesInBoard(GameState state, int boardIndex, Player player)
        {
            int count = 0;
            for (int c = 0; c < GameState.CellsPerBoard; c++)
            {
                if (state.GetCell(boardIndex, c) == player)
                {
                    count++;
                }
            }
            return count;
        }

        private static int CountPlayerOpenLinesWithCount(GameState state, int boardIndex, Player player, int minPlayerPieces)
        {
            Player opponent = GetOpponent(player);
            int openLines = 0;
            for (int i = 0; i < WinLines.Length; i += 3)
            {
                Player p0 = state.GetCell(boardIndex, WinLines[i]);
                Player p1 = state.GetCell(boardIndex, WinLines[i + 1]);
                Player p2 = state.GetCell(boardIndex, WinLines[i + 2]);

                if (p0 == opponent || p1 == opponent || p2 == opponent)
                {
                    continue;
                }

                int cnt = (p0 == player ? 1 : 0) + (p1 == player ? 1 : 0) + (p2 == player ? 1 : 0);
                if (cnt >= minPlayerPieces)
                {
                    openLines++;
                }
            }
            return openLines;
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
