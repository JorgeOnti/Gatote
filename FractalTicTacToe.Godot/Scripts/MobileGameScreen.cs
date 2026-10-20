using System;
using Godot;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.GodotClient
{
    /// <summary>
    /// Pantalla principal 2D optimizada para dispositivos móviles (Portrait 9:16) en Godot 4 (.NET / C#).
    /// Incluye:
    /// - Tarjeta izquierda SIEMPRE asignada al jugador local ("Tú") mostrando su ficha [X] u [O] y tarjeta derecha para el rival.
    /// - Visualización de variación de Rating estilo Lichess (+20 / -20) junto al puntaje al finalizar la partida.
    /// - Reloj dual de partida por jugador (Bullet 1+1, Blitz 3+0, Blitz 3+2, Rápida 5+0) con décimas de segundo bajo presión.
    /// - Animaciones Tweens (Pop elástico al colocar ficha, pulso respiratorio en tableros activos y destello en la línea de 3 ganadora).
    /// </summary>
    public partial class MobileGameScreen : Control
    {
        private GameSessionController _localSession;
        private OnlineMultiplayerClient _onlineClient;
        private bool _isOnlineMode;

        // Referencias de UI para actualización reactiva en O(1)
        private readonly PanelContainer[] _macroBoardPanels = new PanelContainer[GameState.BoardCount];
        private readonly Button[,] _cellButtons = new Button[GameState.BoardCount, GameState.CellsPerBoard];

        // Tarjeta Izquierda = SIEMPRE Jugador Local ("Tú") | Tarjeta Derecha = SIEMPRE Oponente
        private PanelContainer _leftPlayerCard;
        private Label _leftPlayerTitleLabel;
        private Label _leftPlayerRatingLabel;
        private Label _leftClockLabel;

        private PanelContainer _rightPlayerCard;
        private Label _rightPlayerTitleLabel;
        private Label _rightPlayerRatingLabel;
        private Label _rightClockLabel;

        private PanelContainer _statusBannerPanel;
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
        private Button _timeControlButton;
        private Button _undoButton;
        private Button _redoButton;
        private Button _hintButton;
        private Button _newGameOrResignButton;

        private Move? _currentHintMove;
        private Move? _lastAnimatedMove;
        private bool _victoryLineAnimated;
        private int _presetIndex = 2; // Inicia en Humano vs Bot (Difícil)
        private int _timeControlIndex = 2; // Inicia en Blitz 3+2
        private double _pulseTime;

        private static readonly TimeControlConfig[] TimeControls =
        {
            TimeControlConfig.Bullet1Plus1,
            TimeControlConfig.Blitz3Plus0,
            TimeControlConfig.Blitz3Plus2,
            TimeControlConfig.Rapid5Plus0
        };

        // Paleta de colores moderna (Dark Slate Mobile UI)
        private static readonly Color BgColor = new Color(0.06f, 0.08f, 0.12f);
        private static readonly Color CardBgColor = new Color(0.11f, 0.14f, 0.20f);
        private static readonly Color ActiveMacroBorderColor = new Color(0.18f, 0.88f, 0.52f);
        private static readonly Color InactiveMacroBorderColor = new Color(0.22f, 0.26f, 0.34f);
        private static readonly Color PlayerXColor = new Color(1.00f, 0.36f, 0.42f);
        private static readonly Color PlayerOColor = new Color(0.24f, 0.74f, 1.00f);
        private static readonly Color LastMoveHighlightColor = new Color(0.98f, 0.78f, 0.20f);
        private static readonly Color WinningCellHighlightColor = new Color(0.20f, 0.98f, 0.58f);
        private static readonly Color HintHighlightColor = new Color(0.72f, 0.42f, 1.00f);
        private static readonly Color PositiveDeltaColor = new Color(0.25f, 0.95f, 0.55f);
        private static readonly Color NegativeDeltaColor = new Color(1.00f, 0.42f, 0.42f);

        public override void _Ready()
        {
            _localSession = new GameSessionController(
                SessionMode.HumanVsBotX,
                BotDifficulty.Hard,
                timeControl: TimeControls[_timeControlIndex]);
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

        public override void _Process(double delta)
        {
            _pulseTime += delta;

            // 1. Actualizar reloj de partida en tiempo real
            if (_isOnlineMode)
            {
                _onlineClient.TickClock(delta);
            }
            else
            {
                _localSession.TickClock(delta);
            }

            UpdateClockLabelsAndPulseGlow();
        }

        private void BuildMobileLayout()
        {
            var background = new ColorRect { Color = BgColor };
            background.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(background);

            var safeMargin = new MarginContainer();
            safeMargin.SetAnchorsPreset(LayoutPreset.FullRect);
            safeMargin.AddThemeConstantOverride("margin_left", 26);
            safeMargin.AddThemeConstantOverride("margin_right", 26);
            safeMargin.AddThemeConstantOverride("margin_top", 32);
            safeMargin.AddThemeConstantOverride("margin_bottom", 32);
            AddChild(safeMargin);

            var mainColumn = new VBoxContainer();
            mainColumn.AddThemeConstantOverride("separation", 14);
            safeMargin.AddChild(mainColumn);

            // 1. Encabezado y Tarjetas de Jugadores (Izquierda = Tú, Derecha = Rival, con Reloj y Rating)
            BuildHeaderAndProfiles(mainColumn);

            // 2. Barra de Conexión Online (visible solo cuando se elige Modo Online)
            BuildOnlineConnectionPanel(mainColumn);

            // 3. Banner de Confirmación de Takeback del Rival
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
            _telemetryLabel.AddThemeFontSizeOverride("font_size", 19);
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
            titleLabel.AddThemeFontSizeOverride("font_size", 30);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.96f, 1.0f));
            parent.AddChild(titleLabel);

            var profilesRow = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            profilesRow.AddThemeConstantOverride("separation", 14);
            parent.AddChild(profilesRow);

            // Tarjeta Izquierda: SIEMPRE Jugador Local ("Tú")
            _leftPlayerCard = CreateStyledPanel(CardBgColor, PlayerXColor, borderWidth: 3, cornerRadius: 14, contentMargin: 12);
            _leftPlayerCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var leftBox = new VBoxContainer();
            leftBox.AddThemeConstantOverride("separation", 4);

            _leftPlayerTitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _leftPlayerTitleLabel.AddThemeFontSizeOverride("font_size", 22);

            _leftPlayerRatingLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _leftPlayerRatingLabel.AddThemeFontSizeOverride("font_size", 19);

            _leftClockLabel = new Label
            {
                Text = "03:00",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _leftClockLabel.AddThemeFontSizeOverride("font_size", 28);

            leftBox.AddChild(_leftPlayerTitleLabel);
            leftBox.AddChild(_leftPlayerRatingLabel);
            leftBox.AddChild(_leftClockLabel);
            _leftPlayerCard.AddChild(leftBox);
            profilesRow.AddChild(_leftPlayerCard);

            // Tarjeta Derecha: SIEMPRE Oponente (Bot / Rival Online / Jugador 2)
            _rightPlayerCard = CreateStyledPanel(CardBgColor, PlayerOColor, borderWidth: 3, cornerRadius: 14, contentMargin: 12);
            _rightPlayerCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var rightBox = new VBoxContainer();
            rightBox.AddThemeConstantOverride("separation", 4);

            _rightPlayerTitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _rightPlayerTitleLabel.AddThemeFontSizeOverride("font_size", 22);

            _rightPlayerRatingLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _rightPlayerRatingLabel.AddThemeFontSizeOverride("font_size", 19);

            _rightClockLabel = new Label
            {
                Text = "03:00",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _rightClockLabel.AddThemeFontSizeOverride("font_size", 28);

            rightBox.AddChild(_rightPlayerTitleLabel);
            rightBox.AddChild(_rightPlayerRatingLabel);
            rightBox.AddChild(_rightClockLabel);
            _rightPlayerCard.AddChild(rightBox);
            profilesRow.AddChild(_rightPlayerCard);
        }

        private void BuildOnlineConnectionPanel(VBoxContainer parent)
        {
            _onlineConnectPanel = CreateStyledPanel(
                new Color(0.10f, 0.16f, 0.24f),
                new Color(0.24f, 0.64f, 0.96f),
                borderWidth: 2,
                cornerRadius: 12,
                contentMargin: 10);
            _onlineConnectPanel.Visible = false;

            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 10);

            _usernameInput = new LineEdit
            {
                PlaceholderText = "Tu Usuario",
                Text = $"Jugador_{new Random().Next(100, 999)}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            _usernameInput.AddThemeFontSizeOverride("font_size", 20);

            _serverEndpointInput = new LineEdit
            {
                PlaceholderText = "IP:Puerto",
                Text = "127.0.0.1:7777",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            _serverEndpointInput.AddThemeFontSizeOverride("font_size", 20);

            _connectQueueButton = CreateActionButton("🌐 Buscar Rival", OnConnectAndQueuePressed, minHeight: 58);

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
            _takebackOfferLabel.AddThemeFontSizeOverride("font_size", 22);
            _takebackOfferLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);
            col.AddChild(_takebackOfferLabel);

            var btnRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            btnRow.AddThemeConstantOverride("separation", 14);

            var acceptBtn = CreateActionButton("✓ Aceptar Deshacer", () => _onlineClient.RespondToOpponentTakeback(true), minHeight: 60);
            var declineBtn = CreateActionButton("✕ Rechazar", () => _onlineClient.RespondToOpponentTakeback(false), minHeight: 60);

            btnRow.AddChild(acceptBtn);
            btnRow.AddChild(declineBtn);
            col.AddChild(btnRow);

            _takebackOfferPanel.AddChild(col);
            parent.AddChild(_takebackOfferPanel);
        }

        private void BuildStatusBanner(VBoxContainer parent)
        {
            _statusBannerPanel = CreateStyledPanel(
                new Color(0.13f, 0.17f, 0.25f),
                new Color(0.30f, 0.38f, 0.52f),
                borderWidth: 2,
                cornerRadius: 14,
                contentMargin: 12);

            _statusBannerLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _statusBannerLabel.AddThemeFontSizeOverride("font_size", 24);
            _statusBannerPanel.AddChild(_statusBannerLabel);
            parent.AddChild(_statusBannerPanel);
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
            macroGrid.AddThemeConstantOverride("h_separation", 14);
            macroGrid.AddThemeConstantOverride("v_separation", 14);
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
            topControlsRow.AddThemeConstantOverride("separation", 12);
            parent.AddChild(topControlsRow);

            _undoButton = CreateActionButton("⟲ Deshacer", OnUndoPressed, minHeight: 64);
            _redoButton = CreateActionButton("⟳ Rehacer", OnRedoPressed, minHeight: 64);
            _hintButton = CreateActionButton("💡 Pista IA", OnHintPressed, minHeight: 64);
            _timeControlButton = CreateActionButton($"⏱ {TimeControls[_timeControlIndex].Name}", OnCycleTimeControlPressed, minHeight: 64);

            topControlsRow.AddChild(_undoButton);
            topControlsRow.AddChild(_redoButton);
            topControlsRow.AddChild(_hintButton);
            topControlsRow.AddChild(_timeControlButton);

            var bottomControlsRow = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            bottomControlsRow.AddThemeConstantOverride("separation", 12);
            parent.AddChild(bottomControlsRow);

            _modeButton = CreateActionButton("Modo: Vs Bot (Difícil)", OnCycleModePressed, minHeight: 68);
            _newGameOrResignButton = CreateActionButton("★ Nueva Partida", OnNewMatchOrResignPressed, minHeight: 68);

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

        private void OnOnlineAuthenticated(string username, int rating, string rank)
        {
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

        private void OnCycleTimeControlPressed()
        {
            _timeControlIndex = (_timeControlIndex + 1) % TimeControls.Length;
            TimeControlConfig cfg = TimeControls[_timeControlIndex];
            _timeControlButton.Text = $"⏱ {cfg.Name}";

            if (_isOnlineMode)
            {
                _onlineClient.Clock.Reset(cfg);
            }
            else
            {
                _localSession.SetTimeControl(cfg);
            }
        }

        private void OnCycleModePressed()
        {
            _currentHintMove = null;
            _lastAnimatedMove = null;
            _victoryLineAnimated = false;
            _presetIndex = (_presetIndex + 1) % 6;

            if (_presetIndex == 5)
            {
                _isOnlineMode = true;
                _modeButton.Text = "Modo: 🌐 Online";
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
            _lastAnimatedMove = null;
            _victoryLineAnimated = false;

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

            UpdateClockLabelsAndPulseGlow();
        }

        private void RefreshOnlineUI()
        {
            GameState state = _onlineClient.State;
            Move? lastMove = _onlineClient.LastNetworkMove ?? state.GetLastMove();

            // Determinar el rol del jugador local (por defecto X mientras aún no inicia la partida)
            Player myRole = _onlineClient.AssignedRole != Player.None ? _onlineClient.AssignedRole : Player.X;
            Player oppRole = myRole == Player.X ? Player.O : Player.X;

            Color myColor = myRole == Player.X ? PlayerXColor : PlayerOColor;
            Color oppColor = oppRole == Player.X ? PlayerXColor : PlayerOColor;

            SetPanelBorderColor(_leftPlayerCard, myColor);
            SetPanelBorderColor(_rightPlayerCard, oppColor);

            string myName = !string.IsNullOrEmpty(_onlineClient.LocalUsername) ? _onlineClient.LocalUsername : "Tú";
            string oppName = !string.IsNullOrEmpty(_onlineClient.OpponentUsername) ? _onlineClient.OpponentUsername : "Esperando rival...";

            // Calcular deltas de Rating estilo Lichess si la partida acaba de terminar
            int? myDelta = null;
            int? oppDelta = null;
            if (state.IsGameOver && _onlineClient.LastRatingUpdate.HasValue)
            {
                EloMatchUpdate upd = _onlineClient.LastRatingUpdate.Value;
                myDelta = myRole == Player.X ? upd.DeltaX : upd.DeltaO;
                oppDelta = oppRole == Player.X ? upd.DeltaX : upd.DeltaO;
            }

            // Tarjeta Izquierda = SIEMPRE el Jugador Local ("Tú") con su símbolo [X] u [O]
            _leftPlayerTitleLabel.Text = $"[{myRole}] {myName} (Tú)";
            _leftPlayerTitleLabel.AddThemeColorOverride("font_color", myColor);
            ConfigureRatingLabelWithDelta(_leftPlayerRatingLabel, _onlineClient.LocalElo, _onlineClient.LocalRankTitle, myDelta);

            // Tarjeta Derecha = SIEMPRE el Oponente con su símbolo [O] o [X]
            _rightPlayerTitleLabel.Text = $"[{oppRole}] {oppName}";
            _rightPlayerTitleLabel.AddThemeColorOverride("font_color", oppColor);
            string oppRank = EloRatingCalculator.GetRankTitle(_onlineClient.OpponentElo);
            ConfigureRatingLabelWithDelta(_rightPlayerRatingLabel, _onlineClient.OpponentElo, oppRank, oppDelta);

            if (!_onlineClient.IsConnected)
            {
                _statusBannerLabel.Text = "Modo Online • Ingresa tu usuario y presiona 'Buscar Rival'";
            }
            else if (_onlineClient.IsInQueue)
            {
                _statusBannerLabel.Text = $"Buscando oponente compatible con {_onlineClient.LocalElo} pts...";
            }
            else if (state.IsGameOver)
            {
                string reasonText = FormatEndReason(_onlineClient.LastEndReason);
                if (state.Winner == myRole)
                {
                    _statusBannerLabel.Text = $"¡VICTORIA ({reasonText})! Ganaste con [{myRole}]";
                }
                else if (state.Winner == oppRole)
                {
                    _statusBannerLabel.Text = $"DERROTA ({reasonText}) • Ganó {oppName} [{oppRole}]";
                }
                else
                {
                    _statusBannerLabel.Text = "¡EMPATE GLOBAL ONLINE!";
                }
            }
            else if (_onlineClient.IsWaitingForOpponentTakebackResponse)
            {
                _statusBannerLabel.Text = "Esperando que tu oponente acepte o rechace deshacer la jugada...";
            }
            else
            {
                bool isMyTurn = state.CurrentPlayer == myRole;
                string turnText = isMyTurn ? $"¡TU TURNO [{myRole}]!" : $"Turno de {oppName} [{oppRole}]";
                string boardText = state.ActiveBoardIndex == GameState.FreeMoveBoardIndex
                    ? "TIRO LIBRE"
                    : $"TABLERO FORZADO #{state.ActiveBoardIndex}";
                _statusBannerLabel.Text = $"{turnText} • {boardText}";
            }

            if (_onlineClient.HasIncomingTakebackOffer)
            {
                _takebackOfferLabel.Text =
                    $"⚠ {oppName} [{_onlineClient.IncomingTakebackRequester}] pide permiso para deshacer " +
                    $"{_onlineClient.IncomingTakebackPlies} medio(s) turno(s). ¿Aceptas?";
            }

            bool allowTap = _onlineClient.IsInActiveMatch &&
                            state.CurrentPlayer == myRole &&
                            !_onlineClient.IsWaitingForOpponentTakebackResponse;

            RenderBoardCellsAndAnimate(state, lastMove, allowTap);

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

            // En modo local: determinamos quién es el jugador humano local (Izquierda) y quién es el rival (Derecha)
            bool humanIsO = _localSession.Mode == SessionMode.BotVsHumanO;
            Player myRole = humanIsO ? Player.O : Player.X;
            Player oppRole = humanIsO ? Player.X : Player.O;

            PlayerProfile myProfile = myRole == Player.X ? _localSession.ProfileX : _localSession.ProfileO;
            PlayerProfile oppProfile = oppRole == Player.X ? _localSession.ProfileX : _localSession.ProfileO;

            Color myColor = myRole == Player.X ? PlayerXColor : PlayerOColor;
            Color oppColor = oppRole == Player.X ? PlayerXColor : PlayerOColor;

            SetPanelBorderColor(_leftPlayerCard, myColor);
            SetPanelBorderColor(_rightPlayerCard, oppColor);

            int? myDelta = null;
            int? oppDelta = null;
            if (state.IsGameOver && _localSession.LastEloUpdate.HasValue)
            {
                EloMatchUpdate upd = _localSession.LastEloUpdate.Value;
                myDelta = myRole == Player.X ? upd.DeltaX : upd.DeltaO;
                oppDelta = oppRole == Player.X ? upd.DeltaX : upd.DeltaO;
            }

            _leftPlayerTitleLabel.Text = $"[{myRole}] {myProfile.Username} (Tú)";
            _leftPlayerTitleLabel.AddThemeColorOverride("font_color", myColor);
            ConfigureRatingLabelWithDelta(_leftPlayerRatingLabel, myProfile.EloRating, myProfile.RankTitle, myDelta);

            _rightPlayerTitleLabel.Text = $"[{oppRole}] {oppProfile.Username}";
            _rightPlayerTitleLabel.AddThemeColorOverride("font_color", oppColor);
            ConfigureRatingLabelWithDelta(_rightPlayerRatingLabel, oppProfile.EloRating, oppProfile.RankTitle, oppDelta);

            if (state.IsGameOver)
            {
                string reasonText = FormatEndReason(_localSession.LastEndReason);
                if (state.Winner != Player.None)
                {
                    _statusBannerLabel.Text = $"¡FIN DE PARTIDA ({reasonText})! GANADOR: JUGADOR {state.Winner}";
                }
                else
                {
                    _statusBannerLabel.Text = "¡EMPATE GLOBAL! Las 81 casillas están llenas.";
                }
            }
            else if (_localSession.IsBotThinking)
            {
                _statusBannerLabel.Text = $"Bot Minimax [{state.CurrentPlayer}] calculando jugada óptima...";
            }
            else if (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex)
            {
                _statusBannerLabel.Text = $"Turno de [{state.CurrentPlayer}] • TIRO LIBRE (Elige cualquier tablero en verde)";
            }
            else
            {
                _statusBannerLabel.Text = $"Turno de [{state.CurrentPlayer}] • TABLERO FORZADO #{state.ActiveBoardIndex}";
            }

            if (_localSession.LastBotSearchResult.HasValue)
            {
                MinimaxSearchResult r = _localSession.LastBotSearchResult.Value;
                _telemetryLabel.Text =
                    $"IA: {r.BestMove} | Eval: {r.Score} | Prof: {r.MaxDepthReached} | " +
                    $"Nodos: {r.NodesEvaluated:N0} | TT Hits: {r.TranspositionHits:N0} | {r.ElapsedMs} ms";
            }
            else if (!_currentHintMove.HasValue)
            {
                _telemetryLabel.Text = $"Movimientos: {state.MoveCount}/81  •  Partidas en sesión: {_localSession.MatchHistory.Count}";
            }

            bool allowTap = !state.IsGameOver && !_localSession.IsBotThinking && !_localSession.IsCurrentTurnBot();
            RenderBoardCellsAndAnimate(state, lastMove, allowTap);

            _undoButton.Text = "⟲ Deshacer";
            _undoButton.Disabled = _localSession.IsBotThinking || !state.CanUndo;
            _redoButton.Disabled = _localSession.IsBotThinking || !state.CanRedo;
            _hintButton.Disabled = _localSession.IsBotThinking || state.IsGameOver || _localSession.IsCurrentTurnBot();
            _newGameOrResignButton.Text = "★ Nueva Partida";
        }

        private static void ConfigureRatingLabelWithDelta(Label label, int rating, string rankTitle, int? delta)
        {
            if (delta.HasValue)
            {
                string sign = delta.Value >= 0 ? "+" : "";
                label.Text = $"{rating} pts ({sign}{delta.Value}) • {rankTitle}";
                label.AddThemeColorOverride("font_color", delta.Value >= 0 ? PositiveDeltaColor : NegativeDeltaColor);
            }
            else
            {
                label.Text = $"{rating} pts • {rankTitle}";
                label.AddThemeColorOverride("font_color", new Color(0.82f, 0.86f, 0.94f));
            }
        }

        private void UpdateClockLabelsAndPulseGlow()
        {
            GameState state = _isOnlineMode ? _onlineClient.State : _localSession.State;
            MatchClock clock = _isOnlineMode ? _onlineClient.Clock : _localSession.Clock;

            Player myRole = _isOnlineMode
                ? (_onlineClient.AssignedRole != Player.None ? _onlineClient.AssignedRole : Player.X)
                : (_localSession.Mode == SessionMode.BotVsHumanO ? Player.O : Player.X);
            Player oppRole = myRole == Player.X ? Player.O : Player.X;

            double mySeconds = clock.GetRemainingSeconds(myRole);
            double oppSeconds = clock.GetRemainingSeconds(oppRole);

            _leftClockLabel.Text = $"⏱ {MatchClock.FormatClock(mySeconds)}";
            _rightClockLabel.Text = $"⏱ {MatchClock.FormatClock(oppSeconds)}";

            bool myClockActive = !state.IsGameOver && state.CurrentPlayer == myRole;
            bool oppClockActive = !state.IsGameOver && state.CurrentPlayer == oppRole;

            StyleClockLabel(_leftClockLabel, mySeconds, myClockActive);
            StyleClockLabel(_rightClockLabel, oppSeconds, oppClockActive);

            // Efecto visual de respiración (Breathing Glow) sobre los bordes de los tableros macro activos
            if (!state.IsGameOver)
            {
                float wave = 0.5f + 0.5f * Mathf.Sin((float)_pulseTime * 5.5f);
                Color glowingBorder = ActiveMacroBorderColor.Lerp(new Color(0.55f, 1.0f, 0.78f), wave * 0.65f);

                for (int board = 0; board < GameState.BoardCount; board++)
                {
                    bool isBoardActive = state.ActiveBoardIndex == GameState.FreeMoveBoardIndex ||
                                         state.ActiveBoardIndex == board;
                    if (isBoardActive && _macroBoardPanels[board] != null)
                    {
                        var macroStyle = (StyleBoxFlat)_macroBoardPanels[board].GetThemeStylebox("panel");
                        macroStyle.BorderColor = glowingBorder;
                    }
                }
            }
        }

        private static void StyleClockLabel(Label label, double seconds, bool isActiveTurn)
        {
            if (seconds < 20.0 && isActiveTurn)
            {
                label.AddThemeColorOverride("font_color", NegativeDeltaColor);
            }
            else if (isActiveTurn)
            {
                label.AddThemeColorOverride("font_color", ActiveMacroBorderColor);
            }
            else
            {
                label.AddThemeColorOverride("font_color", new Color(0.50f, 0.56f, 0.66f));
            }
        }

        private void RenderBoardCellsAndAnimate(GameState state, Move? lastMove, bool allowHumanInput)
        {
            bool hasWinningLine = state.TryGetWinningLine(out int winBoard, out int winC0, out int winC1, out int winC2);

            for (int board = 0; board < GameState.BoardCount; board++)
            {
                bool isBoardActive = !state.IsGameOver &&
                                     (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex ||
                                      state.ActiveBoardIndex == board);

                var macroStyle = (StyleBoxFlat)_macroBoardPanels[board].GetThemeStylebox("panel");
                if (hasWinningLine && board == winBoard)
                {
                    macroStyle.BorderColor = WinningCellHighlightColor;
                    macroStyle.BgColor = new Color(0.10f, 0.24f, 0.18f);
                }
                else
                {
                    macroStyle.BorderColor = isBoardActive ? ActiveMacroBorderColor : InactiveMacroBorderColor;
                    macroStyle.BgColor = isBoardActive
                        ? new Color(0.13f, 0.18f, 0.24f)
                        : new Color(0.08f, 0.10f, 0.15f);
                }

                for (int cell = 0; cell < GameState.CellsPerBoard; cell++)
                {
                    Button btn = _cellButtons[board, cell];
                    Player occupant = state.GetCell(board, cell);

                    bool isLastMove = lastMove.HasValue &&
                                      lastMove.Value.BoardIndex == board &&
                                      lastMove.Value.CellIndex == cell;

                    bool isWinningCell = hasWinningLine &&
                                         board == winBoard &&
                                         (cell == winC0 || cell == winC1 || cell == winC2);

                    bool isHintMove = _currentHintMove.HasValue &&
                                      _currentHintMove.Value.BoardIndex == board &&
                                      _currentHintMove.Value.CellIndex == cell;

                    StyleCellButton(btn, occupant, isBoardActive && allowHumanInput, isLastMove, isWinningCell, isHintMove);
                }
            }

            // Animación Tween "Pop" elástica cuando se coloca una nueva ficha
            if (lastMove.HasValue && (!_lastAnimatedMove.HasValue || !_lastAnimatedMove.Value.Equals(lastMove.Value)))
            {
                _lastAnimatedMove = lastMove.Value;
                AnimateButtonPop(_cellButtons[lastMove.Value.BoardIndex, lastMove.Value.CellIndex]);
            }

            // Animación de celebración sobre las 3 celdas ganadoras y el banner al ocurrir Muerte Súbita
            if (hasWinningLine && !_victoryLineAnimated)
            {
                _victoryLineAnimated = true;
                AnimateButtonPop(_cellButtons[winBoard, winC0], 0.38f);
                AnimateButtonPop(_cellButtons[winBoard, winC1], 0.44f);
                AnimateButtonPop(_cellButtons[winBoard, winC2], 0.50f);
                AnimateControlPulse(_statusBannerPanel);
            }
            else if (!state.IsGameOver)
            {
                _victoryLineAnimated = false;
            }
        }

        private void AnimateButtonPop(Button btn, float duration = 0.26f)
        {
            if (btn == null) return;

            btn.PivotOffset = btn.Size * 0.5f;
            btn.Scale = new Vector2(0.25f, 0.25f);

            Tween tween = CreateTween();
            tween.SetTrans(Tween.TransitionType.Back);
            tween.SetEase(Tween.EaseType.Out);
            tween.TweenProperty(btn, "scale", Vector2.One, duration);
        }

        private void AnimateControlPulse(Control control)
        {
            if (control == null) return;

            control.PivotOffset = control.Size * 0.5f;
            control.Scale = new Vector2(0.92f, 0.92f);

            Tween tween = CreateTween();
            tween.SetTrans(Tween.TransitionType.Elastic);
            tween.SetEase(Tween.EaseType.Out);
            tween.TweenProperty(control, "scale", Vector2.One, 0.45f);
        }

        private static void StyleCellButton(
            Button btn,
            Player occupant,
            bool isCellInteractive,
            bool isLastMove,
            bool isWinningCell,
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
                style.BgColor = isWinningCell
                    ? new Color(0.32f, 0.16f, 0.20f)
                    : new Color(0.22f, 0.12f, 0.16f);
            }
            else if (occupant == Player.O)
            {
                btn.Text = "O";
                btn.Disabled = true;
                btn.AddThemeColorOverride("font_disabled_color", PlayerOColor);
                style.BgColor = isWinningCell
                    ? new Color(0.14f, 0.28f, 0.36f)
                    : new Color(0.10f, 0.18f, 0.28f);
            }
            else
            {
                btn.Text = string.Empty;
                btn.Disabled = !isCellInteractive;
                style.BgColor = isCellInteractive
                    ? new Color(0.18f, 0.23f, 0.31f)
                    : new Color(0.11f, 0.13f, 0.18f);
            }

            if (isWinningCell)
            {
                style.BorderWidthLeft = 5;
                style.BorderWidthRight = 5;
                style.BorderWidthTop = 5;
                style.BorderWidthBottom = 5;
                style.BorderColor = WinningCellHighlightColor;
            }
            else if (isLastMove)
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

        private static void SetPanelBorderColor(PanelContainer panel, Color borderColor)
        {
            if (panel?.GetThemeStylebox("panel") is StyleBoxFlat style)
            {
                style.BorderColor = borderColor;
            }
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

        private static Button CreateActionButton(string text, Action onPressed, float minHeight = 70f)
        {
            var btn = new Button
            {
                Text = text,
                CustomMinimumSize = new Vector2(0, minHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None
            };
            btn.AddThemeFontSizeOverride("font_size", 21);
            btn.Pressed += onPressed;
            return btn;
        }

        private static string FormatEndReason(MatchEndReason reason)
        {
            switch (reason)
            {
                case MatchEndReason.SuddenDeathThreeInARow: return "Muerte Súbita";
                case MatchEndReason.Timeout: return "Tiempo Agotado";
                case MatchEndReason.Resignation: return "Rendición";
                case MatchEndReason.DisconnectForfeit: return "Abandono";
                case MatchEndReason.GlobalDraw: return "Empate";
                default: return "Finalizada";
            }
        }
    }
}
