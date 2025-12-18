using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public class DataValidationService : IDataValidationService
{
    private readonly NfcbetsContext _context;

    public DataValidationService(NfcbetsContext context)
    {
        _context = context;
    }

    public async Task<DataValidationReport> ValidateDataQualityAsync(int? startRound = null, int? endRound = null)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🔍 DATA QUALITY VALIDATION");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var report = new DataValidationReport
        {
            StartRound = startRound,
            EndRound = endRound,
            ValidationDate = DateTime.UtcNow
        };

        // Check 1: 1:1 Odds (should not exist - no-bet placeholders)
        await CheckForOneToOneOddsAsync(report, startRound, endRound);

        // Check 2: Invalid Odds (negative, zero, or extremely high)
        await CheckForInvalidOddsAsync(report, startRound, endRound);

        // Check 3: Missing Winners
        await CheckForMissingWinnersAsync(report, startRound, endRound);

        // Check 4: Multiple Winners per Arena
        await CheckForMultipleWinnersAsync(report, startRound, endRound);

        // Check 5: Orphaned Records
        await CheckForOrphanedRecordsAsync(report, startRound, endRound);

        // Check 6: Invalid Positions
        await CheckForInvalidPositionsAsync(report, startRound, endRound);

        // Check 7: Invalid Food Adjustments
        await CheckForInvalidFoodAdjustmentsAsync(report, startRound, endRound);

        // Check 8: Incomplete Arenas
        await CheckForIncompleteArenasAsync(report, startRound, endRound);

        // Display Summary
        DisplayValidationSummary(report);

        return report;
    }

    /// <summary>
    ///     Checking for pirate placements whose odds are still 1:1 after the table has been sat,
    ///     as these odds are a placeholder
    /// </summary>
    /// <param name="report"></param>
    /// <param name="startRound"></param>
    /// <param name="endRound"></param>
    private async Task CheckForOneToOneOddsAsync(DataValidationReport report, int? startRound, int? endRound)
    {
        Console.WriteLine("1️⃣ Checking for 1:1 odds (no-bet placeholders)...");

        var query = _context.RoundPiratePlacements.AsQueryable();

        if (startRound.HasValue)
            query = query.Where(rpp => rpp.RoundId >= startRound.Value);
        if (endRound.HasValue)
            query = query.Where(rpp => rpp.RoundId <= endRound.Value);


        //Check for current odds that are 1:1 they should all be 
        var oneToOneOddsRecords = await query
            .Where(rpp => rpp.StartingOdds == 1 || rpp.CurrentOdds == 1)
            .Select(rpp => new
            {
                rpp.RoundId,
                rpp.ArenaId,
                rpp.PirateId,
                rpp.StartingOdds,
                rpp.CurrentOdds
            })
            .ToListAsync();

        if (oneToOneOddsRecords.Any())
        {
            report.Issues.Add(new DataValidationIssue
            {
                Severity = ValidationSeverityEnum.Critical,
                Category = "Invalid Odds",
                Message =
                    $"Found {oneToOneOddsRecords.Count} records with 1:1 odds (should be excluded as no-bet placeholders)",
                AffectedRecords = oneToOneOddsRecords.Count,
                Details = oneToOneOddsRecords.Take(10).Select(r =>
                    $"Round {r.RoundId}, Arena {r.ArenaId}, Pirate {r.PirateId}: Starting={r.StartingOdds}:1, Current={r.CurrentOdds}:1"
                ).ToList()
            });

            Console.WriteLine($"   ❌ CRITICAL: Found {oneToOneOddsRecords.Count} records with 1:1 odds");
            Console.WriteLine("      Sample records:");
            foreach (var record in oneToOneOddsRecords.Take(5))
                Console.WriteLine($"         Round {record.RoundId}, Arena {record.ArenaId}, Pirate {record.PirateId}");
        }
        else
        {
            Console.WriteLine("   ✅ No 1:1 odds found");
        }
    }

    private async Task CheckForInvalidOddsAsync(DataValidationReport report, int? startRound, int? endRound)
    {
        Console.WriteLine("\n2️⃣ Checking for invalid odds (negative, zero, or > 25)...");

        var query = _context.RoundPiratePlacements.AsQueryable();

        if (startRound.HasValue)
            query = query.Where(rpp => rpp.RoundId >= startRound.Value);
        if (endRound.HasValue)
            query = query.Where(rpp => rpp.RoundId <= endRound.Value);

        var invalidOdds = await query
            .Where(rpp => rpp.StartingOdds <= 0 ||
                          rpp.CurrentOdds <= 0 ||
                          rpp.StartingOdds > 25 ||
                          rpp.CurrentOdds > 25)
            .Select(rpp => new
            {
                rpp.RoundId,
                rpp.ArenaId,
                rpp.PirateId,
                rpp.StartingOdds,
                rpp.CurrentOdds
            })
            .ToListAsync();

        if (invalidOdds.Any())
        {
            report.Issues.Add(new DataValidationIssue
            {
                Severity = ValidationSeverityEnum.High,
                Category = "Invalid Odds",
                Message = $"Found {invalidOdds.Count} records with invalid odds (≤0 or >25)",
                AffectedRecords = invalidOdds.Count,
                Details = invalidOdds.Take(10).Select(r =>
                    $"Round {r.RoundId}, Arena {r.ArenaId}, Pirate {r.PirateId}: Starting={r.StartingOdds}:1, Current={r.CurrentOdds}:1"
                ).ToList()
            });

            Console.WriteLine($"   ⚠️  Found {invalidOdds.Count} records with invalid odds");
        }
        else
        {
            Console.WriteLine("   ✅ All odds are valid (2-25:1)");
        }
    }

    private async Task CheckForMissingWinnersAsync(DataValidationReport report, int? startRound, int? endRound)
    {
        Console.WriteLine("\n3️⃣ Checking for rounds with missing winners...");

        var query = _context.RoundResults
            .Where(rr => rr.IsComplete)
            .AsQueryable();

        if (startRound.HasValue)
            query = query.Where(rr => rr.RoundId >= startRound.Value);
        if (endRound.HasValue)
            query = query.Where(rr => rr.RoundId <= endRound.Value);

        var roundsWithArenas = await query
            .GroupBy(rr => new { rr.RoundId, rr.ArenaId })
            .Select(g => new
            {
                g.Key.RoundId,
                g.Key.ArenaId,
                WinnerCount = g.Count(rr => rr.IsWinner)
            })
            .Where(x => x.WinnerCount == 0)
            .ToListAsync();

        if (roundsWithArenas.Any())
        {
            report.Issues.Add(new DataValidationIssue
            {
                Severity = ValidationSeverityEnum.Critical,
                Category = "Missing Winners",
                Message = $"Found {roundsWithArenas.Count} arena/round combinations with no winner",
                AffectedRecords = roundsWithArenas.Count,
                Details = roundsWithArenas.Take(10).Select(r =>
                    $"Round {r.RoundId}, Arena {r.ArenaId}: No winner marked"
                ).ToList()
            });

            Console.WriteLine($"   ❌ CRITICAL: Found {roundsWithArenas.Count} arenas with no winner");
        }
        else
        {
            Console.WriteLine("   ✅ All completed arenas have a winner");
        }
    }

    private async Task CheckForMultipleWinnersAsync(DataValidationReport report, int? startRound, int? endRound)
    {
        Console.WriteLine("\n4️⃣ Checking for arenas with multiple winners...");

        var query = _context.RoundResults
            .Where(rr => rr.IsComplete && rr.IsWinner)
            .AsQueryable();

        if (startRound.HasValue)
            query = query.Where(rr => rr.RoundId >= startRound.Value);
        if (endRound.HasValue)
            query = query.Where(rr => rr.RoundId <= endRound.Value);

        var multipleWinners = await query
            .GroupBy(rr => new { rr.RoundId, rr.ArenaId })
            .Select(g => new
            {
                g.Key.RoundId,
                g.Key.ArenaId,
                WinnerCount = g.Count()
            })
            .Where(x => x.WinnerCount > 1)
            .ToListAsync();

        if (multipleWinners.Any())
        {
            report.Issues.Add(new DataValidationIssue
            {
                Severity = ValidationSeverityEnum.High,
                Category = "Multiple Winners",
                Message = $"Found {multipleWinners.Count} arenas with multiple winners",
                AffectedRecords = multipleWinners.Count,
                Details = multipleWinners.Take(10).Select(r =>
                    $"Round {r.RoundId}, Arena {r.ArenaId}: {r.WinnerCount} winners"
                ).ToList()
            });

            Console.WriteLine($"   ⚠️  Found {multipleWinners.Count} arenas with multiple winners");
        }
        else
        {
            Console.WriteLine("   ✅ All arenas have exactly one winner");
        }
    }

    private async Task CheckForOrphanedRecordsAsync(DataValidationReport report, int? startRound, int? endRound)
    {
        Console.WriteLine("\n5️⃣ Checking for orphaned records (placements without results)...");

        var placementsQuery = _context.RoundPiratePlacements.AsQueryable();
        var resultsQuery = _context.RoundResults.Where(rr => rr.IsComplete).AsQueryable();

        if (startRound.HasValue)
        {
            placementsQuery = placementsQuery.Where(rpp => rpp.RoundId >= startRound.Value);
            resultsQuery = resultsQuery.Where(rr => rr.RoundId >= startRound.Value);
        }

        if (endRound.HasValue)
        {
            placementsQuery = placementsQuery.Where(rpp => rpp.RoundId <= endRound.Value);
            resultsQuery = resultsQuery.Where(rr => rr.RoundId <= endRound.Value);
        }

        var placements = await placementsQuery
            .Where(rpp => rpp.RoundId.HasValue && rpp.ArenaId.HasValue && rpp.PirateId.HasValue) // Ensure non-null
            .Select(rpp => new
            {
                RoundId = rpp.RoundId!.Value,
                ArenaId = rpp.ArenaId!.Value,
                PirateId = rpp.PirateId!.Value
            })
            .ToListAsync();

        var results = await resultsQuery
            .Where(rr => rr.RoundId.HasValue) // Ensure non-null
            .Select(rr => new
            {
                RoundId = rr.RoundId!.Value,
                rr.ArenaId,
                rr.PirateId
            })
            .ToListAsync();

        // Create strongly-typed tuples
        var placementSet = placements
            .Select(p => (p.RoundId, p.ArenaId, p.PirateId))
            .ToHashSet();

        var resultSet = results
            .Select(r => (r.RoundId, r.ArenaId, r.PirateId))
            .ToHashSet();

        // Now Except will work with named tuples
        var orphanedPlacements = placementSet.Except(resultSet).ToList();

        if (orphanedPlacements.Any())
        {
            report.Issues.Add(new DataValidationIssue
            {
                Severity = ValidationSeverityEnum.Medium,
                Category = "Orphaned Records",
                Message = $"Found {orphanedPlacements.Count} placements without corresponding results",
                AffectedRecords = orphanedPlacements.Count,
                Details = orphanedPlacements.Take(10).Select(r =>
                    $"Round {r.RoundId}, Arena {r.ArenaId}, Pirate {r.PirateId}"
                ).ToList()
            });

            Console.WriteLine($"   ⚠️  Found {orphanedPlacements.Count} orphaned placements");
        }
        else
        {
            Console.WriteLine("   ✅ All placements have corresponding results");
        }
    }


    private async Task CheckForInvalidPositionsAsync(DataValidationReport report, int? startRound, int? endRound)
    {
        Console.WriteLine("\n6️⃣ Checking for invalid positions (should be 0-3)...");

        var query = _context.RoundPiratePlacements.AsQueryable();

        if (startRound.HasValue)
            query = query.Where(rpp => rpp.RoundId >= startRound.Value);
        if (endRound.HasValue)
            query = query.Where(rpp => rpp.RoundId <= endRound.Value);

        var invalidPositions = await query
            .Where(rpp => rpp.PirateSeatPosition < 0 || rpp.PirateSeatPosition > 3)
            .Select(rpp => new
            {
                rpp.RoundId,
                rpp.ArenaId,
                rpp.PirateId,
                rpp.PirateSeatPosition
            })
            .ToListAsync();

        if (invalidPositions.Any())
        {
            report.Issues.Add(new DataValidationIssue
            {
                Severity = ValidationSeverityEnum.High,
                Category = "Invalid Positions",
                Message = $"Found {invalidPositions.Count} records with invalid positions (not 0-3)",
                AffectedRecords = invalidPositions.Count,
                Details = invalidPositions.Take(10).Select(r =>
                    $"Round {r.RoundId}, Arena {r.ArenaId}, Pirate {r.PirateId}: Position={r.PirateSeatPosition}"
                ).ToList()
            });

            Console.WriteLine($"   ⚠️  Found {invalidPositions.Count} records with invalid positions");
        }
        else
        {
            Console.WriteLine("   ✅ All positions are valid (0-3)");
        }
    }

    private async Task CheckForInvalidFoodAdjustmentsAsync(DataValidationReport report, int? startRound, int? endRound)
    {
        Console.WriteLine("\n7️⃣ Checking for invalid food adjustments (should be -3 to +3)...");

        var query = _context.RoundPiratePlacements.AsQueryable();

        if (startRound.HasValue)
            query = query.Where(rpp => rpp.RoundId >= startRound.Value);
        if (endRound.HasValue)
            query = query.Where(rpp => rpp.RoundId <= endRound.Value);

        var invalidAdjustments = await query
            .Where(rpp => rpp.PirateFoodAdjustment < -3 || rpp.PirateFoodAdjustment > 3)
            .Select(rpp => new
            {
                rpp.RoundId,
                rpp.ArenaId,
                rpp.PirateId,
                rpp.PirateFoodAdjustment
            })
            .ToListAsync();

        if (invalidAdjustments.Any())
        {
            report.Issues.Add(new DataValidationIssue
            {
                Severity = ValidationSeverityEnum.Medium,
                Category = "Invalid Food Adjustments",
                Message = $"Found {invalidAdjustments.Count} records with food adjustments outside -3 to +3 range",
                AffectedRecords = invalidAdjustments.Count,
                Details = invalidAdjustments.Take(10).Select(r =>
                    $"Round {r.RoundId}, Arena {r.ArenaId}, Pirate {r.PirateId}: Adjustment={r.PirateFoodAdjustment}"
                ).ToList()
            });

            Console.WriteLine($"   ⚠️  Found {invalidAdjustments.Count} records with invalid food adjustments");
        }
        else
        {
            Console.WriteLine("   ✅ All food adjustments are valid (-3 to +3)");
        }
    }

    private async Task CheckForIncompleteArenasAsync(DataValidationReport report, int? startRound, int? endRound)
    {
        Console.WriteLine("\n8️⃣ Checking for incomplete arenas (should have 4 pirates)...");

        var query = _context.RoundPiratePlacements.AsQueryable();

        if (startRound.HasValue)
            query = query.Where(rpp => rpp.RoundId >= startRound.Value);
        if (endRound.HasValue)
            query = query.Where(rpp => rpp.RoundId <= endRound.Value);

        var arenaCount = await query
            .Where(rpp => (rpp.CurrentOdds ?? rpp.StartingOdds) > 1) // Exclude 1:1 placeholders
            .GroupBy(rpp => new { rpp.RoundId, rpp.ArenaId })
            .Select(g => new
            {
                g.Key.RoundId,
                g.Key.ArenaId,
                PirateCount = g.Count()
            })
            .Where(x => x.PirateCount != 4)
            .ToListAsync();

        if (arenaCount.Any())
        {
            report.Issues.Add(new DataValidationIssue
            {
                Severity = ValidationSeverityEnum.Low,
                Category = "Incomplete Arenas",
                Message = $"Found {arenaCount.Count} arenas with != 4 pirates (excluding 1:1 placeholders)",
                AffectedRecords = arenaCount.Count,
                Details = arenaCount.Take(10).Select(r =>
                    $"Round {r.RoundId}, Arena {r.ArenaId}: {r.PirateCount} pirates"
                ).ToList()
            });

            Console.WriteLine($"   ⚠️  Found {arenaCount.Count} arenas with != 4 pirates");
        }
        else
        {
            Console.WriteLine("   ✅ All arenas have exactly 4 pirates (excluding 1:1 placeholders)");
        }
    }

    private void DisplayValidationSummary(DataValidationReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("📊 VALIDATION SUMMARY");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var criticalCount = report.Issues.Count(i => i.Severity == ValidationSeverityEnum.Critical);
        var highCount = report.Issues.Count(i => i.Severity == ValidationSeverityEnum.High);
        var mediumCount = report.Issues.Count(i => i.Severity == ValidationSeverityEnum.Medium);
        var lowCount = report.Issues.Count(i => i.Severity == ValidationSeverityEnum.Low);

        Console.WriteLine($"Total Issues: {report.Issues.Count}");
        Console.WriteLine($"   Critical: {criticalCount}");
        Console.WriteLine($"   High:     {highCount}");
        Console.WriteLine($"   Medium:   {mediumCount}");
        Console.WriteLine($"   Low:      {lowCount}");

        if (criticalCount > 0)
        {
            Console.WriteLine("\n❌ CRITICAL ISSUES FOUND - Must be fixed before training:");
            foreach (var issue in report.Issues.Where(i => i.Severity == ValidationSeverityEnum.Critical))
                Console.WriteLine($"   • {issue.Category}: {issue.Message}");
        }
        else if (highCount > 0)
        {
            Console.WriteLine("\n⚠️  HIGH PRIORITY ISSUES - Should be investigated:");
            foreach (var issue in report.Issues.Where(i => i.Severity == ValidationSeverityEnum.High))
                Console.WriteLine($"   • {issue.Category}: {issue.Message}");
        }
        else
        {
            Console.WriteLine("\n✅ No critical or high-priority issues found");
        }

        report.IsValid = criticalCount == 0 && highCount == 0;
        report.ValidationPassed = criticalCount == 0;
    }

    public async Task<bool> HasValidBettingOpportunities(int roundId, NfcbetsContext context)
    {
        var placements = await context.RoundPiratePlacements
            .Where(rpp => rpp.RoundId == roundId)
            .ToListAsync();

        // Check each arena has at least one pirate with odds > 1
        var arenas = placements.GroupBy(p => p.ArenaId);

        foreach (var arena in arenas)
        {
            var hasValidPirate = arena.Any(p => (p.CurrentOdds ?? p.StartingOdds) > 1);
            if (!hasValidPirate)
            {
                Console.WriteLine($"⚠️  Arena {arena.Key} in round {roundId} has no valid betting options (all 1:1)");
                return false;
            }
        }

        return true;
    }
}