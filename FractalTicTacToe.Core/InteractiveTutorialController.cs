using System;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Controlador aislado para el Tutorial Interactivo de 3 pasos.
    /// Utiliza su propio <see cref="GameState"/> independiente para jamás contaminar
    /// ni sobrescribir el estado de las partidas locales o en línea.
    /// </summary>
    public sealed class InteractiveTutorialController
    {
        public GameState BoardState { get; } = new GameState();
        public bool IsActive { get; private set; }
        public int CurrentStep { get; private set; }
        public Move? TargetHintMove { get; private set; }

        public string StatusTitle
        {
            get
            {
                if (!IsActive) return string.Empty;
                switch (CurrentStep)
                {
                    case 1:
                        return "PASO 1/3: Tu casilla envía al rival al siguiente gato";
                    case 2:
                        return "PASO 2/3: Arma tu ataque y redirige al rival";
                    case 3:
                        return "PASO 3/3: ¡Muerte Súbita! Gana con 3 en línea";
                    default:
                        return "¡Tutorial completado! Ya dominas Gatote";
                }
            }
        }

        public string StepInstruction { get; private set; } = string.Empty;

        public string BannerText => StatusTitle;
        public string DetailText => StepInstruction;

        public string ProgressLabel => CurrentStep <= 3 && CurrentStep >= 1
            ? $"Paso {CurrentStep} de 3"
            : "Completado";

        public void Start()
        {
            BoardState.Reset();
            IsActive = true;
            CurrentStep = 1;
            TargetHintMove = new Move(4, 0);
            StepInstruction = "Toca la casilla morada (Gato central, esquina superior izquierda) para enviar al rival al gato de arriba a la izquierda.";
            GameLogger.Info("TUTORIAL", "Tutorial interactivo iniciado (Paso 1/3).");
        }

        /// <summary>
        /// Sale inmediatamente del tutorial, limpiando por completo su estado y casilla sugerida
        /// para que ninguna partida posterior (En Línea o Local) se vea afectada.
        /// </summary>
        public void Exit()
        {
            if (IsActive)
            {
                GameLogger.Info("TUTORIAL", $"Saliendo del tutorial en el paso {CurrentStep}. Estado liberado.");
            }
            IsActive = false;
            CurrentStep = 0;
            TargetHintMove = null;
            StepInstruction = string.Empty;
            BoardState.Reset();
        }

        public bool TryHandleCellTap(int boardIndex, int cellIndex, out bool wonTutorial)
        {
            wonTutorial = false;
            if (!IsActive)
            {
                return false;
            }

            if (CurrentStep == 1)
            {
                if (boardIndex != 4 || cellIndex != 0)
                {
                    StepInstruction = "Paso 1: Toca la casilla con borde morado (Gato central, esquina superior izquierda).";
                    return false;
                }

                BoardState.MakeMove(new Move(4, 0));
                BoardState.MakeMove(new Move(0, 4));
                CurrentStep = 2;
                TargetHintMove = new Move(4, 1);
                StepInstruction = "El rival tiró en su centro y te regresó al gato central. Toca la casilla morada (arriba al centro) para armar 2 en línea.";
                GameLogger.Info("TUTORIAL", "Paso 1 completado -> Avanzando a Paso 2/3.");
                return true;
            }

            if (CurrentStep == 2)
            {
                if (boardIndex != 4 || cellIndex != 1)
                {
                    StepInstruction = "Paso 2: Toca la casilla con borde morado (Gato central, arriba al centro).";
                    return false;
                }

                BoardState.MakeMove(new Move(4, 1));
                BoardState.MakeMove(new Move(1, 4));
                CurrentStep = 3;
                TargetHintMove = new Move(4, 2);
                StepInstruction = "¡El rival cometió el error de regresarte al gato central! Toca la casilla morada para completar 3 en línea y ganar.";
                GameLogger.Info("TUTORIAL", "Paso 2 completado -> Avanzando a Paso 3/3.");
                return true;
            }

            if (CurrentStep == 3)
            {
                if (boardIndex != 4 || cellIndex != 2)
                {
                    StepInstruction = "Paso 3: Toca la casilla con borde morado (Gato central, esquina superior derecha) para ganar.";
                    return false;
                }

                BoardState.MakeMove(new Move(4, 2));
                CurrentStep = 4;
                TargetHintMove = null;
                wonTutorial = true;
                StepInstruction = "Regla clave: Nunca envíes al rival a un gato donde ya tenga 2 en línea listos para ganar.";
                GameLogger.Info("TUTORIAL", "Paso 3 completado -> ¡Victoria de tutorial alcanzada!");
                return true;
            }

            return false;
        }
    }
}
