# Gaspar System Health

Tool desktop Windows in C# WPF per diagnostica, integrita sistema, rete, aggiornamenti, sicurezza e monitoraggio hardware.

## Funzioni principali

- dashboard compatta con CPU, RAM, disco, uptime e temperature
- controllo completo con avanzamento a fasi
- strumenti di rete, firewall, aggiornamenti, integrita e log
- controllo definizioni antivirus e scansione rapida Defender
- supporto sensori tramite LibreHardwareMonitor

## Requisiti

- Windows 10 o Windows 11
- privilegi amministratore
- .NET Desktop Runtime compatibile con la build

## Sensori temperatura

L'app puo scaricare automaticamente il pacchetto `LibreHardwareMonitor` al primo avvio se non e gia presente accanto all'eseguibile.

## Struttura repository

- `App.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`
- `Models/`
- `Services/`
- `app.manifest`
- `GasparSystemHealth.csproj`

## Build locale

```powershell
dotnet build .\GasparSystemHealth.csproj -c Release
dotnet publish .\GasparSystemHealth.csproj -c Release -o .\publish
```

## Condivisione

Per condividere l'app:

1. carica questa cartella su GitHub o GitLab
2. crea una release
3. allega lo zip della cartella finale pubblicata

## Note

- Le cartelle `publish-v*`, `bin`, `obj` e i file temporanei non sono inclusi in questa copia repo.
- Le vecchie build e i backup restano fuori da questa cartella, nella workspace originale.
