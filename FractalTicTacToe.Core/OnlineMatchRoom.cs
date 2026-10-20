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
    /// Valida turnos por identidad de cuenta, gestiona solicitudes de Takeback con consentimiento mutuo
    /// (estilo Lichess / Chess.com), rendiciones y actualización oficial de ELO e historial.
    /// </summary>
    public sealed class OnlineMatchRoom
    {
        public string RoomId { get; }
        public PlayerProfile PlayerX { get; }
        public PlayerProfile PlayerO { get; }
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

        public OnlineMatchRoom(string roomId, PlayerProfile playerX, PlayerProfile playerO)
        {
            if (playerX == null) throw new ArgumentNullException(nameof(playerX));
            if (playerO == null) throw new ArgumentNullException(nameof(playerO));

            RoomId = string.IsNullOrWhiteSpace(roomId) ? Guid.NewGuid().ToString("N") : roomId;
            PlayerX = playerX;
            PlayerO = playerO;
            State = new GameState();
            EndReason = MatchEndReason.InProgress;
            EffectiveWinner = Player.None;
        }

        /// <summary>
        /// Obtiene el rol (<see cref="Player.X"/>, <see cref="Player.O"/> o <see cref="Player.None"/>) asignado a un ID de cuenta.
        /// </summary>
        public Player GetPlayerRole(string playerId)
        {
            if (string.Equals( playerId, PlayerX.PlayerId, StringComparison.Ordinal))
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
            // se rechaza implícitamente el Takeback (comportamiento estándar de Lichess/Chess.com).
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

            // Regla estilo ajedrez (Lichess):
            // - Si pide deshacer quien acaba de tirar (actualmente es turno del rival): se deshace 1 medio turno (1 ply).
            // - Si pide deshacer quien tiene el turno actual: necesita que existan al menos 2 medios turnos para regresar a su propio turno previo (2 plies).
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
        /// Solo el oponente (<see cref="TakebackRequest.ResponderPlayerId"/>) está autorizado para aceptar.
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

            // El propio solicitante puede cancelar su solicitud enviando accept = false,
            // pero SOLO el oponente (ResponderPlayerId) puede aceptarla (accept = true).
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
        /// Permite a un jugador rendirse (Resign), otorgando la victoria y los puntos ELO a su oponente.
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

            Player winner = role == Player.X ? Player.O : Player.X;
            FinalizeMatch(winner, MatchEndReason.Resignation);
            return true;
        }

        /// <summary>
        /// Declara el fin de la partida por desconexión o agotamiento de tiempo de un jugador.
        /// </summary>
        public bool ForfeitByDisconnectOrTimeout(string disconnectedPlayerId, bool isTimeout = false)
        {
            if (IsMatchOver)
            {
                return false;
            }

            Player role = GetPlayerRole(disconnectedPlayerId);
            if (role == Player.None)
            {
                return false;
            }

            Player winner = role == Player.X ? Player.O : Player.X;
            FinalizeMatch(winner, isTimeout ? MatchEndReason.Timeout : MatchEndReason.DisconnectForfeit);
            return true;
        }

        private void FinalizeMatch(Player winner, MatchEndReason reason)
        {
            PendingTakeback = null;
            EndReason = reason;
            EffectiveWinner = winner;

            int eloXBefore = PlayerX.EloRating;
            int eloOBefore = PlayerO.EloRating;

            EloMatchUpdate update = EloRatingCalculator.CalculateMatchRatingChange(
                eloXBefore,
                PlayerX.GamesPlayed,
                eloOBefore,
                PlayerO.GamesPlayed,
                winner);

            PlayerX.ApplyMatchResult(Player.X, winner, update.NewRatingX);
            PlayerO.ApplyMatchResult(Player.O, winner, update.NewRatingO);

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
