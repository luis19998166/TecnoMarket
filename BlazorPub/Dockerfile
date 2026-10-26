# Etapa 1: compilacion y publicacion (genera .br y .gz de los recursos WASM)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish BlazorPub/BlazorPub.csproj -c Release -o /app/publish

# Etapa 2: imagen de ejecucion liviana
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --retries=3 CMD curl -fs http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "BlazorPub.dll"]
