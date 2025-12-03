using System.Diagnostics;

namespace NFCBets.Utilities;

public static class PerformanceHelper
{
    // For async methods that return a value
    public static async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> func)
    {
        var sw = Stopwatch.StartNew();
        var result = await func();
        sw.Stop();
        Console.WriteLine($"⏱️ {operation}: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalSeconds:F1}s)");
        return result;
    }

    // For async methods that return void (Task)
    public static async Task MeasureAsync(string operation, Func<Task> func)
    {
        var sw = Stopwatch.StartNew();
        await func();
        sw.Stop();
        Console.WriteLine($"⏱️ {operation}: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalSeconds:F1}s)");
    }

    // For sync methods that return a value
    public static T Measure<T>(string operation, Func<T> func)
    {
        var sw = Stopwatch.StartNew();
        var result = func();
        sw.Stop();
        Console.WriteLine($"⏱️ {operation}: {sw.ElapsedMilliseconds}ms");
        return result;
    }

    // For sync methods that return void
    public static void Measure(string operation, Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        Console.WriteLine($"⏱️ {operation}: {sw.ElapsedMilliseconds}ms");
    }
}