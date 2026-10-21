using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.Maui
{
    /// <summary>
    /// Interfaz móvil nativa multi-pantalla en .NET MAUI (Android, iOS y Windows) para Gatote Fractal:
    /// - Comparte el 100% de <see cref="FractalTicTacToe.Core"/> con el cliente Godot y el servidor TCP.
    /// - Auto-inicio de sesión persistente: entra directo a 🏠 Inicio si el usuario ya inició sesión antes.
    /// - Selector rápido de IP LAN (con autodetección 192.168.1.84 en Android físico) para pruebas contra PC.
    /// - Barra inferior de 3 pestañas: 🏠 Inicio, 🎬 Análisis y 🏆 Perfil.
    /// - Tablero Fractal 9x9 responsivo con vibración háptica, relojes Fischer, Takeback y Revancha con confirmación.
    /// </summary>
    public class MainPage : ContentPage
    {
        private const int DefaultServerPort = 7777;
        private const string PcLanDefaultIp = "192.168.1.84";

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

        private string _serverHost;
        private readonly GameSessionController _localSession;
        private readonly OnlineMultiplayerClient _onlineClient;
        private readonly MatchmakingService _localAuthStore = new MatchmakingService();
        private IDispatcherTimer _frameTimer;

        private bool _isOnlineMode;
        private string _authenticatedUsername = string.Empty;
        private string _authenticatedPassword = string.Empty;
        private double _queueElapsedSeconds;

        // Pantalla Splash de Carga Inicial con Logo y Barra de Progreso
        private View _splashScreenView;
        private ProgressBar _splashProgressBar;
        private Label _splashPercentLabel;
        private Label _splashStatusLabel;
        private double _splashProgress;
        private bool _splashCompleted;

        // Audio y Vibración Háptica
        private Button _soundToggleBtn;
        private bool _isMuted;
        private bool _lowTimeBeepPlayed;
        private Move? _lastAnimatedMove;

        private readonly List<MatchRecord> _recordedMatches = new List<MatchRecord>();
        private MatchReplayNavigator _replayNavigator;
        private string _lastRecordedMatchId;
        private Move? _currentHintMove;

        private static readonly TimeControlConfig[] TimeControls =
        {
            TimeControlConfig.Bullet1Plus1,
            TimeControlConfig.Blitz3Plus0,
            TimeControlConfig.Blitz3Plus2,
            TimeControlConfig.Rapid5Plus0
        };

        // Paleta visual Dark Slate Competitive
        private static readonly Color BgColor = Color.FromRgb(15, 20, 31);
        private static readonly Color SurfaceColor = Color.FromRgb(23, 30, 46);
        private static readonly Color CardBgColor = Color.FromRgb(28, 36, 52);
        private static readonly Color AccentPrimaryColor = Color.FromRgb(51, 158, 250);
        private static readonly Color ActiveMacroBorderColor = Color.FromRgb(46, 224, 133);
        private static readonly Color InactiveMacroBorderColor = Color.FromRgb(56, 66, 87);
        private static readonly Color PlayerXColor = Color.FromRgb(255, 92, 107);
        private static readonly Color PlayerOColor = Color.FromRgb(61, 189, 255);
        private static readonly Color LastMoveHighlightColor = Color.FromRgb(250, 199, 51);
        private static readonly Color WinningCellHighlightColor = Color.FromRgb(51, 250, 148);
        private static readonly Color HintHighlightColor = Color.FromRgb(184, 107, 255);
        private static readonly Color PositiveDeltaColor = Color.FromRgb(64, 242, 140);
        private static readonly Color NegativeDeltaColor = Color.FromRgb(255, 107, 107);
        private static readonly Color MutedTextColor = Color.FromRgb(178, 194, 219);

        // Contenedores de pantallas
        private View _topHeaderView;
        private View _authScreenView;
        private View _homeScreenView;
        private View _playScreenView;
        private View _analysisScreenView;
        private View _profileScreenView;
        private Border _bottomNavBar;

        private Label _topHeaderSubtitleLabel;
        private Button _serverHostPillBtn;
        private Button _navHomeBtn;
        private Button _navAnalysisBtn;
        private Button _navProfileBtn;

        // Pantalla 0: Login y Registro
        private Entry _authUsernameInput;
        private Entry _authPasswordInput;
        private Label _authFeedbackLabel;
        private Button _loginBtn;
        private Button _registerBtn;

        // Pantalla 1: Inicio / Lobby
        private Label _lobbyProfileSummaryLabel;
        private Label _lobbyLeagueBadgeLabel;
        private Button _modeBotBtn;
        private Button _modeOnlineBtn;
        private Button _modePassPlayBtn;
        private Label _lobbySection2TitleLabel;
        private Grid _botDifficultyGrid;
        private Grid _quickPairingTimeGrid;
        private Border _matchmakingLoadingPanel;
        private Label _matchmakingLoadingTitleLabel;
        private Label _matchmakingLoadingTimerLabel;

        // Pantalla 2: Tablero Activo (Arena)
        private readonly Border[] _macroBoardBorders = new Border[GameState.BoardCount];
        private readonly Button[,] _cellButtons = new Button[GameState.BoardCount, GameState.CellsPerBoard];
        private Border _leftPlayerCard;
        private Label _leftPlayerTitleLabel;
        private Label _leftPlayerRatingLabel;
        private Label _leftClockLabel;
        private Border _rightPlayerCard;
        private Label _rightPlayerTitleLabel;
        private Label _rightPlayerRatingLabel;
        private Label _rightClockLabel;

        private Border _takebackOfferPanel;
        private Label _takebackOfferLabel;
        private Border _rematchOfferPanel;
        private Label _rematchOfferLabel;
        private Border _abortedMatchPromptPanel;
        private Label _abortedMatchPromptLabel;

        private Border _statusBannerPanel;
        private Label _statusBannerLabel;
        private Label _telemetryLabel;

        private Grid _inGameActionsRow;
        private Button _undoButton;
        private Button _redoButton;
        private Button _hintButton;
        private Button _resignOrLeaveButton;

        private Grid _postGameActionsRow;
        private Button _rematchButton;
        private Button _analyzeFinishedMatchButton;
        private Button _backToMenuButton;

        // Pantalla 3: Sala de Análisis
        private readonly Border[] _analysisMacroBorders = new Border[GameState.BoardCount];
        private readonly Button[,] _analysisCellButtons = new Button[GameState.BoardCount, GameState.CellsPerBoard];
        private Picker _matchSelectorPicker;
        private ProgressBar _evalProgressBar;
        private Label _evalScoreLabel;
        private Label _analysisStepInfoLabel;
        private Entry _transcriptInput;
        private Button _stepStartBtn;
        private Button _stepBackBtn;
        private Button _stepForwardBtn;
        private Button _stepEndBtn;
        private bool _suppressPickerEvent;

        // Pantalla 4: Perfil y Ligas
        private Label _profileNameLabel;
        private Label _profileRatingBigLabel;
        private Label _profileStatsGridLabel;
        private VerticalStackLayout _leaderboardListContainer;

        public MainPage()
        {
            BackgroundColor = BgColor;
            NavigationPage.SetHasNavigationBar(this, false);

            _serverHost = ResolveInitialServerHost();
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
            _onlineClient.MatchEnded += OnOnlineMatchEnded;
            _onlineClient.ErrorReceived += OnOnlineErrorReceived;
            _onlineClient.LeaderboardReceived += OnLeaderboardReceived;

            BuildMobileShellUI();
            StartClockTimer();

            // Mostrar primero la pantalla Splash con el Logo Oficial y la Barra de Loading (0% -> 100%)
            _splashProgress = 0.0;
            _splashCompleted = false;
            NavigateToScreen(ScreenState.SplashLoading);
            RefreshUI();
        }

        private string ResolveInitialServerHost()
        {
            string savedIp = LoadSavedServerHost();
            if (!string.IsNullOrWhiteSpace(savedIp))
            {
                return savedIp;
            }

            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                // En un Android físico en la misma red Wi-Fi, apuntar por defecto a la IP LAN de la PC (192.168.1.84).
                // En emulador Android, 10.0.2.2 redirige al localhost (127.0.0.1) de la PC anfitriona.
                return DeviceInfo.DeviceType == DeviceType.Virtual ? "10.0.2.2" : PcLanDefaultIp;
            }

            return "127.0.0.1";
        }

        private void StartClockTimer()
        {
            _frameTimer = Dispatcher.CreateTimer();
            _frameTimer.Interval = TimeSpan.FromMilliseconds(100);
            _frameTimer.Tick += (_, _) => OnTimerTick(0.1);
            _frameTimer.Start();
        }

        private void OnTimerTick(double delta)
        {
            if (_currentScreen == ScreenState.SplashLoading && !_splashCompleted)
            {
                _splashProgress = Math.Min(1.0, _splashProgress + delta / 1.3);
                int pct = (int)Math.Round(_splashProgress * 100.0);
                if (_splashProgressBar != null)
                {
                    _splashProgressBar.Progress = _splashProgress;
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
                    _matchmakingLoadingTimerLabel.Text =
                        $"Tiempo en cola: {secs / 60:D2}:{secs % 60:D2}{dots}\n" +
                        $"Modo estricto: {_onlineClient.Clock.Config.Name} ({_onlineClient.LocalElo} pts)";
                }
            }

            if (_isOnlineMode)
            {
                _onlineClient.TickClock(delta);
            }
            else
            {
                _localSession.TickClock(delta);
            }

            if (_currentScreen == ScreenState.PlayArena)
            {
                UpdateClockLabels();
            }
        }

        private void CompleteInitialSplashTransition()
        {
            PlayMobileSoundEffect(GameSoundEffect.MoveTap);

            // Si el usuario ya inició sesión anteriormente en este teléfono, entra directo a 🏠 Inicio
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
                NavigateToScreen(ScreenState.AuthScreen);
            }
        }

        // =====================================================================
        // ARQUITECTURA DE SHELL MÓVIL + NAVEGACIÓN (3 PESTAÑAS)
        // =====================================================================

        private void BuildMobileShellUI()
        {
            var rootGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Star },
                    new RowDefinition { Height = GridLength.Auto }
                },
                Padding = new Thickness(12, 10, 12, 10),
                RowSpacing = 8
            };

            // 1. Barra Superior
            _topHeaderView = BuildTopAppHeader();
            rootGrid.Add(_topHeaderView, 0, 0);

            // 2. Contenedor Central de Vistas
            var screenHost = new Grid();
            _splashScreenView = BuildSplashLoadingScreen();
            _authScreenView = BuildAuthScreen();
            _homeScreenView = BuildHomeLobbyScreen();
            _playScreenView = BuildPlayArenaScreen();
            _analysisScreenView = BuildAnalysisStudioScreen();
            _profileScreenView = BuildProfileAndLeaguesScreen();

            screenHost.Add(_splashScreenView);
            screenHost.Add(_authScreenView);
            screenHost.Add(_homeScreenView);
            screenHost.Add(_playScreenView);
            screenHost.Add(_analysisScreenView);
            screenHost.Add(_profileScreenView);
            rootGrid.Add(screenHost, 0, 1);

            // 3. Barra de Navegación Inferior (SOLO 3 Pestañas: Inicio, Análisis, Perfil)
            _bottomNavBar = BuildBottomNavigationBar();
            rootGrid.Add(_bottomNavBar, 0, 2);

            Content = rootGrid;
        }

        private View BuildSplashLoadingScreen()
        {
            var outerGrid = new Grid
            {
                VerticalOptions = LayoutOptions.Fill,
                HorizontalOptions = LayoutOptions.Fill
            };

            var card = CreateCardBorder(CardBgColor, ActiveMacroBorderColor, strokeThickness: 2.5, cornerRadius: 20, padding: new Thickness(20));
            card.VerticalOptions = LayoutOptions.Center;
            card.HorizontalOptions = LayoutOptions.Fill;

            var stack = new VerticalStackLayout { Spacing = 14 };

            var logoImg = new Image
            {
                Source = ImageSource.FromResource("FractalTicTacToe.Maui.Assets.gatote_logo.png", typeof(MainPage).Assembly),
                Aspect = Aspect.AspectFit,
                HeightRequest = 210,
                HorizontalOptions = LayoutOptions.Center
            };
            stack.Add(logoImg);

            stack.Add(new Label
            {
                Text = "⚡ GATOTE FRACTAL • MUERTE SÚBITA",
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = ActiveMacroBorderColor,
                HorizontalTextAlignment = TextAlignment.Center
            });

            _splashProgressBar = new ProgressBar
            {
                Progress = 0.0,
                ProgressColor = ActiveMacroBorderColor,
                HeightRequest = 12
            };
            stack.Add(_splashProgressBar);

            _splashPercentLabel = new Label
            {
                Text = "0%",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = LastMoveHighlightColor,
                HorizontalTextAlignment = TextAlignment.Center
            };
            stack.Add(_splashPercentLabel);

            _splashStatusLabel = new Label
            {
                Text = "Cargando motor táctico Minimax + Zobrist...",
                FontSize = 13,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            };
            stack.Add(_splashStatusLabel);

            card.Content = stack;
            outerGrid.Add(card);
            return outerGrid;
        }

        private View BuildTopAppHeader()
        {
            var headerBorder = CreateCardBorder(SurfaceColor, InactiveMacroBorderColor, strokeThickness: 1.5, cornerRadius: 12, padding: new Thickness(10, 6));

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto }
                },
                ColumnSpacing = 6
            };

            var brandTitle = new Label
            {
                Text = "⚡ GATOTE FRACTAL",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center
            };

            _serverHostPillBtn = new Button
            {
                Text = $"📡 {_serverHost}",
                FontSize = 11,
                HeightRequest = 28,
                Padding = new Thickness(8, 0),
                CornerRadius = 8,
                BackgroundColor = Color.FromRgb(32, 44, 66),
                TextColor = LastMoveHighlightColor
            };
            _serverHostPillBtn.Clicked += OnConfigureServerHostClicked;

            _soundToggleBtn = new Button
            {
                Text = "🔊",
                FontSize = 12,
                HeightRequest = 28,
                WidthRequest = 38,
                Padding = Thickness.Zero,
                CornerRadius = 8,
                BackgroundColor = Color.FromRgb(32, 44, 66),
                TextColor = Colors.White
            };
            _soundToggleBtn.Clicked += (_, _) =>
            {
                _isMuted = !_isMuted;
                _soundToggleBtn.Text = _isMuted ? "🔇" : "🔊";
                if (!_isMuted)
                {
                    PlayMobileSoundEffect(GameSoundEffect.MoveTap);
                }
            };

            _topHeaderSubtitleLabel = new Label
            {
                Text = "Inicia Sesión",
                FontSize = 12,
                TextColor = ActiveMacroBorderColor
            };

            grid.Add(brandTitle, 0, 0);
            grid.Add(_serverHostPillBtn, 1, 0);
            grid.Add(_soundToggleBtn, 2, 0);
            grid.Add(_topHeaderSubtitleLabel, 0, 1);
            Grid.SetColumnSpan(_topHeaderSubtitleLabel, 3);

            headerBorder.Content = grid;
            return headerBorder;
        }

        private async void OnConfigureServerHostClicked(object sender, EventArgs e)
        {
            string action = await DisplayActionSheet(
                $"Servidor TCP actual: {_serverHost}:{DefaultServerPort}",
                "Cancelar",
                null,
                $"📶 PC en Wi-Fi ({PcLanDefaultIp})",
                "📱 Emulador Android (10.0.2.2)",
                "💻 Localhost PC (127.0.0.1)",
                "✏️ Escribir otra IP manualmente...");

            if (string.IsNullOrEmpty(action) || action == "Cancelar")
                return;

            string newHost = _serverHost;
            if (action.Contains(PcLanDefaultIp))
                newHost = PcLanDefaultIp;
            else if (action.Contains("10.0.2.2"))
                newHost = "10.0.2.2";
            else if (action.Contains("127.0.0.1"))
                newHost = "127.0.0.1";
            else if (action.StartsWith("✏️"))
            {
                string custom = await DisplayPromptAsync(
                    "IP del Servidor",
                    "Ingresa la dirección IPv4 de la PC donde corre FractalTicTacToe.Server:",
                    "Guardar",
                    "Cancelar",
                    placeholder: PcLanDefaultIp,
                    initialValue: _serverHost);
                if (!string.IsNullOrWhiteSpace(custom))
                    newHost = custom.Trim();
            }

            if (!string.IsNullOrWhiteSpace(newHost) && newHost != _serverHost)
            {
                _serverHost = newHost;
                SaveServerHostToDisk(_serverHost);
                _serverHostPillBtn.Text = $"📡 {_serverHost}";
                if (_authFeedbackLabel != null)
                {
                    _authFeedbackLabel.Text = $"Servidor activo: {_serverHost}:{DefaultServerPort}";
                }

                // Si ya teníamos sesión iniciada, reconectar al nuevo host en segundo plano
                _onlineClient.Disconnect();
                if (!string.IsNullOrEmpty(_authenticatedUsername))
                {
                    _ = AuthenticateSavedSessionInBackgroundAsync(_authenticatedUsername, _authenticatedPassword);
                }
            }
        }

        private Border BuildBottomNavigationBar()
        {
            var navBorder = CreateCardBorder(SurfaceColor, InactiveMacroBorderColor, strokeThickness: 1.5, cornerRadius: 14, padding: new Thickness(6));
            var navGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 8
            };

            _navHomeBtn = CreateActionButton("🏠 Inicio", () => NavigateToScreen(ScreenState.HomeLobby), height: 46, fontSize: 14);
            _navAnalysisBtn = CreateActionButton("🎬 Análisis", () => NavigateToScreen(ScreenState.AnalysisStudio), height: 46, fontSize: 14);
            _navProfileBtn = CreateActionButton("🏆 Perfil", () => NavigateToScreen(ScreenState.ProfileAndLeagues), height: 46, fontSize: 14);

            navGrid.Add(_navHomeBtn, 0, 0);
            navGrid.Add(_navAnalysisBtn, 1, 0);
            navGrid.Add(_navProfileBtn, 2, 0);

            navBorder.Content = navGrid;
            return navBorder;
        }

        private void NavigateToScreen(ScreenState targetScreen)
        {
            if (_currentScreen == ScreenState.HomeLobby &&
                targetScreen != ScreenState.HomeLobby &&
                targetScreen != ScreenState.PlayArena &&
                _onlineClient.IsInQueue)
            {
                _onlineClient.CancelMatchmakingQueue();
            }

            _currentScreen = targetScreen;

            if (_topHeaderView != null)
            {
                _topHeaderView.IsVisible = targetScreen != ScreenState.SplashLoading;
            }
            if (_splashScreenView != null)
            {
                _splashScreenView.IsVisible = targetScreen == ScreenState.SplashLoading;
            }

            _authScreenView.IsVisible = targetScreen == ScreenState.AuthScreen;
            _homeScreenView.IsVisible = targetScreen == ScreenState.HomeLobby;
            _playScreenView.IsVisible = targetScreen == ScreenState.PlayArena;
            _analysisScreenView.IsVisible = targetScreen == ScreenState.AnalysisStudio;
            _profileScreenView.IsVisible = targetScreen == ScreenState.ProfileAndLeagues;

            _bottomNavBar.IsVisible =
                targetScreen == ScreenState.HomeLobby ||
                targetScreen == ScreenState.AnalysisStudio ||
                targetScreen == ScreenState.ProfileAndLeagues;

            HighlightTabButton(_navHomeBtn, targetScreen == ScreenState.HomeLobby);
            HighlightTabButton(_navAnalysisBtn, targetScreen == ScreenState.AnalysisStudio);
            HighlightTabButton(_navProfileBtn, targetScreen == ScreenState.ProfileAndLeagues);

            if (targetScreen == ScreenState.AnalysisStudio)
            {
                if (_recordedMatches.Count > 0)
                {
                    _suppressPickerEvent = true;
                    _matchSelectorPicker.SelectedIndex = 0;
                    _suppressPickerEvent = false;
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
        // PANTALLA 0: 🔐 LOGIN Y REGISTRO (SOLO USUARIOS NUEVOS)
        // =====================================================================

        private View BuildAuthScreen()
        {
            var scroll = new ScrollView();
            var outerStack = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Spacing = 12,
                Padding = new Thickness(4, 12)
            };

            var card = CreateCardBorder(CardBgColor, AccentPrimaryColor, strokeThickness: 2, cornerRadius: 16, padding: new Thickness(16));
            var formStack = new VerticalStackLayout { Spacing = 10 };

            formStack.Add(new Image
            {
                Source = ImageSource.FromResource("FractalTicTacToe.Maui.Assets.gatote_logo.png", typeof(MainPage).Assembly),
                Aspect = Aspect.AspectFit,
                HeightRequest = 125,
                HorizontalOptions = LayoutOptions.Center
            });

            formStack.Add(new Label
            {
                Text = "🔐 ACCESO DE JUGADOR",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center
            });

            formStack.Add(new Label
            {
                Text = "Inicia sesión una sola vez o regístrate como nuevo usuario. Tu sesión quedará guardada automáticamente en este dispositivo.",
                FontSize = 12,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            });

            _authUsernameInput = new Entry
            {
                Placeholder = "Nombre de usuario (mín. 3 caracteres)",
                PlaceholderColor = Color.FromRgb(120, 135, 160),
                TextColor = Colors.White,
                BackgroundColor = SurfaceColor,
                HeightRequest = 44
            };

            _authPasswordInput = new Entry
            {
                Placeholder = "Contraseña (mín. 4 caracteres)",
                PlaceholderColor = Color.FromRgb(120, 135, 160),
                IsPassword = true,
                TextColor = Colors.White,
                BackgroundColor = SurfaceColor,
                HeightRequest = 44
            };

            _authFeedbackLabel = new Label
            {
                Text = $"Servidor: {_serverHost}:{DefaultServerPort} (Toca 📡 arriba si pruebas desde Android a tu PC)",
                FontSize = 12,
                TextColor = LastMoveHighlightColor,
                HorizontalTextAlignment = TextAlignment.Center
            };

            var btnGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 8
            };

            _loginBtn = CreateActionButton("🔑 Iniciar Sesión", () => OnSubmitAuthPressed(isRegister: false), height: 46, fontSize: 13);
            _registerBtn = CreateActionButton("📝 Registrarse", () => OnSubmitAuthPressed(isRegister: true), height: 46, fontSize: 13);

            btnGrid.Add(_loginBtn, 0, 0);
            btnGrid.Add(_registerBtn, 1, 0);

            var extraGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 8
            };

            var guestBtn = CreateActionButton("👤 Modo Invitado", () =>
            {
                _authenticatedUsername = "Invitado";
                _authenticatedPassword = string.Empty;
                _localSession.ProfileX.Username = "Invitado";
                NavigateToScreen(ScreenState.HomeLobby);
            }, height: 40, fontSize: 12);

            var privacyBtn = CreateActionButton("🔒 Privacidad", OnShowPrivacyPolicyClicked, height: 40, fontSize: 12);

            extraGrid.Add(guestBtn, 0, 0);
            extraGrid.Add(privacyBtn, 1, 0);

            formStack.Add(_authUsernameInput);
            formStack.Add(_authPasswordInput);
            formStack.Add(_authFeedbackLabel);
            formStack.Add(btnGrid);
            formStack.Add(extraGrid);

            card.Content = formStack;
            outerStack.Add(card);
            scroll.Content = outerStack;
            return scroll;
        }

        private async void OnShowPrivacyPolicyClicked()
        {
            await DisplayAlert(
                "🔒 Política de Privacidad — Gatote Fractal",
                SoundEffectsSynthesizer.PrivacyPolicyInAppText,
                "Entendido");
        }

        private async void OnSubmitAuthPressed(bool isRegister)
        {
            string username = _authUsernameInput.Text?.Trim() ?? string.Empty;
            string password = _authPasswordInput.Text?.Trim() ?? string.Empty;

            if (username.Length < 3)
            {
                _authFeedbackLabel.Text = "⚠ El nombre de usuario debe tener al menos 3 caracteres.";
                _authFeedbackLabel.TextColor = NegativeDeltaColor;
                return;
            }

            if (password.Length < 4)
            {
                _authFeedbackLabel.Text = "⚠ La contraseña debe tener al menos 4 caracteres.";
                _authFeedbackLabel.TextColor = NegativeDeltaColor;
                return;
            }

            _authenticatedPassword = password;
            _loginBtn.IsEnabled = false;
            _registerBtn.IsEnabled = false;
            _authFeedbackLabel.Text = isRegister
                ? $"Registrando '{username}' en {_serverHost}:{DefaultServerPort}..."
                : $"Iniciando sesión como '{username}' en {_serverHost}:{DefaultServerPort}...";
            _authFeedbackLabel.TextColor = LastMoveHighlightColor;

            try
            {
                if (isRegister)
                {
                    await _onlineClient.ConnectAndRegisterAsync(_serverHost, DefaultServerPort, username, password);
                }
                else
                {
                    await _onlineClient.ConnectAndLoginAsync(_serverHost, DefaultServerPort, username, password);
                }
            }
            catch
            {
                PlayerProfile localProfile;
                string err;
                bool ok = isRegister
                    ? _localAuthStore.RegisterAccount(username, password, out localProfile, out err)
                    : _localAuthStore.AuthenticateAccount(username, password, out localProfile, out err);

                _loginBtn.IsEnabled = true;
                _registerBtn.IsEnabled = true;

                if (!ok)
                {
                    _authFeedbackLabel.Text = $"⚠ {err}";
                    _authFeedbackLabel.TextColor = NegativeDeltaColor;
                    return;
                }

                SaveLocalAccountsToDisk();
                CompleteLoginSession(localProfile.Username);
            }
        }

        private async Task AuthenticateSavedSessionInBackgroundAsync(string username, string password)
        {
            try
            {
                await _onlineClient.ConnectAndLoginAsync(_serverHost, DefaultServerPort, username, password);
            }
            catch
            {
                // Si el servidor aún no está activo, permanece en Inicio y reconectará al buscar partida
            }
        }

        private void OnOnlineAuthenticated(string username, int rating, string rank)
        {
            Dispatcher.Dispatch(() =>
            {
                _loginBtn.IsEnabled = true;
                _registerBtn.IsEnabled = true;
                CompleteLoginSession(username);
            });
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

        // =====================================================================
        // PANTALLA 1: 🏠 INICIO Y EMPAREJAMIENTO RÁPIDO
        // =====================================================================

        private View BuildHomeLobbyScreen()
        {
            var scroll = new ScrollView();
            var col = new VerticalStackLayout { Spacing = 10 };

            // 1. Resumen de Usuario Autenticado
            var accountCard = CreateCardBorder(CardBgColor, AccentPrimaryColor, strokeThickness: 1.5, cornerRadius: 12, padding: new Thickness(12, 8));
            var accountStack = new VerticalStackLayout { Spacing = 3 };

            _lobbyProfileSummaryLabel = new Label
            {
                Text = "👤 Usuario: Jugador",
                FontSize = 17,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center
            };

            _lobbyLeagueBadgeLabel = new Label
            {
                Text = "Clasificación Online: 1200 pts (Liga Plata) • 0V - 0D - 0E",
                FontSize = 12,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            };

            accountStack.Add(_lobbyProfileSummaryLabel);
            accountStack.Add(_lobbyLeagueBadgeLabel);
            accountCard.Content = accountStack;
            col.Add(accountCard);

            // 2. Selector de Modo de Juego
            col.Add(new Label
            {
                Text = "1. ELIGE EL MODO DE JUEGO",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            });

            var modeGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 8
            };

            _modeBotBtn = CreateActionButton("🤖 Contra IA", () => SelectLobbyOpponentType(LobbyOpponentType.BotMinimax), height: 46, fontSize: 13);
            _modeOnlineBtn = CreateActionButton("🌐 En Línea", () => SelectLobbyOpponentType(LobbyOpponentType.OnlineMultiplayer), height: 46, fontSize: 13);
            _modePassPlayBtn = CreateActionButton("📱 2 Jugadores", () => SelectLobbyOpponentType(LobbyOpponentType.LocalPassAndPlay), height: 46, fontSize: 13);

            modeGrid.Add(_modeBotBtn, 0, 0);
            modeGrid.Add(_modeOnlineBtn, 1, 0);
            modeGrid.Add(_modePassPlayBtn, 2, 0);
            col.Add(modeGrid);

            // 3. Título dinámico de Sección 2
            _lobbySection2TitleLabel = new Label
            {
                Text = "2. SELECCIONA LA DIFICULTAD DE LA IA (PARTIDA SIN TIEMPO)",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            };
            col.Add(_lobbySection2TitleLabel);

            // 4A. Cuadrícula 2x2 de Dificultad IA (SIN TIEMPO)
            _botDifficultyGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = 96 },
                    new RowDefinition { Height = 96 }
                },
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                RowSpacing = 10,
                ColumnSpacing = 10
            };

            BotDifficulty[] diffs = { BotDifficulty.Easy, BotDifficulty.Medium, BotDifficulty.Hard, BotDifficulty.Master };
            string[] diffTitles =
            {
                "🌱 Fácil\nIdeal para aprender\nSin límite de tiempo",
                "⚔ Medio\nTáctico equilibrado\nSin límite de tiempo",
                "🔥 Difícil\nCálculo profundo\nSin límite de tiempo",
                "👑 Maestro\nMinimax + Zobrist TT\nSin límite de tiempo"
            };

            for (int i = 0; i < diffs.Length; i++)
            {
                BotDifficulty capturedDiff = diffs[i];
                Button btn = CreateLargeTileButton(diffTitles[i], () => StartBotMatchWithoutClock(capturedDiff));
                _botDifficultyGrid.Add(btn, i % 2, i / 2);
            }
            col.Add(_botDifficultyGrid);

            // 4B. Cuadrícula 2x2 de Ritmos de Reloj (En Línea y 2 Jugadores)
            _quickPairingTimeGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = 96 },
                    new RowDefinition { Height = 96 }
                },
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                RowSpacing = 10,
                ColumnSpacing = 10,
                IsVisible = false
            };

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
                int capturedIdx = i;
                TimeControlConfig tc = TimeControls[i];
                Button btn = CreateLargeTileButton($"{icons[i]} {tc.Name}\n{subtitles[i]}", () => OnTimeControlCardSelected(capturedIdx));
                _quickPairingTimeGrid.Add(btn, i % 2, i / 2);
            }
            col.Add(_quickPairingTimeGrid);

            // 4C. Panel de Loading al buscar partida en línea desde Inicio
            _matchmakingLoadingPanel = CreateCardBorder(Color.FromRgb(25, 40, 64), ActiveMacroBorderColor, strokeThickness: 2, cornerRadius: 14, padding: new Thickness(16));
            _matchmakingLoadingPanel.IsVisible = false;

            var loadingStack = new VerticalStackLayout { Spacing = 10 };
            _matchmakingLoadingTitleLabel = new Label
            {
                Text = "🔍 BUSCANDO OPONENTE EN LÍNEA...",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = ActiveMacroBorderColor,
                HorizontalTextAlignment = TextAlignment.Center
            };

            _matchmakingLoadingTimerLabel = new Label
            {
                Text = "Tiempo en cola: 00:00",
                FontSize = 14,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center
            };

            var cancelQueueBtn = CreateActionButton("✕ Cancelar Búsqueda", () =>
            {
                _onlineClient.CancelMatchmakingQueue();
                UpdateLobbyModeVisibility();
            }, height: 42, fontSize: 13);

            loadingStack.Add(_matchmakingLoadingTitleLabel);
            loadingStack.Add(_matchmakingLoadingTimerLabel);
            loadingStack.Add(cancelQueueBtn);
            _matchmakingLoadingPanel.Content = loadingStack;
            col.Add(_matchmakingLoadingPanel);

            // 5. Regla destacada
            var ruleCard = CreateCardBorder(SurfaceColor, InactiveMacroBorderColor, strokeThickness: 1, cornerRadius: 10, padding: new Thickness(10, 8));
            ruleCard.Content = new Label
            {
                Text = "Muerte Súbita: Gana al instante quien logre el primer 3 en raya en cualquier sub-tablero. Solo las partidas En Línea suman o restan puntos de liga.",
                FontSize = 12,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            };
            col.Add(ruleCard);

            UpdateLobbyModeVisibility();
            scroll.Content = col;
            return scroll;
        }

        private void SelectLobbyOpponentType(LobbyOpponentType type)
        {
            if (_onlineClient.IsInQueue)
            {
                _onlineClient.CancelMatchmakingQueue();
            }

            _selectedOpponentType = type;
            UpdateLobbyModeVisibility();
        }

        private void UpdateLobbyModeVisibility()
        {
            HighlightTabButton(_modeBotBtn, _selectedOpponentType == LobbyOpponentType.BotMinimax);
            HighlightTabButton(_modeOnlineBtn, _selectedOpponentType == LobbyOpponentType.OnlineMultiplayer);
            HighlightTabButton(_modePassPlayBtn, _selectedOpponentType == LobbyOpponentType.LocalPassAndPlay);

            bool isSearchingOnline = _selectedOpponentType == LobbyOpponentType.OnlineMultiplayer && _onlineClient.IsInQueue;
            _matchmakingLoadingPanel.IsVisible = isSearchingOnline;

            if (isSearchingOnline)
            {
                _botDifficultyGrid.IsVisible = false;
                _quickPairingTimeGrid.IsVisible = false;
                _lobbySection2TitleLabel.Text = "EMPAREJAMIENTO EN CURSO";
                return;
            }

            if (_selectedOpponentType == LobbyOpponentType.BotMinimax)
            {
                _lobbySection2TitleLabel.Text = "2. ELIGE LA DIFICULTAD DE LA IA (PARTIDA SIN TIEMPO)";
                _botDifficultyGrid.IsVisible = true;
                _quickPairingTimeGrid.IsVisible = false;
            }
            else if (_selectedOpponentType == LobbyOpponentType.OnlineMultiplayer)
            {
                _lobbySection2TitleLabel.Text = "2. ELIGE EL MODO DE TIEMPO PARA BUSCAR RIVAL EN LÍNEA";
                _botDifficultyGrid.IsVisible = false;
                _quickPairingTimeGrid.IsVisible = true;
            }
            else
            {
                _lobbySection2TitleLabel.Text = "2. ELIGE EL RITMO DE RELOJ (2 JUGADORES EN ESTE TELÉFONO)";
                _botDifficultyGrid.IsVisible = false;
                _quickPairingTimeGrid.IsVisible = true;
            }
        }

        private void StartBotMatchWithoutClock(BotDifficulty difficulty)
        {
            TriggerSubtleHaptic();
            _selectedBotDifficulty = difficulty;
            _isOnlineMode = false;
            _currentHintMove = null;

            _localSession.ProfileX.Username = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : "Jugador";
            _localSession.SetTimeControl(TimeControlConfig.Unlimited);
            _localSession.ConfigureSession(SessionMode.HumanVsBotX, difficulty, _localSession.ProfileX);

            NavigateToScreen(ScreenState.PlayArena);
        }

        private async void OnTimeControlCardSelected(int timeIndex)
        {
            TriggerSubtleHaptic();
            _timeControlIndex = timeIndex;
            TimeControlConfig selectedTime = TimeControls[_timeControlIndex];
            _currentHintMove = null;

            if (_selectedOpponentType == LobbyOpponentType.LocalPassAndPlay)
            {
                _isOnlineMode = false;
                _localSession.ProfileX.Username = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : "Jugador 1";
                _localSession.SetTimeControl(selectedTime);
                _localSession.ConfigureSession(SessionMode.PassAndPlay2P, _selectedBotDifficulty, _localSession.ProfileX);
                NavigateToScreen(ScreenState.PlayArena);
                return;
            }

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
                    await _onlineClient.ConnectAndLoginAsync(_serverHost, DefaultServerPort, user, pass);
                }

                _onlineClient.JoinMatchmakingQueue(selectedTime);
                _matchmakingLoadingTitleLabel.Text = $"🔍 BUSCANDO RIVAL EN {selectedTime.Name.ToUpperInvariant()}...";
                UpdateLobbyModeVisibility();
            }
            catch (Exception ex)
            {
                _lobbySection2TitleLabel.Text = $"⚠ Servidor {_serverHost}:{DefaultServerPort} no disponible ({ex.Message})";
            }
        }

        private void OnOnlineMatchStarted(string roomId, Player role, string userX, int eloX, string userO, int eloO)
        {
            Dispatcher.Dispatch(() =>
            {
                TriggerSubtleHaptic();
                _isOnlineMode = true;
                _currentHintMove = null;
                UpdateLobbyModeVisibility();
                NavigateToScreen(ScreenState.PlayArena);
            });
        }

        private void OnOnlineMatchEnded(Player winner, MatchEndReason reason, EloMatchUpdate eloUpdate)
        {
            Dispatcher.Dispatch(() =>
            {
                if (_onlineClient.LastCompletedMatchRecord != null &&
                    _onlineClient.LastCompletedMatchRecord.EndReason != MatchEndReason.Aborted)
                {
                    RecordCompletedMatchIfNeeded(_onlineClient.LastCompletedMatchRecord);
                }
                RefreshUI();
            });
        }

        // =====================================================================
        // PANTALLA 2: ♟ TABLERO ACTIVO (SOLO DURANTE LA PARTIDA)
        // =====================================================================

        private View BuildPlayArenaScreen()
        {
            var arenaGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto }, // Tarjetas de jugadores
                    new RowDefinition { Height = GridLength.Auto }, // Banners interactivos
                    new RowDefinition { Height = GridLength.Auto }, // Estado del turno
                    new RowDefinition { Height = GridLength.Star }, // Tablero Fractal 9x9
                    new RowDefinition { Height = GridLength.Auto }, // Telemetría
                    new RowDefinition { Height = GridLength.Auto }  // Botones de acción
                },
                RowSpacing = 6
            };

            // 1. Tarjetas de Jugadores (Izquierda = Tú, Derecha = Oponente)
            var playersRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 8
            };

            _leftPlayerCard = CreatePlayerCard(out _leftPlayerTitleLabel, out _leftPlayerRatingLabel, out _leftClockLabel);
            _rightPlayerCard = CreatePlayerCard(out _rightPlayerTitleLabel, out _rightPlayerRatingLabel, out _rightClockLabel);

            playersRow.Add(_leftPlayerCard, 0, 0);
            playersRow.Add(_rightPlayerCard, 1, 0);
            arenaGrid.Add(playersRow, 0, 0);

            // 2. Banners Interactivos (Takeback, Revancha y Expiración de 30s)
            var bannersStack = new VerticalStackLayout { Spacing = 6 };
            _takebackOfferPanel = BuildConsentBanner(
                LastMoveHighlightColor,
                "Tu oponente solicita deshacer la última jugada. ¿Aceptas?",
                "✓ Aceptar Deshacer",
                () => _onlineClient.RespondToOpponentTakeback(true),
                "✕ Rechazar",
                () => _onlineClient.RespondToOpponentTakeback(false),
                out _takebackOfferLabel);

            _rematchOfferPanel = BuildConsentBanner(
                ActiveMacroBorderColor,
                "🔄 Tu oponente solicita la REVANCHA. ¿Aceptas jugar otra partida?",
                "✓ Aceptar Revancha",
                () => _onlineClient.RespondToOpponentRematch(true),
                "✕ Rechazar",
                () => _onlineClient.RespondToOpponentRematch(false),
                out _rematchOfferLabel);

            _abortedMatchPromptPanel = BuildConsentBanner(
                LastMoveHighlightColor,
                "⏱ Se agotaron los 30s para el primer movimiento y la partida fue cancelada (sin cambio de puntos). ¿Quieres buscar una nueva partida?",
                "🔍 Buscar Nueva Partida",
                () =>
                {
                    _selectedOpponentType = LobbyOpponentType.OnlineMultiplayer;
                    NavigateToScreen(ScreenState.HomeLobby);
                    OnTimeControlCardSelected(_timeControlIndex);
                },
                "🏠 Volver al Menú",
                () => NavigateToScreen(ScreenState.HomeLobby),
                out _abortedMatchPromptLabel);

            bannersStack.Add(_takebackOfferPanel);
            bannersStack.Add(_rematchOfferPanel);
            bannersStack.Add(_abortedMatchPromptPanel);
            arenaGrid.Add(bannersStack, 0, 1);

            // 3. Banner de Estado de Partida
            _statusBannerPanel = CreateCardBorder(SurfaceColor, AccentPrimaryColor, strokeThickness: 1.5, cornerRadius: 10, padding: new Thickness(8, 6));
            _statusBannerLabel = new Label
            {
                Text = "Turno de [X]",
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center
            };
            _statusBannerPanel.Content = _statusBannerLabel;
            arenaGrid.Add(_statusBannerPanel, 0, 2);

            // 4. Tablero Fractal 9x9
            View boardView = CreateFractalBoardGrid(_macroBoardBorders, _cellButtons, OnCellTapped, fontSize: 16);
            arenaGrid.Add(boardView, 0, 3);

            // 5. Telemetría
            _telemetryLabel = new Label
            {
                Text = "Movimientos: 0/81",
                FontSize = 12,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            };
            arenaGrid.Add(_telemetryLabel, 0, 4);

            // 6. Controles en Partida vs Controles Post-Partida
            var actionsHost = new Grid();

            _inGameActionsRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 6
            };

            _undoButton = CreateActionButton("⟲ Deshacer", OnUndoPressed, height: 42, fontSize: 12);
            _redoButton = CreateActionButton("⟳ Rehacer", OnRedoPressed, height: 42, fontSize: 12);
            _hintButton = CreateActionButton("💡 Pista IA", OnHintPressed, height: 42, fontSize: 12);
            _resignOrLeaveButton = CreateActionButton("🏳 Rendirse", OnResignOrLeavePressed, height: 42, fontSize: 12);

            _inGameActionsRow.Add(_undoButton, 0, 0);
            _inGameActionsRow.Add(_redoButton, 1, 0);
            _inGameActionsRow.Add(_hintButton, 2, 0);
            _inGameActionsRow.Add(_resignOrLeaveButton, 3, 0);

            _postGameActionsRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 6,
                IsVisible = false
            };

            _rematchButton = CreateActionButton("🔄 Revancha", OnRematchPressed, height: 44, fontSize: 13);
            _analyzeFinishedMatchButton = CreateActionButton("🔍 Analizar", OnOpenCurrentMatchInAnalysisPressed, height: 44, fontSize: 13);
            _backToMenuButton = CreateActionButton("🏠 Menú", () => NavigateToScreen(ScreenState.HomeLobby), height: 44, fontSize: 13);

            _postGameActionsRow.Add(_rematchButton, 0, 0);
            _postGameActionsRow.Add(_analyzeFinishedMatchButton, 1, 0);
            _postGameActionsRow.Add(_backToMenuButton, 2, 0);

            actionsHost.Add(_inGameActionsRow);
            actionsHost.Add(_postGameActionsRow);
            arenaGrid.Add(actionsHost, 0, 5);

            return arenaGrid;
        }

        private static Border CreatePlayerCard(out Label titleLbl, out Label ratingLbl, out Label clockLbl)
        {
            var card = CreateCardBorder(CardBgColor, PlayerXColor, strokeThickness: 2, cornerRadius: 10, padding: new Thickness(8, 5));
            var stack = new VerticalStackLayout { Spacing = 2 };

            titleLbl = new Label
            {
                Text = "[X] Jugador",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center
            };

            ratingLbl = new Label
            {
                Text = "1200 pts",
                FontSize = 11,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            };

            clockLbl = new Label
            {
                Text = "⏱ 03:00",
                FontSize = 17,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center
            };

            stack.Add(titleLbl);
            stack.Add(ratingLbl);
            stack.Add(clockLbl);
            card.Content = stack;
            return card;
        }

        private static Border BuildConsentBanner(
            Color borderColor,
            string defaultText,
            string acceptText,
            Action onAccept,
            string declineText,
            Action onDecline,
            out Label messageLabel)
        {
            var banner = CreateCardBorder(Color.FromRgb(36, 30, 22), borderColor, strokeThickness: 2, cornerRadius: 10, padding: new Thickness(8));
            banner.IsVisible = false;

            var stack = new VerticalStackLayout { Spacing = 6 };
            messageLabel = new Label
            {
                Text = defaultText,
                FontSize = 12,
                TextColor = borderColor,
                HorizontalTextAlignment = TextAlignment.Center
            };

            var btnGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 8
            };

            btnGrid.Add(CreateActionButton(acceptText, onAccept, height: 36, fontSize: 12), 0, 0);
            btnGrid.Add(CreateActionButton(declineText, onDecline, height: 36, fontSize: 12), 1, 0);

            stack.Add(messageLabel);
            stack.Add(btnGrid);
            banner.Content = stack;
            return banner;
        }

        // =====================================================================
        // PANTALLA 3: 🎬 SALA DE ANÁLISIS (ÚLTIMA PARTIDA POR DEFECTO)
        // =====================================================================

        private View BuildAnalysisStudioScreen()
        {
            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Star },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto }
                },
                RowSpacing = 6
            };

            _matchSelectorPicker = new Picker
            {
                Title = "Selecciona una partida grabada",
                TextColor = Colors.White,
                BackgroundColor = SurfaceColor,
                HeightRequest = 40
            };
            _matchSelectorPicker.SelectedIndexChanged += (_, _) =>
            {
                if (_suppressPickerEvent) return;
                int idx = _matchSelectorPicker.SelectedIndex;
                if (idx >= 0 && idx < _recordedMatches.Count)
                {
                    _replayNavigator = new MatchReplayNavigator(_recordedMatches[idx]);
                    RefreshAnalysisUI();
                }
            };
            grid.Add(_matchSelectorPicker, 0, 0);

            var evalCard = CreateCardBorder(CardBgColor, InactiveMacroBorderColor, strokeThickness: 1.5, cornerRadius: 10, padding: new Thickness(8, 5));
            var evalStack = new VerticalStackLayout { Spacing = 4 };

            _evalScoreLabel = new Label
            {
                Text = "Evaluación Heurística: 0 (Equilibrado)",
                FontSize = 12,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center
            };

            _evalProgressBar = new ProgressBar
            {
                Progress = 0.5,
                ProgressColor = ActiveMacroBorderColor,
                HeightRequest = 8
            };

            evalStack.Add(_evalScoreLabel);
            evalStack.Add(_evalProgressBar);
            evalCard.Content = evalStack;
            grid.Add(evalCard, 0, 1);

            View analysisBoard = CreateFractalBoardGrid(_analysisMacroBorders, _analysisCellButtons, null, fontSize: 14);
            grid.Add(analysisBoard, 0, 2);

            _analysisStepInfoLabel = new Label
            {
                Text = "Juega una partida para analizarla aquí paso a paso",
                FontSize = 13,
                TextColor = LastMoveHighlightColor,
                HorizontalTextAlignment = TextAlignment.Center
            };
            grid.Add(_analysisStepInfoLabel, 0, 3);

            var vcrGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                ColumnSpacing = 6
            };

            _stepStartBtn = CreateActionButton("⏮ Inicio", () => { _replayNavigator?.JumpToStart(); RefreshAnalysisUI(); }, height: 38, fontSize: 12);
            _stepBackBtn = CreateActionButton("◀ Ant.", () => { _replayNavigator?.StepBackward(); RefreshAnalysisUI(); }, height: 38, fontSize: 12);
            _stepForwardBtn = CreateActionButton("Sig. ▶", () => { _replayNavigator?.StepForward(); RefreshAnalysisUI(); }, height: 38, fontSize: 12);
            _stepEndBtn = CreateActionButton("Final ⏭", () => { _replayNavigator?.JumpToEnd(); RefreshAnalysisUI(); }, height: 38, fontSize: 12);

            vcrGrid.Add(_stepStartBtn, 0, 0);
            vcrGrid.Add(_stepBackBtn, 1, 0);
            vcrGrid.Add(_stepForwardBtn, 2, 0);
            vcrGrid.Add(_stepEndBtn, 3, 0);
            grid.Add(vcrGrid, 0, 4);

            var importGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 6
            };

            _transcriptInput = new Entry
            {
                Placeholder = "Transcripción (ej. 01-10-02-20-00)",
                PlaceholderColor = Color.FromRgb(120, 135, 160),
                TextColor = Colors.White,
                BackgroundColor = SurfaceColor,
                HeightRequest = 38,
                FontSize = 12
            };

            var loadBtn = CreateActionButton("📥 Cargar", () =>
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
            }, height: 38, fontSize: 12);

            importGrid.Add(_transcriptInput, 0, 0);
            importGrid.Add(loadBtn, 1, 0);
            grid.Add(importGrid, 0, 5);

            PopulateMatchSelectorDropdown();
            return grid;
        }

        // =====================================================================
        // PANTALLA 4: 🏆 PERFIL, LIGAS Y RANKING ONLINE
        // =====================================================================

        private View BuildProfileAndLeaguesScreen()
        {
            var scroll = new ScrollView();
            var col = new VerticalStackLayout { Spacing = 10 };

            var profileCard = CreateCardBorder(CardBgColor, ActiveMacroBorderColor, strokeThickness: 2, cornerRadius: 14, padding: new Thickness(12));
            var profileStack = new VerticalStackLayout { Spacing = 6 };

            var headerRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                ColumnSpacing = 6
            };

            _profileNameLabel = new Label
            {
                Text = "👤 Jugador (Tú)",
                FontSize = 17,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center
            };

            var privacyBtn = CreateActionButton("🔒 Privacidad", OnShowPrivacyPolicyClicked, height: 34, fontSize: 11);
            var logoutBtn = CreateActionButton("🚪 Salir", OnLogoutPressed, height: 34, fontSize: 11);
            headerRow.Add(_profileNameLabel, 0, 0);
            headerRow.Add(privacyBtn, 1, 0);
            headerRow.Add(logoutBtn, 2, 0);

            _profileRatingBigLabel = new Label
            {
                Text = "1200 pts • Liga Plata (Exclusivo Online)",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = ActiveMacroBorderColor,
                HorizontalTextAlignment = TextAlignment.Center
            };

            _profileStatsGridLabel = new Label
            {
                Text = "Partidas Online: 0 | Victorias: 0 | Derrotas: 0 | Empates: 0",
                FontSize = 12,
                TextColor = MutedTextColor,
                HorizontalTextAlignment = TextAlignment.Center
            };

            var deleteAccountBtn = CreateActionButton("🗑️ Eliminar Cuenta Permanentemente", OnDeleteAccountClicked, height: 36, fontSize: 12);
            deleteAccountBtn.BackgroundColor = Color.FromRgb(68, 28, 36);
            deleteAccountBtn.TextColor = NegativeDeltaColor;

            profileStack.Add(headerRow);
            profileStack.Add(_profileRatingBigLabel);
            profileStack.Add(_profileStatsGridLabel);
            profileStack.Add(deleteAccountBtn);
            profileCard.Content = profileStack;
            col.Add(profileCard);

            var leaguesCard = CreateCardBorder(SurfaceColor, InactiveMacroBorderColor, strokeThickness: 1.5, cornerRadius: 12, padding: new Thickness(10));
            var leaguesStack = new VerticalStackLayout { Spacing = 3 };
            leaguesStack.Add(new Label
            {
                Text = "🏅 ESCALAFÓN COMPETITIVO EN LÍNEA",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = LastMoveHighlightColor,
                HorizontalTextAlignment = TextAlignment.Center
            });

            string[] leagueTiers =
            {
                "👑 Gran Maestro Fractal — 2100+ pts",
                "🔥 Maestro — 1900 a 2099 pts",
                "💎 Diamante — 1700 a 1899 pts",
                "💠 Platino — 1500 a 1699 pts",
                "🥇 Oro — 1300 a 1499 pts",
                "🥈 Plata (Inicial) — 1100 a 1299 pts",
                "🥉 Bronce — 100 a 1099 pts"
            };

            foreach (string tier in leagueTiers)
            {
                leaguesStack.Add(new Label
                {
                    Text = tier,
                    FontSize = 12,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center
                });
            }

            leaguesCard.Content = leaguesStack;
            col.Add(leaguesCard);

            var lbCard = CreateCardBorder(CardBgColor, AccentPrimaryColor, strokeThickness: 1.5, cornerRadius: 12, padding: new Thickness(10));
            var lbStack = new VerticalStackLayout { Spacing = 6 };

            var lbHeader = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };
            lbHeader.Add(new Label
            {
                Text = "🌐 TOP JUGADORES ONLINE",
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            lbHeader.Add(CreateActionButton("↻ Actualizar", () =>
            {
                if (_onlineClient.IsConnected) _onlineClient.RequestLeaderboard();
            }, height: 34, fontSize: 12), 1, 0);

            _leaderboardListContainer = new VerticalStackLayout { Spacing = 4 };
            lbStack.Add(lbHeader);
            lbStack.Add(_leaderboardListContainer);
            lbCard.Content = lbStack;
            col.Add(lbCard);

            scroll.Content = col;
            return scroll;
        }

        private void OnLogoutPressed()
        {
            ClearSavedSessionFromDisk();
            _onlineClient.Disconnect();
            _authenticatedUsername = string.Empty;
            _authenticatedPassword = string.Empty;
            if (_authPasswordInput != null) _authPasswordInput.Text = string.Empty;
            if (_authFeedbackLabel != null) _authFeedbackLabel.Text = "Sesión cerrada correctamente.";
            NavigateToScreen(ScreenState.AuthScreen);
        }

        private async void OnDeleteAccountClicked()
        {
            string displayUser = !string.IsNullOrEmpty(_authenticatedUsername) ? _authenticatedUsername : "Invitado";
            bool confirm = await DisplayAlert(
                "🗑️ Eliminar Cuenta Permanentemente",
                $"¿Estás seguro de que deseas eliminar permanentemente la cuenta '{displayUser}', tus puntos de liga y estadísticas del servidor y de este dispositivo? Esta acción no se puede deshacer.",
                "Sí, Eliminar Cuenta",
                "Cancelar");

            if (!confirm)
            {
                return;
            }

            string pass = !string.IsNullOrEmpty(_authenticatedPassword)
                ? _authenticatedPassword
                : (_authPasswordInput?.Text ?? string.Empty);

            if (!string.IsNullOrEmpty(_authenticatedUsername) && _authenticatedUsername != "Invitado")
            {
                _localAuthStore.DeleteAccount(_authenticatedUsername, pass, out _);
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
                _authFeedbackLabel.TextColor = PositiveDeltaColor;
            }
            NavigateToScreen(ScreenState.AuthScreen);
        }

        private void OnOnlineAccountDeleted(string deletedUsername)
        {
            Dispatcher.Dispatch(() =>
            {
                ClearSavedSessionFromDisk();
                _authenticatedUsername = string.Empty;
                _authenticatedPassword = string.Empty;
                if (_authFeedbackLabel != null)
                {
                    _authFeedbackLabel.Text = $"✓ Cuenta '{deletedUsername}' eliminada permanentemente del servidor.";
                    _authFeedbackLabel.TextColor = PositiveDeltaColor;
                }
                NavigateToScreen(ScreenState.AuthScreen);
            });
        }

        // =====================================================================
        // INTERACCIÓN EN PARTIDA, REVANCHA Y SALA DE ANÁLISIS
        // =====================================================================

        private async void OnCellTapped(int boardIndex, int cellIndex)
        {
            _currentHintMove = null;

            if (_isOnlineMode)
            {
                if (_onlineClient.TrySendMove(boardIndex, cellIndex))
                {
                    TriggerSubtleHaptic();
                }
                return;
            }

            if (!_localSession.TryPlayHumanMove(boardIndex, cellIndex))
            {
                return;
            }

            TriggerSubtleHaptic();
            CheckAndRecordLocalCompletedMatch();

            if (_localSession.IsCurrentTurnBot() && !_localSession.State.IsGameOver)
            {
                await _localSession.PlayBotTurnAsync();
                CheckAndRecordLocalCompletedMatch();
            }
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
                return;

            _hintButton.IsEnabled = false;
            MinimaxSearchResult hint = await _localSession.RequestHintAsync();
            _currentHintMove = hint.BestMove;
            _telemetryLabel.Text =
                $"Pista IA: Tablero {hint.BestMove.BoardIndex}, Celda {hint.BestMove.CellIndex} " +
                $"(Eval {hint.Score}, Prof. {hint.MaxDepthReached}, {hint.ElapsedMs} ms)";
            _hintButton.IsEnabled = true;
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

            NavigateToScreen(ScreenState.HomeLobby);
        }

        private void OnRematchPressed()
        {
            _currentHintMove = null;
            if (_isOnlineMode)
            {
                _onlineClient.RequestRematch();
                RefreshUI();
                return;
            }

            _localSession.StartNewMatch();
            RefreshUI();
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

        private void CheckAndRecordLocalCompletedMatch()
        {
            if (_localSession.State.IsGameOver && _localSession.MatchHistory.Count > 0)
            {
                RecordCompletedMatchIfNeeded(_localSession.MatchHistory[0]);
            }
        }

        private void RecordCompletedMatchIfNeeded(MatchRecord record)
        {
            if (record == null || record.MatchId == _lastRecordedMatchId || string.IsNullOrWhiteSpace(record.MoveTranscript))
                return;

            _lastRecordedMatchId = record.MatchId;
            _recordedMatches.Insert(0, record);
            _replayNavigator = new MatchReplayNavigator(record);
            PopulateMatchSelectorDropdown();
        }

        private void PopulateMatchSelectorDropdown()
        {
            if (_matchSelectorPicker == null) return;

            _suppressPickerEvent = true;
            _matchSelectorPicker.Items.Clear();

            if (_recordedMatches.Count == 0)
            {
                _matchSelectorPicker.Items.Add("Sin partidas registradas aún en esta sesión");
                _matchSelectorPicker.SelectedIndex = 0;
                _suppressPickerEvent = false;
                return;
            }

            for (int i = 0; i < _recordedMatches.Count; i++)
            {
                MatchRecord m = _recordedMatches[i];
                string winnerTag = m.Winner == Player.None ? "Empate" : $"Ganó {m.Winner}";
                string latestTag = i == 0 ? " [Última Partida]" : "";
                _matchSelectorPicker.Items.Add($"#{i + 1}{latestTag}: {m.PlayerXName} vs {m.PlayerOName} ({winnerTag})");
            }

            _matchSelectorPicker.SelectedIndex = 0;
            _suppressPickerEvent = false;
        }

        private void OnOnlineErrorReceived(string errorMsg)
        {
            Dispatcher.Dispatch(() =>
            {
                _loginBtn.IsEnabled = true;
                _registerBtn.IsEnabled = true;

                if (_currentScreen == ScreenState.AuthScreen)
                {
                    _authFeedbackLabel.Text = $"⚠ {errorMsg}";
                    _authFeedbackLabel.TextColor = NegativeDeltaColor;
                }
                else if (_telemetryLabel != null)
                {
                    _telemetryLabel.Text = $"Servidor: {errorMsg}";
                }
            });
        }

        private void OnLeaderboardReceived(string[] entries)
        {
            Dispatcher.Dispatch(() =>
            {
                _leaderboardListContainer.Children.Clear();
                for (int i = 0; i < entries.Length; i++)
                {
                    string[] fields = entries[i].Split(':');
                    if (fields.Length >= 5)
                    {
                        _leaderboardListContainer.Add(new Label
                        {
                            Text = $"#{fields[0]}  {fields[1]} — {fields[2]} pts ({fields[3]}) [{fields[4]}]",
                            FontSize = 13,
                            TextColor = Colors.White
                        });
                    }
                }
            });
        }

        private void OnAnySessionStateChanged()
        {
            Dispatcher.Dispatch(RefreshUI);
        }

        // =====================================================================
        // SINCRONIZACIÓN DE UI
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
                ? $"Acceso de Cuenta • Servidor {_serverHost}:{DefaultServerPort}"
                : $"👤 {displayUser} • {onlineRating} pts ({onlineRank})";

            _lobbyProfileSummaryLabel.Text = $"👤 Usuario: {displayUser}";
            _lobbyLeagueBadgeLabel.Text =
                $"Clasificación Online: {onlineRating} pts (Liga {onlineRank}) • " +
                $"{_onlineClient.LocalWins}V - {_onlineClient.LocalLosses}D - {_onlineClient.LocalDraws}E";

            UpdateLobbyModeVisibility();

            _takebackOfferPanel.IsVisible = _isOnlineMode && _onlineClient.HasIncomingTakebackOffer;

            if (_isOnlineMode)
            {
                RefreshOnlineArenaUI();
            }
            else
            {
                _rematchOfferPanel.IsVisible = false;
                _abortedMatchPromptPanel.IsVisible = false;
                RefreshLocalArenaUI();
            }

            if (_currentScreen == ScreenState.PlayArena)
            {
                UpdateClockLabels();
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

            _leftPlayerCard.Stroke = new SolidColorBrush(myColor);
            _rightPlayerCard.Stroke = new SolidColorBrush(oppColor);

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

            _leftPlayerTitleLabel.Text = $"[{myRole}] {myName} (Tú)";
            _leftPlayerTitleLabel.TextColor = myColor;
            ConfigureOnlineRatingLabel(_leftPlayerRatingLabel, _onlineClient.LocalElo, _onlineClient.LocalRankTitle, myDelta);

            _rightPlayerTitleLabel.Text = $"[{oppRole}] {oppName}";
            _rightPlayerTitleLabel.TextColor = oppColor;
            string oppRank = EloRatingCalculator.GetRankTitle(_onlineClient.OpponentElo);
            ConfigureOnlineRatingLabel(_rightPlayerRatingLabel, _onlineClient.OpponentElo, oppRank, oppDelta);

            _leftClockLabel.IsVisible = true;
            _rightClockLabel.IsVisible = true;

            bool isAbortedFirstMove = state.IsGameOver && _onlineClient.LastEndReason == MatchEndReason.Aborted;
            _abortedMatchPromptPanel.IsVisible = isAbortedFirstMove;

            _rematchOfferPanel.IsVisible = state.IsGameOver && _onlineClient.HasOpponentOfferedRematch;
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

            RenderBoardOnGrid(state, lastMove, allowTap, _macroBoardBorders, _cellButtons);

            _inGameActionsRow.IsVisible = !state.IsGameOver;
            _postGameActionsRow.IsVisible = state.IsGameOver && !isAbortedFirstMove;

            if (!state.IsGameOver)
            {
                _undoButton.Text = "⟲ Deshacer";
                _undoButton.IsEnabled = !_onlineClient.IsInActiveMatch ||
                                        !state.CanUndo ||
                                        _onlineClient.HasIncomingTakebackOffer ||
                                        _onlineClient.IsWaitingForOpponentTakebackResponse ? false : true;
                _redoButton.IsVisible = false;
                _hintButton.IsVisible = false;
                _resignOrLeaveButton.Text = state.MoveCount == 0 ? "✕ Cancelar" : "🏳 Rendirse";
                _resignOrLeaveButton.IsEnabled = _onlineClient.IsInActiveMatch;
            }
            else
            {
                if (_onlineClient.RematchDeclinedByOpponent)
                {
                    _rematchButton.Text = "✕ Rechazada";
                    _rematchButton.IsEnabled = false;
                    _telemetryLabel.Text = $"{oppName} rechazó la solicitud de revancha.";
                }
                else if (_onlineClient.HasRequestedRematch)
                {
                    _rematchButton.Text = "⏳ Esperando...";
                    _rematchButton.IsEnabled = false;
                }
                else if (_onlineClient.HasOpponentOfferedRematch)
                {
                    _rematchButton.Text = "✓ Aceptar Rev.";
                    _rematchButton.IsEnabled = true;
                }
                else
                {
                    _rematchButton.Text = "🔄 Revancha";
                    _rematchButton.IsEnabled = true;
                }

                _analyzeFinishedMatchButton.IsVisible = state.MoveCount > 0;
                _analyzeFinishedMatchButton.IsEnabled = state.MoveCount > 0;
            }
        }

        private void RefreshLocalArenaUI()
        {
            GameState state = _localSession.State;
            Move? lastMove = state.GetLastMove();

            bool isVsAi = _localSession.Mode == SessionMode.HumanVsBotX || _localSession.Mode == SessionMode.BotVsHumanO;
            bool humanIsO = _localSession.Mode == SessionMode.BotVsHumanO;
            Player myRole = humanIsO ? Player.O : Player.X;
            Player oppRole = humanIsO ? Player.X : Player.O;

            PlayerProfile myProfile = myRole == Player.X ? _localSession.ProfileX : _localSession.ProfileO;
            PlayerProfile oppProfile = oppRole == Player.X ? _localSession.ProfileX : _localSession.ProfileO;

            Color myColor = myRole == Player.X ? PlayerXColor : PlayerOColor;
            Color oppColor = oppRole == Player.X ? PlayerXColor : PlayerOColor;

            _leftPlayerCard.Stroke = new SolidColorBrush(myColor);
            _rightPlayerCard.Stroke = new SolidColorBrush(oppColor);

            _leftPlayerTitleLabel.Text = $"[{myRole}] {myProfile.Username} (Tú)";
            _leftPlayerTitleLabel.TextColor = myColor;
            _leftPlayerRatingLabel.Text = isVsAi ? "Partida vs IA (Sin puntos)" : "Jugador Local 1 (Sin puntos)";
            _leftPlayerRatingLabel.TextColor = MutedTextColor;

            _rightPlayerTitleLabel.Text = $"[{oppRole}] {oppProfile.Username}";
            _rightPlayerTitleLabel.TextColor = oppColor;
            _rightPlayerRatingLabel.Text = isVsAi ? $"IA Nivel {_localSession.BotDifficulty}" : "Jugador Local 2 (Sin puntos)";
            _rightPlayerRatingLabel.TextColor = MutedTextColor;

            bool hasClock = !_localSession.Clock.Config.IsUntimed;
            _leftClockLabel.IsVisible = hasClock;
            _rightClockLabel.IsVisible = hasClock;

            if (state.IsGameOver)
            {
                string reasonText = FormatEndReason(_localSession.LastEndReason);
                _statusBannerLabel.Text = state.Winner != Player.None
                    ? $"🏆 ¡FIN DE PARTIDA ({reasonText})! GANADOR: [{state.Winner}]"
                    : "¡EMPATE GLOBAL! Las 81 casillas están llenas.";
            }
            else if (_localSession.IsBotThinking)
            {
                _statusBannerLabel.Text = $"Bot Minimax [{state.CurrentPlayer}] calculando jugada...";
            }
            else if (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex)
            {
                _statusBannerLabel.Text = $"Turno de [{state.CurrentPlayer}] • TIRO LIBRE (Cualquier sub-tablero en verde)";
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
                    $"Nodos: {r.NodesEvaluated:N0} | {r.ElapsedMs} ms";
            }
            else if (!_currentHintMove.HasValue)
            {
                _telemetryLabel.Text = $"Movimientos: {state.MoveCount}/81";
            }

            bool allowTap = !state.IsGameOver && !_localSession.IsBotThinking && !_localSession.IsCurrentTurnBot();
            RenderBoardOnGrid(state, lastMove, allowTap, _macroBoardBorders, _cellButtons);

            _inGameActionsRow.IsVisible = !state.IsGameOver;
            _postGameActionsRow.IsVisible = state.IsGameOver;

            if (!state.IsGameOver)
            {
                _undoButton.Text = "⟲ Deshacer";
                _undoButton.IsEnabled = !_localSession.IsBotThinking && state.CanUndo;
                _redoButton.IsVisible = true;
                _redoButton.IsEnabled = !_localSession.IsBotThinking && state.CanRedo;
                _hintButton.IsVisible = isVsAi;
                _hintButton.IsEnabled = isVsAi && !_localSession.IsBotThinking && !_localSession.IsCurrentTurnBot();
                _resignOrLeaveButton.Text = "🏠 Salir";
                _resignOrLeaveButton.IsEnabled = true;
            }
            else
            {
                _rematchButton.Text = "🔄 Revancha";
                _rematchButton.IsEnabled = true;
                _analyzeFinishedMatchButton.IsVisible = state.MoveCount > 0;
                _analyzeFinishedMatchButton.IsEnabled = state.MoveCount > 0;
            }
        }

        private void RefreshAnalysisUI()
        {
            if (_replayNavigator == null)
            {
                var emptyState = new GameState();
                RenderBoardOnGrid(emptyState, null, false, _analysisMacroBorders, _analysisCellButtons);
                _evalScoreLabel.Text = "Sin partida seleccionada";
                _evalProgressBar.Progress = 0.5;
                _analysisStepInfoLabel.Text = "Termina una partida para analizarla aquí paso a paso";
                _stepStartBtn.IsEnabled = false;
                _stepBackBtn.IsEnabled = false;
                _stepForwardBtn.IsEnabled = false;
                _stepEndBtn.IsEnabled = false;
                return;
            }

            GameState state = _replayNavigator.CurrentState;
            Move? lastMove = _replayNavigator.LastExecutedMove;

            RenderBoardOnGrid(state, lastMove, false, _analysisMacroBorders, _analysisCellButtons);

            int eval = _replayNavigator.GetCurrentEvaluationForX();
            string evalText;
            double barProgress;

            if (eval >= MinimaxBot.WinScore - 500)
            {
                evalText = "Evaluación: ¡VICTORIA POR MUERTE SÚBITA PARA [X]!";
                barProgress = 1.0;
            }
            else if (eval <= -MinimaxBot.WinScore + 500)
            {
                evalText = "Evaluación: ¡VICTORIA POR MUERTE SÚBITA PARA [O]!";
                barProgress = 0.0;
            }
            else
            {
                string sign = eval > 0 ? "+" : "";
                evalText = $"Evaluación Táctica (perspectiva X): {sign}{eval}";
                barProgress = Math.Clamp(0.5 + (eval / 1200.0), 0.05, 0.95);
            }

            _evalScoreLabel.Text = evalText;
            _evalProgressBar.Progress = barProgress;

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

            _stepStartBtn.IsEnabled = _replayNavigator.CanStepBackward;
            _stepBackBtn.IsEnabled = _replayNavigator.CanStepBackward;
            _stepForwardBtn.IsEnabled = _replayNavigator.CanStepForward;
            _stepEndBtn.IsEnabled = _replayNavigator.CanStepForward;
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
                $"Partidas Online: {totalOnline} | Victorias: {wins} | Derrotas: {losses} | Empates: {draws}\n" +
                $"Efectividad Online: {winRate:F1}% | Partidas en Sala de Análisis: {_recordedMatches.Count}";
        }

        private void UpdateClockLabels()
        {
            GameState state = _isOnlineMode ? _onlineClient.State : _localSession.State;
            MatchClock clock = _isOnlineMode ? _onlineClient.Clock : _localSession.Clock;

            if (clock.Config.IsUntimed)
                return;

            Player myRole = _isOnlineMode
                ? (_onlineClient.AssignedRole != Player.None ? _onlineClient.AssignedRole : Player.X)
                : (_localSession.Mode == SessionMode.BotVsHumanO ? Player.O : Player.X);
            Player oppRole = myRole == Player.X ? Player.O : Player.X;

            double mySeconds = clock.GetRemainingSeconds(myRole);
            double oppSeconds = clock.GetRemainingSeconds(oppRole);

            _leftClockLabel.Text = $"⏱ {MatchClock.FormatClock(mySeconds)}";
            _rightClockLabel.Text = $"⏱ {MatchClock.FormatClock(oppSeconds)}";

            if (_isOnlineMode && !state.IsGameOver && clock.IsWaitingForFirstMove)
            {
                int secsLeft = (int)Math.Ceiling(clock.FirstMoveSecondsRemaining);
                _telemetryLabel.Text =
                    $"⏳ {secsLeft}s para realizar el primer movimiento (o la partida se cancelará sin cambio de puntos)";
            }

            bool myClockActive = !state.IsGameOver && !clock.IsWaitingForFirstMove && state.CurrentPlayer == myRole;
            bool oppClockActive = !state.IsGameOver && !clock.IsWaitingForFirstMove && state.CurrentPlayer == oppRole;

            if (myClockActive && mySeconds <= 10.0 && mySeconds > 0.0)
            {
                if (!_lowTimeBeepPlayed)
                {
                    _lowTimeBeepPlayed = true;
                    PlayMobileSoundEffect(GameSoundEffect.LowTimeWarning);
                }
            }
            else
            {
                _lowTimeBeepPlayed = false;
            }

            _leftClockLabel.TextColor = (mySeconds < 20.0 && myClockActive) ? NegativeDeltaColor : (myClockActive ? ActiveMacroBorderColor : MutedTextColor);
            _rightClockLabel.TextColor = (oppSeconds < 20.0 && oppClockActive) ? NegativeDeltaColor : (oppClockActive ? ActiveMacroBorderColor : MutedTextColor);
        }

        // =====================================================================
        // RENDERIZADO DEL TABLERO FRACTAL 9x9
        // =====================================================================

        private View CreateFractalBoardGrid(
            Border[] macroBordersOut,
            Button[,] cellButtonsOut,
            Action<int, int> onCellPressed,
            double fontSize)
        {
            var host = new ContentView
            {
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            var macroGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Star },
                    new RowDefinition { Height = GridLength.Star },
                    new RowDefinition { Height = GridLength.Star }
                },
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Star }
                },
                RowSpacing = 6,
                ColumnSpacing = 6,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            host.SizeChanged += (_, _) =>
            {
                double side = Math.Min(host.Width, host.Height);
                if (side > 40)
                {
                    macroGrid.WidthRequest = side;
                    macroGrid.HeightRequest = side;
                }
            };

            for (int boardIdx = 0; boardIdx < GameState.BoardCount; boardIdx++)
            {
                var macroBorder = CreateCardBorder(CardBgColor, InactiveMacroBorderColor, strokeThickness: 2.5, cornerRadius: 10, padding: new Thickness(4));
                macroBordersOut[boardIdx] = macroBorder;

                var innerGrid = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition { Height = GridLength.Star },
                        new RowDefinition { Height = GridLength.Star },
                        new RowDefinition { Height = GridLength.Star }
                    },
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Star }
                    },
                    RowSpacing = 3,
                    ColumnSpacing = 3
                };

                for (int cellIdx = 0; cellIdx < GameState.CellsPerBoard; cellIdx++)
                {
                    int capturedBoard = boardIdx;
                    int capturedCell = cellIdx;

                    var cellBtn = new Button
                    {
                        Text = string.Empty,
                        FontSize = fontSize,
                        FontAttributes = FontAttributes.Bold,
                        Padding = Thickness.Zero,
                        CornerRadius = 6,
                        BackgroundColor = Color.FromRgb(28, 33, 46)
                    };

                    if (onCellPressed != null)
                    {
                        cellBtn.Clicked += (_, _) => onCellPressed(capturedBoard, capturedCell);
                    }

                    cellButtonsOut[boardIdx, cellIdx] = cellBtn;
                    innerGrid.Add(cellBtn, cellIdx % 3, cellIdx / 3);
                }

                macroBorder.Content = innerGrid;
                macroGrid.Add(macroBorder, boardIdx % 3, boardIdx / 3);
            }

            host.Content = macroGrid;
            return host;
        }

        private void RenderBoardOnGrid(
            GameState state,
            Move? lastMove,
            bool allowHumanInput,
            Border[] macroBorders,
            Button[,] cellButtons)
        {
            bool hasWinningLine = state.TryGetWinningLine(out int winBoard, out int winC0, out int winC1, out int winC2);

            for (int board = 0; board < GameState.BoardCount; board++)
            {
                bool isBoardActive = !state.IsGameOver &&
                                     (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex ||
                                      state.ActiveBoardIndex == board);

                Border macroBorder = macroBorders[board];
                if (hasWinningLine && board == winBoard)
                {
                    macroBorder.Stroke = new SolidColorBrush(WinningCellHighlightColor);
                    macroBorder.BackgroundColor = Color.FromRgb(25, 61, 46);
                }
                else
                {
                    macroBorder.Stroke = new SolidColorBrush(isBoardActive ? ActiveMacroBorderColor : InactiveMacroBorderColor);
                    macroBorder.BackgroundColor = isBoardActive
                        ? Color.FromRgb(33, 46, 61)
                        : Color.FromRgb(20, 26, 38);
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

                    bool isHintMove = _currentHintMove.HasValue &&
                                      _currentHintMove.Value.BoardIndex == board &&
                                      _currentHintMove.Value.CellIndex == cell &&
                                      ReferenceEquals(cellButtons, _cellButtons);

                    bool interactive = isBoardActive && allowHumanInput && occupant == Player.None;
                    btn.IsEnabled = interactive;

                    if (occupant == Player.X)
                    {
                        btn.Text = "X";
                        btn.TextColor = PlayerXColor;
                        btn.BackgroundColor = isWinningCell ? Color.FromRgb(82, 41, 51) : Color.FromRgb(56, 31, 41);
                    }
                    else if (occupant == Player.O)
                    {
                        btn.Text = "O";
                        btn.TextColor = PlayerOColor;
                        btn.BackgroundColor = isWinningCell ? Color.FromRgb(36, 71, 92) : Color.FromRgb(26, 46, 71);
                    }
                    else
                    {
                        btn.Text = isHintMove ? "★" : string.Empty;
                        btn.TextColor = HintHighlightColor;
                        btn.BackgroundColor = interactive ? Color.FromRgb(46, 59, 79) : Color.FromRgb(28, 33, 46);
                    }

                    if (isWinningCell)
                    {
                        btn.BorderColor = WinningCellHighlightColor;
                        btn.BorderWidth = 2.5;
                    }
                    else if (isLastMove)
                    {
                        btn.BorderColor = LastMoveHighlightColor;
                        btn.BorderWidth = 2.0;
                    }
                    else if (isHintMove)
                    {
                        btn.BorderColor = HintHighlightColor;
                        btn.BorderWidth = 2.0;
                    }
                    else
                    {
                        btn.BorderWidth = 0;
                    }
                }
            }

            if (ReferenceEquals(cellButtons, _cellButtons) &&
                lastMove.HasValue &&
                (!_lastAnimatedMove.HasValue || !_lastAnimatedMove.Value.Equals(lastMove.Value)))
            {
                _lastAnimatedMove = lastMove.Value;
                if (state.IsGameOver && state.Winner != Player.None)
                {
                    Player myRole = _isOnlineMode
                        ? (_onlineClient.AssignedRole != Player.None ? _onlineClient.AssignedRole : Player.X)
                        : (_localSession.Mode == SessionMode.BotVsHumanO ? Player.O : Player.X);
                    PlayMobileSoundEffect(state.Winner == myRole ? GameSoundEffect.SuddenDeathVictory : GameSoundEffect.MatchDefeat);
                }
                else
                {
                    PlayMobileSoundEffect(GameSoundEffect.MoveTap);
                }
            }
            else if (ReferenceEquals(cellButtons, _cellButtons) && state.MoveCount == 0)
            {
                _lastAnimatedMove = null;
            }
        }

        // =====================================================================
        // PERSISTENCIA LOCAL (SESIÓN, CUENTAS Y SERVIDOR LAN)
        // =====================================================================

        private static string GetAuthDataDir()
        {
            string dir = Path.Combine(FileSystem.AppDataDirectory, "AuthData");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private void SaveSessionToDisk(string username, string password)
        {
            try
            {
                string file = Path.Combine(GetAuthDataDir(), "saved_session.txt");
                File.WriteAllText(file, $"{username}\t{password}");
            }
            catch { }
        }

        private bool TryLoadSavedSession(out string username, out string password)
        {
            username = string.Empty;
            password = string.Empty;
            try
            {
                string file = Path.Combine(GetAuthDataDir(), "saved_session.txt");
                if (!File.Exists(file)) return false;

                string[] parts = File.ReadAllText(file).Trim().Split('\t');
                if (parts.Length >= 2 && parts[0].Trim().Length >= 3 && parts[1].Trim().Length >= 4)
                {
                    username = parts[0].Trim();
                    password = parts[1].Trim();
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void ClearSavedSessionFromDisk()
        {
            try
            {
                string file = Path.Combine(GetAuthDataDir(), "saved_session.txt");
                if (File.Exists(file)) File.Delete(file);
            }
            catch { }
        }

        private void SaveServerHostToDisk(string host)
        {
            try
            {
                string file = Path.Combine(GetAuthDataDir(), "server_host.txt");
                File.WriteAllText(file, host.Trim());
            }
            catch { }
        }

        private string LoadSavedServerHost()
        {
            try
            {
                string file = Path.Combine(GetAuthDataDir(), "server_host.txt");
                if (File.Exists(file)) return File.ReadAllText(file).Trim();
            }
            catch { }
            return string.Empty;
        }

        private void LoadLocalAccountsFromDisk()
        {
            try
            {
                _localAuthStore.LoadFromDirectory(GetAuthDataDir());
            }
            catch { }
        }

        private void SaveLocalAccountsToDisk()
        {
            try
            {
                _localAuthStore.SaveToDirectory(GetAuthDataDir());
            }
            catch { }
        }

        // =====================================================================
        // HELPERS VISUALES Y HÁPTICOS
        // =====================================================================

        private static void TriggerSubtleHaptic()
        {
            try
            {
                if (HapticFeedback.Default.IsSupported)
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                }
            }
            catch { }
        }

        private void PlayMobileSoundEffect(GameSoundEffect effectType)
        {
            if (_isMuted)
            {
                return;
            }

            TriggerSubtleHaptic();

#if ANDROID
            try
            {
                Android.Media.Tone tone = effectType switch
                {
                    GameSoundEffect.MoveTap => Android.Media.Tone.PropBeep,
                    GameSoundEffect.LowTimeWarning => Android.Media.Tone.CdmaAlertCallGuard,
                    GameSoundEffect.SuddenDeathVictory => Android.Media.Tone.PropAck,
                    GameSoundEffect.MatchDefeat => Android.Media.Tone.PropNack,
                    _ => Android.Media.Tone.PropBeep
                };
                int durationMs = effectType == GameSoundEffect.SuddenDeathVictory ? 220 : 85;
                using var toneGen = new Android.Media.ToneGenerator(Android.Media.Stream.Music, 70);
                toneGen.StartTone(tone, durationMs);
            }
            catch { }
#endif
        }

        private static void ConfigureOnlineRatingLabel(Label label, int rating, string rankTitle, int? delta)
        {
            if (delta.HasValue)
            {
                string sign = delta.Value >= 0 ? "+" : "";
                label.Text = $"{rating} pts ({sign}{delta.Value}) • {rankTitle}";
                label.TextColor = delta.Value >= 0 ? PositiveDeltaColor : NegativeDeltaColor;
            }
            else
            {
                label.Text = $"{rating} pts • {rankTitle}";
                label.TextColor = MutedTextColor;
            }
        }

        private static Border CreateCardBorder(Color bg, Color stroke, double strokeThickness, float cornerRadius, Thickness padding)
        {
            return new Border
            {
                BackgroundColor = bg,
                Stroke = new SolidColorBrush(stroke),
                StrokeThickness = strokeThickness,
                Padding = padding,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = new CornerRadius(cornerRadius)
                }
            };
        }

        private static Button CreateActionButton(string text, Action onClicked, double height = 44, double fontSize = 13)
        {
            var btn = new Button
            {
                Text = text,
                HeightRequest = height,
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 10,
                Padding = new Thickness(8, 2),
                BackgroundColor = Color.FromRgb(36, 48, 72),
                TextColor = Colors.White
            };
            btn.Clicked += (_, _) => onClicked?.Invoke();
            return btn;
        }

        private static Button CreateLargeTileButton(string text, Action onClicked)
        {
            var btn = new Button
            {
                Text = text,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 14,
                Padding = new Thickness(8),
                BackgroundColor = Color.FromRgb(33, 46, 66),
                BorderColor = AccentPrimaryColor,
                BorderWidth = 2,
                TextColor = Colors.White
            };
            btn.Clicked += (_, _) => onClicked?.Invoke();
            return btn;
        }

        private static void HighlightTabButton(Button btn, bool active)
        {
            if (btn == null) return;
            btn.BackgroundColor = active ? Color.FromRgb(41, 76, 117) : Color.FromRgb(28, 36, 52);
            btn.BorderColor = active ? ActiveMacroBorderColor : InactiveMacroBorderColor;
            btn.BorderWidth = active ? 2 : 1;
        }

        private static string FormatEndReason(MatchEndReason reason)
        {
            return reason switch
            {
                MatchEndReason.SuddenDeathThreeInARow => "Muerte Súbita",
                MatchEndReason.Timeout => "Tiempo Agotado",
                MatchEndReason.Resignation => "Rendición",
                MatchEndReason.DisconnectForfeit => "Abandono",
                MatchEndReason.GlobalDraw => "Empate",
                MatchEndReason.Aborted => "Cancelada por Inactividad",
                _ => "Finalizada"
            };
        }
    }
}
