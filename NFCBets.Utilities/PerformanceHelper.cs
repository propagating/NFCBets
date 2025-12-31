using System.Diagnostics;
using NFCBets.Utilities.Models;

namespace NFCBets.Utilities;

public static class PerformanceHelper
{
    private static readonly List<PerformanceRecord> _records = new();

    /// <summary>
    /// Measure async operation that returns a value
    /// </summary>
    public static async Task<T> MeasureAsync<T>(string operationName, Func<Task<T>> operation)
    {
        Console.WriteLine($"⏱️ Starting: {operationName}...");
        
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);
        
        T result;
        try
        {
            result = await operation();
        }
        finally
        {
            stopwatch.Stop();
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryDelta = memoryAfter - memoryBefore;

            var record = new PerformanceRecord
            {
                OperationName = operationName,
                Duration = stopwatch.Elapsed,
                MemoryDelta = memoryDelta,
                Timestamp = DateTime.UtcNow
            };
            
            _records.Add(record);
            
            Console.WriteLine($"✅ Completed: {operationName} in {FormatDuration(stopwatch.Elapsed)} (Memory: {FormatMemory(memoryDelta)})");
        }
        
        return result;
    }

    /// <summary>
    /// Measure async operation that returns void
    /// </summary>
    public static async Task MeasureAsync(string operationName, Func<Task> operation)
    {
        Console.WriteLine($"⏱️ Starting: {operationName}...");
        
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);
        
        try
        {
            await operation();
        }
        finally
        {
            stopwatch.Stop();
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryDelta = memoryAfter - memoryBefore;

            var record = new PerformanceRecord
            {
                OperationName = operationName,
                Duration = stopwatch.Elapsed,
                MemoryDelta = memoryDelta,
                Timestamp = DateTime.UtcNow
            };
            
            _records.Add(record);
            
            Console.WriteLine($"✅ Completed: {operationName} in {FormatDuration(stopwatch.Elapsed)} (Memory: {FormatMemory(memoryDelta)})");
        }
    }

    /// <summary>
    /// Measure synchronous operation that returns a value
    /// </summary>
    public static T Measure<T>(string operationName, Func<T> operation)
    {
        Console.WriteLine($"⏱️ Starting: {operationName}...");
        
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);
        
        T result;
        try
        {
            result = operation();
        }
        finally
        {
            stopwatch.Stop();
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryDelta = memoryAfter - memoryBefore;

            var record = new PerformanceRecord
            {
                OperationName = operationName,
                Duration = stopwatch.Elapsed,
                MemoryDelta = memoryDelta,
                Timestamp = DateTime.UtcNow
            };
            
            _records.Add(record);
            
            Console.WriteLine($"✅ Completed: {operationName} in {FormatDuration(stopwatch.Elapsed)} (Memory: {FormatMemory(memoryDelta)})");
        }
        
        return result;
    }

    /// <summary>
    /// Measure synchronous operation that returns void
    /// </summary>
    public static void Measure(string operationName, Action operation)
    {
        Console.WriteLine($"⏱️ Starting: {operationName}...");
        
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);
        
        try
        {
            operation();
        }
        finally
        {
            stopwatch.Stop();
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryDelta = memoryAfter - memoryBefore;

            var record = new PerformanceRecord
            {
                OperationName = operationName,
                Duration = stopwatch.Elapsed,
                MemoryDelta = memoryDelta,
                Timestamp = DateTime.UtcNow
            };
            
            _records.Add(record);
            
            Console.WriteLine($"✅ Completed: {operationName} in {FormatDuration(stopwatch.Elapsed)} (Memory: {FormatMemory(memoryDelta)})");
        }
    }

    /// <summary>
    /// Display summary of all recorded operations
    /// </summary>
    public static void DisplaySummary()
    {
        if (!_records.Any())
        {
            Console.WriteLine("No performance records captured.");
            return;
        }

        Console.WriteLine("\n" + new string('═', 80));
        Console.WriteLine("📊 PERFORMANCE SUMMARY");
        Console.WriteLine(new string('═', 80) + "\n");

        Console.WriteLine($"{"Operation",-45} {"Duration",-15} {"Memory",-15}");
        Console.WriteLine(new string('─', 80));

        foreach (var record in _records)
        {
            Console.WriteLine($"{TruncateString(record.OperationName, 44),-45} {FormatDuration(record.Duration),-15} {FormatMemory(record.MemoryDelta),-15}");
        }

        Console.WriteLine(new string('─', 80));

        var totalDuration = TimeSpan.FromTicks(_records.Sum(r => r.Duration.Ticks));
        var totalMemory = _records.Sum(r => r.MemoryDelta);

        Console.WriteLine($"{"TOTAL",-45} {FormatDuration(totalDuration),-15} {FormatMemory(totalMemory),-15}");
        Console.WriteLine($"\n📈 Operations: {_records.Count} | Avg Duration: {FormatDuration(TimeSpan.FromTicks(totalDuration.Ticks / _records.Count))}");
    }

    /// <summary>
    /// Clear all recorded performance data
    /// </summary>
    public static void ClearRecords()
    {
        _records.Clear();
    }

    /// <summary>
    /// Get all performance records
    /// </summary>
    public static IReadOnlyList<PerformanceRecord> GetRecords() => _records.AsReadOnly();

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1000)
            return $"{duration.TotalMilliseconds:F0}ms";
        if (duration.TotalSeconds < 60)
            return $"{duration.TotalSeconds:F2}s";
        if (duration.TotalMinutes < 60)
            return $"{duration.TotalMinutes:F1}min";
        return $"{duration.TotalHours:F1}hr";
    }

    private static string FormatMemory(long bytes)
    {
        if (bytes == 0) return "0 B";
        
        var sign = bytes < 0 ? "-" : "+";
        bytes = Math.Abs(bytes);
        
        if (bytes < 1024)
            return $"{sign}{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{sign}{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{sign}{bytes / (1024.0 * 1024):F1} MB";
        return $"{sign}{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return str;
        return str.Length <= maxLength ? str : str.Substring(0, maxLength - 3) + "...";
    }
}

