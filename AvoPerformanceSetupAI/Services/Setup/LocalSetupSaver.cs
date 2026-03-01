namespace AvoPerformanceSetupAI.Services.Setup;

/// <summary>Writes setup files to the local output folder configured in settings.</summary>
public sealed class LocalSetupSaver : ISetupSaver
{
    public async Task<string> SaveAsync(string car, string track, string fileName, string setupText)
    {
        var outFolder = SetupSettings.Instance.OutputFolder;
        if (string.IsNullOrEmpty(outFolder))
            throw new InvalidOperationException(
                "Carpeta de destino no configurada. Ve a Configuración y selecciona la carpeta de destino.");

        Directory.CreateDirectory(outFolder);
        var destPath = Path.Combine(outFolder, fileName);
        await File.WriteAllTextAsync(destPath, setupText);
        return destPath;
    }
}
