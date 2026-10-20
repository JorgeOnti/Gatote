using System;
using Godot;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.GodotClient
{
    /// <summary>
    /// Pantalla principal 2D optimizada para dispositivos móviles (Portrait 9:16) en Godot 4 (.NET / C#).
    /// Soporta juego local (Humano vs. Bot Minimax en 4 dificultades y 2 Jugadores Pasa y Juega)
    /// y juego Multijugador en Línea (<see cref="OnlineMultiplayerClient"/>) con Matchmaking por ELO
    /// y confirmación de Takeback estilo ajedrez (Aceptar/Rechazar).
    /// </summary>
    public partial class MobileGameScreen : Control
    {
        private GameSessionController _localSession;
        private OnlineMultiplayerClient _onlineClient;
        private bool _isOnlineMode;

        // Referencias de UI para actualización reactiva en O(1)
        private readonly PanelContainer[] _macroBoardPanels = new PanelContainer[GameState.BoardCount];
        private readonly Button[,] _cellButtons = new Button[GameState.BoardCount, GameState.CellsPerBoard];

        private Label _playerXTitleLabel;
        private Label _playerXEloLabel;
        private Label _playerOTitleLabel;
        private Label _playerOEloLabel;
        private Label _statusBannerLabel;
        private Label _telemetryLabel;

        // Paneles contextuales para Modo Online y Solicitud de Takeback del Oponente
        private PanelContainer _onlineConnectPanel;
        private LineEdit _usernameInput;
        private LineEdit _serverEndpointInput;
        private Button _connectQueueButton;

        private PanelContainer _takebackOfferPanel;
        private Label _takebackOfferLabel;

        private Button _modeButton;
        private Button _undoButton;
        private Button _redoButton;
        private Button _hintButton;
        private Button _newGameOrResignButton;

        private Move? _currentHintMove;
        private int _presetIndex = 2; // Inicia en Humano vs Bot (Difícil)

        // Paleta de colores moderna (Dark Slate Mobile UI)
        private static readonly Color BgColor = new Color(0.06f, 0.08f, 0.12f);
        private static readonly Color CardBgColor = new Color(0.11f, 0.14f, 0.20f);
        private static readonly Color ActiveMacroBorderColor = new Color(0.18f, 0.88f, 0.52f);
        private static readonly Color InactiveMacroBorderColor = new Color(0.22f, 0.26f, 0.34f);
        private static readonly Color PlayerXColor = new Color(1.00f, 0.36f, 0.42f);
        private static readonly Color PlayerOColor = new Color(0.24f, 0.74f, 1.00f);
        private static readonly Color LastMoveHighlightColor = new Color(0.98f, 0.78f, 0.20f);
        private static readonly Color HintHighlightColor = new Color(0.72f, 0.42f, 1.00f);

        public override void _Ready()
        {
            _localSession = new GameSessionController(SessionMode.HumanVsBotX, BotDifficulty.Hard);
            _localSession.StateChanged += OnAnySessionStateChanged;

            _onlineClient = new OnlineMultiplayerClient();
            _onlineClient.StateChanged += OnAnySessionStateChanged;
            _onlineClient.Authenticated += OnOnlineAuthenticated;
            _onlineClient.ErrorReceived += OnOnlineErrorReceived;

            BuildMobileLayout();
            RefreshUI();
        }

        public override void _ExitTree()
        {
            if (_localSession != null)
            {
                _localSession.StateChanged -= OnAnySessionStateChanged;
            }

            if (_onlineClient != null)
            {
                _onlineClient.StateChanged -= OnAnySessionStateChanged;
                _onlineClient.Authenticated -= OnOnlineAuthenticated;
                _onlineClient.ErrorReceived -= OnOnlineErrorReceived;
                _onlineClient.Dispose();
            }
        }

        private void BuildMobileLayout()
        {
            var background = new ColorRect { Color = BgColor };
            background.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(background);

            var safeMargin = new MarginContainer();
            safeMargin.SetAnchorsPreset(LayoutPreset.FullRect);
            safeMargin.AddThemeConstantOverride("margin_left", 28);
            safeMargin.AddThemeConstantOverride("margin_right", 28);
            safeMargin.AddThemeConstantOverride("margin_top", 36);
            safeMargin.AddThemeConstantOverride("margin_bottom", 36);
            AddChild(safeMargin);

            var mainColumn = new VBoxContainer();
            mainColumn.AddThemeConstantOverride("separation", 18);
            safeMargin.AddChild(mainColumn);

            // 1. Encabezado y Tarjetas de Jugadores (ELO & Rango)
            BuildHeaderAndProfiles(mainColumn);

            // 2. Barra de Conexión Online (visible solo cuando se elige Modo Online)
            BuildOnlineConnectionPanel(mainColumn);

            // 3. Banner de Confirmación de Takeback del Rival (visible cuando el oponente solicita deshacer)
            BuildTakebackConsentBanner(mainColumn);

            // 4. Banner de Estado (Turno / Tablero Forzado / Victoria por Muerte Súbita)
            BuildStatusBanner(mainColumn);

            // 5. Tablero Fractal 9x9 cuadrado y centrado
            BuildFractalBoardGrid(mainColumn);

            // 6. Barra de Telemetría (Nodos Minimax, TT Hits, Estado de Red)
            _telemetryLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _telemetryLabel.AddThemeFontSizeOverride("font_size", 20);
            _telemetryLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.82f));
            mainColumn.AddChild(_telemetryLabel);

            // 7. Barra inferior de botones táctiles
            BuildBottomActionBar(mainColumn);
        }

        private void BuildHeaderAndProfiles(VBoxContainer parent)
        {
            var titleLabel = new Label
            {
                Text = "FRACTAL TIC-TAC-TOE • MUERTE SÚBITA",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titleLabel.AddThemeFontSizeOverride("font_size", 32);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.96f, 1.0f));
            parent.AddChild(titleLabel);

            var profilesRow = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            profilesRow.AddThemeConstantOverride("separation", 16);
            parent.AddChild(profilesRow);

            var cardX = CreateStyledPanel(CardBgColor, PlayerXColor, borderWidth: 3, cornerRadius: 14);
            cardX.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var boxX = new VBoxContainer();
            _playerXTitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _playerXTitleLabel.AddThemeFontSizeOverride("font_size", 24);
            _playerXTitleLabel.AddThemeColorOverride("font_color", PlayerXColor);
            _playerXEloLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _playerXEloLabel.AddThemeFontSizeOverride("font_size", 20);
            boxX.AddChild(_playerXTitleLabel);
            boxX.AddChild(_playerXEloLabel);
            cardX.AddChild(boxX);
            profilesRow.AddChild(cardX);

            var cardO = CreateStyledPanel(CardBgColor, PlayerOColor, borderWidth: 3, cornerRadius: 14);
            cardO.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var boxO = new VBoxContainer();
            _playerOTitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _playerOTitleLabel.AddThemeFontSizeOverride("font_size", 24);
            _playerOTitleLabel.AddThemeColorOverride("font_color", PlayerOColor);
            _playerOEloLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _playerOEloLabel.AddThemeFontSizeOverride("font_size", 20);
            boxO.AddChild(_playerOTitleLabel);
            boxO.AddChild(_playerOEloLabel);
            cardO.AddChild(boxO);
            profilesRow.AddChild(cardO);
        }

        private void BuildOnlineConnectionPanel(VBoxContainer parent)
        {
            _onlineConnectPanel = CreateStyledPanel(
                new Color(0.10f, 0.16f, 0.24f),
                new Color(0.24f, 0.64f, 0.96f),
                borderWidth: 2,
                cornerRadius: 12,
                contentMargin: 12);
            _onlineConnectPanel.Visible = false;

            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 12);

            _usernameInput = new LineEdit
            {
                PlaceholderText = "Tu Usuario",
                Text = $"Jugador_{new Random().Next(100, 999)}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            _usernameInput.AddThemeFontSizeOverride("font_size", 22);

            _serverEndpointInput = new LineEdit
            {
                PlaceholderText = "IP:Puerto",
                Text = "127.0.0.1:7777",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            _serverEndpointInput.AddThemeFontSizeOverride("font_size", 22);

            _connectQueueButton = CreateActionButton("🌐 Buscar Rival", OnConnectAndQueuePressed, minHeight: 62);

            row.AddChild(_usernameInput);
            row.AddChild(_serverEndpointInput);
            row.AddChild(_connectQueueButton);
            _onlineConnectPanel.AddChild(row);
            parent.AddChild(_onlineConnectPanel);
        }

        private void BuildTakebackConsentBanner(VBoxContainer parent)
        {
            _takebackOfferPanel = CreateStyledPanel(
                new Color(0.24f, 0.19f, 0.08f),
                LastMoveHighlightColor,
                borderWidth: 3,
                cornerRadius: 14,
                contentMargin: 12);
            _takebackOfferPanel.Visible = false;

            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 10);

            _takebackOfferLabel = new Label
            {
                Text = "Tu oponente solicita deshacer la última jugada. ¿Aceptas?",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _takebackOfferLabel.AddThemeFontSizeOverride("font_size", 24);
            _takebackOfferLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);
            col.AddChild(_takebackOfferLabel);

            var btnRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            btnRow.AddThemeConstantOverride("separation", 16);

            var acceptBtn = CreateActionButton("✓ Aceptar Deshacer", () => _onlineClient.RespondToOpponentTakeback(true), minHeight: 64);
            var declineBtn = CreateActionButton("✕ Rechazar", () => _onlineClient.RespondToOpponentTakeback(false), minHeight: 64);

            btnRow.AddChild(acceptBtn);
            btnRow.AddChild(declineBtn);
            col.AddChild(btnRow);

            _takebackOfferPanel.AddChild(col);
            parent.AddChild(_takebackOfferPanel);
        }

        private void BuildStatusBanner(VBoxContainer parent)
        {
            var bannerPanel = CreateStyledPanel(
                new Color(0.13f, 0.17f, 0.25f),
                new Color(0.30f, 0.38f, 0.52f),
                borderWidth: 2,
                cornerRadius: 14,
                contentMargin: 14);

            _statusBannerLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _statusBannerLabel.AddThemeFontSizeOverride("font_size", 26);
            bannerPanel.AddChild(_statusBannerLabel);
            parent.AddChild(bannerPanel);
        }

        private void BuildFractalBoardGrid(VBoxContainer parent)
        {
            var aspectContainer = new AspectRatioContainer
            {
                Ratio = 1.0f,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            parent.AddChild(aspectContainer);

            var macroGrid = new GridContainer
            {
                Columns = 3,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            macroGrid.AddThemeConstantOverride("h_separation", 16);
            macroGrid.AddThemeConstantOverride("v_separation", 16);
            aspectContainer.AddChild(macroGrid);

            for (int boardIdx = 0; boardIdx < GameState.BoardCount; boardIdx++)
            {
                var macroPanel = CreateStyledPanel(
                    CardBgColor,
                    InactiveMacroBorderColor,
                    borderWidth: 4,
                    cornerRadius: 16,
                    contentMargin: 10);

                macroPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                macroPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
                _macroBoardPanels[boardIdx] = macroPanel;
                macroGrid.AddChild(macroPanel);

                var innerGrid = new GridContainer
                {
                    Columns = 3,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ExpandFill
                };
                innerGrid.AddThemeConstantOverride("h_separation", 8);
                innerGrid.AddThemeConstantOverride("v_separation", 8);
                macroPanel.AddChild(innerGrid);

                for (int cellIdx = 0; cellIdx < GameState.CellsPerBoard; cellIdx++)
                {
                    int capturedBoard = boardIdx;
                    int capturedCell = cellIdx;

                    var cellBtn = new Button
                    {
                        SizeFlagsHorizontal = SizeFlags.ExpandFill,
                        SizeFlagsVertical = SizeFlags.ExpandFill,
                        FocusMode = FocusModeEnum.None
                    };
                    cellBtn.AddThemeFontSizeOverride("font_size", 34);
                    cellBtn.Pressed += () => OnCellTapped(capturedBoard, capturedCell);

                    _cellButtons[boardIdx, cellIdx] = cellBtn;
                    innerGrid.AddChild(cellBtn);
                }
            }
        }

        private void BuildBottomActionBar(VBoxContainer parent)
        {
            var topControlsRow = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            topControlsRow.AddThemeConstantOverride("separation", 14);
            parent.AddChild(topControlsRow);

            _undoButton = CreateActionButton("⟲ Deshacer", OnUndoPressed);
            _redoButton = CreateActionButton("⟳ Rehacer", OnRedoPressed);
            _hintButton = CreateActionButton("💡 Pista IA", OnHintPressed);

            topControlsRow.AddChild(_undoButton);
            topControlsRow.AddChild(_redoButton);
            topControlsRow.AddChild(_hintButton);

            var bottomControlsRow = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            bottomControlsRow.AddThemeConstantOverride("separation", 14);
            parent.AddChild(bottomControlsRow);

            _modeButton = CreateActionButton("Modo: Vs Bot (Difícil)", OnCycleModePressed);
            _newGameOrResignButton = CreateActionButton("★ Nueva Partida", OnNewMatchOrResignPressed);

            bottomControlsRow.AddChild(_modeButton);
            bottomControlsRow.AddChild(_newGameOrResignButton);
        }

        private async void OnCellTapped(int boardIndex, int cellIndex)
        {
            _currentHintMove = null;

            if (_isOnlineMode)
            {
                _onlineClient.TrySendMove(boardIndex, cellIndex);
                return;
            }

            if (!_localSession.TryPlayHumanMove(boardIndex, cellIndex))
            {
                return;
            }

            if (_localSession.IsCurrentTurnBot() && !_localSession.State.IsGameOver)
            {
                await _localSession.PlayBotTurnAsync();
            }
        }

        private async void OnConnectAndQueuePressed()
        {
            string username = string.IsNullOrWhiteSpace(_usernameInput.Text) ? "Jugador" : _usernameInput.Text.Trim();
            string endpoint = string.IsNullOrWhiteSpace(_serverEndpointInput.Text) ? "127.0.0.1:7777" : _serverEndpointInput.Text.Trim();

            string host = "127.0.0.1";
            int port = 7777;
            string[] parts = endpoint.Split(':');
            if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0])) host = parts[0];
            if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedPort)) port = parsedPort;

            try
            {
                if (!_onlineClient.IsConnected)
                {
                    _telemetryLabel.Text = $"Conectando a {host}:{port} como '{username}'...";
                    await _onlineClient.ConnectAndLoginAsync(host, port, username);
                }
                else
                {
                    _onlineClient.JoinMatchmakingQueue();
                }
            }
            catch (Exception ex)
            {
                _telemetryLabel.Text = $"No se pudo conectar al servidor ({host}:{port}): {ex.Message}";
            }
        }

        private void OnOnlineAuthenticated(string username, int elo, string rank)
        {
            // Una vez autenticado, entra automáticamente a la cola de Matchmaking por ELO
            _onlineClient.JoinMatchmakingQueue();
        }

        private void OnOnlineErrorReceived(string errorMsg)
        {
            Callable.From(() =>
            {
                _telemetryLabel.Text = $"Servidor: {errorMsg}";
            }).CallDeferred();
        }

        private void OnUndoPressed()
        {
            _currentHintMove = null;

            if (_isOnlineMode)
            {
                if (_onlineClient.RequestTakeback())
                {
                    _telemetryLabel.Text = "Solicitud para deshacer enviada. El oponente debe aceptarla.";
                }
                return;
            }

            _localSession.Undo();
        }

        private void OnRedoPressed()
        {
            _currentHintMove = null;
            if (!_isOnlineMode)
            {
                _localSession.Redo();
            }
        }

        private async void OnHintPressed()
        {
            if (_isOnlineMode || _localSession.State.IsGameOver || _localSession.IsBotThinking || _localSession.IsCurrentTurnBot())
            {
                return;
            }

            _hintButton.Disabled = true;
            MinimaxSearchResult hint = await _localSession.RequestHintAsync();
            _currentHintMove = hint.BestMove;
            _telemetryLabel.Text =
                $"Pista IA: Tablero {hint.BestMove.BoardIndex}, Celda {hint.BestMove.CellIndex} " +
                $"(Eval {hint.Score}, Prof. {hint.MaxDepthReached}, {hint.ElapsedMs} ms)";
            _hintButton.Disabled = false;
            RefreshUI();
        }

        private void OnCycleModePressed()
        {
            _currentHintMove = null;
            _presetIndex = (_presetIndex + 1) % 6;

            if (_presetIndex == 5)
            {
                _isOnlineMode = true;
                _modeButton.Text = "Modo: 🌐 Online (ELO)";
                RefreshUI();
                return;
            }

            if (_isOnlineMode)
            {
                _onlineClient.Disconnect();
                _isOnlineMode = false;
            }

            switch (_presetIndex)
            {
                case 0:
                    _localSession.ConfigureSession(SessionMode.HumanVsBotX, BotDifficulty.Easy, _localSession.ProfileX);
                    _modeButton.Text = "Modo: Vs Bot (Fácil)";
                    break;
                case 1:
                    _localSession.ConfigureSession(SessionMode.HumanVsBotX, BotDifficulty.Medium, _localSession.ProfileX);
                    _modeButton.Text = "Modo: Vs Bot (Medio)";
                    break;
                case 2:
                    _localSession.ConfigureSession(SessionMode.HumanVsBotX, BotDifficulty.Hard, _localSession.ProfileX);
                    _modeButton.Text = "Modo: Vs Bot (Difícil)";
                    break;
                case 3:
                    _localSession.ConfigureSession(SessionMode.HumanVsBotX, BotDifficulty.Master, _localSession.ProfileX);
                    _modeButton.Text = "Modo: Vs Bot (Maestro)";
                    break;
                case 4:
                    _localSession.ConfigureSession(SessionMode.PassAndPlay2P, BotDifficulty.Hard, _localSession.ProfileX);
                    _modeButton.Text = "Modo: 2 Jugadores (Móvil)";
                    break;
            }
        }

        private void OnNewMatchOrResignPressed()
        {
            _currentHintMove = null;

            if (_isOnlineMode)
            {
                if (_onlineClient.IsInActiveMatch)
                {
                    _onlineClient.ResignMatch();
                }
                else if (_onlineClient.IsConnected)
                {
                    _onlineClient.JoinMatchmakingQueue();
                }
                else
                {
                    OnConnectAndQueuePressed();
                }
                return;
            }

            _localSession.StartNewMatch();
        }

        private void OnAnySessionStateChanged()
        {
            Callable.From(RefreshUI).CallDeferred();
        }

        private void RefreshUI()
        {
            _onlineConnectPanel.Visible = _isOnlineMode;
            _takebackOfferPanel.Visible = _isOnlineMode && _onlineClient.HasIncomingTakebackOffer;

            if (_isOnlineMode)
            {
                RefreshOnlineUI();
            }
            else
            {
                RefreshLocalUI();
            }
        }

        private void RefreshOnlineUI()
        {
            GameState state = _onlineClient.State;
            Move? lastMove = _onlineClient.LastNetworkMove ?? state.GetLastMove();

            string localTag = _onlineClient.AssignedRole != Player.None
                ? $"{_onlineClient.LocalUsername} (Tú - {_onlineClient.AssignedRole})"
                : (_onlineClient.LocalUsername ?? "Sin conectar");

            string oppTag = !string.IsNullOrEmpty(_onlineClient.OpponentUsername)
                ? _onlineClient.OpponentUsername
                : "Esperando rival...";

            if (_onlineClient.AssignedRole == Player.O)
            {
                _playerXTitleLabel.Text = $"[X] {oppTag}";
                _playerXEloLabel.Text = $"{_onlineClient.OpponentElo} ELO";
                _playerOTitleLabel.Text = $"[O] {localTag}";
                _playerOEloLabel.Text = $"{_onlineClient.LocalElo} ELO • {_onlineClient.LocalRankTitle}";
            }
            else
            {
                _playerXTitleLabel.Text = $"[X] {localTag}";
                _playerXEloLabel.Text = $"{_onlineClient.LocalElo} ELO • {_onlineClient.LocalRankTitle}";
                _playerOTitleLabel.Text = $"[O] {oppTag}";
                _playerOEloLabel.Text = $"{_onlineClient.OpponentElo} ELO";
            }

            if (!_onlineClient.IsConnected)
            {
                _statusBannerLabel.Text = "Modo Online • Ingresa tu usuario y presiona 'Buscar Rival'";
            }
            else if (_onlineClient.IsInQueue)
            {
                _statusBannerLabel.Text = $"Buscando oponente compatible con {_onlineClient.LocalElo} ELO...";
            }
            else if (state.IsGameOver)
            {
                _statusBannerLabel.Text = state.Winner != Player.None
                    ? $"¡FIN DE PARTIDA ONLINE! GANADOR: JUGADOR {state.Winner}"
                    : "¡EMPATE GLOBAL ONLINE!";
            }
            else if (_onlineClient.IsWaitingForOpponentTakebackResponse)
            {
                _statusBannerLabel.Text = "Esperando que tu oponente acepte o rechace deshacer la jugada...";
            }
            else
            {
                bool isMyTurn = state.CurrentPlayer == _onlineClient.AssignedRole;
                string turnText = isMyTurn ? "¡ES TU TURNO!" : $"Turno de tu rival ({state.CurrentPlayer})";
                string boardText = state.ActiveBoardIndex == GameState.FreeMoveBoardIndex
                    ? "TIRO LIBRE"
                    : $"TABLERO FORZADO #{state.ActiveBoardIndex}";
                _statusBannerLabel.Text = $"{turnText} • {boardText}";
            }

            if (_onlineClient.HasIncomingTakebackOffer)
            {
                _takebackOfferLabel.Text =
                    $"⚠ El Jugador {_onlineClient.IncomingTakebackRequester} pide permiso para deshacer " +
                    $"{_onlineClient.IncomingTakebackPlies} medio(s) turno(s). ¿Aceptas?";
            }

            bool allowTap = _onlineClient.IsInActiveMatch &&
                            state.CurrentPlayer == _onlineClient.AssignedRole &&
                            !_onlineClient.IsWaitingForOpponentTakebackResponse;

            RenderBoardCells(state, lastMove, allowTap);

            _undoButton.Text = "⟲ Pedir Deshacer";
            _undoButton.Disabled = !_onlineClient.IsInActiveMatch ||
                                   !state.CanUndo ||
                                   _onlineClient.HasIncomingTakebackOffer ||
                                   _onlineClient.IsWaitingForOpponentTakebackResponse;
            _redoButton.Disabled = true;
            _hintButton.Disabled = true;
            _newGameOrResignButton.Text = _onlineClient.IsInActiveMatch ? "🏳 Rendirse" : "🌐 Buscar Rival";
        }

        private void RefreshLocalUI()
        {
            GameState state = _localSession.State;
            Move? lastMove = state.GetLastMove();

            _playerXTitleLabel.Text = $"[X] {_localSession.ProfileX.Username}";
            _playerXEloLabel.Text = $"{_localSession.ProfileX.EloRating} ELO • {_localSession.ProfileX.RankTitle} ({_localSession.ProfileX.Wins}V/{_localSession.ProfileX.Losses}D)";

            _playerOTitleLabel.Text = $"[O] {_localSession.ProfileO.Username}";
            _playerOEloLabel.Text = $"{_localSession.ProfileO.EloRating} ELO • {_localSession.ProfileO.RankTitle} ({_localSession.ProfileO.Wins}V/{_localSession.ProfileO.Losses}D)";

            if (state.IsGameOver)
            {
                if (state.Winner != Player.None)
                {
                    string deltaInfo = _localSession.LastEloUpdate.HasValue
                        ? $"  |  ELO: X ({FormatDelta(_localSession.LastEloUpdate.Value.DeltaX)}), O ({FormatDelta(_localSession.LastEloUpdate.Value.DeltaO)})"
                        : string.Empty;
                    _statusBannerLabel.Text = $"¡VICTORIA POR MUERTE SÚBITA! GANADOR: JUGADOR {state.Winner}{deltaInfo}";
                }
                else
                {
                    _statusBannerLabel.Text = "¡EMPATE GLOBAL! Las 81 casillas están llenas.";
                }
            }
            else if (_localSession.IsBotThinking)
            {
                _statusBannerLabel.Text = $"Bot Minimax ({state.CurrentPlayer}) calculando jugada óptima...";
            }
            else if (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex)
            {
                _statusBannerLabel.Text = $"Turno de {state.CurrentPlayer} • TIRO LIBRE (Elige cualquier tablero en verde)";
            }
            else
            {
                _statusBannerLabel.Text = $"Turno de {state.CurrentPlayer} • TABLERO FORZADO #{state.ActiveBoardIndex}";
            }

            if (_localSession.LastBotSearchResult.HasValue)
            {
                MinimaxSearchResult r = _localSession.LastBotSearchResult.Value;
                _telemetryLabel.Text =
                    $"Último cálculo IA: {r.BestMove} | Eval: {r.Score} | Prof: {r.MaxDepthReached} | " +
                    $"Nodos: {r.NodesEvaluated:N0} | TT Hits: {r.TranspositionHits:N0} | {r.ElapsedMs} ms";
            }
            else if (!_currentHintMove.HasValue)
            {
                _telemetryLabel.Text = $"Movimientos: {state.MoveCount}/81  •  Partidas en sesión: {_localSession.MatchHistory.Count}";
            }

            bool allowTap = !state.IsGameOver && !_localSession.IsBotThinking && !_localSession.IsCurrentTurnBot();
            RenderBoardCells(state, lastMove, allowTap);

            _undoButton.Text = "⟲ Deshacer";
            _undoButton.Disabled = _localSession.IsBotThinking || !state.CanUndo;
            _redoButton.Disabled = _localSession.IsBotThinking || !state.CanRedo;
            _hintButton.Disabled = _localSession.IsBotThinking || state.IsGameOver || _localSession.IsCurrentTurnBot();
            _newGameOrResignButton.Text = "★ Nueva Partida";
        }

        private void RenderBoardCells(GameState state, Move? lastMove, bool allowHumanInput)
        {
            for (int board = 0; board < GameState.BoardCount; board++)
            {
                bool isBoardActive = !state.IsGameOver &&
                                     (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex ||
                                      state.ActiveBoardIndex == board);

                var macroStyle = (StyleBoxFlat)_macroBoardPanels[board].GetThemeStylebox("panel");
                macroStyle.BorderColor = isBoardActive ? ActiveMacroBorderColor : InactiveMacroBorderColor;
                macroStyle.BgColor = isBoardActive
                    ? new Color(0.13f, 0.18f, 0.24f)
                    : new Color(0.08f, 0.10f, 0.15f);

                for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                {
                    Button btn = _cellButtons[board, cell];
                    Player occupant = state.GetCell(board, cell);

                    bool isLastMove = lastMove.HasValue &&
                                      lastMove.Value.BoardIndex == board &&
                                      lastMove.Value.CellIndex == cell;

                    bool isHintMove = _currentHintMove.HasValue &&
                                      _currentHintMove.Value.BoardIndex == board &&
                                      _currentHintMove.Value.CellIndex == cell;

                    StyleCellButton(btn, occupant, isBoardActive && allowHumanInput, isLastMove, isHintMove);
                }
            }
        }

        private static void StyleCellButton(
            Button btn,
            Player occupant,
            bool isCellInteractive,
            bool isLastMove,
            bool isHintMove)
        {
            var style = new StyleBoxFlat
            {
                CornerRadiusTopLeft = 10,
                CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10,
                CornerRadiusBottomRight = 10
            };

            if (occupant == Player.X)
            {
                btn.Text = "X";
                btn.Disabled = true;
                btn.AddThemeColorOverride("font_disabled_color", PlayerXColor);
                style.BgColor = new Color(0.22f, 0.12f, 0.16f);
            }
            else if (occupant == Player.O)
            {
                btn.Text = "O";
                btn.Disabled = true;
                btn.AddThemeColorOverride("font_disabled_color", PlayerOColor);
                style.BgColor = new Color(0.10f, 0.18f, 0.28f);
            }
            else
            {
                btn.Text = string.Empty;
                btn.Disabled = !isCellInteractive;
                style.BgColor = isCellInteractive
                    ? new Color(0.18f, 0.23f, 0.31f)
                    : new Color(0.11f, 0.13f, 0.18f);
            }

            if (isLastMove)
            {
                style.BorderWidthLeft = 4;
                style.BorderWidthRight = 4;
                style.BorderWidthTop = 4;
                style.BorderWidthBottom = 4;
                style.BorderColor = LastMoveHighlightColor;
            }
            else if (isHintMove)
            {
                style.BorderWidthLeft = 4;
                style.BorderWidthRight = 4;
                style.BorderWidthTop = 4;
                style.BorderWidthBottom = 4;
                style.BorderColor = HintHighlightColor;
            }

            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeStyleboxOverride("hover", style);
            btn.AddThemeStyleboxOverride("pressed", style);
            btn.AddThemeStyleboxOverride("disabled", style);
        }

        private static PanelContainer CreateStyledPanel(
            Color bgColor,
            Color borderColor,
            int borderWidth,
            int cornerRadius,
            int contentMargin = 16)
        {
            var panel = new PanelContainer();
            var style = new StyleBoxFlat
            {
                BgColor = bgColor,
                BorderColor = borderColor,
                BorderWidthLeft = borderWidth,
                BorderWidthRight = borderWidth,
                BorderWidthTop = borderWidth,
                BorderWidthBottom = borderWidth,
                CornerRadiusTopLeft = cornerRadius,
                CornerRadiusTopRight = cornerRadius,
                CornerRadiusBottomLeft = cornerRadius,
                CornerRadiusBottomRight = cornerRadius,
                ContentMarginLeft = contentMargin,
                ContentMarginRight = contentMargin,
                ContentMarginTop = contentMargin,
                ContentMarginBottom = contentMargin
            };
            panel.AddThemeStyleboxOverride("panel", style);
            return panel;
        }

        private static Button CreateActionButton(string text, Action onPressed, float minHeight = 72f)
        {
            var btn = new Button
            {
                Text = text,
                CustomMinimumSize = new Vector2(0, minHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None
            };
            btn.AddThemeFontSizeOverride("font_size", 22);
            btn.Pressed += onPressed;
            return btn;
        }

        private static string FormatDelta(int delta) => delta >= 0 ? $"+{delta}" : delta.ToString();
    }
}
