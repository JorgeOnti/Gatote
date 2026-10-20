using System;
using System.Collections.Generic;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Navegador interactivo de repeticiones y sala de análisis paso a paso para partidas concluidas o en curso.
    /// Permite avanzar, retroceder, saltar a cualquier turno (ply) y consultar la evaluación estática del motor.
    /// </summary>
    public sealed class MatchReplayNavigator
    {
        private readonly List<Move> _allMoves;
        private readonly GameState _workingState;

        public MatchRecord SourceRecord { get; private set; }
        public GameState CurrentState => _workingState;
        public int CurrentPly => _workingState.MoveCount;
        public int TotalPlies => _allMoves.Count;
        public bool CanStepBackward => _workingState.MoveCount > 0;
        public bool CanStepForward => _workingState.MoveCount < _allMoves.Count;
        public Move? LastExecutedMove => _workingState.GetLastMove();

        public MatchReplayNavigator()
        {
            _allMoves = new List<Move>();
            _workingState = new GameState();
        }

        public MatchReplayNavigator(MatchRecord record) : this()
        {
            LoadFromMatchRecord(record);
        }

        public MatchReplayNavigator(string moveTranscript) : this()
        {
            GameState imported = GameSerializer.ImportMoveHistory(moveTranscript ?? string.Empty);
            LoadFromMoves(imported.GetMoveHistory());
        }

        public MatchReplayNavigator(IEnumerable<Move> moves) : this()
        {
            LoadFromMoves(moves);
        }

        /// <summary>
        /// Carga una partida desde un <see cref="MatchRecord"/> y se posiciona al final de la misma.
        /// </summary>
        public void LoadFromMatchRecord(MatchRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            SourceRecord = record;
            GameState reconstructed = record.ReconstructGameState();
            LoadFromMoves(reconstructed.GetMoveHistory());
        }

        /// <summary>
        /// Carga una secuencia de movimientos y posiciona el navegador en el último movimiento.
        /// </summary>
        public void LoadFromMoves(IEnumerable<Move> moves)
        {
            if (moves == null)
            {
                throw new ArgumentNullException(nameof(moves));
            }

            _allMoves.Clear();
            _workingState.Reset();

            foreach (Move move in moves)
            {
                if (!_workingState.MakeMove(move))
                {
                    throw new InvalidOperationException($"Secuencia de repetición inválida en el movimiento {move}.");
                }
                _allMoves.Add(move);
            }
        }

        /// <summary>
        /// Salta al inicio de la partida (turno 0, tablero vacío).
        /// </summary>
        public void JumpToStart()
        {
            while (_workingState.CanUndo)
            {
                _workingState.UndoMove();
            }
        }

        /// <summary>
        /// Salta al estado final de la partida.
        /// </summary>
        public void JumpToEnd()
        {
            while (_workingState.MoveCount < _allMoves.Count)
            {
                Move nextMove = _allMoves[_workingState.MoveCount];
                _workingState.MakeMove(nextMove);
            }
        }

        /// <summary>
        /// Retrocede un medio turno (1 ply).
        /// </summary>
        public bool StepBackward()
        {
            if (!CanStepBackward)
            {
                return false;
            }

            return _workingState.UndoMove();
        }

        /// <summary>
        /// Avanza un medio turno (1 ply).
        /// </summary>
        public bool StepForward()
        {
            if (!CanStepForward)
            {
                return false;
            }

            Move nextMove = _allMoves[_workingState.MoveCount];
            return _workingState.MakeMove(nextMove);
        }

        /// <summary>
        /// Salta directamente a un número de jugada específico (<c>0 .. TotalPlies</c>).
        /// </summary>
        public void JumpToPly(int targetPly)
        {
            if (targetPly < 0 || targetPly > _allMoves.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(targetPly), "El índice de jugada está fuera de rango.");
            }

            while (_workingState.MoveCount > targetPly)
            {
                _workingState.UndoMove();
            }

            while (_workingState.MoveCount < targetPly)
            {
                _workingState.MakeMove(_allMoves[_workingState.MoveCount]);
            }
        }

        /// <summary>
        /// Obtiene la evaluación heurística estática desde la perspectiva de <see cref="Player.X"/>
        /// en el turno actualmente visualizado en la repetición.
        /// </summary>
        public int GetCurrentEvaluationForX()
        {
            if (_workingState.IsGameOver)
            {
                if (_workingState.Winner == Player.X) return MinimaxBot.WinScore;
                if (_workingState.Winner == Player.O) return -MinimaxBot.WinScore;
                return 0;
            }

            return MinimaxBot.EvaluateState(_workingState, Player.X);
        }
    }
}
