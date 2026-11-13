FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --chown=1654:1654 publish/ .
EXPOSE 8443/tcp 7443/tcp
USER $APP_UID
ENTRYPOINT ["dotnet", "BarkFluff.BftProxy.dll"]
