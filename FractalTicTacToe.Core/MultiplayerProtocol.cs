using System;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Especificación y constructor de paquetes del protocolo de red en tiempo real para Fractal Tic-Tac-Toe.
    /// Basado en tramas de texto delimitadas por pipe (<c>|</c>) sobre TCP/WebSockets sin dependencias externas.
    /// </summary>
    public static class MultiplayerProtocol
    {
        // Comandos Cliente -> Servidor
        public const string CmdRegister = "REGISTER";               // REGISTER|<username>|<password>
        public const string CmdLogin = "LOGIN";                     // LOGIN|<username>|<password>
        public const string CmdQueue = "QUEUE";                     // QUEUE|<maxEloGap>|<timeControlCode>
        public const string CmdCancelQueue = "CANCEL_QUEUE";        // CANCEL_QUEUE
        public const string CmdMove = "MOVE";                       // MOVE|<roomId>|<boardIndex>|<cellIndex>
        public const string CmdTakebackRequest = "TAKEBACK_REQ";    // TAKEBACK_REQ|<roomId>
        public const string CmdTakebackRespond = "TAKEBACK_RESP";   // TAKEBACK_RESP|<roomId>|<ACCEPT|DECLINE>
        public const string CmdResign = "RESIGN";                   // RESIGN|<roomId>
        public const string CmdTimeout = "TIMEOUT";                 // TIMEOUT|<roomId>
        public const string CmdAbort = "ABORT";                     // ABORT|<roomId>
        public const string CmdRematchRequest = "REMATCH_REQ";      // REMATCH_REQ|<roomId>
        public const string CmdRematchRespond = "REMATCH_RESP";     // REMATCH_RESP|<roomId>|<ACCEPT|DECLINE>
        public const string CmdLeaderboard = "LEADERBOARD";         // LEADERBOARD
        public const string CmdHistory = "HISTORY";                 // HISTORY|<username>
        public const string CmdDeleteAccount = "DELETE_ACCOUNT";    // DELETE_ACCOUNT|<password>

        // Eventos Servidor -> Cliente
        public const string EvtAuthOk = "AUTH_OK";                  // AUTH_OK|<playerId>|<username>|<elo>|<rank>|<wins>|<losses>|<draws>
        public const string EvtQueued = "QUEUED";                   // QUEUED|<elo>|<timeControlCode>
        public const string EvtQueueCancelled = "QUEUE_CANCELLED";  // QUEUE_CANCELLED
        public const string EvtMatchStart = "MATCH_START";          // MATCH_START|<roomId>|<assignedRole>|<userX>|<eloX>|<userO>|<eloO>|<fen>|<timeControlCode>
        public const string EvtStateSync = "STATE_SYNC";            // STATE_SYNC|<roomId>|<lastBoard>|<lastCell>|<fen>|<movesTranscript>
        public const string EvtTakebackOffered = "TAKEBACK_OFFERED";// TAKEBACK_OFFERED|<roomId>|<requesterRole>|<pliesToUndo>
        public const string EvtTakebackResolved = "TAKEBACK_RESOLVED";// TAKEBACK_RESOLVED|<roomId>|<ACCEPTED|DECLINED>|<fen>|<movesTranscript>
        public const string EvtMatchEnd = "MATCH_END";              // MATCH_END|<roomId>|<winner>|<reason>|<newEloX>|<deltaX>|<newEloO>|<deltaO>|<fen>|<movesTranscript>
        public const string EvtRematchOffered = "REMATCH_OFFERED";  // REMATCH_OFFERED|<roomId>|<requesterRole>
        public const string EvtRematchDeclined = "REMATCH_DECLINED";// REMATCH_DECLINED|<roomId>|<declinerRole>
        public const string EvtAccountDeleted = "ACCOUNT_DELETED";  // ACCOUNT_DELETED|<username>
        public const string EvtError = "ERROR";                     // ERROR|<message>

        public static string BuildRegister(string username, string password) =>
            $"{CmdRegister}|{Sanitize(username)}|{Sanitize(password)}";

        public static string BuildLogin(string username, string password = "") =>
            string.IsNullOrEmpty(password)
                ? $"{CmdLogin}|{Sanitize(username)}"
                : $"{CmdLogin}|{Sanitize(username)}|{Sanitize(password)}";

        public static string BuildQueue(int maxEloGap = 400, string timeControlCode = "3+2") =>
            $"{CmdQueue}|{maxEloGap}|{Sanitize(timeControlCode)}";

        public static string BuildCancelQueue() => CmdCancelQueue;

        public static string BuildMove(string roomId, Move move) =>
            $"{CmdMove}|{roomId}|{move.BoardIndex}|{move.CellIndex}";

        public static string BuildTakebackRequest(string roomId) =>
            $"{CmdTakebackRequest}|{roomId}";

        public static string BuildTakebackResponse(string roomId, bool accept) =>
            $"{CmdTakebackRespond}|{roomId}|{(accept ? "ACCEPT" : "DECLINE")}";

        public static string BuildResign(string roomId) => $"{CmdResign}|{roomId}";
        public static string BuildTimeout(string roomId) => $"{CmdTimeout}|{roomId}";
        public static string BuildAbort(string roomId) => $"{CmdAbort}|{roomId}";
        public static string BuildRematchRequest(string roomId) => $"{CmdRematchRequest}|{roomId}";
        public static string BuildRematchResponse(string roomId, bool accept) =>
            $"{CmdRematchRespond}|{roomId}|{(accept ? "ACCEPT" : "DECLINE")}";

        public static string BuildAuthOk(PlayerProfile profile) =>
            $"{EvtAuthOk}|{profile.PlayerId}|{profile.Username}|{profile.EloRating}|{profile.RankTitle}|" +
            $"{profile.Wins}|{profile.Losses}|{profile.Draws}";

        public static string BuildMatchStart(OnlineMatchRoom room, Player assignedRole) =>
            $"{EvtMatchStart}|{room.RoomId}|{assignedRole}|{room.PlayerX.Username}|{room.PlayerX.EloRating}|" +
            $"{room.PlayerO.Username}|{room.PlayerO.EloRating}|{GameSerializer.ToFen(room.State)}|{room.TimeControl.ToCode()}";

        public static string BuildStateSync(OnlineMatchRoom room, Move lastMove) =>
            $"{EvtStateSync}|{room.RoomId}|{lastMove.BoardIndex}|{lastMove.CellIndex}|" +
            $"{GameSerializer.ToFen(room.State)}|{GameSerializer.ExportMoveHistory(room.State)}";

        public static string BuildTakebackOffered(OnlineMatchRoom room, TakebackRequest request) =>
            $"{EvtTakebackOffered}|{room.RoomId}|{request.RequesterRole}|{request.PliesToUndo}";

        public static string BuildTakebackResolved(OnlineMatchRoom room, bool accepted) =>
            $"{EvtTakebackResolved}|{room.RoomId}|{(accepted ? "ACCEPTED" : "DECLINED")}|" +
            $"{GameSerializer.ToFen(room.State)}|{GameSerializer.ExportMoveHistory(room.State)}";

        public static string BuildMatchEnd(OnlineMatchRoom room, MatchRecord record, EloMatchUpdate eloUpdate) =>
            $"{EvtMatchEnd}|{room.RoomId}|{record.Winner}|{record.EndReason}|" +
            $"{eloUpdate.NewRatingX}|{eloUpdate.DeltaX}|{eloUpdate.NewRatingO}|{eloUpdate.DeltaO}|" +
            $"{record.FinalFen}|{record.MoveTranscript}";

        public static string BuildRematchOffered(OnlineMatchRoom room, Player requesterRole) =>
            $"{EvtRematchOffered}|{room.RoomId}|{requesterRole}";

        public static string BuildRematchDeclined(OnlineMatchRoom room, Player declinerRole) =>
            $"{EvtRematchDeclined}|{room.RoomId}|{declinerRole}";

        public static string BuildDeleteAccount(string password = "") =>
            $"{CmdDeleteAccount}|{Sanitize(password)}";

        public static string BuildAccountDeleted(string username) =>
            $"{EvtAccountDeleted}|{Sanitize(username)}";

        public static string BuildError(string message) => $"{EvtError}|{Sanitize(message)}";

        public static string[] SplitPacket(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return Array.Empty<string>();
            }
            return rawLine.Trim().Split('|');
        }

        private static string Sanitize(string text)
        {
            return (text ?? string.Empty).Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
