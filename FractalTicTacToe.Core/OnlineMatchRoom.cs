using System;

namespace FractalTicTacToe.Core
{
#if NET6_0_OR_GREATER
    /// <summary>
    /// Representa una solicitud pendiente para deshacer jugada (Takeback) que requiere aceptación del oponente.
    /// </summary>
    public readonly record struct TakebackRequest(
        string RequesterPlayerId,
        Player RequesterRole,
        string ResponderPlayerId,
        Player ResponderRole,
        int PliesToUndo,
        DateTime RequestedAtUtc);
#else
    /// <summary>
    /// Representa una solicitud pendiente para deshacer jugada (Takeback) que requiere aceptación del oponente.
    /// </summary>
    public readonly struct TakebackRequest : IEquatable<TakebackRequest>
    {
        public string RequesterPlayerId { get; }
        public Player RequesterRole { get; }
        public string ResponderPlayerId { get; }
        public Player ResponderRole { get; }
        public int PliesToUndo { get; }
        public DateTime RequestedAtUtc { get; }

        public TakebackRequest(
            string requesterPlayerId,
            Player requesterRole,
            string responderPlayerId,
            Player responderRole,
            int pliesToUndo,
            DateTime requestedAtUtc)
        {
            RequesterPlayerId = requesterPlayerId;
            RequesterRole = requesterRole;
            ResponderPlayerId = responderPlayerId;
            ResponderRole = responderRole;
            PliesToUndo = pliesToUndo;
            RequestedAtUtc = requestedAtUtc;
        }

        public bool Equals(TakebackRequest other) =>
            RequesterPlayerId == other.RequesterPlayerId &&
            RequesterRole == other.RequesterRole &&
            ResponderPlayerId == other.ResponderPlayerId &&
            ResponderRole == other.ResponderRole &&
            PliesToUndo == other.PliesToUndo;

        public override bool Equals(object obj) => obj is TakebackRequest other && Equals(other);

        public override int GetHashCode() =>
            ((RequesterPlayerId != null ? RequesterPlayerId.GetHashCode() : 0) * 397) ^ PliesToUndo;
    }
#endif

    /// <summary>
    /// Sala de partida en línea con autoridad de servidor (Anti-Cheat).
    /// Valida turnos por identidad de cuenta, modo de tiempo, solicitudes de Takeback con consentimiento mutuo,
    /// cancelación por inactividad en los primeros 30s (Abort), revanchas y ajuste oficial de puntaje.
    /// </summary>
    public sealed class OnlineMatchRoom
    {
        private bool _rematchRequestedByX;
        private bool _rematchRequestedByO;

        public string RoomId { get; }
        public PlayerProfile PlayerX { get; }
        public PlayerProfile PlayerO { get; }
        public TimeControlConfig TimeControl { get; }
        public GameState State { get; }
        public TakebackRequest? PendingTakeback { get; private set; }
        public MatchEndReason EndReason { get; private set; }
        public Player EffectiveWinner { get; private set; }
        public MatchRecord CompletedMatchRecord { get; private set; }
        public EloMatchUpdate? CompletedEloUpdate { get; private set; }

        public bool IsMatchOver => EndReason != MatchEndReason.InProgress;

        // Eventos del servidor autoritativo para difundir a ambos clientes conectados
        public event Action<OnlineMatchRoom, Move, Player> MoveAccepted;
        public event Action<OnlineMatchRoom, TakebackRequest> TakebackRequested;
        public event Action<OnlineMatchRoom, TakebackRequest, bool> TakebackResolved;
        public event Action<OnlineMatchRoom, MatchRecord, EloMatchUpdate> MatchConcluded;

        public OnlineMatchRoom(
            string roomId,
            PlayerProfile playerX,
            PlayerProfile playerO,
            TimeControlConfig? timeControl = null)
        {
            if (playerX == null) throw new ArgumentNullException(nameof(playerX));
            if (playerO == null) throw new ArgumentNullException(nameof(playerO));

            RoomId = string.IsNullOrWhiteSpace(roomId) ? Guid.NewGuid().ToString("N") : roomId;
            PlayerX = playerX;
            PlayerO = playerO;
            TimeControl = timeControl ?? TimeControlConfig.Blitz3Plus2;
            State = new GameState();
            EndReason = MatchEndReason.InProgress;
            EffectiveWinner = Player.None;
        }

        /// <summary>
        /// Obtiene el rol (<see cref="Player.X"/>, <see cref="Player.O"/> o <see cref="Player.None"/>) asignado a un ID de cuenta.
        /// </summary>
        public Player GetPlayerRole(string playerId)
        {
            if (string.Equals(playerId, PlayerX.PlayerId, StringComparison.Ordinal))
            {
                return Player.X;
            }

            if (string.Equals(playerId, PlayerO.PlayerId, StringComparison.Ordinal))
            {
                return Player.O;
            }

            return Player.None;
        }

