using System.Collections;
using System.Reflection;
using System.IO;
using GasparSystemHealth.Models;

namespace GasparSystemHealth.Services;

public sealed class LibreHardwareMonitorReader : IDisposable
{
    private readonly string _appRoot;
    private object? _computer;
    private Type? _computerType;
    private bool _initialized;
    private string _source = "Non disponibile";
    private string _note = "Sensori non inizializzati";

    public LibreHardwareMonitorReader(string appRoot)
    {
        _appRoot = appRoot;
    }

    public bool IsInstalled => File.Exists(Path.Combine(Path.Combine(_appRoot, "LibreHardwareMonitor"), "LibreHardwareMonitorLib.dll"));

    public TemperatureSnapshot ReadSnapshot()
    {
        if (!EnsureInitialized())
        {
            return new TemperatureSnapshot { Source = _source, Note = _note };
        }

        double? cpu = null;
        double? gpu = null;

        try
        {
            foreach (object hardware in EnumerateItems(_computerType?.GetProperty("Hardware")?.GetValue(_computer)))
            {
                ReadHardwareNode(hardware, ref cpu, ref gpu);
            }
        }
        catch (Exception ex)
        {
            _note = SecurityHelpers.ToSafeUserMessage(ex, "Errore durante la lettura dei sensori.");
        }

        return new TemperatureSnapshot
        {
            CpuCelsius = Normalize(cpu),
            GpuCelsius = Normalize(gpu),
            Source = _source,
            Note = _note
        };
    }

    private bool EnsureInitialized()
    {
        if (_initialized)
        {
            return _computer is not null;
        }

        _initialized = true;
        string basePath = Path.Combine(_appRoot, "LibreHardwareMonitor");
        string dllPath = Path.Combine(basePath, "LibreHardwareMonitorLib.dll");

        if (!File.Exists(dllPath))
        {
            _source = "LibreHardwareMonitor mancante";
            _note = "Copia la cartella LibreHardwareMonitor accanto all'app.";
            return false;
        }

        try
        {
            foreach (string dependency in Directory.EnumerateFiles(basePath, "*.dll"))
            {
                try { Assembly.LoadFrom(dependency); } catch { }
            }

            Assembly assembly = Assembly.LoadFrom(dllPath);
            _computerType = assembly.GetType("LibreHardwareMonitor.Hardware.Computer", throwOnError: true)
                ?? throw new InvalidOperationException("Tipo Computer non trovato nella libreria sensori.");
            _computer = Activator.CreateInstance(_computerType)
                ?? throw new InvalidOperationException("Impossibile creare l'istanza del monitor hardware.");
            SetBooleanProperty(_computerType, _computer, "IsCpuEnabled", true);
            SetBooleanProperty(_computerType, _computer, "IsGpuEnabled", true);
            SetBooleanProperty(_computerType, _computer, "IsMotherboardEnabled", true);
            _computerType.GetMethod("Open")?.Invoke(_computer, null);

            _source = "LibreHardwareMonitor";
            _note = "Sensori live attivi";
            return true;
        }
        catch (Exception ex)
        {
            _source = "LibreHardwareMonitor errore";
            _note = SecurityHelpers.ToSafeUserMessage(ex, "Inizializzazione sensori non riuscita.");
            return false;
        }
    }

    private static void SetBooleanProperty(Type type, object instance, string name, bool value)
    {
        type.GetProperty(name)?.SetValue(instance, value);
    }

    private void ReadHardwareNode(object node, ref double? cpu, ref double? gpu)
    {
        Type hardwareType = node.GetType();
        hardwareType.GetMethod("Update")?.Invoke(node, null);

        string hardwareKind = hardwareType.GetProperty("HardwareType")?.GetValue(node)?.ToString() ?? string.Empty;

        foreach (object sensor in EnumerateItems(hardwareType.GetProperty("Sensors")?.GetValue(node)))
        {
            Type sensorType = sensor.GetType();
            string sensorKind = sensorType.GetProperty("SensorType")?.GetValue(sensor)?.ToString() ?? string.Empty;
            if (!string.Equals(sensorKind, "Temperature", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            object? rawValue = sensorType.GetProperty("Value")?.GetValue(sensor);
            if (rawValue is null)
            {
                continue;
            }

            double value = Convert.ToDouble(rawValue);
            if (value <= 0 || value > 125)
            {
                continue;
            }

            if (hardwareKind.StartsWith("Cpu", StringComparison.OrdinalIgnoreCase))
            {
                cpu = cpu.HasValue ? Math.Round((cpu.Value + value) / 2d, 1) : Math.Round(value, 1);
            }
            else if (hardwareKind.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase))
            {
                gpu = gpu.HasValue ? Math.Round((gpu.Value + value) / 2d, 1) : Math.Round(value, 1);
            }
        }

        foreach (object child in EnumerateItems(hardwareType.GetProperty("SubHardware")?.GetValue(node)))
        {
            ReadHardwareNode(child, ref cpu, ref gpu);
        }
    }

    private static IEnumerable<object> EnumerateItems(object? value)
    {
        if (value is null)
        {
            yield break;
        }

        if (value is IEnumerable enumerable and not string)
        {
            foreach (object? item in enumerable)
            {
                if (item is not null)
                {
                    yield return item;
                }
            }

            yield break;
        }

        yield return value;
    }

    private static double? Normalize(double? value)
    {
        if (!value.HasValue || value <= 0 || value > 125)
        {
            return null;
        }

        return Math.Round(value.Value, 1);
    }

    public void Dispose()
    {
        if (_computer is null || _computerType is null)
        {
            return;
        }

        try
        {
            _computerType.GetMethod("Close")?.Invoke(_computer, null);
        }
        catch
        {
            // ignore dispose issues on shutdown
        }
    }

    public void Reset()
    {
        Dispose();
        _computer = null;
        _computerType = null;
        _initialized = false;
        _source = "Non disponibile";
        _note = "Sensori in attesa";
    }
}
