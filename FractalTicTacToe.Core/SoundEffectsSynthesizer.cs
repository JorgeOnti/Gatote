using System;
using System.IO;

namespace FractalTicTacToe.Core
{
    /// <summary>
    /// Tipos de efectos de sonido sintéticos del juego.
    /// </summary>
    public enum GameSoundEffect
    {
        MoveTap = 0,
        LowTimeWarning = 1,
        SuddenDeathVictory = 2,
        MatchDefeat = 3
    }

    /// <summary>
    /// Sintetizador ligero de efectos de sonido PCM de 16 bits (44.1 kHz, Mono) en memoria.
    /// No requiere archivos de audio externos y genera tanto flujos WAV completos como muestras PCM
    /// para reproducirse de forma nativa en Godot 4 (<c>AudioStreamWav</c>) y clientes móviles.
    /// </summary>
    public static class SoundEffectsSynthesizer
    {
        public const int SampleRate = 22050;

        public const string PrivacyPolicyInAppText =
            "POLÍTICA DE PRIVACIDAD — GATOTE FRACTAL\n\n" +
            "1. Datos recopilados: Únicamente tu nombre de usuario elegido, tu contraseña cifrada con SHA-256 " +
            "y el historial/puntaje de tus partidas en línea. No solicitamos correo electrónico, teléfono ni ubicación.\n" +
            "2. Uso de datos: Exclusivamente para el emparejamiento multijugador (Matchmaking) y tabla de ligas. " +
            "No compartimos ni vendemos datos a terceros ni usamos rastreadores publicitarios.\n" +
            "3. Eliminación de cuenta: Puedes borrar permanentemente tu cuenta y tus datos en cualquier momento " +
            "desde la pestaña '🏆 Perfil' tocando el botón '🗑️ Eliminar Cuenta'.";

        /// <summary>
        /// Genera los bytes crudos PCM de 16 bits (Little-Endian) para un efecto de sonido.
        /// Ideal para <c>AudioStreamWav.Data</c> en Godot 4.
        /// </summary>
        public static byte[] GeneratePcm16Bytes(GameSoundEffect effect)
        {
            return effect switch
            {
                GameSoundEffect.MoveTap => SynthesizeToneSequence(
                    new[] { (660.0, 0.045, 0.45), (880.0, 0.035, 0.35) }),
                GameSoundEffect.LowTimeWarning => SynthesizeToneSequence(
                    new[] { (980.0, 0.06, 0.40) }),
                GameSoundEffect.SuddenDeathVictory => SynthesizeToneSequence(
                    new[]
                    {
                        (523.25, 0.09, 0.50), // C5
                        (659.25, 0.09, 0.50), // E5
                        (783.99, 0.09, 0.50), // G5
                        (1046.50, 0.22, 0.55) // C6
                    }),
                GameSoundEffect.MatchDefeat => SynthesizeToneSequence(
                    new[]
                    {
                        (440.0, 0.12, 0.45),  // A4
                        (349.23, 0.22, 0.45)  // F4
                    }),
                _ => Array.Empty<byte>()
            };
        }

        /// <summary>
        /// Genera un archivo WAV completo en memoria (con cabecera RIFF/WAVE de 44 bytes) listo para reproducirse desde un <see cref="MemoryStream"/>.
        /// </summary>
        public static byte[] GenerateWavFileBytes(GameSoundEffect effect)
        {
            byte[] pcm = GeneratePcm16Bytes(effect);
            using var ms = new MemoryStream(44 + pcm.Length);
            using var bw = new BinaryWriter(ms);

            // Cabecera RIFF WAVE estándar (16-bit Mono PCM)
            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + pcm.Length);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });
            bw.Write(new[] { 'f', 'm', 't', ' ' });
            bw.Write(16); // Subchunk1Size (PCM)
            bw.Write((short)1); // AudioFormat = 1 (PCM)
            bw.Write((short)1); // NumChannels = 1 (Mono)
            bw.Write(SampleRate);
            bw.Write(SampleRate * 2); // ByteRate
            bw.Write((short)2); // BlockAlign
            bw.Write((short)16); // BitsPerSample
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(pcm.Length);
            bw.Write(pcm);

            return ms.ToArray();
        }

        private static byte[] SynthesizeToneSequence((double freqHz, double durationSec, double amplitude)[] notes)
        {
            int totalSamples = 0;
            for (int i = 0; i < notes.Length; i++)
            {
                totalSamples += Math.Max(1, (int)(notes[i].durationSec * SampleRate));
            }

            byte[] buffer = new byte[totalSamples * 2];
            int sampleOffset = 0;

            for (int n = 0; n < notes.Length; n++)
            {
                double freq = notes[n].freqHz;
                int count = Math.Max(1, (int)(notes[n].durationSec * SampleRate));
                double amp = notes[n].amplitude;

                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / SampleRate;
                    // Envolvente suave de ataque y caída exponencial para evitar clics de audio
                    double attack = Math.Min(1.0, i / (SampleRate * 0.005));
                    double decay = 1.0 - ((double)i / count) * 0.75;
                    double env = attack * decay;

                    double sample = Math.Sin(2.0 * Math.PI * freq * t) * amp * env;
                    short pcmVal = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);

                    int byteIdx = (sampleOffset + i) * 2;
                    buffer[byteIdx] = (byte)(pcmVal & 0xFF);
                    buffer[byteIdx + 1] = (byte)((pcmVal >> 8) & 0xFF);
                }

                sampleOffset += count;
            }

            return buffer;
        }
    }
}
