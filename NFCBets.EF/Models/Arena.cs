using System;
using System.Collections.Generic;

namespace NFCBets.EF.Models;

public partial class Arena
{
    public int Id { get; set; }

    public int ArenaId { get; set; }

    public string ArenaName { get; set; } = null!;

    public virtual ICollection<RoundFoodCourse> RoundFoodCourses { get; set; } = new List<RoundFoodCourse>();

    public virtual ICollection<RoundPiratePlacement> RoundPiratePlacements { get; set; } = new List<RoundPiratePlacement>();

    public virtual ICollection<RoundResult> RoundResults { get; set; } = new List<RoundResult>();
}
