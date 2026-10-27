using System;
using System.Collections.Generic;
using Godot;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.GodotClient
{
    /// <summary>
    /// Aplicación móvil multi-pantalla (Portrait 9:16) en Godot 4 (.NET / C#):
    /// - Pantalla de Autenticación (Iniciar Sesión y Registro de Nuevos Usuarios).
    /// - Barra de navegación principal de 3 pestañas: 🏠 Inicio, 🎬 Análisis y 🏆 Perfil.
    /// - Pantalla de Tablero (Arena) exclusiva durante partidas activas, con opciones post-partida de
    ///   🔄 Revancha, 🔍 Analizar Partida y 🏠 Volver al Menú.
    /// </summary>
    public partial class MobileGameScreen : Control
    {
        private const string DefaultServerHost = OnlineMultiplayerClient.OfficialCloudServerHost;
        private const int DefaultServerPort = 7777;
        private const string LocalAccountsFilePath = "user://local_accounts.tsv";
        private const string SavedSessionFilePath = "user://AuthData/saved_session.txt";

        private enum ScreenState
        {
            SplashLoading = -1,
            AuthScreen = 0,
            HomeLobby = 1,
            PlayArena = 2,
            AnalysisStudio = 3,
            ProfileAndLeagues = 4
        }

        private enum LobbyOpponentType
        {
            BotMinimax = 0,
            OnlineMultiplayer = 1,
            LocalPassAndPlay = 2
        }

        private ScreenState _currentScreen = ScreenState.SplashLoading;
        private LobbyOpponentType _selectedOpponentType = LobbyOpponentType.BotMinimax;
        private BotDifficulty _selectedBotDifficulty = BotDifficulty.Hard;
        private int _timeControlIndex = 2; // Blitz 3+2 por defecto

        private GameSessionController _localSession;
        private OnlineMultiplayerClient _onlineClient;
        private readonly MatchmakingService _localAuthStore = new MatchmakingService();
        private bool _isOnlineMode;
        private bool _hasUnfinishedLocalMatch;
        private readonly InteractiveTutorialController _tutorial = new InteractiveTutorialController();
        private string _authenticatedUsername = string.Empty;
        private string _authenticatedPassword = string.Empty;
        private double _queueElapsedSeconds;

        // Pantalla Splash de Carga Inicial con Logo y Barra de Progreso
        private Control _splashScreenContainer;
        private ProgressBar _splashProgressBar;
        private Label _splashPercentLabel;
        private Label _splashStatusLabel;
        private double _splashProgress;
        private bool _splashCompleted;

        // Audio Procedural Sintetizado (PCM 16-bit en memoria)
        private AudioStreamPlayer _sfxPlayer;
        private Button _soundToggleBtn;
        private bool _isMuted;
        private bool _lowTimeBeepPlayed;

        // Historial de partidas jugadas por el usuario (la posición 0 es SIEMPRE la última partida jugada)
        private readonly List<MatchRecord> _recordedMatches = new List<MatchRecord>();
        private MatchReplayNavigator _replayNavigator;
        private string _lastRecordedMatchId;

        // Contenedores raíz
        private PanelContainer _topHeaderPanel;
        private Control _authScreenContainer;
        private Control _homeScreenContainer;
        private Control _playScreenContainer;
        private Control _analysisScreenContainer;
        private Control _profileScreenContainer;
        private PanelContainer _bottomNavPanel;

        // Modales de cumplimiento de tiendas (Privacidad y Eliminación de Cuenta)
        private PanelContainer _deleteAccountConfirmPanel;
        private PanelContainer _privacyPolicyModalPanel;

        // Botones de la barra de navegación inferior (SOLO 3: Inicio, Análisis, Perfil)
        private Button _navHomeBtn;
        private Button _navAnalysisBtn;
        private Button _navProfileBtn;
        private Label _topHeaderSubtitleLabel;

        // =====================================================================
        // Referencias de UI: Pantalla 0 (🔐 LOGIN Y REGISTRO)
        // =====================================================================
        private LineEdit _authUsernameInput;
        private LineEdit _authPasswordInput;
        private Label _authFeedbackLabel;
        private Button _loginBtn;
        private Button _registerBtn;
        private bool _pendingRegisterAction;

        // =====================================================================
        // Referencias de UI: Pantalla 1 (🏠 INICIO / LOBBY)
        // =====================================================================
        private Label _lobbyProfileSummaryLabel;
        private Label _lobbyLeagueBadgeLabel;
        private PanelContainer _activeMatchesPanel;
        private VBoxContainer _activeMatchesListContainer;
        private Button _modeBotBtn;
        private Button _modeOnlineBtn;
        private Button _modePassPlayBtn;

        private Label _lobbySection2TitleLabel;
        private GridContainer _botDifficultyGrid;
        private GridContainer _quickPairingTimeGrid;

        // Panel de Loading al buscar partida en línea desde Inicio
        private PanelContainer _matchmakingLoadingPanel;
        private Label _matchmakingLoadingTitleLabel;
        private Label _matchmakingLoadingTimerLabel;

        // Salas privadas por código e aviso de desconexión
        private PanelContainer _privateLobbyPanel;
        private LineEdit _privateRoomCodeInput;
        private PanelContainer _serverDisconnectNoticePanel;
        private Label _serverDisconnectNoticeLabel;

        // =====================================================================
        // Referencias de UI: Pantalla 2 (♟ TABLERO ACTIVO - SOLO EN PARTIDA)
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

        private PanelContainer _rematchOfferPanel;
        private Label _rematchOfferLabel;

        private PanelContainer _abortedMatchPromptPanel;
        private Label _abortedMatchPromptLabel;

        private PanelContainer _statusBannerPanel;
        private Label _statusBannerLabel;
        private Label _telemetryLabel;

        // Barra durante la partida vs Barra al finalizar la partida
        private HBoxContainer _inGameActionsRow;
        private Button _undoButton;
        private Button _redoButton;
        private Button _hintButton;
        private Button _resignOrLeaveButton;

        private HBoxContainer _postGameActionsRow;
        private Button _rematchButton;
        private Button _analyzeFinishedMatchButton;
        private Button _backToMenuButton;

        // =====================================================================
        // Referencias de UI: Pantalla 3 (🎬 SALA DE ANÁLISIS)
        // =====================================================================
        private readonly PanelContainer[] _analysisMacroPanels = new PanelContainer[GameState.BoardCount];
        private readonly Button[,] _analysisCellButtons = new Button[GameState.BoardCount, GameState.CellsPerBoard];

        private OptionButton _matchSelectorDropdown;
        private ProgressBar _evalProgressBar;
        private Label _evalScoreLabel;
        private Label _analysisStepInfoLabel;
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

        // Estado de animación
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
            _sfxPlayer = new AudioStreamPlayer();
            AddChild(_sfxPlayer);

            try
            {
                string logDir = ProjectSettings.GlobalizePath("user://Logs");
                System.IO.Directory.CreateDirectory(logDir);
                GameLogger.ConfigureFileSink(System.IO.Path.Combine(logDir, "gatote_godot.log"));
            }
            catch { }
            GameLogger.Info("UI.Godot", "Aplicación Godot inicializada.");

            LoadLocalAccountsFromDisk();

            _localSession = new GameSessionController(
                SessionMode.HumanVsBotX,
                BotDifficulty.Hard,
                timeControl: TimeControlConfig.Unlimited);
            _localSession.StateChanged += OnAnySessionStateChanged;

            _onlineClient = new OnlineMultiplayerClient();
            _onlineClient.StateChanged += OnAnySessionStateChanged;
            _onlineClient.Authenticated += OnOnlineAuthenticated;
            _onlineClient.AccountDeleted += OnOnlineAccountDeleted;
            _onlineClient.MatchStarted += OnOnlineMatchStarted;
            _onlineClient.SessionResumed += OnOnlineSessionResumed;
            _onlineClient.MatchEnded += OnOnlineMatchEnded;
            _onlineClient.ServerConnectionLost += OnServerConnectionLost;
            _onlineClient.PrivateLobbyCreated += OnPrivateLobbyCreated;
            _onlineClient.ErrorReceived += OnOnlineErrorReceived;
            _onlineClient.LeaderboardReceived += OnLeaderboardReceived;

            BuildMultiScreenMobileApp();

            // Mostrar primero la pantalla Splash con el Logo Oficial y la Barra de Loading (0% -> 100%)
            _splashProgress = 0.0;
            _splashCompleted = false;
            NavigateToScreen(ScreenState.SplashLoading);
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
                _onlineClient.AccountDeleted -= OnOnlineAccountDeleted;
                _onlineClient.MatchStarted -= OnOnlineMatchStarted;
                _onlineClient.MatchEnded -= OnOnlineMatchEnded;
                _onlineClient.ServerConnectionLost -= OnServerConnectionLost;
                _onlineClient.PrivateLobbyCreated -= OnPrivateLobbyCreated;
                _onlineClient.ErrorReceived -= OnOnlineErrorReceived;
                _onlineClient.LeaderboardReceived -= OnLeaderboardReceived;
                _onlineClient.Dispose();
            }
        }

        public override void _Process(double delta)
        {
            _pulseTime += delta;

            if (_currentScreen == ScreenState.SplashLoading && !_splashCompleted)
            {
                _splashProgress = Math.Min(1.0, _splashProgress + delta / 1.35);
                int pct = (int)Math.Round(_splashProgress * 100.0);
                if (_splashProgressBar != null)
                {
                    _splashProgressBar.Value = pct;
                }
                if (_splashPercentLabel != null)
                {
                    _splashPercentLabel.Text = $"{pct}%";
                }
                if (_splashStatusLabel != null)
                {
                    if (_splashProgress < 0.35)
                        _splashStatusLabel.Text = "Cargando motor táctico Minimax + Zobrist...";
                    else if (_splashProgress < 0.72)
                        _splashStatusLabel.Text = "Inicializando tableros fractales y audio...";
                    else if (_splashProgress < 0.99)
                        _splashStatusLabel.Text = "Verificando sesión guardada en el dispositivo...";
                    else
                        _splashStatusLabel.Text = "¡Listo!";
                }

                if (_splashProgress >= 1.0)
                {
                    _splashCompleted = true;
                    CompleteInitialSplashTransition();
                }
                return;
            }

            if (_onlineClient.IsInQueue)
            {
                _queueElapsedSeconds += delta;
                if (_matchmakingLoadingTimerLabel != null)
                {
                    int secs = (int)Math.Floor(_queueElapsedSeconds);
                    string dots = new string('.', (secs % 3) + 1);
                    if (!string.IsNullOrEmpty(_onlineClient.PendingInviteCode))
                    {
                        _matchmakingLoadingTimerLabel.Text =
                            $"Comparte este código con tu amigo\nEspera: {secs / 60:D2}:{secs % 60:D2}{dots}";
                    }
                    else
                    {
                        _matchmakingLoadingTimerLabel.Text =
                            $"Espera: {secs / 60:D2}:{secs % 60:D2}{dots} • Modo {_onlineClient.Clock.Config.Name}";
                    }
                }
            }

            if (_onlineClient.IsInActiveMatch)
            {
                if (_currentScreen == ScreenState.PlayArena && _isOnlineMode &&
                    !_onlineClient.IsConnected && !_onlineClient.State.IsGameOver)
                {
                    _onlineClient.HandleUnexpectedServerDisconnect();
                    return;
                }
                _onlineClient.TickClock(delta);
            }

            if (_hasUnfinishedLocalMatch && !_localSession.State.IsGameOver)
            {
                _localSession.TickClock(delta);
            }

            if (_currentScreen == ScreenState.PlayArena)
            {
                UpdateClockLabelsAndPulseGlow();
            }
        }

        private void CompleteInitialSplashTransition()
        {
            PlaySynthesizedSound(GameSoundEffect.MoveTap);

            // Si el usuario ya inició sesión una vez, entrar directamente a Inicio sin pedir login de nuevo
            if (TryLoadSavedSession(out string savedUser, out string savedPass))
            {
                _authenticatedUsername = savedUser;
                _authenticatedPassword = savedPass;
                _localSession.ProfileX.Username = savedUser;
                NavigateToScreen(ScreenState.HomeLobby);
                _ = AuthenticateSavedSessionInBackgroundAsync(savedUser, savedPass);
            }
            else
            {
                // Solo para usuarios nuevos (o tras cerrar sesión explícitamente)
                NavigateToScreen(ScreenState.AuthScreen);
            }
        }

        private void PlaySynthesizedSound(GameSoundEffect effectType)
        {
            if (_isMuted || _sfxPlayer == null)
            {
                return;
            }

            try
            {
                byte[] pcmBytes = SoundEffectsSynthesizer.GeneratePcm16Bytes(effectType);
                var wavStream = new AudioStreamWav
                {
                    Format = AudioStreamWav.FormatEnum.Format16Bits,
                    MixRate = SoundEffectsSynthesizer.SampleRate,
                    Stereo = false,
                    Data = pcmBytes
                };
                _sfxPlayer.Stream = wavStream;
                _sfxPlayer.Play();
            }
            catch
            {
                // Ignorar si el dispositivo de audio no está disponible
            }
        }

        // =====================================================================
        // CONSTRUCCIÓN DE ARQUITECTURA DE PANTALLAS
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

            // 1. Barra Superior de Marca
            BuildTopAppHeader(rootColumn);

            // 2. Contenedor Central de Vistas
            var screenHost = new Control
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            rootColumn.AddChild(screenHost);

            _splashScreenContainer = BuildSplashLoadingScreen();
            _authScreenContainer = BuildAuthScreen();
            _homeScreenContainer = BuildHomeLobbyScreen();
            _playScreenContainer = BuildPlayArenaScreen();
            _analysisScreenContainer = BuildAnalysisStudioScreen();
            _profileScreenContainer = BuildProfileAndLeaguesScreen();

            screenHost.AddChild(_splashScreenContainer);
            screenHost.AddChild(_authScreenContainer);
            screenHost.AddChild(_homeScreenContainer);
            screenHost.AddChild(_playScreenContainer);
            screenHost.AddChild(_analysisScreenContainer);
            screenHost.AddChild(_profileScreenContainer);

            // Modales superpuestos (Política de Privacidad y Confirmación de Eliminación de Cuenta)
            _privacyPolicyModalPanel = BuildPrivacyPolicyModal();
            screenHost.AddChild(_privacyPolicyModalPanel);

            // 3. Barra de Navegación Inferior (SOLO 3 Pestañas: Inicio, Análisis, Perfil)
            BuildBottomNavigationBar(rootColumn);
        }

        private Control BuildSplashLoadingScreen()
        {
            var centerCol = new VBoxContainer();
            centerCol.SetAnchorsPreset(LayoutPreset.FullRect);
            centerCol.Alignment = BoxContainer.AlignmentMode.Center;
            centerCol.AddThemeConstantOverride("separation", 24);

            var splashCard = CreateStyledPanel(CardBgColor, ActiveMacroBorderColor, borderWidth: 3, cornerRadius: 24, contentMargin: 28);
            var innerCol = new VBoxContainer();
            innerCol.AddThemeConstantOverride("separation", 18);

            // Logo Oficial del Juego
            Texture2D logoTex = ResourceLoader.Exists("res://Assets/gatote_logo.png")
                ? ResourceLoader.Load<Texture2D>("res://Assets/gatote_logo.png")
                : null;

            if (logoTex != null)
            {
                var logoRect = new TextureRect
                {
                    Texture = logoTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(0, 340),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };
                innerCol.AddChild(logoRect);
            }

            var brandHeader = new Label
            {
                Text = "⚡ GATOTE FRACTAL • MUERTE SÚBITA",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            brandHeader.AddThemeFontSizeOverride("font_size", 28);
            brandHeader.AddThemeColorOverride("font_color", ActiveMacroBorderColor);
            innerCol.AddChild(brandHeader);

            _splashProgressBar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = 0,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 26),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            var barBg = new StyleBoxFlat
            {
                BgColor = SurfaceColor,
                CornerRadiusTopLeft = 12,
                CornerRadiusTopRight = 12,
                CornerRadiusBottomLeft = 12,
                CornerRadiusBottomRight = 12
            };
            var barFill = new StyleBoxFlat
            {
                BgColor = ActiveMacroBorderColor,
                CornerRadiusTopLeft = 12,
                CornerRadiusTopRight = 12,
                CornerRadiusBottomLeft = 12,
                CornerRadiusBottomRight = 12
            };
            _splashProgressBar.AddThemeStyleboxOverride("background", barBg);
            _splashProgressBar.AddThemeStyleboxOverride("fill", barFill);
            innerCol.AddChild(_splashProgressBar);

            _splashPercentLabel = new Label
            {
                Text = "0%",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _splashPercentLabel.AddThemeFontSizeOverride("font_size", 24);
            _splashPercentLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);
            innerCol.AddChild(_splashPercentLabel);

            _splashStatusLabel = new Label
            {
                Text = "Cargando motor táctico Minimax + Zobrist...",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _splashStatusLabel.AddThemeFontSizeOverride("font_size", 19);
            _splashStatusLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 0.92f));
            innerCol.AddChild(_splashStatusLabel);

            splashCard.AddChild(innerCol);
            centerCol.AddChild(splashCard);
            return centerCol;
        }

        private PanelContainer BuildPrivacyPolicyModal()
        {
            var modal = CreateStyledPanel(
                new Color(0.07f, 0.10f, 0.16f, 0.98f),
                AccentPrimaryColor,
                borderWidth: 3,
                cornerRadius: 20,
                contentMargin: 24);
            modal.SetAnchorsPreset(LayoutPreset.FullRect);
            modal.Visible = false;

            var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            col.AddThemeConstantOverride("separation", 16);

            var title = new Label
            {
                Text = "🔒 POLÍTICA DE PRIVACIDAD Y DATOS",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            title.AddThemeFontSizeOverride("font_size", 26);
            title.AddThemeColorOverride("font_color", ActiveMacroBorderColor);
            col.AddChild(title);

            var bodyLbl = new Label
            {
                Text = SoundEffectsSynthesizer.PrivacyPolicyInAppText,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            bodyLbl.AddThemeFontSizeOverride("font_size", 20);
            col.AddChild(bodyLbl);

            var closeBtn = CreateActionButton("✓ Entendido y Cerrar", () => modal.Visible = false, minHeight: 62);
            col.AddChild(closeBtn);

            modal.AddChild(col);
            return modal;
        }

        private void BuildTopAppHeader(VBoxContainer parent)
        {
            _topHeaderPanel = CreateStyledPanel(SurfaceColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 14, contentMargin: 12);
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 12);

            var brandTitle = new Label
            {
                Text = "GATOTE",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            brandTitle.AddThemeFontSizeOverride("font_size", 26);
            brandTitle.AddThemeColorOverride("font_color", new Color(0.95f, 0.98f, 1.0f));

            _topHeaderSubtitleLabel = new Label
            {
                Text = "Acceso",
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _topHeaderSubtitleLabel.AddThemeFontSizeOverride("font_size", 20);
            _topHeaderSubtitleLabel.AddThemeColorOverride("font_color", ActiveMacroBorderColor);

            _soundToggleBtn = CreateActionButton("Audio: Sí", OnToggleMutePressed, minHeight: 46);
            _soundToggleBtn.CustomMinimumSize = new Vector2(96, 46);
            _soundToggleBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

            row.AddChild(brandTitle);
            row.AddChild(_topHeaderSubtitleLabel);
            row.AddChild(_soundToggleBtn);
            _topHeaderPanel.AddChild(row);
            parent.AddChild(_topHeaderPanel);
        }

        private void OnToggleMutePressed()
        {
            _isMuted = !_isMuted;
            if (_soundToggleBtn != null)
            {
                _soundToggleBtn.Text = _isMuted ? "Audio: No" : "Audio: Sí";
            }
            if (!_isMuted)
            {
                PlaySynthesizedSound(GameSoundEffect.MoveTap);
            }
        }

        private void BuildBottomNavigationBar(VBoxContainer parent)
        {
            _bottomNavPanel = CreateStyledPanel(SurfaceColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 16, contentMargin: 8);
            var navRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            navRow.AddThemeConstantOverride("separation", 12);

            // REQUERIMIENTO 2: Solo 3 botones en el menú principal (Inicio, Análisis y Perfil).
            // La pantalla de Tablero solo aparece cuando se inicia una partida.
            _navHomeBtn = CreateActionButton("Inicio", () => NavigateToScreen(ScreenState.HomeLobby), minHeight: 68);
            _navAnalysisBtn = CreateActionButton("Análisis", () => NavigateToScreen(ScreenState.AnalysisStudio), minHeight: 68);
            _navProfileBtn = CreateActionButton("Perfil", () => NavigateToScreen(ScreenState.ProfileAndLeagues), minHeight: 68);

            navRow.AddChild(_navHomeBtn);
            navRow.AddChild(_navAnalysisBtn);
            navRow.AddChild(_navProfileBtn);
            _bottomNavPanel.AddChild(navRow);
            parent.AddChild(_bottomNavPanel);
        }

        private void ExitTutorialMode()
        {
            if (_tutorial.IsActive || _tutorial.CurrentStep != 0)
            {
                _tutorial.Exit();
                _currentHintMove = null;
            }
        }

        private void NavigateToScreen(ScreenState targetScreen)
        {
            if (targetScreen != ScreenState.PlayArena)
            {
                ExitTutorialMode();
            }

            // Si el usuario sale de Inicio mientras estaba en cola online, cancelar la búsqueda
            if (_currentScreen == ScreenState.HomeLobby && targetScreen != ScreenState.HomeLobby &&
                targetScreen != ScreenState.PlayArena && _onlineClient.IsInQueue)
            {
                _onlineClient.CancelMatchmakingQueue();
            }

            _currentScreen = targetScreen;

            if (_topHeaderPanel != null)
            {
                _topHeaderPanel.Visible = targetScreen != ScreenState.SplashLoading;
            }
            if (_splashScreenContainer != null)
            {
                _splashScreenContainer.Visible = targetScreen == ScreenState.SplashLoading;
            }

            _authScreenContainer.Visible = targetScreen == ScreenState.AuthScreen;
            _homeScreenContainer.Visible = targetScreen == ScreenState.HomeLobby;
            _playScreenContainer.Visible = targetScreen == ScreenState.PlayArena;
            _analysisScreenContainer.Visible = targetScreen == ScreenState.AnalysisStudio;
            _profileScreenContainer.Visible = targetScreen == ScreenState.ProfileAndLeagues;

            _bottomNavPanel.Visible =
                targetScreen == ScreenState.HomeLobby ||
                targetScreen == ScreenState.PlayArena ||
                targetScreen == ScreenState.AnalysisStudio ||
                targetScreen == ScreenState.ProfileAndLeagues;

            HighlightNavTabButton(_navHomeBtn, targetScreen == ScreenState.HomeLobby);
            HighlightNavTabButton(_navAnalysisBtn, targetScreen == ScreenState.AnalysisStudio);
            HighlightNavTabButton(_navProfileBtn, targetScreen == ScreenState.ProfileAndLeagues);

            if (targetScreen == ScreenState.AnalysisStudio)
            {
                // REQUERIMIENTO 3: Seleccionar por defecto la última partida jugada (índice 0)
                if (_recordedMatches.Count > 0)
                {
                    _matchSelectorDropdown.Selected = 0;
                    _replayNavigator = new MatchReplayNavigator(_recordedMatches[0]);
                }
                RefreshAnalysisUI();
            }
            else if (targetScreen == ScreenState.ProfileAndLeagues)
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
        // PANTALLA 0: 🔐 LOGIN Y REGISTRO DE USUARIOS (REQUERIMIENTO 6)
        // =====================================================================

        private Control BuildAuthScreen()
        {
            var centerCol = new VBoxContainer();
            centerCol.SetAnchorsPreset(LayoutPreset.FullRect);
            centerCol.Alignment = BoxContainer.AlignmentMode.Center;
            centerCol.AddThemeConstantOverride("separation", 18);

            var card = CreateStyledPanel(CardBgColor, AccentPrimaryColor, borderWidth: 3, cornerRadius: 20, contentMargin: 22);
            var formCol = new VBoxContainer();
            formCol.AddThemeConstantOverride("separation", 14);

            Texture2D logoTex = ResourceLoader.Exists("res://Assets/gatote_logo.png")
                ? ResourceLoader.Load<Texture2D>("res://Assets/gatote_logo.png")
                : null;
            if (logoTex != null)
            {
                var authLogo = new TextureRect
                {
                    Texture = logoTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(0, 170),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };
                formCol.AddChild(authLogo);
            }

            var title = new Label
            {
                Text = "TU CUENTA",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            title.AddThemeFontSizeOverride("font_size", 28);

            var subtitle = new Label
            {
                Text = "Entra con tu usuario o crea uno nuevo.",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            subtitle.AddThemeFontSizeOverride("font_size", 18);
            subtitle.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 0.92f));

            _authUsernameInput = new LineEdit
            {
                PlaceholderText = "Usuario (mín. 3 letras)",
                CustomMinimumSize = new Vector2(0, 58)
            };
            _authUsernameInput.AddThemeFontSizeOverride("font_size", 21);

            _authPasswordInput = new LineEdit
            {
                PlaceholderText = "Contraseña (mín. 4 caracteres)",
                Secret = true,
                CustomMinimumSize = new Vector2(0, 58)
            };
            _authPasswordInput.AddThemeFontSizeOverride("font_size", 21);

            _authFeedbackLabel = new Label
            {
                Text = string.Empty,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _authFeedbackLabel.AddThemeFontSizeOverride("font_size", 18);
            _authFeedbackLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);

            var btnRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            btnRow.AddThemeConstantOverride("separation", 12);

            _loginBtn = CreateActionButton("Entrar", () => OnSubmitAuthPressed(isRegister: false), minHeight: 62);
            _registerBtn = CreateActionButton("Crear cuenta", () => OnSubmitAuthPressed(isRegister: true), minHeight: 62);

            btnRow.AddChild(_loginBtn);
            btnRow.AddChild(_registerBtn);

            var secondaryRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            secondaryRow.AddThemeConstantOverride("separation", 12);

            var guestBtn = CreateActionButton("Modo Invitado", OnGuestLoginPressed, minHeight: 54);
            var privacyBtn = CreateActionButton("Privacidad", () => _privacyPolicyModalPanel.Visible = true, minHeight: 54);

            secondaryRow.AddChild(guestBtn);
            secondaryRow.AddChild(privacyBtn);

            formCol.AddChild(title);
            formCol.AddChild(subtitle);
            formCol.AddChild(_authUsernameInput);
            formCol.AddChild(_authPasswordInput);
            formCol.AddChild(_authFeedbackLabel);
            formCol.AddChild(btnRow);
            formCol.AddChild(secondaryRow);
            card.AddChild(formCol);
            centerCol.AddChild(card);

            return centerCol;
        }

        private void OnGuestLoginPressed()
        {
            _authenticatedUsername = "Invitado";
            _authenticatedPassword = string.Empty;
            _localSession.ProfileX.Username = "Invitado";
            NavigateToScreen(ScreenState.HomeLobby);
        }

        private async void OnSubmitAuthPressed(bool isRegister)
        {
            string username = _authUsernameInput.Text?.Trim() ?? string.Empty;
            string password = _authPasswordInput.Text?.Trim() ?? string.Empty;

            if (username.Length < 3)
            {
                _authFeedbackLabel.Text = "⚠ El nombre de usuario debe tener al menos 3 caracteres.";
                _authFeedbackLabel.AddThemeColorOverride("font_color", NegativeDeltaColor);
                return;
            }

            if (password.Length < 4)
            {
                _authFeedbackLabel.Text = "⚠ La contraseña debe tener al menos 4 caracteres.";
                _authFeedbackLabel.AddThemeColorOverride("font_color", NegativeDeltaColor);
                return;
            }

            _authenticatedPassword = password;
            _pendingRegisterAction = isRegister;
            _loginBtn.Disabled = true;
            _registerBtn.Disabled = true;
            _authFeedbackLabel.Text = isRegister
                ? $"Creando cuenta '{username}'..."
                : $"Iniciando sesión como '{username}'...";
            _authFeedbackLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);

            try
            {
                if (isRegister)
                {
                    await _onlineClient.ConnectAndRegisterAsync(DefaultServerHost, DefaultServerPort, username, password);
                }
                else
                {
                    await _onlineClient.ConnectAndLoginAsync(DefaultServerHost, DefaultServerPort, username, password);
                }
            }
            catch
            {
                // Si el servidor local 127.0.0.1:7777 no está encendido, autenticar contra el almacén local
                PlayerProfile localProfile;
                string err;
                bool ok = isRegister
                    ? _localAuthStore.RegisterAccount(username, password, out localProfile, out err)
                    : _localAuthStore.AuthenticateAccount(username, password, out localProfile, out err);

                _loginBtn.Disabled = false;
                _registerBtn.Disabled = false;

                if (!ok)
                {
                    _authFeedbackLabel.Text = $"⚠ {err}";
                    _authFeedbackLabel.AddThemeColorOverride("font_color", NegativeDeltaColor);
                    return;
                }

                SaveLocalAccountsToDisk();
                CompleteLoginSession(localProfile.Username);
            }
        }

        private async System.Threading.Tasks.Task AuthenticateSavedSessionInBackgroundAsync(string username, string password)
        {
            try
            {
                await _onlineClient.ConnectAndLoginAsync(DefaultServerHost, DefaultServerPort, username, password);
            }
            catch
            {
                // Si el servidor no está activo al abrir la app, permanece en Inicio y reconectará al buscar partida
            }
        }

        private void OnOnlineAuthenticated(string username, int rating, string rank)
        {
            Callable.From(() =>
            {
                _loginBtn.Disabled = false;
                _registerBtn.Disabled = false;
                CompleteLoginSession(username);
            }).CallDeferred();
        }

        private void CompleteLoginSession(string username)
        {
            _authenticatedUsername = username;
            _localSession.ProfileX.Username = username;

            if (!string.IsNullOrEmpty(_authenticatedPassword))
            {
                SaveSessionToDisk(_authenticatedUsername, _authenticatedPassword);
            }

            if (_currentScreen == ScreenState.AuthScreen)
            {
                NavigateToScreen(ScreenState.HomeLobby);
            }
            else
            {
                RefreshUI();
            }
        }

        private void SaveSessionToDisk(string username, string password)
        {
            try
            {
                string globalDir = ProjectSettings.GlobalizePath("user://AuthData");
                System.IO.Directory.CreateDirectory(globalDir);
                string sessionFile = System.IO.Path.Combine(globalDir, "saved_session.txt");
                System.IO.File.WriteAllText(sessionFile, $"{username}\t{password}");
            }
            catch
            {
                // Ignorar errores de disco
            }
        }

        private bool TryLoadSavedSession(out string username, out string password)
        {
            username = string.Empty;
            password = string.Empty;
            try
            {
                string globalDir = ProjectSettings.GlobalizePath("user://AuthData");
                string sessionFile = System.IO.Path.Combine(globalDir, "saved_session.txt");
                if (!System.IO.File.Exists(sessionFile))
                {
                    return false;
                }

                string raw = System.IO.File.ReadAllText(sessionFile).Trim();
                string[] parts = raw.Split('\t');
                if (parts.Length >= 2 && parts[0].Trim().Length >= 3 && parts[1].Trim().Length >= 4)
                {
                    username = parts[0].Trim();
                    password = parts[1].Trim();
                    return true;
                }
            }
            catch
            {
                // Ignorar
            }
            return false;
        }

        private void ClearSavedSessionFromDisk()
        {
            try
            {
                string globalDir = ProjectSettings.GlobalizePath("user://AuthData");
                string sessionFile = System.IO.Path.Combine(globalDir, "saved_session.txt");
                if (System.IO.File.Exists(sessionFile))
                {
                    System.IO.File.Delete(sessionFile);
                }
            }
            catch
            {
                // Ignorar
            }
        }

        private void LoadLocalAccountsFromDisk()
        {
            try
            {
                string globalDir = ProjectSettings.GlobalizePath("user://AuthData");
                _localAuthStore.LoadFromDirectory(globalDir);
            }
            catch
            {
                // Ignorar si no existe aún
            }
        }

        private void SaveLocalAccountsToDisk()
        {
            try
            {
                string globalDir = ProjectSettings.GlobalizePath("user://AuthData");
                _localAuthStore.SaveToDirectory(globalDir);
            }
            catch
            {
                // Ignorar
            }
        }

        // =====================================================================
        // PANTALLA 1: 🏠 INICIO Y EMPAREJAMIENTO RÁPIDO
        // =====================================================================

        private Control BuildHomeLobbyScreen()
        {
            var col = new VBoxContainer();
            col.SetAnchorsPreset(LayoutPreset.FullRect);
            col.AddThemeConstantOverride("separation", 12);

            // Aviso en caso de que el servidor se haya caído durante una partida
            _serverDisconnectNoticePanel = CreateStyledPanel(
                new Color(0.24f, 0.11f, 0.14f),
                NegativeDeltaColor,
                borderWidth: 2,
                cornerRadius: 14,
                contentMargin: 12);
            _serverDisconnectNoticePanel.Visible = false;
            var disconnectRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            disconnectRow.AddThemeConstantOverride("separation", 10);
            _serverDisconnectNoticeLabel = new Label
            {
                Text = "Se perdió la conexión con el servidor. La partida fue cancelada sin cambio de puntos.",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _serverDisconnectNoticeLabel.AddThemeFontSizeOverride("font_size", 17);
            _serverDisconnectNoticeLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.85f));
            var dismissNoticeBtn = CreateActionButton("Cerrar", () => _serverDisconnectNoticePanel.Visible = false, minHeight: 42);
            dismissNoticeBtn.CustomMinimumSize = new Vector2(90, 42);
            dismissNoticeBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            disconnectRow.AddChild(_serverDisconnectNoticeLabel);
            disconnectRow.AddChild(dismissNoticeBtn);
            _serverDisconnectNoticePanel.AddChild(disconnectRow);
            col.AddChild(_serverDisconnectNoticePanel);

            // 1. Tarjeta de Usuario Autenticado
            var accountCard = CreateStyledPanel(CardBgColor, AccentPrimaryColor, borderWidth: 2, cornerRadius: 16, contentMargin: 14);
            var accountBox = new VBoxContainer();
            accountBox.AddThemeConstantOverride("separation", 4);

            _lobbyProfileSummaryLabel = new Label
            {
                Text = "Jugador",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _lobbyProfileSummaryLabel.AddThemeFontSizeOverride("font_size", 24);

            _lobbyLeagueBadgeLabel = new Label
            {
                Text = "1200 pts • Liga Plata • 0V - 0D - 0E",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _lobbyLeagueBadgeLabel.AddThemeFontSizeOverride("font_size", 18);
            _lobbyLeagueBadgeLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.84f, 0.96f));

            accountBox.AddChild(_lobbyProfileSummaryLabel);
            accountBox.AddChild(_lobbyLeagueBadgeLabel);
            accountCard.AddChild(accountBox);
            col.AddChild(accountCard);

            // 1B. Recuadro de Partidas Activas (Estilo Lichess: permite regresar a una partida en curso desde Inicio)
            _activeMatchesPanel = CreateStyledPanel(
                new Color(0.09f, 0.17f, 0.15f),
                ActiveMacroBorderColor,
                borderWidth: 2,
                cornerRadius: 14,
                contentMargin: 12);
            _activeMatchesPanel.Visible = false;
            var activeOuterCol = new VBoxContainer();
            activeOuterCol.AddThemeConstantOverride("separation", 8);
            var activeHeaderLbl = new Label
            {
                Text = "PARTIDAS ACTIVAS",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            activeHeaderLbl.AddThemeFontSizeOverride("font_size", 18);
            activeHeaderLbl.AddThemeColorOverride("font_color", ActiveMacroBorderColor);
            activeOuterCol.AddChild(activeHeaderLbl);
            _activeMatchesListContainer = new VBoxContainer();
            _activeMatchesListContainer.AddThemeConstantOverride("separation", 8);
            activeOuterCol.AddChild(_activeMatchesListContainer);
            _activeMatchesPanel.AddChild(activeOuterCol);
            col.AddChild(_activeMatchesPanel);

            // 2. Selector de Modalidad de Juego
            var modeSectionTitle = new Label
            {
                Text = "MODO DE JUEGO",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            modeSectionTitle.AddThemeFontSizeOverride("font_size", 19);
            modeSectionTitle.AddThemeColorOverride("font_color", new Color(0.68f, 0.76f, 0.88f));
            col.AddChild(modeSectionTitle);

            var modeRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            modeRow.AddThemeConstantOverride("separation", 10);

            _modeBotBtn = CreateActionButton("Contra IA", () => SelectLobbyOpponentType(LobbyOpponentType.BotMinimax), minHeight: 62);
            _modeOnlineBtn = CreateActionButton("En Línea", () => SelectLobbyOpponentType(LobbyOpponentType.OnlineMultiplayer), minHeight: 62);
            _modePassPlayBtn = CreateActionButton("2 Jugadores", () => SelectLobbyOpponentType(LobbyOpponentType.LocalPassAndPlay), minHeight: 62);

            modeRow.AddChild(_modeBotBtn);
            modeRow.AddChild(_modeOnlineBtn);
            modeRow.AddChild(_modePassPlayBtn);
            col.AddChild(modeRow);

            // 3. Título dinámico de la sección 2 (Dificultad IA vs Ritmo de Tiempo)
            _lobbySection2TitleLabel = new Label
            {
                Text = "DIFICULTAD (SIN RELOJ)",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _lobbySection2TitleLabel.AddThemeFontSizeOverride("font_size", 19);
            _lobbySection2TitleLabel.AddThemeColorOverride("font_color", new Color(0.68f, 0.76f, 0.88f));
            col.AddChild(_lobbySection2TitleLabel);

            // 4A. REQUERIMIENTO 1: Cuadrícula 2x2 de Dificultad para Contra IA (SIN TIEMPO)
            _botDifficultyGrid = new GridContainer
            {
                Columns = 2,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            _botDifficultyGrid.AddThemeConstantOverride("h_separation", 14);
            _botDifficultyGrid.AddThemeConstantOverride("v_separation", 14);
            col.AddChild(_botDifficultyGrid);

            BotDifficulty[] diffs = { BotDifficulty.Easy, BotDifficulty.Medium, BotDifficulty.Hard, BotDifficulty.Master };
            string[] diffTitles =
            {
                "Fácil\nPara empezar",
                "Medio\nEquilibrado",
                "Difícil\nReto táctico",
                "Maestro\nNivel máximo"
            };

            for (int i = 0; i < diffs.Length; i++)
            {
                BotDifficulty capturedDiff = diffs[i];
                Button cardBtn = CreateLargeGridCardButton(diffTitles[i], () => StartBotMatchWithoutClock(capturedDiff));
                _botDifficultyGrid.AddChild(cardBtn);
            }

            // 4B. Cuadrícula 2x2 de Ritmos de Tiempo (Visible solo para En Línea y 2 Jugadores)
            _quickPairingTimeGrid = new GridContainer
            {
                Columns = 2,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                Visible = false
            };
            _quickPairingTimeGrid.AddThemeConstantOverride("h_separation", 14);
            _quickPairingTimeGrid.AddThemeConstantOverride("v_separation", 14);
            col.AddChild(_quickPairingTimeGrid);

            string[] subtitles =
            {
                "1 min + 1s\nRápido",
                "3 min + 0s\nDinámico",
                "3 min + 2s\nEstándar",
                "5 min + 0s\nCon calma"
            };

            for (int i = 0; i < TimeControls.Length; i++)
            {
                int capturedTimeIndex = i;
                TimeControlConfig tc = TimeControls[i];
                Button cardBtn = CreateLargeGridCardButton(
                    $"{tc.Name}\n{subtitles[i]}",
                    () => OnTimeControlCardSelected(capturedTimeIndex));
                _quickPairingTimeGrid.AddChild(cardBtn);
            }

            // 4B-Extra: Sala privada con amigo por código
            _privateLobbyPanel = CreateStyledPanel(CardBgColor, InactiveMacroBorderColor, borderWidth: 1, cornerRadius: 14, contentMargin: 10);
            _privateLobbyPanel.Visible = false;
            var privRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            privRow.AddThemeConstantOverride("separation", 8);

            var createPrivBtn = CreateActionButton("Crear sala", OnCreatePrivateRoomPressed, minHeight: 50);
            _privateRoomCodeInput = new LineEdit
            {
                PlaceholderText = "Código",
                MaxLength = 6,
                CustomMinimumSize = new Vector2(110, 50),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            _privateRoomCodeInput.AddThemeFontSizeOverride("font_size", 19);
            var joinPrivBtn = CreateActionButton("Unirse", OnJoinPrivateRoomPressed, minHeight: 50);

            privRow.AddChild(createPrivBtn);
            privRow.AddChild(_privateRoomCodeInput);
            privRow.AddChild(joinPrivBtn);
            _privateLobbyPanel.AddChild(privRow);
            col.AddChild(_privateLobbyPanel);

            // 4C. REQUERIMIENTO 11: Panel de Loading mientras busca rival en línea en Inicio
            _matchmakingLoadingPanel = CreateStyledPanel(
                new Color(0.10f, 0.16f, 0.25f),
                ActiveMacroBorderColor,
                borderWidth: 3,
                cornerRadius: 18,
                contentMargin: 24);
            _matchmakingLoadingPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _matchmakingLoadingPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
            _matchmakingLoadingPanel.Visible = false;

            var loadingCol = new VBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            loadingCol.AddThemeConstantOverride("separation", 18);

            _matchmakingLoadingTitleLabel = new Label
            {
                Text = "Buscando rival...",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _matchmakingLoadingTitleLabel.AddThemeFontSizeOverride("font_size", 26);
            _matchmakingLoadingTitleLabel.AddThemeColorOverride("font_color", ActiveMacroBorderColor);

            _matchmakingLoadingTimerLabel = new Label
            {
                Text = "Espera: 00:00",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _matchmakingLoadingTimerLabel.AddThemeFontSizeOverride("font_size", 22);

            var cancelQueueBtn = CreateActionButton("Cancelar", OnCancelMatchmakingPressed, minHeight: 60);

            loadingCol.AddChild(_matchmakingLoadingTitleLabel);
            loadingCol.AddChild(_matchmakingLoadingTimerLabel);
            loadingCol.AddChild(cancelQueueBtn);
            _matchmakingLoadingPanel.AddChild(loadingCol);
            col.AddChild(_matchmakingLoadingPanel);

            // 5. Guía minimalista de 3 pasos + Botón de Tutorial Interactivo
            var ruleCard = CreateStyledPanel(SurfaceColor, ActiveMacroBorderColor, borderWidth: 2, cornerRadius: 14, contentMargin: 12);
            var ruleCol = new VBoxContainer();
            ruleCol.AddThemeConstantOverride("separation", 8);
            var ruleLbl = new Label
            {
                Text = "Cómo jugar: 1. Juega en un cuadro verde • 2. Tu casilla envía al rival al siguiente tablero • 3. Gana el primero en hacer 3 en línea.",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            ruleLbl.AddThemeFontSizeOverride("font_size", 17);
            ruleLbl.AddThemeColorOverride("font_color", new Color(0.72f, 0.80f, 0.90f));
            var startTutorialBtn = CreateActionButton("Cómo jugar (Tutorial interactivo)", StartInteractiveTutorial, minHeight: 52);
            ruleCol.AddChild(ruleLbl);
            ruleCol.AddChild(startTutorialBtn);
            ruleCard.AddChild(ruleCol);
            col.AddChild(ruleCard);

            UpdateLobbyModeVisibility();
            return col;
        }

        private static Button CreateLargeGridCardButton(string text, Action onPressed)
        {
            var cardBtn = new Button
            {
                Text = text,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None
            };
            cardBtn.AddThemeFontSizeOverride("font_size", 23);

            var cardStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.13f, 0.18f, 0.26f),
                BorderColor = AccentPrimaryColor,
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
            cardBtn.Pressed += onPressed;
            return cardBtn;
        }

        private void SelectLobbyOpponentType(LobbyOpponentType type)
        {
            ExitTutorialMode();

            if (_onlineClient.IsInQueue)
            {
                _onlineClient.CancelMatchmakingQueue();
            }

            _selectedOpponentType = type;
            UpdateLobbyModeVisibility();
        }

        private void UpdateLobbyModeVisibility()
        {
            HighlightSelectionButton(_modeBotBtn, _selectedOpponentType == LobbyOpponentType.BotMinimax);
            HighlightSelectionButton(_modeOnlineBtn, _selectedOpponentType == LobbyOpponentType.OnlineMultiplayer);
            HighlightSelectionButton(_modePassPlayBtn, _selectedOpponentType == LobbyOpponentType.LocalPassAndPlay);

            bool isSearchingOnline = _selectedOpponentType == LobbyOpponentType.OnlineMultiplayer && _onlineClient.IsInQueue;
            _matchmakingLoadingPanel.Visible = isSearchingOnline;
            if (_privateLobbyPanel != null)
            {
                _privateLobbyPanel.Visible = _selectedOpponentType == LobbyOpponentType.OnlineMultiplayer && !isSearchingOnline;
            }

            if (isSearchingOnline)
            {
                _botDifficultyGrid.Visible = false;
                _quickPairingTimeGrid.Visible = false;
                _lobbySection2TitleLabel.Text = "BUSCANDO PARTIDA";
                return;
            }

            if (_selectedOpponentType == LobbyOpponentType.BotMinimax)
            {
                _lobbySection2TitleLabel.Text = "DIFICULTAD (SIN RELOJ)";
                _botDifficultyGrid.Visible = true;
                _quickPairingTimeGrid.Visible = false;
            }
            else if (_selectedOpponentType == LobbyOpponentType.OnlineMultiplayer)
            {
                _lobbySection2TitleLabel.Text = "RITMO DE JUEGO EN LÍNEA";
                _botDifficultyGrid.Visible = false;
                _quickPairingTimeGrid.Visible = true;
            }
            else
            {
                _lobbySection2TitleLabel.Text = "RITMO DE RELOJ (2 JUGADORES)";
                _botDifficultyGrid.Visible = false;
                _quickPairingTimeGrid.Visible = true;
            }
        }

        private void StartInteractiveTutorial()
        {
            if (_onlineClient.IsInQueue || !string.IsNullOrEmpty(_onlineClient.PendingInviteCode))
            {
                _onlineClient.CancelMatchmakingQueue();
            }
            _isOnlineMode = false;
            _lastAnimatedMove = null;
            _victoryLineAnimated = false;
            _tutorial.Start();
            _currentHintMove = _tutorial.TargetHintMove;

            NavigateToScreen(ScreenState.PlayArena);
        }

        private void StartBotMatchWithoutClock(BotDifficulty difficulty)
        {
            ExitTutorialMode();
            _selectedBotDifficulty = difficulty;
            _isOnlineMode = false;
            _hasUnfinishedLocalMatch = true;
            _currentHintMove = null;
            _lastAnimatedMove = null;
            _victoryLineAnimated = false;

            _localSession.ProfileX.Username = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : "Jugador";
            // REQUERIMIENTO 1: Contra IA no tiene límite de tiempo
            _localSession.SetTimeControl(TimeControlConfig.Unlimited);
            _localSession.ConfigureSession(SessionMode.HumanVsBotX, difficulty, _localSession.ProfileX);

            NavigateToScreen(ScreenState.PlayArena);
        }

        private async void OnTimeControlCardSelected(int timeIndex)
        {
            ExitTutorialMode();
            _timeControlIndex = timeIndex;
            TimeControlConfig selectedTime = TimeControls[_timeControlIndex];

            _currentHintMove = null;
            _lastAnimatedMove = null;
            _victoryLineAnimated = false;

            if (_selectedOpponentType == LobbyOpponentType.LocalPassAndPlay)
            {
                _isOnlineMode = false;
                _hasUnfinishedLocalMatch = true;
                _localSession.ProfileX.Username = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : "Jugador 1";
                _localSession.SetTimeControl(selectedTime);
                _localSession.ConfigureSession(SessionMode.PassAndPlay2P, _selectedBotDifficulty, _localSession.ProfileX);
                NavigateToScreen(ScreenState.PlayArena);
                return;
            }

            // REQUERIMIENTO 11: En modo En Línea, mostrar Loading ahí mismo en Inicio mientras encuentra rival
            _isOnlineMode = true;
            _queueElapsedSeconds = 0.0;

            try
            {
                if (!_onlineClient.IsConnected)
                {
                    string user = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : "Jugador";
                    string pass = !string.IsNullOrEmpty(_authenticatedPassword)
                        ? _authenticatedPassword
                        : (_authPasswordInput?.Text ?? string.Empty);
                    await _onlineClient.ConnectAndLoginAsync(DefaultServerHost, DefaultServerPort, user, pass);
                }

                _onlineClient.JoinMatchmakingQueue(selectedTime);
                _matchmakingLoadingTitleLabel.Text = $"Buscando rival ({selectedTime.Name})...";
                UpdateLobbyModeVisibility();
            }
            catch
            {
                _isOnlineMode = false;
                _lobbySection2TitleLabel.Text = "Servidor no disponible en este momento";
            }
        }

        private async void OnCreatePrivateRoomPressed()
        {
            ExitTutorialMode();
            TimeControlConfig selectedTime = TimeControls[_timeControlIndex];
            _isOnlineMode = true;
            _queueElapsedSeconds = 0.0;

            try
            {
                if (!_onlineClient.IsConnected)
                {
                    string user = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : "Jugador";
                    string pass = !string.IsNullOrEmpty(_authenticatedPassword)
                        ? _authenticatedPassword
                        : (_authPasswordInput?.Text ?? string.Empty);
                    await _onlineClient.ConnectAndLoginAsync(DefaultServerHost, DefaultServerPort, user, pass);
                }

                _onlineClient.CreatePrivateLobby(selectedTime);
                _matchmakingLoadingTitleLabel.Text = "Creando sala privada...";
                UpdateLobbyModeVisibility();
            }
            catch
            {
                _isOnlineMode = false;
                _lobbySection2TitleLabel.Text = "Servidor no disponible en este momento";
            }
        }

        private async void OnJoinPrivateRoomPressed()
        {
            string code = _privateRoomCodeInput?.Text?.Trim().ToUpperInvariant() ?? string.Empty;
            if (code.Length < 4)
            {
                _lobbySection2TitleLabel.Text = "Ingresa un código de 4 letras o números";
                return;
            }

            ExitTutorialMode();
            _isOnlineMode = true;
            _queueElapsedSeconds = 0.0;

            try
            {
                if (!_onlineClient.IsConnected)
                {
                    string user = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : "Jugador";
                    string pass = !string.IsNullOrEmpty(_authenticatedPassword)
                        ? _authenticatedPassword
                        : (_authPasswordInput?.Text ?? string.Empty);
                    await _onlineClient.ConnectAndLoginAsync(DefaultServerHost, DefaultServerPort, user, pass);
                }

                _onlineClient.JoinPrivateLobby(code);
            }
            catch
            {
                _isOnlineMode = false;
                _lobbySection2TitleLabel.Text = "Servidor no disponible en este momento";
            }
        }

        private void OnPrivateLobbyCreated(string code, string timeModeCode)
        {
            Callable.From(() =>
            {
                _matchmakingLoadingTitleLabel.Text = $"Sala privada: {code}";
                _matchmakingLoadingTimerLabel.Text = "Comparte este código con tu amigo";
                UpdateLobbyModeVisibility();
            }).CallDeferred();
        }

        private void OnServerConnectionLost(string message)
        {
            Callable.From(() =>
            {
                _isOnlineMode = false;
                _currentHintMove = null;
                _lastAnimatedMove = null;
                _victoryLineAnimated = false;

                if (_serverDisconnectNoticePanel != null && _serverDisconnectNoticeLabel != null)
                {
                    _serverDisconnectNoticeLabel.Text = message;
                    _serverDisconnectNoticePanel.Visible = true;
                }

                UpdateLobbyModeVisibility();
                if (_currentScreen != ScreenState.AuthScreen && _currentScreen != ScreenState.SplashLoading)
                {
                    NavigateToScreen(ScreenState.HomeLobby);
                }
            }).CallDeferred();
        }

        private void OnCancelMatchmakingPressed()
        {
            _onlineClient.CancelMatchmakingQueue();
            UpdateLobbyModeVisibility();
        }

        private void OnOnlineMatchStarted(string roomId, Player role, string userX, int eloX, string userO, int eloO)
        {
            Callable.From(() =>
            {
                ExitTutorialMode();
                _isOnlineMode = true;
                _currentHintMove = null;
                _lastAnimatedMove = null;
                _victoryLineAnimated = false;
                UpdateLobbyModeVisibility();
                NavigateToScreen(ScreenState.PlayArena);
            }).CallDeferred();
        }

        private void OnOnlineSessionResumed(string roomId)
        {
            Callable.From(RefreshUI).CallDeferred();
        }

        private void OnOnlineMatchEnded(Player winner, MatchEndReason reason, EloMatchUpdate eloUpdate)
        {
            Callable.From(() =>
            {
                if (_onlineClient.LastCompletedMatchRecord != null &&
                    _onlineClient.LastCompletedMatchRecord.EndReason != MatchEndReason.Aborted)
                {
                    RecordCompletedMatchIfNeeded(_onlineClient.LastCompletedMatchRecord);
                }
                RefreshUI();
            }).CallDeferred();
        }

        // =====================================================================
        // PANTALLA 2: ♟ TABLERO ACTIVO (SOLO DURANTE LA PARTIDA)
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

            // 2. Banners Interactivos de Confirmación (Takeback, Revancha y Expiración de 30s)
            BuildTakebackConsentBanner(mainColumn);
            BuildRematchConsentBanner(mainColumn);
            BuildAbortedMatchPromptBanner(mainColumn);

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

            // 5. Línea de Telemetría
            _telemetryLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _telemetryLabel.AddThemeFontSizeOverride("font_size", 18);
            _telemetryLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.82f));
            mainColumn.AddChild(_telemetryLabel);

            // 6A. Controles DURANTE la partida activa
            _inGameActionsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _inGameActionsRow.AddThemeConstantOverride("separation", 10);
            mainColumn.AddChild(_inGameActionsRow);

            _undoButton = CreateActionButton("Deshacer", OnUndoPressed, minHeight: 64);
            _redoButton = CreateActionButton("Rehacer", OnRedoPressed, minHeight: 64);
            _hintButton = CreateActionButton("Pista", OnHintPressed, minHeight: 64);
            _resignOrLeaveButton = CreateActionButton("Rendirse", OnResignOrLeavePressed, minHeight: 64);

            _inGameActionsRow.AddChild(_undoButton);
            _inGameActionsRow.AddChild(_redoButton);
            _inGameActionsRow.AddChild(_hintButton);
            _inGameActionsRow.AddChild(_resignOrLeaveButton);

            // 6B. Controles AL TERMINAR la partida (REQUERIMIENTO 2, 9 y 10: Revancha, Analizar y Volver al Menú)
            _postGameActionsRow = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Visible = false
            };
            _postGameActionsRow.AddThemeConstantOverride("separation", 10);
            mainColumn.AddChild(_postGameActionsRow);

            _rematchButton = CreateActionButton("Revancha", OnRematchPressed, minHeight: 68);
            _analyzeFinishedMatchButton = CreateActionButton("Analizar", OnOpenCurrentMatchInAnalysisPressed, minHeight: 68);
            _backToMenuButton = CreateActionButton("Inicio", () => NavigateToScreen(ScreenState.HomeLobby), minHeight: 68);

            _postGameActionsRow.AddChild(_rematchButton);
            _postGameActionsRow.AddChild(_analyzeFinishedMatchButton);
            _postGameActionsRow.AddChild(_backToMenuButton);

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

        private void BuildRematchConsentBanner(VBoxContainer parent)
        {
            _rematchOfferPanel = CreateStyledPanel(
                new Color(0.10f, 0.22f, 0.16f),
                ActiveMacroBorderColor,
                borderWidth: 3,
                cornerRadius: 14,
                contentMargin: 10);
            _rematchOfferPanel.Visible = false;

            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 8);

            _rematchOfferLabel = new Label
            {
                Text = "🔄 Tu oponente solicita la REVANCHA. ¿Aceptas jugar otra partida?",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _rematchOfferLabel.AddThemeFontSizeOverride("font_size", 20);
            _rematchOfferLabel.AddThemeColorOverride("font_color", ActiveMacroBorderColor);
            col.AddChild(_rematchOfferLabel);

            var btnRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            btnRow.AddThemeConstantOverride("separation", 12);

            var acceptBtn = CreateActionButton("✓ Aceptar Revancha", () => _onlineClient.RespondToOpponentRematch(true), minHeight: 54);
            var declineBtn = CreateActionButton("✕ Rechazar", () => _onlineClient.RespondToOpponentRematch(false), minHeight: 54);

            btnRow.AddChild(acceptBtn);
            btnRow.AddChild(declineBtn);
            col.AddChild(btnRow);

            _rematchOfferPanel.AddChild(col);
            parent.AddChild(_rematchOfferPanel);
        }

        private void BuildAbortedMatchPromptBanner(VBoxContainer parent)
        {
            _abortedMatchPromptPanel = CreateStyledPanel(
                new Color(0.24f, 0.13f, 0.15f),
                LastMoveHighlightColor,
                borderWidth: 3,
                cornerRadius: 14,
                contentMargin: 12);
            _abortedMatchPromptPanel.Visible = false;

            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 10);

            _abortedMatchPromptLabel = new Label
            {
                Text = "⏱ Se agotaron los 30s para realizar el primer movimiento y la partida fue cancelada (sin cambio de puntos).\n¿Quieres buscar una nueva partida?",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _abortedMatchPromptLabel.AddThemeFontSizeOverride("font_size", 20);
            _abortedMatchPromptLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);
            col.AddChild(_abortedMatchPromptLabel);

            var btnRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            btnRow.AddThemeConstantOverride("separation", 12);

            var searchAgainBtn = CreateActionButton("🔍 Buscar Nueva Partida", () =>
            {
                _selectedOpponentType = LobbyOpponentType.OnlineMultiplayer;
                NavigateToScreen(ScreenState.HomeLobby);
                OnTimeControlCardSelected(_timeControlIndex);
            }, minHeight: 58);

            var backMenuBtn = CreateActionButton("🏠 Volver al Menú", () => NavigateToScreen(ScreenState.HomeLobby), minHeight: 58);

            btnRow.AddChild(searchAgainBtn);
            btnRow.AddChild(backMenuBtn);
            col.AddChild(btnRow);

            _abortedMatchPromptPanel.AddChild(col);
            parent.AddChild(_abortedMatchPromptPanel);
        }

        // =====================================================================
        // PANTALLA 3: 🎬 SALA DE ANÁLISIS (POR DEFAULT LA ÚLTIMA PARTIDA JUGADA)
        // =====================================================================

        private Control BuildAnalysisStudioScreen()
        {
            var col = new VBoxContainer();
            col.SetAnchorsPreset(LayoutPreset.FullRect);
            col.AddThemeConstantOverride("separation", 10);

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

            var evalPanel = CreateStyledPanel(CardBgColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 12, contentMargin: 10);
            var evalCol = new VBoxContainer();
            evalCol.AddThemeConstantOverride("separation", 6);

            _evalScoreLabel = new Label
            {
                Text = "Partida equilibrada",
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

            Control analysisBoard = CreateFractalBoardGrid(_analysisMacroPanels, _analysisCellButtons, null, cellFontSize: 28);
            col.AddChild(analysisBoard);

            _analysisStepInfoLabel = new Label
            {
                Text = "Juega una partida para revisarla aquí jugada por jugada",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _analysisStepInfoLabel.AddThemeFontSizeOverride("font_size", 20);
            _analysisStepInfoLabel.AddThemeColorOverride("font_color", LastMoveHighlightColor);
            col.AddChild(_analysisStepInfoLabel);

            var navControlsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            navControlsRow.AddThemeConstantOverride("separation", 10);

            _stepStartBtn = CreateActionButton("Inicio", () => { _replayNavigator?.JumpToStart(); RefreshAnalysisUI(); }, minHeight: 58);
            _stepBackBtn = CreateActionButton("Anterior", () => { _replayNavigator?.StepBackward(); RefreshAnalysisUI(); }, minHeight: 58);
            _stepForwardBtn = CreateActionButton("Siguiente", () => { _replayNavigator?.StepForward(); RefreshAnalysisUI(); }, minHeight: 58);
            _stepEndBtn = CreateActionButton("Final", () => { _replayNavigator?.JumpToEnd(); RefreshAnalysisUI(); }, minHeight: 58);

            navControlsRow.AddChild(_stepStartBtn);
            navControlsRow.AddChild(_stepBackBtn);
            navControlsRow.AddChild(_stepForwardBtn);
            navControlsRow.AddChild(_stepEndBtn);
            col.AddChild(navControlsRow);

            PopulateMatchSelectorDropdown();
            return col;
        }

        private void RecordCompletedMatchIfNeeded(MatchRecord record)
        {
            if (record == null || record.MatchId == _lastRecordedMatchId || string.IsNullOrWhiteSpace(record.MoveTranscript))
            {
                return;
            }

            _lastRecordedMatchId = record.MatchId;
            // REQUERIMIENTO 3: Insertar siempre en índice 0 para que la última partida jugada sea la predeterminada
            _recordedMatches.Insert(0, record);
            _replayNavigator = new MatchReplayNavigator(record);
            PopulateMatchSelectorDropdown();
        }

        private void PopulateMatchSelectorDropdown()
        {
            if (_matchSelectorDropdown == null) return;

            _matchSelectorDropdown.Clear();
            if (_recordedMatches.Count == 0)
            {
                _matchSelectorDropdown.AddItem("Sin partidas registradas aún en esta sesión");
                return;
            }

            for (int i = 0; i < _recordedMatches.Count; i++)
            {
                MatchRecord m = _recordedMatches[i];
                string winnerTag = m.Winner == Player.None ? "Empate" : $"Ganó {m.Winner}";
                string latestTag = i == 0 ? " [Última Partida]" : "";
                _matchSelectorDropdown.AddItem($"#{i + 1}{latestTag}: {m.PlayerXName} vs {m.PlayerOName} ({winnerTag})");
            }

            _matchSelectorDropdown.Selected = 0;
        }

        private void OnReplayMatchSelected(long index)
        {
            if (index >= 0 && index < _recordedMatches.Count)
            {
                _replayNavigator = new MatchReplayNavigator(_recordedMatches[(int)index]);
                RefreshAnalysisUI();
            }
        }

        private void OnOpenCurrentMatchInAnalysisPressed()
        {
            if (_isOnlineMode && _onlineClient.LastCompletedMatchRecord != null)
            {
                RecordCompletedMatchIfNeeded(_onlineClient.LastCompletedMatchRecord);
            }
            else
            {
                CheckAndRecordLocalCompletedMatch();
            }

            NavigateToScreen(ScreenState.AnalysisStudio);
        }

        private void RefreshAnalysisUI()
        {
            if (_replayNavigator == null)
            {
                var emptyState = new GameState();
                RenderStaticBoardOnGrid(emptyState, null, _analysisMacroPanels, _analysisCellButtons);
                _evalScoreLabel.Text = "Selecciona una partida jugada";
                _evalProgressBar.Value = 50;
                _analysisStepInfoLabel.Text = "Termina una partida para revisarla aquí jugada por jugada";
                _stepStartBtn.Disabled = true;
                _stepBackBtn.Disabled = true;
                _stepForwardBtn.Disabled = true;
                _stepEndBtn.Disabled = true;
                return;
            }

            GameState state = _replayNavigator.CurrentState;
            Move? lastMove = _replayNavigator.LastExecutedMove;

            RenderStaticBoardOnGrid(state, lastMove, _analysisMacroPanels, _analysisCellButtons);

            int eval = _replayNavigator.GetCurrentEvaluationForX();
            float barPercent;

            if (eval >= MinimaxBot.WinScore - 500)
            {
                barPercent = 100f;
            }
            else if (eval <= -MinimaxBot.WinScore + 500)
            {
                barPercent = 0f;
            }
            else
            {
                barPercent = Mathf.Clamp(50f + (eval / 20.0f), 8f, 92f);
            }

            _evalScoreLabel.Text = _replayNavigator.GetCurrentPositionStatus();
            _evalProgressBar.Value = barPercent;

            if (lastMove.HasValue)
            {
                Player mover = state.CurrentPlayer == Player.X ? Player.O : Player.X;
                string quality = _replayNavigator.GetCurrentMoveQuality(out string explanation);
                _analysisStepInfoLabel.Text =
                    $"Jugada {_replayNavigator.CurrentPly}/{_replayNavigator.TotalPlies} ({mover}): " +
                    $"{quality} — {explanation}";
            }
            else
            {
                _analysisStepInfoLabel.Text = $"Inicio de la partida (0/{_replayNavigator.TotalPlies}) • Tiro libre";
            }

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
            col.AddThemeConstantOverride("separation", 10);

            var profileCard = CreateStyledPanel(CardBgColor, ActiveMacroBorderColor, borderWidth: 3, cornerRadius: 18, contentMargin: 16);
            var profileCol = new VBoxContainer();
            profileCol.AddThemeConstantOverride("separation", 8);

            var headerRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            headerRow.AddThemeConstantOverride("separation", 8);
            _profileNameLabel = new Label
            {
                Text = "Jugador (Tú)",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            _profileNameLabel.AddThemeFontSizeOverride("font_size", 24);

            var privacyBtn = CreateActionButton("🔒 Privacidad", () => _privacyPolicyModalPanel.Visible = true, minHeight: 46);
            privacyBtn.CustomMinimumSize = new Vector2(150, 46);
            privacyBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

            var logoutBtn = CreateActionButton("🚪 Salir", OnLogoutPressed, minHeight: 46);
            logoutBtn.CustomMinimumSize = new Vector2(130, 46);
            logoutBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

            headerRow.AddChild(_profileNameLabel);
            headerRow.AddChild(privacyBtn);
            headerRow.AddChild(logoutBtn);

            _profileRatingBigLabel = new Label
            {
                Text = "1200 pts • Liga Plata (Exclusivo Online)",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _profileRatingBigLabel.AddThemeFontSizeOverride("font_size", 24);
            _profileRatingBigLabel.AddThemeColorOverride("font_color", ActiveMacroBorderColor);

            _profileStatsGridLabel = new Label
            {
                Text = "Partidas Online: 0  |  Victorias: 0  |  Derrotas: 0  |  Empates: 0",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _profileStatsGridLabel.AddThemeFontSizeOverride("font_size", 18);

            var deleteAccountBtn = CreateActionButton("🗑️ Eliminar Cuenta Permanentemente", () =>
            {
                _deleteAccountConfirmPanel.Visible = !_deleteAccountConfirmPanel.Visible;
            }, minHeight: 46);

            _deleteAccountConfirmPanel = CreateStyledPanel(
                new Color(0.24f, 0.11f, 0.14f),
                NegativeDeltaColor,
                borderWidth: 2,
                cornerRadius: 12,
                contentMargin: 10);
            _deleteAccountConfirmPanel.Visible = false;

            var delCol = new VBoxContainer();
            delCol.AddThemeConstantOverride("separation", 8);
            var delLbl = new Label
            {
                Text = "⚠ ¿Seguro que deseas eliminar permanentemente tu cuenta, puntos y estadísticas del servidor y de este dispositivo?",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            delLbl.AddThemeFontSizeOverride("font_size", 17);
            delLbl.AddThemeColorOverride("font_color", NegativeDeltaColor);
            delCol.AddChild(delLbl);

            var delRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            delRow.AddThemeConstantOverride("separation", 10);
            var confirmDelBtn = CreateActionButton("🗑️ Sí, Eliminar Definitivamente", OnConfirmDeleteAccountPressed, minHeight: 48);
            var cancelDelBtn = CreateActionButton("✕ Cancelar", () => _deleteAccountConfirmPanel.Visible = false, minHeight: 48);
            delRow.AddChild(confirmDelBtn);
            delRow.AddChild(cancelDelBtn);
            delCol.AddChild(delRow);
            _deleteAccountConfirmPanel.AddChild(delCol);

            profileCol.AddChild(headerRow);
            profileCol.AddChild(_profileRatingBigLabel);
            profileCol.AddChild(_profileStatsGridLabel);
            profileCol.AddChild(deleteAccountBtn);
            profileCol.AddChild(_deleteAccountConfirmPanel);
            profileCard.AddChild(profileCol);
            col.AddChild(profileCard);

            var leaguesCard = CreateStyledPanel(SurfaceColor, InactiveMacroBorderColor, borderWidth: 2, cornerRadius: 16, contentMargin: 12);
            var leaguesCol = new VBoxContainer();
            leaguesCol.AddThemeConstantOverride("separation", 4);

            var leaguesTitle = new Label
            {
                Text = "🏅 ESCALAFÓN COMPETITIVO EN LÍNEA",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            leaguesTitle.AddThemeFontSizeOverride("font_size", 20);
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
                tierLbl.AddThemeFontSizeOverride("font_size", 17);
                leaguesCol.AddChild(tierLbl);
            }

            leaguesCard.AddChild(leaguesCol);
            col.AddChild(leaguesCard);

            var lbPanel = CreateStyledPanel(CardBgColor, AccentPrimaryColor, borderWidth: 2, cornerRadius: 16, contentMargin: 14);
            lbPanel.SizeFlagsVertical = SizeFlags.ExpandFill;

            var lbOuterCol = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            lbOuterCol.AddThemeConstantOverride("separation", 8);

            var lbHeaderRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var lbTitle = new Label
            {
                Text = "🌐 TOP JUGADORES ONLINE",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            lbTitle.AddThemeFontSizeOverride("font_size", 20);

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

        private void OnLogoutPressed()
        {
            if (_deleteAccountConfirmPanel != null)
            {
                _deleteAccountConfirmPanel.Visible = false;
            }
            ClearSavedSessionFromDisk();
            _onlineClient.Disconnect();
            _authenticatedUsername = string.Empty;
            _authenticatedPassword = string.Empty;
            _authPasswordInput.Text = string.Empty;
            _authFeedbackLabel.Text = "Sesión cerrada correctamente.";
            NavigateToScreen(ScreenState.AuthScreen);
        }

        private void OnConfirmDeleteAccountPressed()
        {
            if (_deleteAccountConfirmPanel != null)
            {
                _deleteAccountConfirmPanel.Visible = false;
            }

            string user = _authenticatedUsername;
            string pass = !string.IsNullOrEmpty(_authenticatedPassword)
                ? _authenticatedPassword
                : (_authPasswordInput?.Text ?? string.Empty);

            if (!string.IsNullOrEmpty(user) && user != "Invitado")
            {
                _localAuthStore.DeleteAccount(user, pass, out _);
                SaveLocalAccountsToDisk();
            }

            if (_onlineClient.IsConnected && _onlineClient.IsAuthenticated && !string.IsNullOrEmpty(pass))
            {
                _onlineClient.RequestDeleteAccount(pass);
            }

            ClearSavedSessionFromDisk();
            _onlineClient.Disconnect();
            _authenticatedUsername = string.Empty;
            _authenticatedPassword = string.Empty;
            if (_authUsernameInput != null) _authUsernameInput.Text = string.Empty;
            if (_authPasswordInput != null) _authPasswordInput.Text = string.Empty;
            if (_authFeedbackLabel != null)
            {
                _authFeedbackLabel.Text = "✓ Tu cuenta y todos tus datos han sido eliminados permanentemente.";
                _authFeedbackLabel.AddThemeColorOverride("font_color", PositiveDeltaColor);
            }
            NavigateToScreen(ScreenState.AuthScreen);
        }

        private void OnOnlineAccountDeleted(string deletedUsername)
        {
            Callable.From(() =>
            {
                ClearSavedSessionFromDisk();
                _authenticatedUsername = string.Empty;
                _authenticatedPassword = string.Empty;
                if (_authFeedbackLabel != null)
                {
                    _authFeedbackLabel.Text = $"✓ Cuenta '{deletedUsername}' eliminada permanentemente del servidor.";
                    _authFeedbackLabel.AddThemeColorOverride("font_color", PositiveDeltaColor);
                }
                NavigateToScreen(ScreenState.AuthScreen);
            }).CallDeferred();
        }

        private void RefreshProfileScreenUI()
        {
            string name = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : _localSession.ProfileX.Username;
            int rating = _onlineClient.IsAuthenticated ? _onlineClient.LocalElo : EloRatingCalculator.DefaultInitialRating;
            string rank = EloRatingCalculator.GetRankTitle(rating);
            int wins = _onlineClient.LocalWins;
            int losses = _onlineClient.LocalLosses;
            int draws = _onlineClient.LocalDraws;
            int totalOnline = wins + losses + draws;
            double winRate = totalOnline == 0 ? 0.0 : (wins * 100.0) / totalOnline;

            _profileNameLabel.Text = $"👤 {name} (Tú)";
            _profileRatingBigLabel.Text = $"{rating} pts • Liga {rank} (Clasificación Online)";
            _profileStatsGridLabel.Text =
                $"Partidas Online: {totalOnline}  |  Victorias: {wins}  |  Derrotas: {losses}  |  Empates: {draws}\n" +
                $"Efectividad Online: {winRate:F1}%  |  Partidas en Sala de Análisis: {_recordedMatches.Count}";
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
        // INTERACCIÓN EN PARTIDA, REVANCHA Y SALIDA AL MENÚ
        // =====================================================================

        private async void OnCellTapped(int boardIndex, int cellIndex)
        {
            if (_isOnlineMode)
            {
                _currentHintMove = null;
                _onlineClient.TrySendMove(boardIndex, cellIndex);
                return;
            }

            if (_tutorial.IsActive)
            {
                HandleTutorialCellTapped(boardIndex, cellIndex);
                return;
            }

            _currentHintMove = null;

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

        private void OnOnlineErrorReceived(string errorMsg)
        {
            Callable.From(() =>
            {
                _loginBtn.Disabled = false;
                _registerBtn.Disabled = false;

                if (_currentScreen == ScreenState.AuthScreen)
                {
                    _authFeedbackLabel.Text = $"⚠ {errorMsg}";
                    _authFeedbackLabel.AddThemeColorOverride("font_color", NegativeDeltaColor);
                }
                else if (_telemetryLabel != null)
                {
                    _telemetryLabel.Text = $"Servidor: {errorMsg}";
                }
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
                $"Jugada sugerida: Tablero {hint.BestMove.BoardIndex + 1}, Casilla {hint.BestMove.CellIndex + 1}";
            _hintButton.Disabled = false;
            RefreshUI();
        }

        private void OnResignOrLeavePressed()
        {
            if (_isOnlineMode)
            {
                if (_onlineClient.IsInActiveMatch)
                {
                    _onlineClient.ResignMatch();
                }
                return;
            }

            ExitTutorialMode();
            // En partida local (contra IA, 2 jugadores o tutorial), permite abandonar y volver al menú principal
            NavigateToScreen(ScreenState.HomeLobby);
        }

        private void HandleTutorialCellTapped(int boardIndex, int cellIndex)
        {
            bool accepted = _tutorial.TryHandleCellTap(boardIndex, cellIndex, out _);
            _currentHintMove = _tutorial.TargetHintMove;

            if (!accepted)
            {
                _telemetryLabel.Text = _tutorial.DetailText;
                return;
            }

            RefreshUI();
        }

        private void OnRematchPressed()
        {
            if (_tutorial.IsActive)
            {
                StartInteractiveTutorial();
                return;
            }

            _currentHintMove = null;
            _lastAnimatedMove = null;
            _victoryLineAnimated = false;

            if (_isOnlineMode)
            {
                _onlineClient.RequestRematch();
                RefreshUI();
                return;
            }

            _localSession.StartNewMatch();
            _hasUnfinishedLocalMatch = true;
            RefreshUI();
        }

        private void ResumeActiveOnlineMatch()
        {
            if (!_onlineClient.IsInActiveMatch) return;
            ExitTutorialMode();
            _isOnlineMode = true;
            _currentHintMove = null;
            NavigateToScreen(ScreenState.PlayArena);
        }

        private void ResumeActiveLocalMatch()
        {
            if (_localSession.State.IsGameOver) return;
            ExitTutorialMode();
            _isOnlineMode = false;
            _currentHintMove = null;
            NavigateToScreen(ScreenState.PlayArena);
        }

        private void DiscardActiveLocalMatch()
        {
            _hasUnfinishedLocalMatch = false;
            _localSession.StartNewMatch();
            RefreshUI();
        }

        private void RefreshActiveMatchesPanelUI()
        {
            if (_activeMatchesPanel == null || _activeMatchesListContainer == null)
            {
                return;
            }

            if (_localSession.State.IsGameOver)
            {
                _hasUnfinishedLocalMatch = false;
            }

            bool hasOnlineActive = _onlineClient.IsInActiveMatch;
            bool hasLocalActive = _hasUnfinishedLocalMatch && !_localSession.State.IsGameOver;

            foreach (Node child in _activeMatchesListContainer.GetChildren())
            {
                child.QueueFree();
            }

            if (!hasOnlineActive && !hasLocalActive)
            {
                _activeMatchesPanel.Visible = false;
                return;
            }

            _activeMatchesPanel.Visible = true;

            if (hasOnlineActive)
            {
                string oppName = !string.IsNullOrEmpty(_onlineClient.OpponentUsername) ? _onlineClient.OpponentUsername : "Rival";
                Player myRole = _onlineClient.AssignedRole != Player.None ? _onlineClient.AssignedRole : Player.X;
                bool isMyTurn = _onlineClient.State.CurrentPlayer == myRole;
                string turnText = isMyTurn ? "Tu turno" : $"Turno de {oppName}";

                var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddThemeConstantOverride("separation", 10);
                var lbl = new Label
                {
                    Text = $"En línea vs {oppName} ({_onlineClient.Clock.Config.Name}) • Jugada {_onlineClient.State.MoveCount} ({turnText})",
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                lbl.AddThemeFontSizeOverride("font_size", 17);
                var resumeBtn = CreateActionButton("Regresar", ResumeActiveOnlineMatch, minHeight: 44);
                resumeBtn.CustomMinimumSize = new Vector2(120, 44);
                resumeBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

                row.AddChild(lbl);
                row.AddChild(resumeBtn);
                _activeMatchesListContainer.AddChild(row);
            }

            if (hasLocalActive)
            {
                bool isVsAi = _localSession.Mode == SessionMode.HumanVsBotX || _localSession.Mode == SessionMode.BotVsHumanO;
                string modeDesc = isVsAi
                    ? $"Contra IA ({_localSession.BotDifficulty})"
                    : $"2 Jugadores ({_localSession.Clock.Config.Name})";

                var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddThemeConstantOverride("separation", 8);
                var lbl = new Label
                {
                    Text = $"{modeDesc} • Jugada {_localSession.State.MoveCount} (Turno de {_localSession.State.CurrentPlayer})",
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                lbl.AddThemeFontSizeOverride("font_size", 17);
                var resumeBtn = CreateActionButton("Regresar", ResumeActiveLocalMatch, minHeight: 44);
                resumeBtn.CustomMinimumSize = new Vector2(115, 44);
                resumeBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
                var closeBtn = CreateActionButton("Cerrar", DiscardActiveLocalMatch, minHeight: 44);
                closeBtn.CustomMinimumSize = new Vector2(95, 44);
                closeBtn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;

                row.AddChild(lbl);
                row.AddChild(resumeBtn);
                row.AddChild(closeBtn);
                _activeMatchesListContainer.AddChild(row);
            }
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

            string displayUser = !string.IsNullOrEmpty(_authenticatedUsername)
                ? _authenticatedUsername
                : _localSession.ProfileX.Username;

            int onlineRating = _onlineClient.IsAuthenticated ? _onlineClient.LocalElo : EloRatingCalculator.DefaultInitialRating;
            string onlineRank = EloRatingCalculator.GetRankTitle(onlineRating);

            _topHeaderSubtitleLabel.Text = _currentScreen == ScreenState.AuthScreen
                ? "Acceso"
                : $"{displayUser} • {onlineRating} pts";

            _lobbyProfileSummaryLabel.Text = displayUser;
            _lobbyLeagueBadgeLabel.Text =
                $"{onlineRating} pts • Liga {onlineRank} • " +
                $"{_onlineClient.LocalWins}V - {_onlineClient.LocalLosses}D - {_onlineClient.LocalDraws}E";

            RefreshActiveMatchesPanelUI();
            UpdateLobbyModeVisibility();

            _takebackOfferPanel.Visible = _isOnlineMode && _onlineClient.HasIncomingTakebackOffer;

            if (_isOnlineMode)
            {
                RefreshOnlineArenaUI();
            }
            else
            {
                _rematchOfferPanel.Visible = false;
                _abortedMatchPromptPanel.Visible = false;
                RefreshLocalArenaUI();
            }

            if (_currentScreen == ScreenState.PlayArena)
            {
                UpdateClockLabelsAndPulseGlow();
            }
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

            string myName = !string.IsNullOrEmpty(_onlineClient.LocalUsername) ? _onlineClient.LocalUsername : _authenticatedUsername;
            string oppName = !string.IsNullOrEmpty(_onlineClient.OpponentUsername) ? _onlineClient.OpponentUsername : "Oponente";

            int? myDelta = null;
            int? oppDelta = null;
            if (state.IsGameOver && _onlineClient.LastRatingUpdate.HasValue && _onlineClient.LastEndReason != MatchEndReason.Aborted)
            {
                EloMatchUpdate upd = _onlineClient.LastRatingUpdate.Value;
                myDelta = myRole == Player.X ? upd.DeltaX : upd.DeltaO;
                oppDelta = oppRole == Player.X ? upd.DeltaX : upd.DeltaO;
            }

            // Tarjeta Izquierda = SIEMPRE Jugador Local ("Tú")
            _leftPlayerTitleLabel.Text = $"[{myRole}] {myName} (Tú)";
            _leftPlayerTitleLabel.AddThemeColorOverride("font_color", myColor);
            ConfigureOnlineRatingLabel(_leftPlayerRatingLabel, _onlineClient.LocalElo, _onlineClient.LocalRankTitle, myDelta);

            // Tarjeta Derecha = SIEMPRE Oponente
            _rightPlayerTitleLabel.Text = $"[{oppRole}] {oppName}";
            _rightPlayerTitleLabel.AddThemeColorOverride("font_color", oppColor);
            string oppRank = EloRatingCalculator.GetRankTitle(_onlineClient.OpponentElo);
            ConfigureOnlineRatingLabel(_rightPlayerRatingLabel, _onlineClient.OpponentElo, oppRank, oppDelta);

            _leftClockLabel.Visible = true;
            _rightClockLabel.Visible = true;

            bool isAbortedFirstMove = state.IsGameOver && _onlineClient.LastEndReason == MatchEndReason.Aborted;
            _abortedMatchPromptPanel.Visible = isAbortedFirstMove;

            _rematchOfferPanel.Visible = state.IsGameOver && _onlineClient.HasOpponentOfferedRematch;
            if (_onlineClient.HasOpponentOfferedRematch)
            {
                _rematchOfferLabel.Text = $"🔄 {oppName} [{oppRole}] solicita la REVANCHA. ¿Aceptas jugar otra partida?";
            }

            if (state.IsGameOver)
            {
                string reasonText = FormatEndReason(_onlineClient.LastEndReason);
                if (isAbortedFirstMove)
                {
                    _statusBannerLabel.Text = "⚠ PARTIDA CANCELADA (Sin primer movimiento en 30s) • Sin cambio de puntos";
                }
                else if (state.Winner == myRole)
                {
                    _statusBannerLabel.Text = $"🏆 ¡VICTORIA ({reasonText})! Ganaste con [{myRole}]";
                }
                else if (state.Winner == oppRole)
                {
                    _statusBannerLabel.Text = $"DERROTA ({reasonText}) • Ganó {oppName} [{oppRole}]";
                }
                else
                {
                    _statusBannerLabel.Text = $"¡EMPATE GLOBAL ({reasonText})!";
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
                            !state.IsGameOver &&
                            !isAbortedFirstMove &&
                            state.CurrentPlayer == myRole &&
                            !_onlineClient.IsWaitingForOpponentTakebackResponse;

            RenderBoardCellsAndAnimate(state, lastMove, allowTap);

            // REQUERIMIENTO 9 y 10: Al terminar la partida, ocultar controles en vivo (y deshabilitar Rendirse)
            // y mostrar la barra post-partida (Revancha, Analizar y Volver al Menú)
            _inGameActionsRow.Visible = !state.IsGameOver;
            _postGameActionsRow.Visible = state.IsGameOver && !isAbortedFirstMove;

            if (!state.IsGameOver)
            {
                if (_onlineClient.Clock.IsWaitingForFirstMove)
                {
                    int secsLeft = (int)Math.Ceiling(_onlineClient.Clock.FirstMoveSecondsRemaining);
                    _telemetryLabel.Text = $"{secsLeft}s para hacer el primer tiro";
                }
                else if (!_onlineClient.IsWaitingForOpponentTakebackResponse)
                {
                    _telemetryLabel.Text = string.Empty;
                }

                _undoButton.Text = "⟲ Pedir Deshacer";
                _undoButton.Disabled = !_onlineClient.IsInActiveMatch ||
                                       !state.CanUndo ||
                                       _onlineClient.HasIncomingTakebackOffer ||
                                       _onlineClient.IsWaitingForOpponentTakebackResponse;
                _redoButton.Visible = false;
                // REQUERIMIENTO 5: Pista solo contra la IA
                _hintButton.Visible = false;
                _resignOrLeaveButton.Text = state.MoveCount == 0 ? "✕ Cancelar" : "🏳 Rendirse";
                _resignOrLeaveButton.Disabled = !_onlineClient.IsInActiveMatch;
            }
            else
            {
                if (_onlineClient.RematchDeclinedByOpponent)
                {
                    _rematchButton.Text = "✕ Revancha Rechazada";
                    _rematchButton.Disabled = true;
                    _telemetryLabel.Text = $"{oppName} rechazó la solicitud de revancha.";
                }
                else if (_onlineClient.HasRequestedRematch)
                {
                    _rematchButton.Text = "⏳ Esperando Rival...";
                    _rematchButton.Disabled = true;
                }
                else if (_onlineClient.HasOpponentOfferedRematch)
                {
                    _rematchButton.Text = "✓ Aceptar Revancha";
                    _rematchButton.Disabled = false;
                }
                else
                {
                    _rematchButton.Text = "🔄 Revancha";
                    _rematchButton.Disabled = false;
                }

                _analyzeFinishedMatchButton.Visible = state.MoveCount > 0;
                _analyzeFinishedMatchButton.Disabled = state.MoveCount == 0;
            }
        }

        private void RefreshLocalArenaUI()
        {
            if (_tutorial.IsActive)
            {
                GameState tutorialState = _tutorial.BoardState;
                Move? tutorialLastMove = tutorialState.GetLastMove();

                SetPanelBorderColor(_leftPlayerCard, PlayerXColor);
                SetPanelBorderColor(_rightPlayerCard, PlayerOColor);

                _leftPlayerTitleLabel.Text = "[X] Tú";
                _leftPlayerTitleLabel.AddThemeColorOverride("font_color", PlayerXColor);
                _leftPlayerRatingLabel.Text = "Tutorial interactivo";

                _rightPlayerTitleLabel.Text = "[O] Guía";
                _rightPlayerTitleLabel.AddThemeColorOverride("font_color", PlayerOColor);
                _rightPlayerRatingLabel.Text = _tutorial.ProgressLabel;

                _leftClockLabel.Visible = false;
                _rightClockLabel.Visible = false;

                _currentHintMove = _tutorial.TargetHintMove;
                _statusBannerLabel.Text = _tutorial.BannerText;
                _telemetryLabel.Text = _tutorial.DetailText;

                RenderBoardCellsAndAnimate(tutorialState, tutorialLastMove, !tutorialState.IsGameOver);

                _inGameActionsRow.Visible = !tutorialState.IsGameOver;
                _postGameActionsRow.Visible = tutorialState.IsGameOver;

                if (!tutorialState.IsGameOver)
                {
                    _undoButton.Visible = false;
                    _redoButton.Visible = false;
                    _hintButton.Visible = false;
                    _resignOrLeaveButton.Text = "Salir";
                    _resignOrLeaveButton.Disabled = false;
                }
                else
                {
                    _rematchButton.Text = "Repetir tutorial";
                    _rematchButton.Disabled = false;
                    _analyzeFinishedMatchButton.Visible = false;
                }
                return;
            }

            GameState state = _localSession.State;
            Move? lastMove = state.GetLastMove();

            _undoButton.Visible = true;

            bool isVsAi = _localSession.Mode == SessionMode.HumanVsBotX || _localSession.Mode == SessionMode.BotVsHumanO;
            bool humanIsO = _localSession.Mode == SessionMode.BotVsHumanO;
            Player myRole = humanIsO ? Player.O : Player.X;
            Player oppRole = humanIsO ? Player.X : Player.O;

            PlayerProfile myProfile = myRole == Player.X ? _localSession.ProfileX : _localSession.ProfileO;
            PlayerProfile oppProfile = oppRole == Player.X ? _localSession.ProfileX : _localSession.ProfileO;

            Color myColor = myRole == Player.X ? PlayerXColor : PlayerOColor;
            Color oppColor = oppRole == Player.X ? PlayerXColor : PlayerOColor;

            SetPanelBorderColor(_leftPlayerCard, myColor);
            SetPanelBorderColor(_rightPlayerCard, oppColor);

            // REQUERIMIENTO 8: Las partidas contra IA o 2 jugadores locales NO suman ni restan puntaje competitivo
            _leftPlayerTitleLabel.Text = $"[{myRole}] {myProfile.Username} (Tú)";
            _leftPlayerTitleLabel.AddThemeColorOverride("font_color", myColor);
            _leftPlayerRatingLabel.Text = isVsAi ? "Partida vs IA (Sin puntos)" : "Jugador Local 1 (Sin puntos)";
            _leftPlayerRatingLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 0.92f));

            _rightPlayerTitleLabel.Text = $"[{oppRole}] {oppProfile.Username}";
            _rightPlayerTitleLabel.AddThemeColorOverride("font_color", oppColor);
            _rightPlayerRatingLabel.Text = isVsAi ? $"IA Nivel {_localSession.BotDifficulty}" : "Jugador Local 2 (Sin puntos)";
            _rightPlayerRatingLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 0.92f));

            // REQUERIMIENTO 1: Ocultar el reloj cuando se juega contra la IA (Sin límite de tiempo)
            bool hasClock = !_localSession.Clock.Config.IsUntimed;
            _leftClockLabel.Visible = hasClock;
            _rightClockLabel.Visible = hasClock;

            if (state.IsGameOver)
            {
                string reasonText = FormatEndReason(_localSession.LastEndReason);
                if (state.Winner != Player.None)
                {
                    _statusBannerLabel.Text = $"Fin de partida ({reasonText}) • Ganó {state.Winner}";
                }
                else
                {
                    _statusBannerLabel.Text = "Empate global";
                }
            }
            else if (_localSession.IsBotThinking)
            {
                _statusBannerLabel.Text = "La IA está pensando...";
            }
            else if (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex)
            {
                _statusBannerLabel.Text = $"Turno de {state.CurrentPlayer} • Tiro libre (cuadros verdes)";
            }
            else
            {
                _statusBannerLabel.Text = $"Turno de {state.CurrentPlayer} • Juega en el cuadro verde";
            }

            if (!_currentHintMove.HasValue)
            {
                _telemetryLabel.Text = state.MoveCount == 0
                    ? "Toca cualquier casilla para empezar"
                    : $"Jugada {state.MoveCount}";
            }

            bool allowTap = !state.IsGameOver && !_localSession.IsBotThinking && !_localSession.IsCurrentTurnBot();
            RenderBoardCellsAndAnimate(state, lastMove, allowTap);

            _inGameActionsRow.Visible = !state.IsGameOver;
            _postGameActionsRow.Visible = state.IsGameOver;

            if (!state.IsGameOver)
            {
                _undoButton.Text = "Deshacer";
                _undoButton.Disabled = _localSession.IsBotThinking || !state.CanUndo;
                _redoButton.Visible = true;
                _redoButton.Disabled = _localSession.IsBotThinking || !state.CanRedo;

                // REQUERIMIENTO 5: La opción de Pista SOLO aparece en partidas contra la IA
                _hintButton.Visible = isVsAi;
                _hintButton.Disabled = !isVsAi || _localSession.IsBotThinking || _localSession.IsCurrentTurnBot();

                _resignOrLeaveButton.Text = "Salir";
                _resignOrLeaveButton.Disabled = false;
            }
            else
            {
                _rematchButton.Text = "Revancha";
                _rematchButton.Disabled = false;
                _analyzeFinishedMatchButton.Visible = state.MoveCount > 0;
                _analyzeFinishedMatchButton.Disabled = state.MoveCount == 0;
            }
        }

        // =====================================================================
        // RENDERIZADO COMPARTIDO DEL TABLERO FRACTAL 9x9, RELOJES Y ANIMACIONES
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

        private static void ConfigureOnlineRatingLabel(Label label, int rating, string rankTitle, int? delta)
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
            GameState state = _isOnlineMode
                ? _onlineClient.State
                : (_tutorial.IsActive ? _tutorial.BoardState : _localSession.State);
            MatchClock clock = _isOnlineMode ? _onlineClient.Clock : _localSession.Clock;

            if (!clock.Config.IsUntimed)
            {
                Player myRole = _isOnlineMode
                    ? (_onlineClient.AssignedRole != Player.None ? _onlineClient.AssignedRole : Player.X)
                    : (_localSession.Mode == SessionMode.BotVsHumanO ? Player.O : Player.X);
                Player oppRole = myRole == Player.X ? Player.O : Player.X;

                double mySeconds = clock.GetRemainingSeconds(myRole);
                double oppSeconds = clock.GetRemainingSeconds(oppRole);

                _leftClockLabel.Text = $"⏱ {MatchClock.FormatClock(mySeconds)}";
                _rightClockLabel.Text = $"⏱ {MatchClock.FormatClock(oppSeconds)}";

                // REQUERIMIENTO 12: Mostrar cuenta regresiva de 30s para el primer movimiento en línea y limpiarla en cuanto empieza la partida
                if (_isOnlineMode && !state.IsGameOver)
                {
                    if (clock.IsWaitingForFirstMove)
                    {
                        int secsLeft = (int)Math.Ceiling(clock.FirstMoveSecondsRemaining);
                        _telemetryLabel.Text = $"{secsLeft}s para hacer el primer tiro";
                    }
                    else if (!_onlineClient.IsWaitingForOpponentTakebackResponse &&
                             _telemetryLabel.Text != null &&
                             (_telemetryLabel.Text.EndsWith("para hacer el primer tiro", StringComparison.Ordinal) ||
                              _telemetryLabel.Text.Contains("para realizar el primer movimiento", StringComparison.Ordinal)))
                    {
                        _telemetryLabel.Text = string.Empty;
                    }
                }

                bool myClockActive = !state.IsGameOver && !clock.IsWaitingForFirstMove && state.CurrentPlayer == myRole;
                bool oppClockActive = !state.IsGameOver && !clock.IsWaitingForFirstMove && state.CurrentPlayer == oppRole;

                if (myClockActive && mySeconds <= 10.0 && mySeconds > 0.0)
                {
                    if (!_lowTimeBeepPlayed)
                    {
                        _lowTimeBeepPlayed = true;
                        PlaySynthesizedSound(GameSoundEffect.LowTimeWarning);
                    }
                }
                else
                {
                    _lowTimeBeepPlayed = false;
                }

                StyleClockLabel(_leftClockLabel, mySeconds, myClockActive);
                StyleClockLabel(_rightClockLabel, oppSeconds, oppClockActive);
            }

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
                PlaySynthesizedSound(GameSoundEffect.MoveTap);
            }

            if (hasWinningLine && !_victoryLineAnimated)
            {
                _victoryLineAnimated = true;
                AnimateButtonPop(_cellButtons[winBoard, winC0], 0.38f);
                AnimateButtonPop(_cellButtons[winBoard, winC1], 0.44f);
                AnimateButtonPop(_cellButtons[winBoard, winC2], 0.50f);
                AnimateControlPulse(_statusBannerPanel);

                Player myRole = _isOnlineMode
                    ? (_onlineClient.AssignedRole != Player.None ? _onlineClient.AssignedRole : Player.X)
                    : (_localSession.Mode == SessionMode.BotVsHumanO ? Player.O : Player.X);
                PlaySynthesizedSound(state.Winner == myRole ? GameSoundEffect.SuddenDeathVictory : GameSoundEffect.MatchDefeat);
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
                case MatchEndReason.Aborted: return "Cancelada por Inactividad";
                default: return "Finalizada";
            }
        }
    }
}
