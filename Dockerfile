# Etapa 1: Compilación del Servidor Autoritativo (.NET 8 SDK)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY FractalTicTacToe.Core/FractalTicTacToe.Core.csproj FractalTicTacToe.Core/
COPY FractalTicTacToe.Server/FractalTicTacToe.Server.csproj FractalTicTacToe.Server/
RUN dotnet restore FractalTicTacToe.Server/FractalTicTacToe.Server.csproj

COPY FractalTicTacToe.Core/ FractalTicTacToe.Core/
COPY FractalTicTacToe.Server/ FractalTicTacToe.Server/
RUN dotnet publish FractalTicTacToe.Server/FractalTicTacToe.Server.csproj -c Release -o /app/publish --no-restore

# Etapa 2: Imagen ligera de ejecución (.NET 8 Runtime)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
RUN mkdir -p /app/ServerData
VOLUME ["/app/ServerData"]

ENV PORT=7777
EXPOSE 7777

ENTRYPOINT ["dotnet", "FractalTicTacToe.Server.dll"]
