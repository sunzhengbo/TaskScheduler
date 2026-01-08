using System.Reflection;
using System.Text;

namespace TaskScheduler.Infra.Helpers;

public static class AssemblyHelper
{
    public static string ReadEmbeddedResource(Assembly assembly, string filePath)
    {
        var path = $"{assembly.GetName().Name}.{filePath}";
        using var stream = assembly.GetManifestResourceStream(path);
        if (stream == null)
        {
            throw new FileNotFoundException($"Resource '{path}' not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}