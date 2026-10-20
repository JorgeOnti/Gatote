using System;
using System.Collections.Generic;
using Godot;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.GodotClient
{
    /// <summary>
    /// Aplicación móvil multi-pantalla (Portrait 9:16) en Godot 4 (.NET / C#) con arquitectura UX inspirada
    /// en plataformas competitivas modernas, libre de derechos de autor y lista para uso comercial:
    /// 1. 🏠 INICIO (Lobby y Emparejamiento Rápido 2x2 por ritmo de juego: Bullet 1+1, Blitz 3+0, Blitz 3+2, Rápida 5+0).
    /// 2. ♟ TABLERO (Arena de juego con tarjeta izquierda fija para el jugador local, relojes Fischer, Takeback y animaciones).
    /// 3. 🎬 ANÁLISIS (Sala de repetición paso a paso con MatchReplayNavigator, barra de evaluación y exportación FEN/PGN).
    /// 4. 🏆 PERFIL (Estadísticas competitivas, escalafón de Ligas Fractales en 'pts' y clasificación online en vivo).
    /// </summary>
    public partial class MobileGameScreen : Control
    {
        private enum AppTab
        {
            HomeLobby = 0,
            PlayArena = 1,
            AnalysisStudio = 2,
            ProfileAndLeagues = 3
        }

        private enum LobbyOpponentType
        {
            BotMinimax = 0,
            OnlineMultiplayer = 1,
            LocalPassAndPlay = 2
        }

        private AppTab _currentTab = AppTab.HomeLobby;
        private LobbyOpponentType _selectedOpponentType = LobbyOpponentType.BotMinimax;
        private BotDifficulty _selectedBotDifficulty = BotDifficulty.Hard;
        private int _timeControlIndex = 2; // Blitz 3+2 por defecto

        private GameSessionController _localSession;
        private OnlineMultiplayerClient _onlineClient;
        private bool _isOnlineMode;

        // Historial unificado de partidas para la Sala de Análisis + navegador de repeticiones
        private readonly List<MatchRecord> _recordedMatches = new List<MatchRecord>();
        private MatchReplayNavigator _replayNavigator;
        private string _lastRecordedMatchId;

        // Contenedores de las 4 pantallas principales
        private Control _homeScreenContainer;
        private Control _playScreenContainer;
        private Control _analysisScreenContainer;
        private Control _profileScreenContainer;

        // Botones de la barra de navegación inferior
        private Button _navHomeBtn;
        private Button _navPlayBtn;
        private Button _navAnalysisBtn;
        private Button _navProfileBtn;
        private Label _topHeaderSubtitleLabel;

        // =====================================================================
        // Referencias de UI: Pantalla 1 (🏠 INICIO / LOBBY)
        // =====================================================================
        private Label _lobbyProfileSummaryLabel;
        private Label _lobbyLeagueBadgeLabel;
        private Button _modeBotBtn;
        private Button _modeOnlineBtn;
        private Button _modePassPlayBtn;

        private HBoxContainer _botDifficultyRow;
        private readonly Button[] _difficultyButtons = new Button[4];

        private PanelContainer _lobbyOnlineConfigCard;
        private LineEdit _usernameInput;
        private LineEdit _serverEndpointInput;
        private Label _lobbyHintLabel;

        // =====================================================================
        // Referencias de UI: Pantalla 2 (♟ TABLERO ACTIVO)
        // =====================================================================
        private readonly PanelContainer[] _macroBoardPanels = new PanelContainer[GameState.BoardCount];
        private readonly Button[,] _cellButtons = new Button[GameState.BoardCount, GameState.CellsPerBoard];

        private PanelContainer _leftPlayerCard;
        private Label _leftPlayerTitleLabel;
        private Label _leftPlayerRatingLabel;
        private Label _leftClockLabel;

        private PanelContainer _rightPlayerCard;
        private Label _rightPlayerTitleLabel;
        private Label _rightPlayerRatingLabel;
        private Label _rightClockLabel;

        private PanelContainer _takebackOfferPanel;
        private Label _takebackOfferLabel;

        private PanelContainer _statusBannerPanel;
        private Label _statusBannerLabel;
        private Label _telemetryLabel;

        private Button _undoButton;
        private Button _redoButton;
        private Button _hintButton;
        private Button _analyzeMatchButton;
        private Button _newGameOrResignButton;

        // =====================================================================
        // Referencias de UI: Pantalla 3 (🎬 SALA DE ANÁLISIS Y REPETICIONES)
        // =====================================================================
        private readonly PanelContainer[] _analysisMacroPanels = new PanelContainer[GameState.BoardCount];
        private readonly Button[,] _analysisCellButtons = new Button[GameState.BoardCount, GameState.CellsPerBoard];

        private OptionButton _matchSelectorDropdown;
        private ProgressBar _evalProgressBar;
        private Label _evalScoreLabel;
        private Label _analysisStepInfoLabel;
        private LineEdit _transcriptInput;
        private Button _stepStartBtn;
        private Button _stepBackBtn;
        private Button _stepForwardBtn;
        private Button _stepEndBtn;

        // =====================================================================
        // Referencias de UI: Pantalla 4 (🏆 PERFIL Y LIGAS)
        // =====================================================================
        private Label _profileNameLabel;
        private Label _profileRatingBigLabel;
        private Label _profileStatsGridLabel;
        private VBoxContainer _leaderboardListContainer;

        // Estado de animación y telemetría
        private Move? _currentHintMove;
        private Move? _lastAnimatedMove;
        private bool _victoryLineAnimated;
        private double _pulseTime;

        private static readonly TimeControlConfig[] TimeControls =
        {
            TimeControlConfig.Bullet1Plus1,
            TimeControlConfig.Blitz3Plus0,
            TimeControlConfig.Blitz3Plus2,
            TimeControlConfig.Rapid5Plus0
        };

        // Paleta de colores comercial (Dark Slate Competitive UI)
        private static readonly Color BgColor = new Color(0.06f, 0.08f, 0.12f);
        private static readonly Color SurfaceColor = new Color(0.09f, 0.12f, 0.18f);
        private static readonly Color CardBgColor = new Color(0.11f, 0.14f, 0.20f);
        private static readonly Color AccentPrimaryColor = new Color(0.20f, 0.62f, 0.98f);
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
            _onlineClient.LeaderboardReceived += OnLeaderboardReceived;

            SeedSampleTacticalMatchForAnalysis();
            BuildMultiScreenMobileApp();
            SwitchTab(AppTab.HomeLobby);
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
                _onlineClient.LeaderboardReceived -= OnLeaderboardReceived;
                _onlineClient.Dispose();
            }
        }

        public override void _Process(double delta)
        {
            _pulseTime += delta;

            if (_isOnlineMode)
            {
                _onlineClient.TickClock(delta);
            }
            else
            {
                _localSession.TickClock(delta);
            }

            if (_currentTab == AppTab.PlayArena)
            {
                UpdateClockLabelsAndPulseGlow();
            }
        }

        // =====================================================================
        // CONSTRUCCIÓN DE ARQUITECTURA MULTI-PANTALLA
        // =====================================================================

        private void BuildMultiScreenMobileApp()
        {
            var background = new ColorRect { Color = BgColor };
            background.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(background);

            var safeMargin = new MarginContainer();
            safeMargin.SetAnchorsPreset(LayoutPreset.FullRect);
            safeMargin.AddThemeConstantOverride("margin_left", 24);
            safeMargin.AddThemeConstantOverride("margin_right", 24);
            safeMargin.AddThemeConstantOverride("margin_top", 26);
            safeMargin.AddThemeConstantOverride("margin_bottom", 22);
            AddChild(safeMargin);

            var rootColumn = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            rootColumn.AddThemeConstantOverride("separation", 12);
            safeMargin.AddChild(rootColumn);

            // 1. Barra Superior de Marca (App Header)
            BuildTopAppHeader(rootColumn);

            // 2. Contenedor Central Dinámico (Alberga las 4 Pantallas)
            var screenHost = new Control
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            rootColumn.AddChild(screenHost);

            _homeScreenContainer = BuildHomeLobbyScreen();
            _playScreenContainer = BuildPlayArenaScreen();
            _analysisScreenContainer = BuildAnalysisStudioScreen();
            _profileScreenContainer = BuildProfileAndLeaguesScreen();

            screenHost.AddChild(_homeScreenContainer);
            screenHost.AddChild(_playScreenContainer);
            screenHost.AddChild(_analysisScreenContainer);
            screenHost.AddChild(_profileScreenContainer);

            // 3. Barra de Navegación Inferior Persistente (4 Pestañas)
            BuildBottomNavigationBar(rootColumn);
        }

        private void BuildTopAppHeader(VBoxContainer parent)
        {
            var headerPanel = CreateStyledPanel(SurfaceColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 14, contentMargin: 12);
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 12);

            var brandTitle = new Label
            {
                Text = "⚡ GATOTE FRACTAL",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            brandTitle.AddThemeFontSizeOverride("font_size", 26);
            brandTitle.AddThemeColorOverride("font_color", new Color(0.95f, 0.98f, 1.0f));

            _topHeaderSubtitleLabel = new Label
            {
                Text = "Muerte Súbita • 1200 pts",
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _topHeaderSubtitleLabel.AddThemeFontSizeOverride("font_size", 20);
            _topHeaderSubtitleLabel.AddThemeColorOverride("font_color", ActiveMacroBorderColor);

            row.AddChild(brandTitle);
            row.AddChild(_topHeaderSubtitleLabel);
            headerPanel.AddChild(row);
            parent.AddChild(headerPanel);
        }

        private void BuildBottomNavigationBar(VBoxContainer parent)
        {
            var navPanel = CreateStyledPanel(SurfaceColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 16, contentMargin: 8);
            var navRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            navRow.AddThemeConstantOverride("separation", 10);

            _navHomeBtn = CreateActionButton("🏠 Inicio", () => SwitchTab(AppTab.HomeLobby), minHeight: 68);
            _navPlayBtn = CreateActionButton("♟ Tablero", () => SwitchTab(AppTab.PlayArena), minHeight: 68);
            _navAnalysisBtn = CreateActionButton("🎬 Análisis", () => SwitchTab(AppTab.AnalysisStudio), minHeight: 68);
            _navProfileBtn = CreateActionButton("🏆 Perfil", () => SwitchTab(AppTab.ProfileAndLeagues), minHeight: 68);

            navRow.AddChild(_navHomeBtn);
            navRow.AddChild(_navPlayBtn);
            navRow.AddChild(_navAnalysisBtn);
            navRow.AddChild(_navProfileBtn);
            navPanel.AddChild(navRow);
            parent.AddChild(navPanel);
        }

        private void SwitchTab(AppTab targetTab)
        {
            _currentTab = targetTab;

            _homeScreenContainer.Visible = targetTab == AppTab.HomeLobby;
            _playScreenContainer.Visible = targetTab == AppTab.PlayArena;
            _analysisScreenContainer.Visible = targetTab == AppTab.AnalysisStudio;
            _profileScreenContainer.Visible = targetTab == AppTab.ProfileAndLeagues;

            HighlightNavTabButton(_navHomeBtn, targetTab == AppTab.HomeLobby);
            HighlightNavTabButton(_navPlayBtn, targetTab == AppTab.PlayArena);
            HighlightNavTabButton(_navAnalysisBtn, targetTab == AppTab.AnalysisStudio);
            HighlightNavTabButton(_navProfileBtn, targetTab == AppTab.ProfileAndLeagues);

            if (targetTab == AppTab.AnalysisStudio)
            {
                RefreshAnalysisUI();
            }
            else if (targetTab == AppTab.ProfileAndLeagues)
            {
                RefreshProfileScreenUI();
                if (_onlineClient.IsConnected)
                {
                    _onlineClient.RequestLeaderboard();
                }
            }

            RefreshUI();
        }

        // =====================================================================
        // PANTALLA 1: 🏠 INICIO Y EMPAREJAMIENTO RÁPIDO (QUICK PAIRING)
        // =====================================================================

        private Control BuildHomeLobbyScreen()
        {
            var col = new VBoxContainer();
            col.SetAnchorsPreset(LayoutPreset.FullRect);
            col.AddThemeConstantOverride("separation", 16);

            // 1. Tarjeta de Bienvenida y Resumen de Cuenta Competitiva
            var accountCard = CreateStyledPanel(CardBgColor, AccentPrimaryColor, borderWidth: 2, cornerRadius: 16, contentMargin: 16);
            var accountBox = new VBoxContainer();
            accountBox.AddThemeConstantOverride("separation", 6);

            _lobbyProfileSummaryLabel = new Label
            {
                Text = "Jugador (Tú) • 1200 pts",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _lobbyProfileSummaryLabel.AddThemeFontSizeOverride("font_size", 26);

            _lobbyLeagueBadgeLabel = new Label
            {
                Text = "Liga Plata • 0V - 0D - 0E",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _lobbyLeagueBadgeLabel.AddThemeFontSizeOverride("font_size", 20);
            _lobbyLeagueBadgeLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.84f, 0.96f));

            accountBox.AddChild(_lobbyProfileSummaryLabel);
            accountBox.AddChild(_lobbyLeagueBadgeLabel);
            accountCard.AddChild(accountBox);
            col.AddChild(accountCard);

            // 2. Selector de Modalidad de Juego
            var modeSectionTitle = new Label
            {
                Text = "1. ELIGE TU MODALIDAD DE JUEGO",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            modeSectionTitle.AddThemeFontSizeOverride("font_size", 20);
            modeSectionTitle.AddThemeColorOverride("font_color", new Color(0.68f, 0.76f, 0.88f));
            col.AddChild(modeSectionTitle);

            var modeRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            modeRow.AddThemeConstantOverride("separation", 10);

            _modeBotBtn = CreateActionButton("🤖 Contra IA", () => SelectLobbyOpponentType(LobbyOpponentType.BotMinimax), minHeight: 68);
            _modeOnlineBtn = CreateActionButton("🌐 En Línea", () => SelectLobbyOpponentType(LobbyOpponentType.OnlineMultiplayer), minHeight: 68);
            _modePassPlayBtn = CreateActionButton("📱 2 Jugadores", () => SelectLobbyOpponentType(LobbyOpponentType.LocalPassAndPlay), minHeight: 68);

            modeRow.AddChild(_modeBotBtn);
            modeRow.AddChild(_modeOnlineBtn);
            modeRow.AddChild(_modePassPlayBtn);
            col.AddChild(modeRow);

            // 3A. Selector de Nivel de IA (Visible cuando se elige Contra IA)
            _botDifficultyRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _botDifficultyRow.AddThemeConstantOverride("separation", 8);

            BotDifficulty[] diffs = { BotDifficulty.Easy, BotDifficulty.Medium, BotDifficulty.Hard, BotDifficulty.Master };
            string[] diffNames = { "Fácil", "Medio", "Difícil", "Maestro" };
            for (int i = 0; i < diffs.Length; i++)
            {
                BotDifficulty capturedDiff = diffs[i];
                Button diffBtn = CreateActionButton(diffNames[i], () => SelectBotDifficulty(capturedDiff), minHeight: 58);
                _difficultyButtons[i] = diffBtn;
                _botDifficultyRow.AddChild(diffBtn);
            }
            col.AddChild(_botDifficultyRow);

            // 3B. Tarjeta de Configuración de Servidor Online (Visible cuando se elige En Línea)
            _lobbyOnlineConfigCard = CreateStyledPanel(
                new Color(0.10f, 0.16f, 0.24f),
                AccentPrimaryColor,
                borderWidth: 2,
                cornerRadius: 14,
                contentMargin: 12);
            _lobbyOnlineConfigCard.Visible = false;

            var onlineInputsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            onlineInputsRow.AddThemeConstantOverride("separation", 10);

            _usernameInput = new LineEdit
            {
                PlaceholderText = "Tu Apodo",
                Text = $"Jugador_{new Random().Next(100, 999)}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 54)
            };
            _usernameInput.AddThemeFontSizeOverride("font_size", 20);

            _serverEndpointInput = new LineEdit
            {
                PlaceholderText = "Servidor IP:Puerto",
                Text = "127.0.0.1:7777",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 54)
            };
            _serverEndpointInput.AddThemeFontSizeOverride("font_size", 20);

            onlineInputsRow.AddChild(_usernameInput);
            onlineInputsRow.AddChild(_serverEndpointInput);
            _lobbyOnlineConfigCard.AddChild(onlineInputsRow);
            col.AddChild(_lobbyOnlineConfigCard);

            // 4. Cuadrícula 2x2 de Emparejamiento Rápido por Control de Tiempo (Quick Pairing Grid)
            var pairingTitle = new Label
            {
                Text = "2. TOCA UN RITMO DE TIEMPO PARA INICIAR PARTIDA",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            pairingTitle.AddThemeFontSizeOverride("font_size", 20);
            pairingTitle.AddThemeColorOverride("font_color", new Color(0.68f, 0.76f, 0.88f));
            col.AddChild(pairingTitle);

            var quickPairingGrid = new GridContainer
            {
                Columns = 2,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            quickPairingGrid.AddThemeConstantOverride("h_separation", 16);
            quickPairingGrid.AddThemeConstantOverride("v_separation", 16);
            col.AddChild(quickPairingGrid);

            string[] icons = { "⚡", "🔥", "⏱", "🏛" };
            string[] subtitles =
            {
                "1 min + 1s incr.\nUltra rápido",
                "3 min + 0s incr.\nRitmo intenso",
                "3 min + 2s incr.\nEstándar competitivo",
                "5 min + 0s incr.\nCálculo profundo"
            };

            for (int i = 0; i < TimeControls.Length; i++)
            {
                int capturedTimeIndex = i;
                TimeControlConfig tc = TimeControls[i];

                var cardBtn = new Button
                {
                    Text = $"{icons[i]}  {tc.Name}\n{subtitles[i]}",
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ExpandFill,
                    FocusMode = FocusModeEnum.None
                };
                cardBtn.AddThemeFontSizeOverride("font_size", 25);

                var cardStyle = new StyleBoxFlat
                {
                    BgColor = new Color(0.13f, 0.18f, 0.26f),
                    BorderColor = i == _timeControlIndex ? ActiveMacroBorderColor : AccentPrimaryColor,
                    BorderWidthLeft = 3,
                    BorderWidthRight = 3,
                    BorderWidthTop = 3,
                    BorderWidthBottom = 3,
                    CornerRadiusTopLeft = 18,
                    CornerRadiusTopRight = 18,
                    CornerRadiusBottomLeft = 18,
                    CornerRadiusBottomRight = 18
                };
                cardBtn.AddThemeStyleboxOverride("normal", cardStyle);
                cardBtn.AddThemeStyleboxOverride("hover", cardStyle);
                cardBtn.AddThemeStyleboxOverride("pressed", cardStyle);

                cardBtn.Pressed += () => OnQuickPairingCardSelected(capturedTimeIndex);
                quickPairingGrid.AddChild(cardBtn);
            }

            // 5. Regla destacada de Muerte Súbita
            var ruleCard = CreateStyledPanel(SurfaceColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 14, contentMargin: 12);
            _lobbyHintLabel = new Label
            {
                Text = "Regla Muerte Súbita: El primer jugador en lograr 3 en raya en CUALQUIER sub-tablero gana al instante. Tu casilla jugada redirige el siguiente tablero del rival.",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _lobbyHintLabel.AddThemeFontSizeOverride("font_size", 18);
            _lobbyHintLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.80f, 0.90f));
            ruleCard.AddChild(_lobbyHintLabel);
            col.AddChild(ruleCard);

            UpdateLobbySelectionStyles();
            return col;
        }

        private void SelectLobbyOpponentType(LobbyOpponentType type)
        {
            _selectedOpponentType = type;
            _botDifficultyRow.Visible = type == LobbyOpponentType.BotMinimax;
            _lobbyOnlineConfigCard.Visible = type == LobbyOpponentType.OnlineMultiplayer;
            UpdateLobbySelectionStyles();
        }

        private void SelectBotDifficulty(BotDifficulty difficulty)
        {
            _selectedBotDifficulty = difficulty;
            UpdateLobbySelectionStyles();
        }

        private void UpdateLobbySelectionStyles()
        {
            HighlightSelectionButton(_modeBotBtn, _selectedOpponentType == LobbyOpponentType.BotMinimax);
            HighlightSelectionButton(_modeOnlineBtn, _selectedOpponentType == LobbyOpponentType.OnlineMultiplayer);
            HighlightSelectionButton(_modePassPlayBtn, _selectedOpponentType == LobbyOpponentType.LocalPassAndPlay);

            BotDifficulty[] diffs = { BotDifficulty.Easy, BotDifficulty.Medium, BotDifficulty.Hard, BotDifficulty.Master };
            for (int i = 0; i < diffs.Length; i++)
            {
                if (_difficultyButtons[i] != null)
                {
                    HighlightSelectionButton(_difficultyButtons[i], _selectedBotDifficulty == diffs[i]);
                }
            }
        }

        private void OnQuickPairingCardSelected(int timeIndex)
        {
            _timeControlIndex = timeIndex;
            TimeControlConfig selectedTime = TimeControls[_timeControlIndex];

            _currentHintMove = null;
            _lastAnimatedMove = null;
            _victoryLineAnimated = false;

            if (_selectedOpponentType == LobbyOpponentType.OnlineMultiplayer)
            {
                _isOnlineMode = true;
                _onlineClient.Clock.Reset(selectedTime);
                SwitchTab(AppTab.PlayArena);
                ConnectAndJoinOnlineQueue();
                return;
            }

            if (_isOnlineMode)
            {
                _onlineClient.Disconnect();
                _isOnlineMode = false;
            }

            string customName = !string.IsNullOrWhiteSpace(_usernameInput?.Text) ? _usernameInput.Text.Trim() : _localSession.ProfileX.Username;
            _localSession.ProfileX.Username = customName;

            SessionMode targetMode = _selectedOpponentType == LobbyOpponentType.LocalPassAndPlay
                ? SessionMode.PassAndPlay2P
                : SessionMode.HumanVsBotX;

            _localSession.SetTimeControl(selectedTime);
            _localSession.ConfigureSession(targetMode, _selectedBotDifficulty, _localSession.ProfileX);
            SwitchTab(AppTab.PlayArena);
        }

        // =====================================================================
        // PANTALLA 2: ♟ TABLERO (ARENA DE PARTIDA ACTIVA)
        // =====================================================================

        private Control BuildPlayArenaScreen()
        {
            var mainColumn = new VBoxContainer();
            mainColumn.SetAnchorsPreset(LayoutPreset.FullRect);
            mainColumn.AddThemeConstantOverride("separation", 12);

            // 1. Tarjetas de Jugadores (Izquierda = SIEMPRE Jugador Local "Tú" | Derecha = Oponente)
            var profilesRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            profilesRow.AddThemeConstantOverride("separation", 12);
            mainColumn.AddChild(profilesRow);

            _leftPlayerCard = CreateStyledPanel(CardBgColor, PlayerXColor, borderWidth: 3, cornerRadius: 14, contentMargin: 10);
            _leftPlayerCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var leftBox = new VBoxContainer();
            leftBox.AddThemeConstantOverride("separation", 3);

            _leftPlayerTitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _leftPlayerTitleLabel.AddThemeFontSizeOverride("font_size", 21);

            _leftPlayerRatingLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _leftPlayerRatingLabel.AddThemeFontSizeOverride("font_size", 18);

            _leftClockLabel = new Label { Text = "03:00", HorizontalAlignment = HorizontalAlignment.Center };
            _leftClockLabel.AddThemeFontSizeOverride("font_size", 27);

            leftBox.AddChild(_leftPlayerTitleLabel);
            leftBox.AddChild(_leftPlayerRatingLabel);
            leftBox.AddChild(_leftClockLabel);
            _leftPlayerCard.AddChild(leftBox);
            profilesRow.AddChild(_leftPlayerCard);

            _rightPlayerCard = CreateStyledPanel(CardBgColor, PlayerOColor, borderWidth: 3, cornerRadius: 14, contentMargin: 10);
            _rightPlayerCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var rightBox = new VBoxContainer();
            rightBox.AddThemeConstantOverride("separation", 3);

            _rightPlayerTitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _rightPlayerTitleLabel.AddThemeFontSizeOverride("font_size", 21);

            _rightPlayerRatingLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _rightPlayerRatingLabel.AddThemeFontSizeOverride("font_size", 18);

            _rightClockLabel = new Label { Text = "03:00", HorizontalAlignment = HorizontalAlignment.Center };
            _rightClockLabel.AddThemeFontSizeOverride("font_size", 27);

            rightBox.AddChild(_rightPlayerTitleLabel);
            rightBox.AddChild(_rightPlayerRatingLabel);
            rightBox.AddChild(_rightClockLabel);
            _rightPlayerCard.AddChild(rightBox);
            profilesRow.AddChild(_rightPlayerCard);

            // 2. Banner de Confirmación de Takeback del Rival
            BuildTakebackConsentBanner(mainColumn);

            // 3. Banner de Estado de Partida
            _statusBannerPanel = CreateStyledPanel(
                new Color(0.13f, 0.17f, 0.25f),
                new Color(0.30f, 0.38f, 0.52f),
                borderWidth: 2,
                cornerRadius: 14,
                contentMargin: 10);

            _statusBannerLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _statusBannerLabel.AddThemeFontSizeOverride("font_size", 22);
            _statusBannerPanel.AddChild(_statusBannerLabel);
            mainColumn.AddChild(_statusBannerPanel);

            // 4. Tablero Fractal 9x9
            Control boardView = CreateFractalBoardGrid(_macroBoardPanels, _cellButtons, OnCellTapped, cellFontSize: 32);
            mainColumn.AddChild(boardView);

            // 5. Línea de Telemetría (Minimax / Estado de Red)
            _telemetryLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _telemetryLabel.AddThemeFontSizeOverride("font_size", 18);
            _telemetryLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.82f));
            mainColumn.AddChild(_telemetryLabel);

            // 6. Controles Táctiles de Partida
            var actionsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            actionsRow.AddThemeConstantOverride("separation", 10);
            mainColumn.AddChild(actionsRow);

            _undoButton = CreateActionButton("⟲ Deshacer", OnUndoPressed, minHeight: 60);
            _redoButton = CreateActionButton("⟳ Rehacer", OnRedoPressed, minHeight: 60);
            _hintButton = CreateActionButton("💡 Pista", OnHintPressed, minHeight: 60);
            _analyzeMatchButton = CreateActionButton("🔍 Analizar", OnOpenCurrentMatchInAnalysisPressed, minHeight: 60);
            _newGameOrResignButton = CreateActionButton("★ Nueva", OnNewMatchOrResignPressed, minHeight: 60);

            actionsRow.AddChild(_undoButton);
            actionsRow.AddChild(_redoButton);
            actionsRow.AddChild(_hintButton);
            actionsRow.AddChild(_analyzeMatchButton);
            actionsRow.AddChild(_newGameOrResignButton);

            return mainColumn;
        }

        private void BuildTakebackConsentBanner(VBoxContainer parent)
        {
            _takebackOfferPanel = CreateStyledPanel(
                new Color(0.24f, 0.19f, 0.08f),
                LastMoveHighlightColor,
                borderWidth: 3,
                cornerRadius: 14,
                contentMargin: 10);
            _takebackOfferPanel.Visible = false;

            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 8);

            _takebackOfferLabel = new Label
            {
                Text = "Tu oponente solicita deshacer la última jugada. ¿Aceptas?",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _takebackOfferLabel.AddThemeFontSizeOverride("font_size", 20);
            _takebackOfferLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);
            col.AddChild(_takebackOfferLabel);

            var btnRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            btnRow.AddThemeConstantOverride("separation", 12);

            var acceptBtn = CreateActionButton("✓ Aceptar Deshacer", () => _onlineClient.RespondToOpponentTakeback(true), minHeight: 54);
            var declineBtn = CreateActionButton("✕ Rechazar", () => _onlineClient.RespondToOpponentTakeback(false), minHeight: 54);

            btnRow.AddChild(acceptBtn);
            btnRow.AddChild(declineBtn);
            col.AddChild(btnRow);

            _takebackOfferPanel.AddChild(col);
            parent.AddChild(_takebackOfferPanel);
        }

        // =====================================================================
        // PANTALLA 3: 🎬 SALA DE ANÁLISIS Y REPETICIONES (REPLAY STUDIO)
        // =====================================================================

        private Control BuildAnalysisStudioScreen()
        {
            var col = new VBoxContainer();
            col.SetAnchorsPreset(LayoutPreset.FullRect);
            col.AddThemeConstantOverride("separation", 10);

            // 1. Selector de Partida Grabada
            var topRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            topRow.AddThemeConstantOverride("separation", 10);

            _matchSelectorDropdown = new OptionButton
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 54)
            };
            _matchSelectorDropdown.AddThemeFontSizeOverride("font_size", 19);
            _matchSelectorDropdown.ItemSelected += OnReplayMatchSelected;

            topRow.AddChild(_matchSelectorDropdown);
            col.AddChild(topRow);

            // 2. Barra de Evaluación Táctica (Eval Bar estilo Lichess)
            var evalPanel = CreateStyledPanel(CardBgColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 12, contentMargin: 10);
            var evalCol = new VBoxContainer();
            evalCol.AddThemeConstantOverride("separation", 6);

            _evalScoreLabel = new Label
            {
                Text = "Evaluación Heurística: 0 (Equilibrado)",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _evalScoreLabel.AddThemeFontSizeOverride("font_size", 19);

            _evalProgressBar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = 50,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 18)
            };

            evalCol.AddChild(_evalScoreLabel);
            evalCol.AddChild(_evalProgressBar);
            evalPanel.AddChild(evalCol);
            col.AddChild(evalPanel);

            // 3. Tablero Fractal de Análisis 9x9
            Control analysisBoard = CreateFractalBoardGrid(_analysisMacroPanels, _analysisCellButtons, null, cellFontSize: 28);
            col.AddChild(analysisBoard);

            // 4. Etiqueta del Movimiento Actual (Ply / Coordenada / FEN)
            _analysisStepInfoLabel = new Label
            {
                Text = "Inicio de la partida (Turno 0/0)",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _analysisStepInfoLabel.AddThemeFontSizeOverride("font_size", 21);
            _analysisStepInfoLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);
            col.AddChild(_analysisStepInfoLabel);

            // 5. Controles de Navegación Paso a Paso (⏮ ◀ ▶ ⏭)
            var navControlsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            navControlsRow.AddThemeConstantOverride("separation", 10);

            _stepStartBtn = CreateActionButton("⏮ Inicio", () => { _replayNavigator?.JumpToStart(); RefreshAnalysisUI(); }, minHeight: 58);
            _stepBackBtn = CreateActionButton("◀ Anterior", () => { _replayNavigator?.StepBackward(); RefreshAnalysisUI(); }, minHeight: 58);
            _stepForwardBtn = CreateActionButton("Siguiente ▶", () => { _replayNavigator?.StepForward(); RefreshAnalysisUI(); }, minHeight: 58);
            _stepEndBtn = CreateActionButton("Final ⏭", () => { _replayNavigator?.JumpToEnd(); RefreshAnalysisUI(); }, minHeight: 58);

            navControlsRow.AddChild(_stepStartBtn);
            navControlsRow.AddChild(_stepBackBtn);
            navControlsRow.AddChild(_stepForwardBtn);
            navControlsRow.AddChild(_stepEndBtn);
            col.AddChild(navControlsRow);

            // 6. Importador / Exportador de Transcripción
            var importRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            importRow.AddThemeConstantOverride("separation", 8);

            _transcriptInput = new LineEdit
            {
                PlaceholderText = "Transcripción (ej. 01-10-02-20-00)",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 52)
            };
            _transcriptInput.AddThemeFontSizeOverride("font_size", 18);

            var loadTranscriptBtn = CreateActionButton("📥 Cargar", OnLoadCustomTranscriptPressed, minHeight: 52);
            loadTranscriptBtn.CustomMinimumSize = new Vector2(150, 52);
            loadTranscriptBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

            importRow.AddChild(_transcriptInput);
            importRow.AddChild(loadTranscriptBtn);
            col.AddChild(importRow);

            PopulateMatchSelectorDropdown();
            return col;
        }

        private void SeedSampleTacticalMatchForAnalysis()
        {
            var pX = new PlayerProfile("sample_x", "Maestra_X", 1620);
            var pO = new PlayerProfile("sample_o", "Tactico_O", 1590);
            GameState sampleGame = GameSerializer.ImportMoveHistory("44-40-04-41-14-42");
            MatchRecord sampleRecord = MatchRecord.CreateFromGame(sampleGame, pX, pO);
            _recordedMatches.Add(sampleRecord);
            _replayNavigator = new MatchReplayNavigator(sampleRecord);
        }

        private void RecordCompletedMatchIfNeeded(MatchRecord record)
        {
            if (record == null || record.MatchId == _lastRecordedMatchId)
            {
                return;
            }

            _lastRecordedMatchId = record.MatchId;
            _recordedMatches.Insert(0, record);
            _replayNavigator = new MatchReplayNavigator(record);
            PopulateMatchSelectorDropdown();
        }

        private void PopulateMatchSelectorDropdown()
        {
            if (_matchSelectorDropdown == null) return;

            _matchSelectorDropdown.Clear();
            for (int i = 0; i < _recordedMatches.Count; i++)
            {
                MatchRecord m = _recordedMatches[i];
                string winnerTag = m.Winner == Player.None ? "Empate" : $"Ganó {m.Winner}";
                _matchSelectorDropdown.AddItem($"#{i + 1}: {m.PlayerXName} vs {m.PlayerOName} ({winnerTag})");
            }

            if (_recordedMatches.Count > 0)
            {
                _matchSelectorDropdown.Selected = 0;
            }
        }

        private void OnReplayMatchSelected(long index)
        {
            if (index >= 0 && index < _recordedMatches.Count)
            {
                _replayNavigator = new MatchReplayNavigator(_recordedMatches[(int)index]);
                RefreshAnalysisUI();
            }
        }

        private void OnLoadCustomTranscriptPressed()
        {
            if (string.IsNullOrWhiteSpace(_transcriptInput.Text)) return;

            try
            {
                _replayNavigator = new MatchReplayNavigator(_transcriptInput.Text.Trim());
                RefreshAnalysisUI();
            }
            catch (Exception ex)
            {
                _analysisStepInfoLabel.Text = $"Transcripción inválida: {ex.Message}";
            }
        }

        private void OnOpenCurrentMatchInAnalysisPressed()
        {
            GameState currentState = _isOnlineMode ? _onlineClient.State : _localSession.State;
            string transcript = GameSerializer.ExportMoveHistory(currentState);

            if (!string.IsNullOrWhiteSpace(transcript))
            {
                _replayNavigator = new MatchReplayNavigator(transcript);
            }

            SwitchTab(AppTab.AnalysisStudio);
        }

        private void RefreshAnalysisUI()
        {
            if (_replayNavigator == null) return;

            GameState state = _replayNavigator.CurrentState;
            Move? lastMove = _replayNavigator.LastExecutedMove;

            RenderStaticBoardOnGrid(state, lastMove, _analysisMacroPanels, _analysisCellButtons);

            int eval = _replayNavigator.GetCurrentEvaluationForX();
            string evalText;
            float barPercent;

            if (eval >= MinimaxBot.WinScore - 500)
            {
                evalText = "Evaluación: ¡VICTORIA POR MUERTE SÚBITA PARA [X]!";
                barPercent = 100f;
            }
            else if (eval <= -MinimaxBot.WinScore + 500)
            {
                evalText = "Evaluación: ¡VICTORIA POR MUERTE SÚBITA PARA [O]!";
                barPercent = 0f;
            }
            else
            {
                string sign = eval > 0 ? "+" : "";
                evalText = $"Evaluación Táctica (perspectiva X): {sign}{eval} pts heurísticos";
                barPercent = Mathf.Clamp(50f + (eval / 12.0f), 5f, 95f);
            }

            _evalScoreLabel.Text = evalText;
            _evalProgressBar.Value = barPercent;

            if (lastMove.HasValue)
            {
                Player mover = state.CurrentPlayer == Player.X ? Player.O : Player.X;
                _analysisStepInfoLabel.Text =
                    $"Jugada {_replayNavigator.CurrentPly}/{_replayNavigator.TotalPlies}: " +
                    $"[{mover}] tiró en Tablero #{lastMove.Value.BoardIndex}, Casilla #{lastMove.Value.CellIndex}";
            }
            else
            {
                _analysisStepInfoLabel.Text = $"Posición Inicial (0/{_replayNavigator.TotalPlies}) • Tiro Libre";
            }

            _transcriptInput.Text = GameSerializer.ExportMoveHistory(state);

            _stepStartBtn.Disabled = !_replayNavigator.CanStepBackward;
            _stepBackBtn.Disabled = !_replayNavigator.CanStepBackward;
            _stepForwardBtn.Disabled = !_replayNavigator.CanStepForward;
            _stepEndBtn.Disabled = !_replayNavigator.CanStepForward;
        }

        // =====================================================================
        // PANTALLA 4: 🏆 PERFIL, LIGAS FRACTALES Y RANKING ONLINE
        // =====================================================================

        private Control BuildProfileAndLeaguesScreen()
        {
            var col = new VBoxContainer();
            col.SetAnchorsPreset(LayoutPreset.FullRect);
            col.AddThemeConstantOverride("separation", 14);

            // 1. Tarjeta Principal del Jugador
            var profileCard = CreateStyledPanel(CardBgColor, ActiveMacroBorderColor, borderWidth: 3, cornerRadius: 18, contentMargin: 18);
            var profileCol = new VBoxContainer();
            profileCol.AddThemeConstantOverride("separation", 8);

            _profileNameLabel = new Label
            {
                Text = "Jugador (Tú)",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _profileNameLabel.AddThemeFontSizeOverride("font_size", 28);

            _profileRatingBigLabel = new Label
            {
                Text = "1200 pts • Liga Plata",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _profileRatingBigLabel.AddThemeFontSizeOverride("font_size", 25);
            _profileRatingBigLabel.AddThemeColorOverride("font_color", ActiveMacroBorderColor);

            _profileStatsGridLabel = new Label
            {
                Text = "Partidas: 0  |  Victorias: 0  |  Derrotas: 0  |  Empates: 0  |  Efectividad: 0%",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _profileStatsGridLabel.AddThemeFontSizeOverride("font_size", 20);

            profileCol.AddChild(_profileNameLabel);
            profileCol.AddChild(_profileRatingBigLabel);
            profileCol.AddChild(_profileStatsGridLabel);
            profileCard.AddChild(profileCol);
            col.AddChild(profileCard);

            // 2. Escalafón Oficial de Ligas Fractales (Sin marcas registradas de terceros)
            var leaguesCard = CreateStyledPanel(SurfaceColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 16, contentMargin: 14);
            var leaguesCol = new VBoxContainer();
            leaguesCol.AddThemeConstantOverride("separation", 6);

            var leaguesTitle = new Label
            {
                Text = "🏅 ESCALAFÓN COMPETITIVO DE LIGAS FRACTALES",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            leaguesTitle.AddThemeFontSizeOverride("font_size", 21);
            leaguesTitle.AddThemeColorOverride("font_color", LastMoveHighlightColor);
            leaguesCol.AddChild(leaguesTitle);

            string[] leagueTiers =
            {
                "👑 Gran Maestro Fractal  —  2100+ pts",
                "🔥 Maestro               —  1900 a 2099 pts",
                "💎 Diamante              —  1700 a 1899 pts",
                "💠 Platino               —  1500 a 1699 pts",
                "🥇 Oro                   —  1300 a 1499 pts",
                "🥈 Plata (Inicial)       —  1100 a 1299 pts",
                "🥉 Bronce                —  100 a 1099 pts"
            };

            foreach (string tier in leagueTiers)
            {
                var tierLbl = new Label { Text = tier, HorizontalAlignment = HorizontalAlignment.Center };
                tierLbl.AddThemeFontSizeOverride("font_size", 18);
                leaguesCol.AddChild(tierLbl);
            }

            leaguesCard.AddChild(leaguesCol);
            col.AddChild(leaguesCard);

            // 3. Clasificación Online en Vivo (Leaderboard)
            var lbPanel = CreateStyledPanel(CardBgColor, AccentPrimaryColor, borderWidth: 2, cornerRadius: 16, contentMargin: 14);
            lbPanel.SizeFlagsVertical = SizeFlags.ExpandFill;

            var lbOuterCol = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            lbOuterCol.AddThemeConstantOverride("separation", 8);

            var lbHeaderRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var lbTitle = new Label
            {
                Text = "🌐 TOP JUGADORES DEL SERVIDOR",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            lbTitle.AddThemeFontSizeOverride("font_size", 21);

            var refreshLbBtn = CreateActionButton("↻ Actualizar", () =>
            {
                if (_onlineClient.IsConnected) _onlineClient.RequestLeaderboard();
            }, minHeight: 46);
            refreshLbBtn.CustomMinimumSize = new Vector2(160, 46);
            refreshLbBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

            lbHeaderRow.AddChild(lbTitle);
            lbHeaderRow.AddChild(refreshLbBtn);
            lbOuterCol.AddChild(lbHeaderRow);

            _leaderboardListContainer = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            _leaderboardListContainer.AddThemeConstantOverride("separation", 6);
            lbOuterCol.AddChild(_leaderboardListContainer);

            lbPanel.AddChild(lbOuterCol);
            col.AddChild(lbPanel);

            return col;
        }

        private void RefreshProfileScreenUI()
        {
            PlayerProfile p = _localSession.ProfileX;
            int rating = _isOnlineMode && _onlineClient.IsConnected ? _onlineClient.LocalElo : p.EloRating;
            string rank = EloRatingCalculator.GetRankTitle(rating);
            string name = _isOnlineMode && !string.IsNullOrEmpty(_onlineClient.LocalUsername)
                ? _onlineClient.LocalUsername
                : p.Username;

            _profileNameLabel.Text = $"{name} (Tú)";
            _profileRatingBigLabel.Text = $"{rating} pts • Liga {rank}";
            _profileStatsGridLabel.Text =
                $"Partidas: {p.GamesPlayed}  |  Victorias: {p.Wins}  |  Derrotas: {p.Losses}  |  Empates: {p.Draws}\n" +
                $"Efectividad Global: {p.WinRatePercentage:F1}%  |  Partidas Guardadas en Análisis: {_recordedMatches.Count}";

            if (_leaderboardListContainer.GetChildCount() == 0)
            {
                PopulateDefaultLeaderboardPreview();
            }
        }

        private void PopulateDefaultLeaderboardPreview()
        {
            foreach (Node child in _leaderboardListContainer.GetChildren())
            {
                child.QueueFree();
            }

            PlayerProfile local = _localSession.ProfileX;
            var infoLbl = new Label
            {
                Text = $"1. {local.Username} (Tú) — {local.EloRating} pts [{local.RankTitle}]\n" +
                       "2. Bot_Master — 2000 pts [Maestro]\n" +
                       "3. Bot_Hard — 1650 pts [Platino]\n" +
                       "(Conéctate en Modo En Línea para consultar el ranking global del servidor en vivo)",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            infoLbl.AddThemeFontSizeOverride("font_size", 18);
            infoLbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 0.92f));
            _leaderboardListContainer.AddChild(infoLbl);
        }

        private void OnLeaderboardReceived(string[] entries)
        {
            Callable.From(() =>
            {
                foreach (Node child in _leaderboardListContainer.GetChildren())
                {
                    child.QueueFree();
                }

                for (int i = 0; i < entries.Length; i++)
                {
                    string[] fields = entries[i].Split(':');
                    if (fields.Length >= 5)
                    {
                        var rowLbl = new Label
                        {
                            Text = $"#{fields[0]}  {fields[1]}  —  {fields[2]} pts ({fields[3]})  [{fields[4]}]"
                        };
                        rowLbl.AddThemeFontSizeOverride("font_size", 19);
                        _leaderboardListContainer.AddChild(rowLbl);
                    }
                }
            }).CallDeferred();
        }

        // =====================================================================
        // INTERACCIÓN EN PARTIDA Y RED ONLINE
        // =====================================================================

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

            CheckAndRecordLocalCompletedMatch();

            if (_localSession.IsCurrentTurnBot() && !_localSession.State.IsGameOver)
            {
                await _localSession.PlayBotTurnAsync();
                CheckAndRecordLocalCompletedMatch();
            }
        }

        private void CheckAndRecordLocalCompletedMatch()
        {
            if (_localSession.State.IsGameOver && _localSession.MatchHistory.Count > 0)
            {
                RecordCompletedMatchIfNeeded(_localSession.MatchHistory[0]);
            }
        }

        private async void ConnectAndJoinOnlineQueue()
        {
            string username = string.IsNullOrWhiteSpace(_usernameInput?.Text) ? "Jugador" : _usernameInput.Text.Trim();
            string endpoint = string.IsNullOrWhiteSpace(_serverEndpointInput?.Text) ? "127.0.0.1:7777" : _serverEndpointInput.Text.Trim();

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
                else
                {
                    ConnectAndJoinOnlineQueue();
                }
                return;
            }

            _localSession.StartNewMatch();
        }

        private void OnAnySessionStateChanged()
        {
            Callable.From(RefreshUI).CallDeferred();
        }

        // =====================================================================
        // SINCRONIZACIÓN REACTIVA DE INTERFAZ Y ANIMACIONES
        // =====================================================================

        private void RefreshUI()
        {
            CheckAndRecordLocalCompletedMatch();

            // Actualizar encabezado y resumen del Lobby
            PlayerProfile localProf = _localSession.ProfileX;
            int currentRating = _isOnlineMode && _onlineClient.IsConnected ? _onlineClient.LocalElo : localProf.EloRating;
            string currentRank = EloRatingCalculator.GetRankTitle(currentRating);

            _topHeaderSubtitleLabel.Text = $"{TimeControls[_timeControlIndex].Name} • {currentRating} pts ({currentRank})";
            _lobbyProfileSummaryLabel.Text = $"{localProf.Username} (Tú) • {currentRating} pts";
            _lobbyLeagueBadgeLabel.Text = $"Liga {currentRank} • {localProf.Wins}V - {localProf.Losses}D - {localProf.Draws}E";

            _takebackOfferPanel.Visible = _isOnlineMode && _onlineClient.HasIncomingTakebackOffer;

            if (_isOnlineMode)
            {
                RefreshOnlineArenaUI();
            }
            else
            {
                RefreshLocalArenaUI();
            }

            UpdateClockLabelsAndPulseGlow();
        }

        private void RefreshOnlineArenaUI()
        {
            GameState state = _onlineClient.State;
            Move? lastMove = _onlineClient.LastNetworkMove ?? state.GetLastMove();

            Player myRole = _onlineClient.AssignedRole != Player.None ? _onlineClient.AssignedRole : Player.X;
            Player oppRole = myRole == Player.X ? Player.O : Player.X;

            Color myColor = myRole == Player.X ? PlayerXColor : PlayerOColor;
            Color oppColor = oppRole == Player.X ? PlayerXColor : PlayerOColor;

            SetPanelBorderColor(_leftPlayerCard, myColor);
            SetPanelBorderColor(_rightPlayerCard, oppColor);

            string myName = !string.IsNullOrEmpty(_onlineClient.LocalUsername) ? _onlineClient.LocalUsername : "Tú";
            string oppName = !string.IsNullOrEmpty(_onlineClient.OpponentUsername) ? _onlineClient.OpponentUsername : "Esperando rival...";

            int? myDelta = null;
            int? oppDelta = null;
            if (state.IsGameOver && _onlineClient.LastRatingUpdate.HasValue)
            {
                EloMatchUpdate upd = _onlineClient.LastRatingUpdate.Value;
                myDelta = myRole == Player.X ? upd.DeltaX : upd.DeltaO;
                oppDelta = oppRole == Player.X ? upd.DeltaX : upd.DeltaO;
            }

            // Tarjeta Izquierda = SIEMPRE Jugador Local ("Tú") con [X] u [O]
            _leftPlayerTitleLabel.Text = $"[{myRole}] {myName} (Tú)";
            _leftPlayerTitleLabel.AddThemeColorOverride("font_color", myColor);
            ConfigureRatingLabelWithDelta(_leftPlayerRatingLabel, _onlineClient.LocalElo, _onlineClient.LocalRankTitle, myDelta);

            // Tarjeta Derecha = SIEMPRE Oponente
            _rightPlayerTitleLabel.Text = $"[{oppRole}] {oppName}";
            _rightPlayerTitleLabel.AddThemeColorOverride("font_color", oppColor);
            string oppRank = EloRatingCalculator.GetRankTitle(_onlineClient.OpponentElo);
            ConfigureRatingLabelWithDelta(_rightPlayerRatingLabel, _onlineClient.OpponentElo, oppRank, oppDelta);

            if (!_onlineClient.IsConnected)
            {
                _statusBannerLabel.Text = "Modo En Línea • Presiona '🌐 Buscar' para conectar";
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

            _undoButton.Text = "⟲ Pedir";
            _undoButton.Disabled = !_onlineClient.IsInActiveMatch ||
                                   !state.CanUndo ||
                                   _onlineClient.HasIncomingTakebackOffer ||
                                   _onlineClient.IsWaitingForOpponentTakebackResponse;
            _redoButton.Disabled = true;
            _hintButton.Disabled = true;
            _analyzeMatchButton.Disabled = state.MoveCount == 0;
            _newGameOrResignButton.Text = _onlineClient.IsInActiveMatch ? "🏳 Rendirse" : "🌐 Buscar";
        }

        private void RefreshLocalArenaUI()
        {
            GameState state = _localSession.State;
            Move? lastMove = state.GetLastMove();

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
                    _statusBannerLabel.Text = $"¡FIN DE PARTIDA ({reasonText})! GANADOR: [{state.Winner}] • Toca '🔍 Analizar'";
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
                _telemetryLabel.Text = $"Movimientos: {state.MoveCount}/81  •  Partidas guardadas: {_recordedMatches.Count}";
            }

            bool allowTap = !state.IsGameOver && !_localSession.IsBotThinking && !_localSession.IsCurrentTurnBot();
            RenderBoardCellsAndAnimate(state, lastMove, allowTap);

            _undoButton.Text = "⟲ Deshacer";
            _undoButton.Disabled = _localSession.IsBotThinking || !state.CanUndo;
            _redoButton.Disabled = _localSession.IsBotThinking || !state.CanRedo;
            _hintButton.Disabled = _localSession.IsBotThinking || state.IsGameOver || _localSession.IsCurrentTurnBot();
            _analyzeMatchButton.Disabled = state.MoveCount == 0;
            _newGameOrResignButton.Text = "★ Nueva";
        }

        // =====================================================================
        // RENDERIZADO COMPARTIDO DEL TABLERO FRACTAL 9x9 Y ANIMACIONES
        // =====================================================================

        private Control CreateFractalBoardGrid(
            PanelContainer[] macroPanelsOut,
            Button[,] cellButtonsOut,
            Action<int, int> onCellPressed,
            int cellFontSize)
        {
            var aspectContainer = new AspectRatioContainer
            {
                Ratio = 1.0f,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };

            var macroGrid = new GridContainer
            {
                Columns = 3,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            macroGrid.AddThemeConstantOverride("h_separation", 12);
            macroGrid.AddThemeConstantOverride("v_separation", 12);
            aspectContainer.AddChild(macroGrid);

            for (int boardIdx = 0; boardIdx < GameState.BoardCount; boardIdx++)
            {
                var macroPanel = CreateStyledPanel(
                    CardBgColor,
                    InactiveMacroBorderColor,
                    borderWidth: 4,
                    cornerRadius: 16,
                    contentMargin: 8);

                macroPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                macroPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
                macroPanelsOut[boardIdx] = macroPanel;
                macroGrid.AddChild(macroPanel);

                var innerGrid = new GridContainer
                {
                    Columns = 3,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ExpandFill
                };
                innerGrid.AddThemeConstantOverride("h_separation", 6);
                innerGrid.AddThemeConstantOverride("v_separation", 6);
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
                    cellBtn.AddThemeFontSizeOverride("font_size", cellFontSize);
                    if (onCellPressed != null)
                    {
                        cellBtn.Pressed += () => onCellPressed(capturedBoard, capturedCell);
                    }

                    cellButtonsOut[boardIdx, cellIdx] = cellBtn;
                    innerGrid.AddChild(cellBtn);
                }
            }

            return aspectContainer;
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

            if (lastMove.HasValue && (!_lastAnimatedMove.HasValue || !_lastAnimatedMove.Value.Equals(lastMove.Value)))
            {
                _lastAnimatedMove = lastMove.Value;
                AnimateButtonPop(_cellButtons[lastMove.Value.BoardIndex, lastMove.Value.CellIndex]);
            }

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

        private static void RenderStaticBoardOnGrid(
            GameState state,
            Move? lastMove,
            PanelContainer[] macroPanels,
            Button[,] cellButtons)
        {
            bool hasWinningLine = state.TryGetWinningLine(out int winBoard, out int winC0, out int winC1, out int winC2);

            for (int board = 0; board < GameState.BoardCount; board++)
            {
                bool isBoardActive = !state.IsGameOver &&
                                     (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex ||
                                      state.ActiveBoardIndex == board);

                var macroStyle = (StyleBoxFlat)macroPanels[board].GetThemeStylebox("panel");
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
                    Button btn = cellButtons[board, cell];
                    Player occupant = state.GetCell(board, cell);

                    bool isLastMove = lastMove.HasValue &&
                                      lastMove.Value.BoardIndex == board &&
                                      lastMove.Value.CellIndex == cell;

                    bool isWinningCell = hasWinningLine &&
                                         board == winBoard &&
                                         (cell == winC0 || cell == winC1 || cell == winC2);

                    StyleCellButton(btn, occupant, isCellInteractive: false, isLastMove, isWinningCell, isHintMove: false);
                }
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

        private static void HighlightNavTabButton(Button btn, bool isActive)
        {
            if (btn == null) return;
            var style = new StyleBoxFlat
            {
                BgColor = isActive ? new Color(0.16f, 0.28f, 0.42f) : new Color(0.11f, 0.14f, 0.20f),
                BorderColor = isActive ? ActiveMacroBorderColor : InactiveMacroBorderColor,
                BorderWidthBottom = isActive ? 4 : 1,
                BorderWidthTop = isActive ? 2 : 1,
                BorderWidthLeft = isActive ? 2 : 1,
                BorderWidthRight = isActive ? 2 : 1,
                CornerRadiusTopLeft = 12,
                CornerRadiusTopRight = 12,
                CornerRadiusBottomLeft = 12,
                CornerRadiusBottomRight = 12
            };
            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeStyleboxOverride("hover", style);
            btn.AddThemeStyleboxOverride("pressed", style);
        }

        private static void HighlightSelectionButton(Button btn, bool isSelected)
        {
            if (btn == null) return;
            var style = new StyleBoxFlat
            {
                BgColor = isSelected ? new Color(0.16f, 0.30f, 0.46f) : new Color(0.12f, 0.15f, 0.22f),
                BorderColor = isSelected ? ActiveMacroBorderColor : InactiveMacroBorderColor,
                BorderWidthLeft = isSelected ? 3 : 1,
                BorderWidthRight = isSelected ? 3 : 1,
                BorderWidthTop = isSelected ? 3 : 1,
                BorderWidthBottom = isSelected ? 3 : 1,
                CornerRadiusTopLeft = 12,
                CornerRadiusTopRight = 12,
                CornerRadiusBottomLeft = 12,
                CornerRadiusBottomRight = 12
            };
            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeStyleboxOverride("hover", style);
            btn.AddThemeStyleboxOverride("pressed", style);
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
            btn.AddThemeFontSizeOverride("font_size", 20);
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