        /// <summary>
        /// Procesa un movimiento enviado por un jugador en línea.
        /// Verifica que la partida siga activa, que sea el turno de ese jugador y que el tiro sea legal.
        /// </summary>
        public bool SubmitMove(string playerId, Move move, out string errorMessage)
        {
            errorMessage = null;

            if (IsMatchOver)
            {
                errorMessage = "La partida ya ha finalizado.";
                return false;
            }

            Player role = GetPlayerRole(playerId);
            if (role == Player.None)
            {
                errorMessage = "El jugador no pertenece a esta sala.";
                return false;
            }

            if (State.CurrentPlayer != role)
            {
                errorMessage = "No es tu turno.";
                return false;
            }

            // Si había una solicitud de Takeback pendiente del rival y el jugador en turno decide tirar,
            // se rechaza implícitamente el Takeback.
            if (PendingTakeback.HasValue)
            {
                TakebackRequest implicitlyDeclined = PendingTakeback.Value;
                PendingTakeback = null;
                TakebackResolved?.Invoke(this, implicitlyDeclined, false);
            }

            if (!State.MakeMove(move))
            {
                errorMessage = $"El movimiento {move} no es legal en el estado actual.";
                return false;
            }

            MoveAccepted?.Invoke(this, move, role);

            if (State.IsGameOver)
            {
                MatchEndReason reason = State.Winner != Player.None
                    ? MatchEndReason.SuddenDeathThreeInARow
                    : MatchEndReason.GlobalDraw;
                FinalizeMatch(State.Winner, reason);
            }

            return true;
        }

        /// <summary>
        /// Inicia una solicitud formal para deshacer la última jugada (Takeback).
        /// El movimiento NO se deshace hasta que el oponente lo acepte mediante <see cref="RespondToTakeback"/>.
        /// </summary>
        public bool RequestTakeback(string requesterPlayerId, out TakebackRequest request, out string errorMessage)
        {
            request = default;
            errorMessage = null;

            if (IsMatchOver)
            {
                errorMessage = "No se puede solicitar deshacer jugada cuando la partida ya terminó.";
                return false;
            }

            if (!State.CanUndo)
            {
                errorMessage = "Aún no se han realizado movimientos en la partida.";
                return false;
            }

            if (PendingTakeback.HasValue)
            {
                errorMessage = "Ya existe una solicitud para deshacer jugada esperando respuesta.";
                return false;
            }

            Player requesterRole = GetPlayerRole(requesterPlayerId);
            if (requesterRole == Player.None)
            {
                errorMessage = "El jugador no pertenece a esta sala.";
                return false;
            }

            int pliesToUndo;
            if (State.CurrentPlayer != requesterRole)
            {
                pliesToUndo = 1;
            }
            else
            {
                if (State.UndoCount < 2)
                {
                    errorMessage = "Aún no has realizado un movimiento previo que puedas deshacer.";
                    return false;
                }
                pliesToUndo = 2;
            }

            Player responderRole = requesterRole == Player.X ? Player.O : Player.X;
            string responderId = responderRole == Player.X ? PlayerX.PlayerId : PlayerO.PlayerId;

            request = new TakebackRequest(
                requesterPlayerId,
                requesterRole,
                responderId,
                responderRole,
                pliesToUndo,
                DateTime.UtcNow);

            PendingTakeback = request;
            TakebackRequested?.Invoke(this, request);
            return true;
        }

        /// <summary>
        /// Permite al oponente aceptar o rechazar la solicitud pendiente para deshacer jugada (Takeback).
        /// </summary>
        public bool RespondToTakeback(string responderPlayerId, bool accept, out string errorMessage)
        {
            errorMessage = null;

            if (!PendingTakeback.HasValue)
            {
                errorMessage = "No hay ninguna solicitud pendiente para deshacer jugada.";
                return false;
            }

            TakebackRequest currentRequest = PendingTakeback.Value;

            if (accept && !string.Equals(responderPlayerId, currentRequest.ResponderPlayerId, StringComparison.Ordinal))
            {
                errorMessage = "Solo el oponente puede aceptar la solicitud para deshacer la jugada.";
                return false;
            }

            if (!accept &&
                !string.Equals(responderPlayerId, currentRequest.ResponderPlayerId, StringComparison.Ordinal) &&
                !string.Equals(responderPlayerId, currentRequest.RequesterPlayerId, StringComparison.Ordinal))
            {
                errorMessage = "El jugador no pertenece a esta sala.";
                return false;
            }

            PendingTakeback = null;

            if (accept)
            {
                for (int i = 0; i < currentRequest.PliesToUndo && State.CanUndo; i++)
                {
                    State.UndoMove();
                }
            }

            TakebackResolved?.Invoke(this, currentRequest, accept);
            return true;
        }

