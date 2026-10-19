using System;
using Godot;
using FractalTicTacToe.Core;

namespace FractalTicTacToe.GodotClient
{
    /// <summary>
    /// Pantalla principal 2D optimizada para dispositivos móviles (Portrait 9:16) en Godot 4 (.NET / C#).
    /// Construye de forma responsiva el tablero fractal 9x9, las tarjetas de perfil ELO y la barra de acciones táctiles,
    /// delegando toda la lógica de dominio y búsqueda asíncrona Minimax a <see cref="GameSessionController"/>.
    /// </summary>
    public partial class MobileGameScreen : Control
    {
        private GameSessionController _session;

        // Referencias de UI para actualización reactiva en O(1)
        private readonly PanelContainer[] _macroBoardPanels = new PanelContainer[GameState.BoardCount];
        private readonly Button[,] _cellButtons = new Button[GameState.BoardCount, GameState.CellsPerBoard];

        private Label _playerXTitleLabel;
        private Label _playerXEloLabel;
        private Label _playerOTitleLabel;
        private Label _playerOEloLabel;
        private Label _statusBannerLabel;
        private Label _telemetryLabel;
        private Button _modeButton;
        private Button _undoButton;
        private Button _redoButton;
        private Button _hintButton;

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
            _session = new GameSessionController(SessionMode.HumanVsBotX, BotDifficulty.Hard);
            _session.StateChanged += OnSessionStateChanged;

            BuildMobileLayout();
            RefreshUI();
        }

        public override void _ExitTree()
        {
            if (_session != null)
            {
                _session.StateChanged -= OnSessionStateChanged;
            }
        }

