using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Modalidad de sesión de juego (local contra IA, 2 jugadores en el mismo dispositivo o simulación).
    /// </summary>
    public enum SessionMode
    {
        HumanVsBotX = 1, // Humano juega X, Bot juega O
        BotVsHumanO = 2, // Bot juega X, Humano juega O
        PassAndPlay2P = 3, // 2 Humanos en el mismo celular (Pasa y Juega)
        BotVsBot = 4
    }

    /// <summary>
    /// Controlador de sesión de juego desacoplado de motor gráfico (patrón MVVM / Presenter).
    /// Orquesta turnos, reloj de partida (<see cref="MatchClock"/>), llamadas asíncronas a <see cref="MinimaxBot"/>,
    /// historial Undo/Redo, actualización de Rating y registro de partidas (<see cref="MatchRecord"/>).
    /// </summary>
    public sealed class GameSessionController
    {
        private readonly List<MatchRecord> _matchHistory = new List<MatchRecord>();
        private MinimaxBot _bot;
        private bool _eloCommittedForCurrentMatch;

        public GameState State { get; private set; }
        public MatchClock Clock { get; }
        public SessionMode Mode { get; private set; }
        public BotDifficulty BotDifficulty => _bot.Difficulty;
        public PlayerProfile ProfileX { get; private set; }
        public PlayerProfile ProfileO { get; private set; }
        public bool IsBotThinking { get; private set; }
        public MinimaxSearchResult? LastBotSearchResult { get; private set; }
        public EloMatchUpdate? LastEloUpdate { get; private set; }
        public MatchEndReason LastEndReason { get; private set; }
        public IReadOnlyList<MatchRecord> MatchHistory => _matchHistory;

        // Eventos para enlazar con Godot 4, consola o cliente móvil
        public event Action StateChanged;
        public event Action<Move, Player> MoveExecuted;
        public event Action<Player> BotThinkingStarted;
        public event Action<MinimaxSearchResult> BotThinkingCompleted;
        public event Action<MatchRecord, EloMatchUpdate> MatchCompleted;

        public GameSessionController(
            SessionMode mode = SessionMode.HumanVsBotX,
            BotDifficulty difficulty = BotDifficulty.Hard,
            PlayerProfile humanProfile = null,
            TimeControlConfig? timeControl = null)
        {
            State = new GameState();
            Clock = new MatchClock(timeControl ?? TimeControlConfig.Blitz3Plus2);
            Clock.TimeExpired += OnClockTimeExpired;
            ConfigureSession(mode, difficulty, humanProfile);
        }

        /// <summary>
        /// Cambia el control de tiempo del reloj y reinicia la partida.
        /// </summary>
        public void SetTimeControl(TimeControlConfig config)
        {
            Clock.Reset(config);
            StartNewMatch();
        }

        /// <summary>
        /// Configura o cambia el modo de juego y la dificultad del Bot, iniciando una nueva partida.
        /// </summary>
        public void ConfigureSession(
            SessionMode mode,
            BotDifficulty difficulty,
            PlayerProfile existingHumanProfile = null)
        {
            Mode = mode;
            _bot = new MinimaxBot(difficulty);

            PlayerProfile human = existingHumanProfile ?? new PlayerProfile("local-human", "Jugador", 1200);
            int botElo = GetBotBaseElo(difficulty);
            var botProfile = new PlayerProfile($"bot-{difficulty}", $"Bot ({difficulty})", botElo);

            switch (mode)
            {
                case SessionMode.HumanVsBotX:
                    ProfileX = human;
                    ProfileO = botProfile;
                    break;

                case SessionMode.BotVsHumanO:
                    ProfileX = botProfile;
                    ProfileO = human;
                    break;

                case SessionMode.BotVsBot:
                    ProfileX = new PlayerProfile("bot-x", $"Bot X ({difficulty})", botElo);
                    ProfileO = new PlayerProfile("bot-o", $"Bot O ({difficulty})", botElo);
                    break;

                default:
                    ProfileX = human;
                    ProfileO = new PlayerProfile("local-human-2", "Jugador 2", 1200);
                    break;
            }

            StartNewMatch();
        }

        /// <summary>
        /// Reinicia el tablero y el reloj para una nueva partida manteniendo los perfiles y el Rating acumulado.
        /// </summary>
        public void StartNewMatch()
        {
            State.Reset();
            Clock.Reset(Clock.Config);
            _bot.ClearTranspositionTable();
            _eloCommittedForCurrentMatch = false;
            LastBotSearchResult = null;
            LastEloUpdate = null;
            LastEndReason = MatchEndReason.InProgress;
            IsBotThinking = false;
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Actualiza el reloj activo en tiempo real. Debe llamarse desde <c>_Process(double delta)</c> en Godot 4.
        /// </summary>
        public bool TickClock(double deltaSeconds)
        {
            if (State.IsGameOver)
            {
                return false;
            }

            return Clock.Tick(deltaSeconds);
        }

        /// <summary>
        /// Indica si el turno actual corresponde a un jugador controlado por la IA.
        /// </summary>
        public bool IsCurrentTurnBot()
        {
            if (State.IsGameOver)
            {
                return false;
            }

            switch (Mode)
            {
                case SessionMode.HumanVsBotX:
                    return State.CurrentPlayer == Player.O;
                case SessionMode.BotVsHumanO:
                    return State.CurrentPlayer == Player.X;
                case SessionMode.BotVsBot:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Intenta ejecutar un movimiento originado por un toque/clic del usuario en la interfaz gráfica.
        /// </summary>
        public bool TryPlayHumanMove(int boardIndex, int cellIndex)
        {
            if (IsBotThinking || IsCurrentTurnBot() || State.IsGameOver)
            {
                return false;
            }

            return ApplyMoveAndNotify(new Move(boardIndex, cellIndex));
        }

        /// <summary>
        /// Calcula y aplica de forma asíncrona (en un hilo de fondo) la jugada del Bot Minimax.
        /// </summary>
        public async Task<MinimaxSearchResult?> PlayBotTurnAsync()
        {
            if (IsBotThinking || !IsCurrentTurnBot() || State.IsGameOver)
            {
                return null;
            }

            IsBotThinking = true;
            Player botPlayer = State.CurrentPlayer;
            BotThinkingStarted?.Invoke(botPlayer);
            StateChanged?.Invoke();

            try
            {
                GameState snapshot = State.Clone();
                MinimaxSearchResult result = await Task.Run(() => _bot.FindBestMove(snapshot)).ConfigureAwait(false);

                IsBotThinking = false;
                LastBotSearchResult = result;
                BotThinkingCompleted?.Invoke(result);

                if (!State.IsGameOver)
                {
                    ApplyMoveAndNotify(result.BestMove);
                }
                return result;
            }
            catch
            {
                IsBotThinking = false;
                StateChanged?.Invoke();
                throw;
            }
        }

        /// <summary>
        /// Calcula de forma asíncrona una sugerencia de jugada para el jugador humano sin aplicarla al tablero.
        /// </summary>
        public Task<MinimaxSearchResult> RequestHintAsync()
        {
            GameState snapshot = State.Clone();
            return Task.Run(() => _bot.FindBestMove(snapshot));
        }

        public bool Undo()
        {
            if (IsBotThinking || !State.CanUndo)
            {
                return false;
            }

            State.UndoMove();
            if ((Mode == SessionMode.HumanVsBotX || Mode == SessionMode.BotVsHumanO) &&
                IsCurrentTurnBot() &&
                State.CanUndo)
            {
                State.UndoMove();
            }

            if (State.MoveCount == 0)
            {
                Clock.Reset(Clock.Config);
            }

            StateChanged?.Invoke();
            return true;
        }

        public bool Redo()
        {
            if (IsBotThinking || !State.CanRedo)
            {
                return false;
            }

            State.RedoMove();
            if ((Mode == SessionMode.HumanVsBotX || Mode == SessionMode.BotVsHumanO) &&
                IsCurrentTurnBot() &&
                State.CanRedo)
            {
                State.RedoMove();
            }

            StateChanged?.Invoke();
            return true;
        }

        private bool ApplyMoveAndNotify(Move move)
        {
            Player mover = State.CurrentPlayer;
            if (!State.MakeMove(move))
            {
                return false;
            }

            if (State.IsGameOver)
            {
                Clock.Pause();
            }
            else
            {
                Clock.OnMoveCompleted(mover, State.CurrentPlayer);
            }

            MoveExecuted?.Invoke(move, mover);

            if (State.IsGameOver && !_eloCommittedForCurrentMatch)
            {
                MatchEndReason reason = State.Winner != Player.None
                    ? MatchEndReason.SuddenDeathThreeInARow
                    : MatchEndReason.GlobalDraw;
                CommitMatchRatingAndRecord(reason);
            }

            StateChanged?.Invoke();
            return true;
        }

        private void OnClockTimeExpired(Player timedOutPlayer)
        {
            if (State.IsGameOver)
            {
                return;
            }

            if (State.ForfeitByTimeout(timedOutPlayer) && !_eloCommittedForCurrentMatch)
            {
                CommitMatchRatingAndRecord(MatchEndReason.Timeout);
                StateChanged?.Invoke();
            }
        }

        private void CommitMatchRatingAndRecord(MatchEndReason reason)
        {
            LastEndReason = reason;
            EloMatchUpdate eloUpdate = EloRatingCalculator.CalculateMatchRatingChange(
                ProfileX.EloRating,
                ProfileX.GamesPlayed,
                ProfileO.EloRating,
                ProfileO.GamesPlayed,
                State.Winner);

            MatchRecord record = MatchRecord.CreateFromGame(
                State,
                ProfileX,
                ProfileO,
                updateProfilesIfGameOver: true);
            record.EndReason = reason;

            _matchHistory.Add(record);
            LastEloUpdate = eloUpdate;
            _eloCommittedForCurrentMatch = true;

            MatchCompleted?.Invoke(record, eloUpdate);
        }

        private static int GetBotBaseElo(BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Easy: return 1000;
                case BotDifficulty.Medium: return 1300;
                case BotDifficulty.Hard: return 1650;
                case BotDifficulty.Master: return 2050;
                default: return 1600;
            }
        }
    }
}