        /// <summary>
        /// Permite a un jugador rendirse (Resign), otorgando la victoria y los puntos a su oponente.
        /// </summary>
        public bool Resign(string playerId, out string errorMessage)
        {
            errorMessage = null;
            if (IsMatchOver)
            {
                errorMessage = "La partida ya ha finalizado.";
                return false;
            }

            Player role = GetPlayerRole(playerId);
            if (role == Player.None)
            {
                errorMessage = "El jugador no pertenece a esta sala.";
                return false;
            }

            // Si no se ha realizado ningún movimiento aún, cancelar la partida sin variación de puntos
            if (State.MoveCount == 0)
            {
                FinalizeMatch(Player.None, MatchEndReason.Aborted);
                return true;
            }

            Player winner = role == Player.X ? Player.O : Player.X;
            FinalizeMatch(winner, MatchEndReason.Resignation);
            return true;
        }

        /// <summary>
        /// Cancela la partida si transcurrieron los 30 segundos iniciales sin que se realice el primer movimiento.
        /// No suma ni resta puntos de clasificación.
        /// </summary>
        public bool AbortByFirstMoveTimeout()
        {
            if (IsMatchOver || State.MoveCount > 0)
            {
                return false;
            }

            FinalizeMatch(Player.None, MatchEndReason.Aborted);
            return true;
        }

        /// <summary>
        /// Declara el fin de la partida por desconexión o agotamiento de tiempo de un jugador.
        /// </summary>
        public bool ForfeitByDisconnectOrTimeout(string disconnectedOrTimedOutPlayerId, bool isTimeout = false)
        {
            if (IsMatchOver)
            {
                return false;
            }

            Player role = GetPlayerRole(disconnectedOrTimedOutPlayerId);
            if (role == Player.None)
            {
                return false;
            }

            if (State.MoveCount == 0)
            {
                FinalizeMatch(Player.None, MatchEndReason.Aborted);
                return true;
            }

            Player winner = role == Player.X ? Player.O : Player.X;
            FinalizeMatch(winner, isTimeout ? MatchEndReason.Timeout : MatchEndReason.DisconnectForfeit);
            return true;
        }

        /// <summary>
        /// Registra la solicitud de revancha de uno de los jugadores tras finalizar la partida.
        /// </summary>
        public bool RegisterRematchVote(string playerId, out Player requesterRole, out bool bothPlayersAccepted)
        {
            requesterRole = GetPlayerRole(playerId);
            bothPlayersAccepted = false;

            if (!IsMatchOver || requesterRole == Player.None)
            {
                return false;
            }

            if (requesterRole == Player.X) _rematchRequestedByX = true;
            if (requesterRole == Player.O) _rematchRequestedByO = true;

            bothPlayersAccepted = _rematchRequestedByX && _rematchRequestedByO;
            return true;
        }

        /// <summary>
        /// Limpia las solicitudes de revancha cuando uno de los jugadores rechaza la invitación.
        /// </summary>
        public void ClearRematchVotes()
        {
            _rematchRequestedByX = false;
            _rematchRequestedByO = false;
        }

        private void FinalizeMatch(Player winner, MatchEndReason reason)
        {
            PendingTakeback = null;
            EndReason = reason;
            EffectiveWinner = winner;

            // Garantizar que el GameState quede marcado como terminado con el ganador efectivo antes de exportar FEN
            State.ForceConclude(winner);

            int eloXBefore = PlayerX.GetRatingForTimeControl(TimeControl);
            int eloOBefore = PlayerO.GetRatingForTimeControl(TimeControl);
            EloMatchUpdate update;

            if (reason == MatchEndReason.Aborted)
            {
                update = new EloMatchUpdate(eloXBefore, 0, eloOBefore, 0);
            }
            else
            {
                update = EloRatingCalculator.CalculateMatchRatingChange(
                    eloXBefore,
                    PlayerX.GamesPlayed,
                    eloOBefore,
                    PlayerO.GamesPlayed,
                    winner);

                PlayerX.ApplyMatchResultForTimeControl(TimeControl, Player.X, winner, update.NewRatingX);
                PlayerO.ApplyMatchResultForTimeControl(TimeControl, Player.O, winner, update.NewRatingO);
            }

            CompletedEloUpdate = update;
            CompletedMatchRecord = new MatchRecord
            {
                MatchId = RoomId,
                PlayedAtUtc = DateTime.UtcNow,
                PlayerXName = PlayerX.Username,
                PlayerOName = PlayerO.Username,
                PlayerXEloBefore = eloXBefore,
                PlayerOEloBefore = eloOBefore,
                PlayerXEloAfter = update.NewRatingX,
                PlayerOEloAfter = update.NewRatingO,
                Winner = winner,
                EndReason = reason,
                FinalFen = GameSerializer.ToFen(State),
                MoveTranscript = GameSerializer.ExportMoveHistory(State)
            };

            MatchConcluded?.Invoke(this, CompletedMatchRecord, update);
        }
    }
}