        private void BuildMobileLayout()
        {
            // Fondo general oscuro
            var background = new ColorRect
            {
                Color = BgColor
            };
            background.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(background);

            // Contenedor principal con márgenes seguros para pantallas móviles (Safe Area)
            var safeMargin = new MarginContainer();
            safeMargin.SetAnchorsPreset(LayoutPreset.FullRect);
            safeMargin.AddThemeConstantOverride("margin_left", 32);
            safeMargin.AddThemeConstantOverride("margin_right", 32);
            safeMargin.AddThemeConstantOverride("margin_top", 48);
            safeMargin.AddThemeConstantOverride("margin_bottom", 48);
            AddChild(safeMargin);

            var mainColumn = new VBoxContainer();
            mainColumn.AddThemeConstantOverride("separation", 24);
            safeMargin.AddChild(mainColumn);

            // 1. Encabezado y Tarjetas de Jugadores (ELO & Rango)
            BuildHeaderAndProfiles(mainColumn);

            // 2. Banner de Estado (Turno / Tablero Forzado / Victoria por Muerte Súbita)
            BuildStatusBanner(mainColumn);

            // 3. Tablero Fractal 9x9 cuadrado y centrado
            BuildFractalBoardGrid(mainColumn);

            // 4. Barra de Telemetría (Nodos Minimax, TT Hits, FEN)
            _telemetryLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _telemetryLabel.AddThemeFontSizeOverride("font_size", 22);
            _telemetryLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.82f));
            mainColumn.AddChild(_telemetryLabel);

            // 5. Barra inferior de botones táctiles (Undo, Redo, Pista, Dificultad, Reiniciar)
            BuildBottomActionBar(mainColumn);
        }

        private void BuildHeaderAndProfiles(VBoxContainer parent)
        {
            var titleLabel = new Label
            {
                Text = "FRACTAL TIC-TAC-TOE • MUERTE SÚBITA",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titleLabel.AddThemeFontSizeOverride("font_size", 34);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.96f, 1.0f));
            parent.AddChild(titleLabel);

            var profilesRow = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            profilesRow.AddThemeConstantOverride("separation", 20);
            parent.AddChild(profilesRow);

            // Tarjeta Jugador X
            var cardX = CreateStyledPanel(CardBgColor, PlayerXColor, borderWidth: 3, cornerRadius: 16);
            cardX.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var boxX = new VBoxContainer();
            _playerXTitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _playerXTitleLabel.AddThemeFontSizeOverride("font_size", 26);
            _playerXTitleLabel.AddThemeColorOverride("font_color", PlayerXColor);
            _playerXEloLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _playerXEloLabel.AddThemeFontSizeOverride("font_size", 22);
            boxX.AddChild(_playerXTitleLabel);
            boxX.AddChild(_playerXEloLabel);
            cardX.AddChild(boxX);
            profilesRow.AddChild(cardX);

            // Tarjeta Jugador O
            var cardO = CreateStyledPanel(CardBgColor, PlayerOColor, borderWidth: 3, cornerRadius: 16);
            cardO.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var boxO = new VBoxContainer();
            _playerOTitleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _playerOTitleLabel.AddThemeFontSizeOverride("font_size", 26);
            _playerOTitleLabel.AddThemeColorOverride("font_color", PlayerOColor);
            _playerOEloLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _playerOEloLabel.AddThemeFontSizeOverride("font_size", 22);
            boxO.AddChild(_playerOTitleLabel);
            boxO.AddChild(_playerOEloLabel);
            cardO.AddChild(boxO);
            profilesRow.AddChild(cardO);
        }

        private void BuildStatusBanner(VBoxContainer parent)
        {
            var bannerPanel = CreateStyledPanel(
                new Color(0.13f, 0.17f, 0.25f),
                new Color(0.30f, 0.38f, 0.52f),
                borderWidth: 2,
                cornerRadius: 14);

            _statusBannerLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            _statusBannerLabel.AddThemeFontSizeOverride("font_size", 28);
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
            macroGrid.AddThemeConstantOverride("h_separation", 18);
            macroGrid.AddThemeConstantOverride("v_separation", 18);
            aspectContainer.AddChild(macroGrid);

            for (int boardIdx = 0; boardIdx < GameState.BoardCount; boardIdx++)
            {
                var macroPanel = CreateStyledPanel(
                    CardBgColor,
                    InactiveMacroBorderColor,
                    borderWidth: 4,
                    cornerRadius: 16,
                    contentMargin: 12);

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
                    cellBtn.AddThemeFontSizeOverride("font_size", 36);
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
            topControlsRow.AddThemeConstantOverride("separation", 16);
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
            bottomControlsRow.AddThemeConstantOverride("separation", 16);
            parent.AddChild(bottomControlsRow);

            _modeButton = CreateActionButton("Modo: Vs Bot (Difícil)", OnCycleModePressed);
            var newGameButton = CreateActionButton("★ Nueva Partida", OnNewMatchPressed);

            bottomControlsRow.AddChild(_modeButton);
            bottomControlsRow.AddChild(newGameButton);
        }

        private async void OnCellTapped(int boardIndex, int cellIndex)
        {
            _currentHintMove = null;

            if (!_session.TryPlayHumanMove(boardIndex, cellIndex))
            {
                return;
            }

            // Si tras el tiro humano ahora es turno del Bot Minimax, ejecutarlo asíncronamente
            if (_session.IsCurrentTurnBot() && !_session.State.IsGameOver)
            {
                await _session.PlayBotTurnAsync();
            }
        }

        private void OnUndoPressed()
        {
            _currentHintMove = null;
            _session.Undo();
        }

        private void OnRedoPressed()
        {
            _currentHintMove = null;
            _session.Redo();
        }

        private async void OnHintPressed()
        {
            if (_session.State.IsGameOver || _session.IsBotThinking || _session.IsCurrentTurnBot())
            {
                return;
            }

            _hintButton.Disabled = true;
            MinimaxSearchResult hint = await _session.RequestHintAsync();
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
            _presetIndex = (_presetIndex + 1) % 5;

            switch (_presetIndex)
            {
                case 0:
                    _session.ConfigureSession(SessionMode.HumanVsBotX, BotDifficulty.Easy, _session.ProfileX);
                    _modeButton.Text = "Modo: Vs Bot (Fácil)";
                    break;
                case 1:
                    _session.ConfigureSession(SessionMode.HumanVsBotX, BotDifficulty.Medium, _session.ProfileX);
                    _modeButton.Text = "Modo: Vs Bot (Medio)";
                    break;
                case 2:
                    _session.ConfigureSession(SessionMode.HumanVsBotX, BotDifficulty.Hard, _session.ProfileX);
                    _modeButton.Text = "Modo: Vs Bot (Difícil)";
                    break;
                case 3:
                    _session.ConfigureSession(SessionMode.HumanVsBotX, BotDifficulty.Master, _session.ProfileX);
                    _modeButton.Text = "Modo: Vs Bot (Maestro)";
                    break;
                case 4:
                    _session.ConfigureSession(SessionMode.PassAndPlay2P, BotDifficulty.Hard, _session.ProfileX);
                    _modeButton.Text = "Modo: 2 Jugadores (Móvil)";
                    break;
            }
        }

        private void OnNewMatchPressed()
        {
            _currentHintMove = null;
            _session.StartNewMatch();
        }

        private void OnSessionStateChanged()
        {
            // Asegurar que la actualización visual ocurra siempre en el hilo principal de Godot
            Callable.From(RefreshUI).CallDeferred();
        }

        private void RefreshUI()
        {
            GameState state = _session.State;
            Move? lastMove = state.GetLastMove();

            // 1. Actualizar tarjetas de perfil y ELO
            _playerXTitleLabel.Text = $"[X] {_session.ProfileX.Username}";
            _playerXEloLabel.Text = $"{_session.ProfileX.EloRating} ELO • {_session.ProfileX.RankTitle} ({_session.ProfileX.Wins}V/{_session.ProfileX.Losses}D)";

            _playerOTitleLabel.Text = $"[O] {_session.ProfileO.Username}";
            _playerOEloLabel.Text = $"{_session.ProfileO.EloRating} ELO • {_session.ProfileO.RankTitle} ({_session.ProfileO.Wins}V/{_session.ProfileO.Losses}D)";

            // 2. Actualizar Banner de Estado
            if (state.IsGameOver)
            {
                if (state.Winner != Player.None)
                {
                    string deltaInfo = _session.LastEloUpdate.HasValue
                        ? $"  |  ELO: X ({FormatDelta(_session.LastEloUpdate.Value.DeltaX)}), O ({FormatDelta(_session.LastEloUpdate.Value.DeltaO)})"
                        : string.Empty;
                    _statusBannerLabel.Text = $"¡VICTORIA POR MUERTE SÚBITA! GANADOR: JUGADOR {state.Winner}{deltaInfo}";
                }
                else
                {
                    _statusBannerLabel.Text = "¡EMPATE GLOBAL! Las 81 casillas están llenas.";
                }
            }
            else if (_session.IsBotThinking)
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

            // 3. Actualizar telemetría
            if (_session.LastBotSearchResult.HasValue)
            {
                MinimaxSearchResult r = _session.LastBotSearchResult.Value;
                _telemetryLabel.Text =
                    $"Último cálculo IA: {r.BestMove} | Eval: {r.Score} | Prof: {r.MaxDepthReached} | " +
                    $"Nodos: {r.NodesEvaluated:N0} | TT Hits: {r.TranspositionHits:N0} | {r.ElapsedMs} ms";
            }
            else if (!_currentHintMove.HasValue)
            {
                _telemetryLabel.Text = $"Movimientos: {state.MoveCount}/81  •  Partidas en sesión: {_session.MatchHistory.Count}";
            }

            // 4. Actualizar los 9 sub-tableros y sus 81 botones táctiles
            for (int board = 0; board < GameState.BoardCount; board++)
            {
                bool isBoardPlayable = !state.IsGameOver &&
                                       (state.ActiveBoardIndex == GameState.FreeMoveBoardIndex ||
                                        state.ActiveBoardIndex == board);

                var macroStyle = (StyleBoxFlat)_macroBoardPanels[board].GetThemeStylebox("panel");
                macroStyle.BorderColor = isBoardPlayable ? ActiveMacroBorderColor : InactiveMacroBorderColor;
                macroStyle.BgColor = isBoardPlayable
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

                    StyleCellButton(btn, occupant, isBoardPlayable, isLastMove, isHintMove);
                }
            }

            // 5. Estado de botones de acción
            _undoButton.Disabled = _session.IsBotThinking || !state.CanUndo;
            _redoButton.Disabled = _session.IsBotThinking || !state.CanRedo;
            _hintButton.Disabled = _session.IsBotThinking || state.IsGameOver || _session.IsCurrentTurnBot();
        }

        private static void StyleCellButton(
            Button btn,
            Player occupant,
            bool isBoardPlayable,
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
                btn.Disabled = !isBoardPlayable;
                style.BgColor = isBoardPlayable
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

        private static Button CreateActionButton(string text, Action onPressed)
        {
            var btn = new Button
            {
                Text = text,
                CustomMinimumSize = new Vector2(0, 76),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None
            };
            btn.AddThemeFontSizeOverride("font_size", 24);
            btn.Pressed += onPressed;
            return btn;
        }

        private static string FormatDelta(int delta) => delta >= 0 ? $"+{delta}" : delta.ToString();
    }
}
