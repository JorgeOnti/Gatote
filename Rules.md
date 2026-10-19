# Fractal Tic-Tac-Toe (Muerte Súbita) - Especificaciones del Sistema

## Arquitectura
- Lenguaje: C# (.NET) / Desacoplado de motor gráfico.
- Arquitectura pura: Modelo de Dominio independiente, orientado a pruebas unitarias y Minimax.

## Estructura de Datos
- 9 tableros pequeños (índices 0 a 8) dentro de 1 macro-tablero (índice 0 a 8).
- Total de casillas: 81 celdas.

## Reglas de Movimiento
1. Primer movimiento: Libre en cualquier casilla de cualquier tablero.
2. Movimientos subsecuentes: La celda relativa (0..8) elegida por el jugador actual
   determina obligatoriamente el índice del tablero macro (0..8) donde debe jugar el rival.
3. Excepción de Tiro Libre: Si el tablero macro de destino ya está completamente lleno
   (empate local de 9 fichas), el jugador activo puede colocar su ficha en cualquier 
   casilla vacía de cualquier otro tablero disponible.

## Condición de Fin de Partida (Muerte Súbita)
- En el momento en que un jugador complete una línea de 3 en CUALQUIER tablero pequeño, 
  gana la partida inmediatamente.
- No se requiere conquistar 3 tableros macro.